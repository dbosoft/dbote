using Azure.Messaging.ServiceBus;
using Azure.Storage.Sas;
using Dbosoft.Bote.Primitives;
using Dbosoft.Bote.Transport.Abstractions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Azure.Storage.Blobs;

namespace Dbosoft.Bote.BoteWorker;

internal partial class BoteHub
{
    /// <summary>
    /// Message from client to be sent to Service Bus queue.
    /// </summary>
    /// <param name="invocationContext"></param>
    /// <param name="queue"></param>
    /// <param name="message"></param>
    /// <returns></returns>
    /// <exception cref="UnauthorizedAccessException"></exception>
    [Function(nameof(SendMessage))]
    public async Task SendMessage(
        [SignalRTrigger(
            HubName,
            "messages",
            nameof(this.SendMessage),
            nameof(queue), nameof(message),
            ConnectionStringSetting = "dbote:Worker:SignalR:Connection")]
        SignalRInvocationContext invocationContext,
        string queue,
        BoteMessage message)
    {
        var tenantId = ExtractTenantId(invocationContext);
        var clientId = ExtractClientId(invocationContext);


        // Validate queue name against whitelist
        var actualQueueName = queue.StartsWith($"{serviceBusOptions.Value.Queues.Clients}-")
            ? $"{serviceBusOptions.Value.Queues.Clients}"
            : queue;

        // clients can only send to Cloud queue or clients queue
        var allowedQueues = new[]
        {
            serviceBusOptions.Value.Queues.Cloud,
            serviceBusOptions.Value.Queues.Clients
        };

        if (!allowedQueues.Contains(actualQueueName))
        {
            logger.LogWarning("Client {TenantId}-{ClientId} attempted to send to unauthorized queue: {Queue}",
                tenantId, clientId, queue);
            throw new UnauthorizedAccessException($"Queue '{queue}' is not allowed");
        }

        // Validate headers - REJECT if client attempts to inject security-critical headers
        var blockedHeaders = new[]
        {
            BoteHeaders.TenantId,
            BoteHeaders.ClientId
        };

        foreach (var blockedHeader in blockedHeaders)
        {
            if (!message.Headers.ContainsKey(blockedHeader)) continue;
            logger.LogError("Client {TenantId}-{ClientId} attempted to inject blocked header: {Header}",
                tenantId, clientId, blockedHeader);
            throw new UnauthorizedAccessException($"Attempted to inject blocked header: {blockedHeader}");
        }

        var serviceBusMessage = messageConverter.ToServiceBus(message);

        // Add authenticated tenant/client identity to message properties
        serviceBusMessage.ApplicationProperties.Add(BoteHeaders.TenantId, tenantId);
        serviceBusMessage.ApplicationProperties.Add(BoteHeaders.ClientId, clientId);


        var targetQueueName = serviceBusOptions.Value.Queues.Cloud;
        Uri? sasUri = null;

        // Check if this is an attachment message that needs monitoring
        if (serviceBusMessage.ApplicationProperties.TryGetValue(BoteHeaders.AttachmentId,
                out var attachmentIdOb))
        {
            var attachmentId = attachmentIdOb.ToString()
                               ?? throw new InvalidOperationException(
                                   $"Invalid {BoteHeaders.AttachmentId} header value.");
            var container = blobServiceClient.GetBlobContainerClient(BoteStorageConstants.Inbox);

            // Check attachment readiness
            var attachmentState = await IsAttachmentReady(container, tenantId, attachmentId);

            switch (attachmentState)
            {
                case AttachmentState.Failed:
                    throw new InvalidDataException($"Failed to copy attachment {BoteHeaders.AttachmentId}");
                case AttachmentState.Ready:
                    // fall through to forwarding logic
                    sasUri = CreateAttachmentInboxSasUri(container, tenantId, attachmentId);

                    break;
                case AttachmentState.Pending:
                default:
                    // we have to send this message to our own queue for rescheduling
                    targetQueueName = serviceBusOptions.Value.Queues.CloudIncoming;
                    break;
            }

        }

        if (sasUri != null)
            serviceBusMessage.ApplicationProperties[BoteHeaders.DataBusInboxSas] = sasUri.ToString();

        await using var serviceBusSender = serviceBusClient.CreateSender(targetQueueName);
        await serviceBusSender.SendMessageAsync(serviceBusMessage);


    }


    /// <summary>
    /// Message received from Service Bus queue to be processed and forwarded to client(s).
    /// </summary>
    /// <param name="message"></param>
    /// <param name="messageActions"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    [Function(nameof(ServiceBusReceivedMessageFunction))]
    public async Task ServiceBusReceivedMessageFunction(
        [ServiceBusTrigger("%dbote:Worker:ServiceBus:Queues:Clients%", Connection = "dbote:Worker:ServiceBus:Connection")]
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions)
    {

        if (!message.ApplicationProperties.TryGetValue(BoteHeaders.TenantId, out var tenantIdObj))
            throw new InvalidOperationException($"Missing {BoteHeaders.TenantId} in message properties.");

        var tenantId = tenantIdObj.ToString()!;
        logger.LogInformation("Received message {MessageId} to for tenant {TenantId}",
            message.MessageId, tenantId);


        // Check if this is an attachment message that needs monitoring
        if (message.ApplicationProperties.TryGetValue(BoteHeaders.AttachmentId,
                out var attachmentIdObj))
        {
            var attachmentId = attachmentIdObj.ToString()!;
            var blobContainer = await GetClientBlobContainer(tenantId);
            // Check attachment readiness
            var attachmentState = await IsAttachmentReady(blobContainer, tenantId, attachmentId);

            switch (attachmentState)
            {
                case AttachmentState.Failed:
                    await messageActions.DeadLetterMessageAsync(message,
                        new Dictionary<string, object>
                        {
                            ["Reason"] = "Attachment copy failed"
                        });
                    break;
                case AttachmentState.Ready:
                    // fall through to forwarding logic
                    break;
                case AttachmentState.Pending:
                default:
                    // Blob not ready - reschedule or dead-letter
                    await RescheduleOrDeadLetterAsync(
                        message,
                        messageActions,
                        serviceBusOptions.Value.Queues.Clients);
                    return;
            }

        }

        // Forward message to client (remove internal tracking headers)
        var boteMsg = messageConverter.ToBote(message);
        boteMsg.Headers.Remove(BoteHeaders.AttachmentId);
        boteMsg.Headers.Remove(BoteHeaders.ReScheduleCounter);

        logger.LogInformation("Ready to forward message {MessageId} to tenant {TenantId}", 
            message.MessageId, tenantId);

        if (message.ApplicationProperties.TryGetValue(BoteHeaders.Topic, out var msgTopic))
        {
            await BroadcastToTopic(tenantId, msgTopic.ToString()!, boteMsg);
        }
        else if (message.ApplicationProperties.TryGetValue(BoteHeaders.ClientId, out var msgClientId))
        {
            await ForwardToClientQueue(tenantId, msgClientId.ToString()!, boteMsg);
        }
        else
        {
            throw new InvalidOperationException(
                "Message must have either Topic (for broadcast) or ClientId (for targeted delivery).");
        }

        await messageActions.CompleteMessageAsync(message);


    }

    [Function(nameof(CloudIncomingMonitor))]
    public async Task CloudIncomingMonitor(
        [ServiceBusTrigger("%dbote:Worker:ServiceBus:Queues:CloudIncoming%", Connection = "dbote:Worker:ServiceBus:Connection")]
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions)
    {
        if (!message.ApplicationProperties.TryGetValue(BoteHeaders.TenantId, out var tenantIdObj))
            throw new InvalidOperationException($"Missing {BoteHeaders.TenantId} in message properties.");

        var tenantId = tenantIdObj.ToString()!;
        Uri? sasUri = null;
        if (message.ApplicationProperties.TryGetValue(BoteHeaders.AttachmentId,
                out var attachmentIdOb))
        {
            var attachmentId = attachmentIdOb.ToString()
                               ?? throw new InvalidOperationException(
                                   $"Invalid {BoteHeaders.AttachmentId} header value.");
            var container = blobServiceClient.GetBlobContainerClient(BoteStorageConstants.Inbox);

            // Check attachment readiness
            var attachmentState = await IsAttachmentReady(container, tenantId, attachmentId);

            switch (attachmentState)
            {
                case AttachmentState.Failed:
                    await messageActions.DeadLetterMessageAsync(message,
                            new Dictionary<string, object>
                            {
                                ["Reason"] = "Attachment copy failed"
                            });
                    break;
                case AttachmentState.Ready:
                    // Attachment is ready! Generate SAS URL and forward to Cloud queue
                    sasUri = CreateAttachmentInboxSasUri(container, tenantId, attachmentId);
                    break;
                case AttachmentState.Pending:
                default:
                    // Blob not ready - reschedule or dead-letter
                    await RescheduleOrDeadLetterAsync(
                        message,
                        messageActions,
                        serviceBusOptions.Value.Queues.CloudIncoming);
                    return;
            }
            
        }

        var readyMessage = new ServiceBusMessage(message.Body)
        {
            // Copy system properties
            ContentType = message.ContentType,
            CorrelationId = message.CorrelationId,
            MessageId = message.MessageId,
            TimeToLive = message.TimeToLive
        };

        // Copy all application properties except internal tracking headers
        foreach (var prop in message.ApplicationProperties)
        {
            if(prop.Key == BoteHeaders.ReScheduleCounter)
                continue;

            readyMessage.ApplicationProperties[prop.Key] = prop.Value;

        }

        if(sasUri!= null)
            readyMessage.ApplicationProperties[BoteHeaders.DataBusInboxSas] = sasUri.ToString();

        await using var serviceBusSender = serviceBusClient.CreateSender(
            serviceBusOptions.Value.Queues.Cloud);
        await serviceBusSender.SendMessageAsync(readyMessage);
        await messageActions.CompleteMessageAsync(message);
    }

    private Uri CreateAttachmentInboxSasUri(BlobContainerClient container,
        string tenantId, string attachmentId)
    {
        var blobPath = $"{tenantId}/{attachmentId}";
        var blobClient = container.GetBlobClient(blobPath);
        var sasUri = blobClient.GenerateSasUri(
            BlobSasPermissions.Read | BlobSasPermissions.Delete,
            DateTimeOffset.UtcNow.AddDays(30));
        return sasUri;
    }

    /// <summary>
    /// Handles rescheduling or dead-lettering of attachment messages that are not ready.
    /// This is the shared rescheduling logic used by both Cloud→Client and Client→Cloud flows.
    /// </summary>
    /// <param name="message">The original Service Bus message</param>
    /// <param name="messageActions">Actions for message completion/dead-lettering</param>
    /// <param name="queueName">Name of queue for scheduling</param>
    private async Task RescheduleOrDeadLetterAsync(
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions,
        string queueName)
    {
        var rescheduleCounter = 0;
        if (message.ApplicationProperties.TryGetValue(BoteHeaders.ReScheduleCounter,
                out var rescheduleObj))
        {
            if (int.TryParse(rescheduleObj.ToString(), out var rescheduleInt))
                rescheduleCounter = rescheduleInt;
        }

        // Allow up to 2 hours for attachment copy to complete (130 attempts)
        if (rescheduleCounter < 130)
        {
            var newMessage = new ServiceBusMessage(message.Body)
            {
                // Copy system properties
                ContentType = message.ContentType,
                CorrelationId = message.CorrelationId,
                TimeToLive = message.TimeToLive
            };

            // Copy application properties except internal tracking headers
            foreach (var prop in message.ApplicationProperties)
            {
                newMessage.ApplicationProperties[prop.Key] = prop.Value;
            }

            var delay = rescheduleCounter == 0 ? 0 : Math.Min(Math.Pow(1.5, rescheduleCounter), 60);
            rescheduleCounter++;
            newMessage.ApplicationProperties[BoteHeaders.ReScheduleCounter] = rescheduleCounter;

            await using var sender = serviceBusClient.CreateSender(queueName);

            await sender.ScheduleMessageAsync(newMessage, DateTimeOffset.UtcNow.AddSeconds(delay));
            await messageActions.CompleteMessageAsync(message);

            logger.LogInformation("Queue {Queue}: Reschedule Message {MessageId} with delay {Delay}",
                queueName,
                message.MessageId, delay);

        }
        else
        {
            await messageActions.DeadLetterMessageAsync(message,
                new Dictionary<string, object>
                {
                    ["Reason"] = "Attachment not ready after 2 hours"
                });


            logger.LogError("Queue {Queue}: Reschedule timed out after 2 hours: {MessageId}",
                queueName,
                message.MessageId);
        }
    }

    private async Task ForwardToClientQueue(string tenantId, string clientId, BoteMessage boteMessage)
    {
        logger.LogInformation("Forwarding message to tenant/client {TenantId}/{ClientId}",
            tenantId, clientId);

        var queueClient = await tenantStorageResolver.ResolveQueueClient(tenantId, clientId);
        await queueClient.CreateIfNotExistsAsync();

        var receipt = await queueClient.SendMessageAsync(
            JsonSerializer.Serialize(boteMessage));

        // Notify specific client via SignalR
        await Clients.User($"{tenantId}-{clientId}").NewMessage(receipt.Value.MessageId);
    }

}