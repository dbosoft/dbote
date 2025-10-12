using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Dbosoft.Bote.Rebus.Config;
using Dbosoft.Bote.Transport.Abstractions;
using Rebus.Exceptions;
using Rebus.Extensions;
using Rebus.Logging;
using Rebus.Messages;
using Rebus.Threading;
using Rebus.Transport;

namespace Dbosoft.Bote.Rebus.Transport;

// Based on Rebus.AzureQueues
// https://github.dev/rebus-org/Rebus.AzureQueues/blob/master/Rebus.AzureQueues
// Licensed under MIT License

internal sealed class BoteTransport : ITransport, IDisposable
{
    /// <summary>
    /// External timeout manager address set to this magic address will be routed to the destination address specified by the <see cref="Headers.DeferredRecipient"/> header
    /// </summary>
    internal const string MagicDeferredMessagesAddress = "___deferred___";

    private readonly ILog _log;
    private readonly IAsyncTask _messageLockRenewalTask;
    // TODO is transport multithreaded?
    private readonly ConcurrentDictionary<string, MessageLockRenewer> _messageLockRenewers = new();
    private readonly string? _queueName;
    private readonly BoteTransportOptions _options;
    private readonly ISignalRClient _signalRClient;
    private readonly IPendingMessagesIndicators _pendingMessagesIndicator;
    private QueueClient? _queueClient;

    public BoteTransport(
        string? queueName,
        BoteTransportOptions options,
        ISignalRClient signalRClient,
        IPendingMessagesIndicators pendingMessagesIndicator,
        IRebusLoggerFactory loggerFactory,
        IAsyncTaskFactory asyncTaskFactory
    )
    {
        _queueName = queueName;
        _options = options;
        _signalRClient = signalRClient;
        _pendingMessagesIndicator = pendingMessagesIndicator;
        _log = loggerFactory.GetLogger<BoteTransport>();
        _messageLockRenewalTask = asyncTaskFactory.Create(
            "Bote Rebus transport message lock renewal",
            RenewPeekLocks,
            prettyInsignificant: true,
            intervalSeconds: 10);
    }

    public string? Address => _queueName;

    public void CreateQueue(string address)
    {
        // Intentionally not implemented. The Bote infrastructure is responsible
        // for creating the necessary queues.
    }

    public async Task<TransportMessage?> Receive(ITransactionContext context, CancellationToken cancellationToken)
    {
        if (Address is null)
            throw new InvalidOperationException("This Bote transport does not have an input queue, which means that it is configured to be a one-way client. Therefore, it is not possible to receive anything.");

        var hasPendingMessages = await _pendingMessagesIndicator.GetAsync();
        if (!hasPendingMessages)
            return null;

        var response = await ExecuteWithRetryAsync(
            queueClient => queueClient.ReceiveMessageAsync(
                visibilityTimeout: _options.InitialVisibilityDelay,
                cancellationToken: cancellationToken));

        var queueMessage = response.Value;
        if (queueMessage is null)
        {
            await _pendingMessagesIndicator.ClearAsync();
            return null;
        }
        
        if (_options.AutomaticPeekLockRenewalEnabled)
        {
            _messageLockRenewers.TryAdd(queueMessage.MessageId, new MessageLockRenewer(queueMessage));
        }

        SetUpCompletion(context, queueMessage);

        var boteMessage = JsonSerializer.Deserialize<BoteMessage>(
            queueMessage.Body.ToString());

        var transportMessage = new TransportMessage(
            boteMessage!.Headers.ToDictionary(),
            Encoding.UTF8.GetBytes(boteMessage.Body));
        return transportMessage;
    }

    public Task Send(string destinationAddress, TransportMessage transportMessage, ITransactionContext context)
    {
        var outgoingMessages = context.GetOrAdd("outgoing-messages", () =>
        {
            var messagesToSend = new ConcurrentQueue<(string Destination, BoteMessage Message)>();

            context.OnCommit(_ =>
            {
                // TODO fix defer messages

                return Task.WhenAll(messagesToSend.ToList().Select(async t =>
                {
                    await _signalRClient.SendMessage(t.Destination, t.Message);

                    //var errorText = $"Could not send message with ID {transportMessage.GetMessageId()} to '{message.DestinationAddress}'";
                    //throw new RebusApplicationException(exception, errorText);
                }));
            });

            return messagesToSend;
        });


        var headers = transportMessage.Headers;

        if (destinationAddress == MagicDeferredMessagesAddress)
        {
            var actualDestinationAddress = transportMessage.Headers.GetValueOrNull(Headers.DeferredRecipient);
            if(actualDestinationAddress is null)
                throw new ArgumentException($"Message was deferred, but the '{Headers.DeferredRecipient}' header could not be found. When a message is sent 'into the future', the '{Headers.DeferredRecipient}' header must indicate which queue to deliver the message after the delay has passed, and the '{Headers.DeferredUntil}' header must indicate the earliest time of when the message must be delivered.");

            if (!headers.ContainsKey(Headers.DeferredUntil))
                throw new ArgumentException($"Message was deferred, but the '{Headers.DeferredUntil}' header could not be found. When a message is sent 'into the future', the '{Headers.DeferredRecipient}' header must indicate which queue to deliver the message after the delay has passed, and the '{Headers.DeferredUntil}' header must indicate the earliest time of when the message must be delivered.");
            

            var clonedHeaders = headers.Clone();
            clonedHeaders.Remove(Headers.DeferredRecipient);

            outgoingMessages.Enqueue((actualDestinationAddress, new BoteMessage()
            {
                Headers = clonedHeaders,
                Body = Encoding.UTF8.GetString(transportMessage.Body),
            }));

            return Task.CompletedTask;
        }

        outgoingMessages.Enqueue((destinationAddress, new BoteMessage()
        {
            Headers = transportMessage.Headers.Clone(),
            Body = Encoding.UTF8.GetString(transportMessage.Body),
        }));

        return Task.CompletedTask;
    }
    private void SetUpCompletion(ITransactionContext context, QueueMessage cloudQueueMessage)
    {
        var messageId = cloudQueueMessage.MessageId;

        context.OnAck(async _ =>
        {
            //if the message has been Automatic renewed, the popreceipt might have changed since setup
            var popReceipt = _messageLockRenewers.TryRemove(messageId, out var renewer)
                ? renewer.PopReceipt
                : cloudQueueMessage.PopReceipt;

            try
            {
                // if we get this far, don't pass on the cancellation token
                // ReSharper disable once MethodSupportsCancellation
                await ExecuteWithRetryAsync(
                    queueClient => queueClient.DeleteMessageAsync(
                        messageId: messageId, popReceipt: popReceipt));
            }
            catch (Exception exception)
            {
                throw new RebusApplicationException(exception, $"Could not delete message with ID {messageId} and pop receipt {popReceipt} from the input queue");
            }
        });

        context.OnNack(async _ =>
        {
            var visibilityTimeout = TimeSpan.FromSeconds(0);

            var popReceipt = _messageLockRenewers.TryRemove(messageId, out var renewer)
                ? renewer.PopReceipt
                : cloudQueueMessage.PopReceipt;

            try
            {
                await ExecuteWithRetryAsync(
                    queueClient => queueClient.UpdateMessageAsync(
                        cloudQueueMessage.MessageId, popReceipt, visibilityTimeout: visibilityTimeout));
            }
            catch
            {
                // ignore if this fails
            }
        });

        context.OnDisposed(ctx => _messageLockRenewers.TryRemove(messageId, out _));
    }

    private async Task RenewPeekLocks()
    {
        var mustBeRenewed = _messageLockRenewers.Values
            .Where(r => r.IsDue)
            .ToList();

        if (mustBeRenewed.Count == 0)
            return;

        _log.Debug("Found {count} peek locks to be renewed", mustBeRenewed.Count);

        await Task.WhenAll(mustBeRenewed.Select(async r =>
        {
            try
            {
                await ExecuteWithRetryAsync(r.Renew);

                _log.Debug("Successfully renewed peek lock for message with ID {messageId}", r.MessageId);
            }
            catch (Exception exception)
            {
                _log.Warn(exception, "Error when renewing peek lock for message with ID {messageId}", r.MessageId);
            }
        }));
    }


    private async Task<T> ExecuteWithRetryAsync<T>(Func<QueueClient, Task<T>> operation)
    {
        if (_queueClient is not null)
        {
            try
            {
                return await operation(_queueClient);
            }
            catch (RequestFailedException rfe) when (rfe.Status == (int)HttpStatusCode.Unauthorized)
            {

            }
        }
        
        var queueConnection = await _signalRClient.GetQueueConnection();
        _queueClient = new QueueClient(new Uri(queueConnection));

        return await operation(_queueClient);
    }

    public void Dispose()
    {
        _messageLockRenewalTask.Dispose();
    }
}
