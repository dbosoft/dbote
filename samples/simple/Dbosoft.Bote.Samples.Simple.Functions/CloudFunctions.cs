using Azure.Messaging.ServiceBus;
using Dbosoft.Bote.Cloud;
using Dbosoft.Bote.Samples.Simple.Messages;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Dbosoft.Bote.Samples.Simple.Functions;

public class CloudFunctions(BoteCloudSender sender, ILogger<CloudFunctions> logger)
{
    private static int _pushCounter;

    /// <summary>
    /// Takes what the clients send from the cloud queue. A failure throws, so Service Bus
    /// delivers the message again and dead-letters it after the queue's max delivery count.
    /// </summary>
    [Function("Cloud")]
    public async Task Receive(
        [ServiceBusTrigger("%dbote:Cloud:ServiceBus:Queues:Cloud%", Connection = "dbote:Cloud:ServiceBus:Connection")]
        ServiceBusReceivedMessage message,
        CancellationToken cancellationToken)
    {
        var incoming = BoteIncomingMessage.From(message);

        if (incoming.Is<PingMessage>())
        {
            var ping = incoming.Deserialize<PingMessage>();
            logger.LogInformation("PING!: {Message} - {Counter} from {TenantId}/{ClientId}",
                ping.Message, ping.Counter, incoming.TenantId, incoming.ClientId);

            await sender.ReplyAsync(incoming, new PongMessage
            {
                Message = $"Hello back {ping.Counter}",
                Counter = ping.Counter,
            }, cancellationToken: cancellationToken);
            return;
        }

        logger.LogWarning("Ignoring message {MessageId} of unknown type {MessageType}",
            incoming.MessageId, incoming.MessageType);
    }

    [Function("Push")]
    public async Task Push([TimerTrigger("*/5 * * * * *")] TimerInfo timer, CancellationToken cancellationToken)
    {
        var counter = Interlocked.Increment(ref _pushCounter);
        foreach (var tenantId in new[] { "tenant-a", "tenant-b" })
        {
            await sender.SendToClientAsync(tenantId, "client-a", new PushMessage
            {
                Message = $"Push {counter}",
                Counter = counter,
            }, cancellationToken: cancellationToken);
        }
    }
}
