using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Dbosoft.Bote.Rebus.Config;
using Dbosoft.Bote.Transport.Abstractions;
using Rebus.Bus;
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

internal sealed class BoteTransport : ITransport, IInitializable, IDisposable
{
    /// <summary>
    /// External timeout manager address set to this magic address will be routed to the destination address specified by the <see cref="Headers.DeferredRecipient"/> header
    /// </summary>
    internal const string MagicDeferredMessagesAddress = "___deferred___";

    // W3C trace-context header names (https://www.w3.org/TR/trace-context/).
    private const string TraceParentHeader = "traceparent";
    private const string TraceStateHeader = "tracestate";

    private readonly ILog _log;
    private readonly IAsyncTask _messageLockRenewalTask;
    private readonly IAsyncTask _pendingMessagesReconcileTask;
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
        var reconcileSeconds = Math.Max(1, (int)options.PendingMessagesReconcileInterval.TotalSeconds);
        _pendingMessagesReconcileTask = asyncTaskFactory.Create(
            "Bote Rebus transport pending-messages reconcile",
            ReconcilePendingMessages,
            prettyInsignificant: true,
            intervalSeconds: reconcileSeconds);
    }

    public string? Address => _queueName;

    public void Initialize()
    {
        // One-way clients have no input queue - nothing to receive or reconcile.
        if (Address is null)
            return;

        if (_options.AutomaticPeekLockRenewalEnabled)
            _messageLockRenewalTask.Start();

        // Safety net against the push-only polling model missing messages (lost push,
        // arrived-while-disconnected, stranded retry). Rebus never started these background
        // tasks before, so this also fixes the never-running lock renewal above.
        if (_options.PendingMessagesReconcileInterval > TimeSpan.Zero)
            _pendingMessagesReconcileTask.Start();
    }

    public void CreateQueue(string address)
    {
        // Intentionally not implemented. The Bote infrastructure is responsible
        // for creating the necessary queues.
    }

    // Periodically re-arm the pending-messages flag so an idle receive loop re-checks the
    // queue even when no SignalR push arrives. An empty poll clears the flag again, so this
    // costs at most one poll per interval when the queue is empty.
    private Task ReconcilePendingMessages() => _pendingMessagesIndicator.SetAsync();

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
            queueMessage.Body.ToMemory().Span);

        var transportMessage = new TransportMessage(
            boteMessage!.Headers.ToDictionary(),
            // Rebus' TransportMessage is defined in terms of byte[], so this copy is unavoidable
            // here (client receive path, not the worker hot path).
            boteMessage.Body.ToArray());
        return transportMessage;
    }

    public Task Send(string destinationAddress, TransportMessage transportMessage, ITransactionContext context)
    {
        var outgoingMessages = context.GetOrAdd("outgoing-messages", () =>
        {
            var messagesToSend = new ConcurrentQueue<(string Destination, BoteMessage Message)>();

            context.OnCommit(_ =>
                // Let any send failure propagate out of the commit: SendMessage now
                // awaits the server (InvokeAsync), so a rejected/failed send faults the
                // commit and Rebus retries instead of silently dropping the message.
                Task.WhenAll(messagesToSend.ToList().Select(t =>
                    _signalRClient.SendMessage(t.Destination, t.Message))));

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
            AddTraceContext(clonedHeaders);

            outgoingMessages.Enqueue((actualDestinationAddress, new BoteMessage()
            {
                Headers = clonedHeaders,
                Body = transportMessage.Body,
            }));

            return Task.CompletedTask;
        }

        var outgoingHeaders = transportMessage.Headers.Clone();
        AddTraceContext(outgoingHeaders);

        outgoingMessages.Enqueue((destinationAddress, new BoteMessage()
        {
            Headers = outgoingHeaders,
            Body = transportMessage.Body,
        }));

        return Task.CompletedTask;
    }

    // Carry the ambient W3C trace context onto the outgoing message so a distributed trace
    // the application started survives the SignalR/Storage-Queue hop instead of breaking at
    // the framework boundary. The worker's converter already forwards all headers on, and the
    // cloud->client direction inherits the Service Bus trace context the same way. Never
    // overwrite a trace id the message already carries.
    private static void AddTraceContext(IDictionary<string, string> headers)
    {
        var activity = Activity.Current;
        if (activity?.IdFormat != ActivityIdFormat.W3C || activity.Id is null)
            return;

        headers.TryAdd(TraceParentHeader, activity.Id);

        var traceState = activity.TraceStateString;
        if (!string.IsNullOrEmpty(traceState))
            headers.TryAdd(TraceStateHeader, traceState);
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

            // Re-arm polling for the redelivery: the message is visible again, but no SignalR
            // push is sent for a retry. Without this, a concurrent empty poll (Rebus receives
            // with parallelism > 1) clears the pending-messages flag while this message is
            // briefly in flight, and the redelivery is never polled - so Rebus never reaches
            // its max delivery count and the message is never dead-lettered.
            await _pendingMessagesIndicator.SetAsync();
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
            catch (RequestFailedException rfe)
                when (rfe.Status is (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden)
            {
                // The queue SAS expired (Storage returns 403 on an expired/invalid signature, not
                // just 401) or was revoked - fall through to fetch a fresh one. Without handling
                // 403 the client goes permanently silent once its ~1h SAS expires.
            }
        }

        var queueConnection = await _signalRClient.GetQueueConnection();
        _queueClient = new QueueClient(new Uri(queueConnection));

        return await operation(_queueClient);
    }

    public void Dispose()
    {
        _messageLockRenewalTask.Dispose();
        _pendingMessagesReconcileTask.Dispose();
    }
}
