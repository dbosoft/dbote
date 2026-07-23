using System.Net;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Queues;
using Dbosoft.Bote.Transport.Abstractions;
using Rebus.Extensions;
using Rebus.Logging;
using Rebus.Messages;
using Rebus.Retry;
using Rebus.Transport;

namespace Dbosoft.Bote.Rebus.Transport;

/// <summary>
/// Writes a poison message to the client's local dead-letter queue. Separated from
/// the transport so the destination can be faked in tests.
/// </summary>
internal interface IErrorQueueWriter
{
    Task WriteAsync(BoteMessage message);
}

/// <summary>
/// Rebus error handler for the Bote client transport. Instead of routing poison
/// messages back through the SignalR <c>SendMessage</c> path (whose whitelist only
/// accepts the cloud/clients queues and would reject the error queue), it drops them
/// into a client-owned Storage Queue at the edge, where the application can triage them.
/// This replaces Rebus' default deadletter handler for this transport.
/// </summary>
internal sealed class BoteErrorHandler(
    IErrorQueueWriter errorQueueWriter,
    IRebusLoggerFactory loggerFactory) : IErrorHandler
{
    private readonly ILog _log = loggerFactory.GetLogger<BoteErrorHandler>();

    public async Task HandlePoisonMessage(
        TransportMessage transportMessage,
        ITransactionContext transactionContext,
        ExceptionInfo exception)
    {
        var message = new BoteMessage
        {
            Headers = transportMessage.Headers.Clone(),
            Body = transportMessage.Body,
        };

        // Annotate with the final error, like Rebus' default deadletter handler, so the
        // edge application can see why the message failed when inspecting its error queue.
        message.Headers[Headers.ErrorDetails] = exception.Details;

        var messageId = transportMessage.Headers.GetValueOrNull(Headers.MessageId);

        await errorQueueWriter.WriteAsync(message);

        _log.Warn("Moved poison message {messageId} to the local dbote error queue: {errorMessage}",
            messageId ?? "<no-id>", exception.Message);
    }
}

/// <summary>
/// Default <see cref="IErrorQueueWriter"/>: obtains a write-only SAS for the client's
/// local error queue from the worker and enqueues the serialized message. The SAS is
/// cached and refreshed on an authorization failure (mirrors <see cref="BoteTransport"/>).
/// </summary>
internal sealed class BoteErrorQueueWriter(ISignalRClient signalRClient) : IErrorQueueWriter
{
    private QueueClient? _queueClient;

    public Task WriteAsync(BoteMessage message) =>
        ExecuteWithRetryAsync(client => client.SendMessageAsync(JsonSerializer.Serialize(message)));

    private async Task ExecuteWithRetryAsync(Func<QueueClient, Task> operation)
    {
        if (_queueClient is not null)
        {
            try
            {
                await operation(_queueClient);
                return;
            }
            catch (RequestFailedException rfe)
                when (rfe.Status is (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden)
            {
                // SAS expired or revoked - fall through to fetch a fresh one.
            }
        }

        var queueConnection = await signalRClient.GetErrorQueueConnection();
        _queueClient = new QueueClient(new Uri(queueConnection));

        await operation(_queueClient);
    }
}
