using Azure.Messaging.ServiceBus;
using Azure.Storage.Sas;
using Dbosoft.Bote.Primitives;
using Dbosoft.Bote.Transport.Abstractions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using Azure.Storage.Blobs;

namespace Dbosoft.Bote.BoteWorker;

internal partial class BoteHub
{
    // Azure Storage Queue hard limit for a single message payload (64 KiB).
    private const int MaxStorageQueueMessageBytes = 64 * 1024;

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
            nameof(SendMessage),
            nameof(queue), nameof(message),
            ConnectionStringSetting = "dbote:Worker:SignalR:Connection")]
        SignalRInvocationContext invocationContext,
        string queue,
        BoteMessage message)
    {
        var inboundStart = Stopwatch.GetTimestamp();
        var tenantId = ExtractTenantId(invocationContext);
        var clientId = ExtractClientId(invocationContext);


        // Clients send to the cloud queue, or to their own queue to defer a message.
        var target = BoteQueuePolicy.ResolveClientSendTarget(queue,
            serviceBusOptions.Value.Queues.Cloud, serviceBusOptions.Value.Queues.Clients, clientId);

        if (target is null)
        {
            logger.LogWarning("Client {TenantId}-{ClientId} attempted to send to unauthorized queue: {Queue}",
                tenantId, clientId, queue);
            throw new UnauthorizedAccessException($"Queue '{queue}' is not allowed");
        }

        // Validate headers against the trust-boundary allow-list - REJECT if the client
        // tries to set any worker-controlled dbote-* header (identity, routing, inbox SAS,
        // retry tracking). Only dbote-attachment-id and non-dbote headers are permitted.
        var disallowedHeaders = BoteHeaderPolicy.DisallowedClientHeaders(message.Headers.Keys);
        if (disallowedHeaders.Count > 0)
        {
            logger.LogError("Client {TenantId}-{ClientId} attempted to set disallowed header(s): {Headers}",
                tenantId, clientId, string.Join(", ", disallowedHeaders));
            throw new UnauthorizedAccessException(
                $"Attempted to set disallowed header(s): {string.Join(", ", disallowedHeaders)}");
        }

        var tsb = Stopwatch.GetTimestamp();
        var serviceBusMessage = messageConverter.ToServiceBus(message);
        workerMetrics.InToServiceBus.Record(WorkerMetrics.MsSince(tsb));

        // Add authenticated tenant/client identity to message properties
        serviceBusMessage.ApplicationProperties.Add(BoteHeaders.TenantId, tenantId);
        serviceBusMessage.ApplicationProperties.Add(BoteHeaders.ClientId, clientId);


        // A message to itself goes through the clients queue, which delivers it back to the
        // client at its scheduled time; the identity set above routes it to the sender only.
        var targetQueueName = target == ClientSendTarget.Self
            ? serviceBusOptions.Value.Queues.Clients
            : serviceBusOptions.Value.Queues.Cloud;
        Uri? sasUri = null;

        // Check if this is an attachment message that needs monitoring
        if (serviceBusMessage.ApplicationProperties.TryGetValue(BoteHeaders.AttachmentId,
                out var attachmentIdOb))
        {
            if (target == ClientSendTarget.Self)
                throw new NotSupportedException("A client cannot send an attachment to itself.");

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

        var asb = Stopwatch.GetTimestamp();
        await senderCache.GetSender(targetQueueName).SendMessageAsync(serviceBusMessage);
        workerMetrics.InAsbSend.Record(WorkerMetrics.MsSince(asb));
        workerMetrics.InTotal.Record(WorkerMetrics.MsSince(inboundStart));
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
        var tb = Stopwatch.GetTimestamp();
        var boteMsg = messageConverter.ToBote(message);
        workerMetrics.OutToBote.Record(WorkerMetrics.MsSince(tb));
        // The dbote-* headers are the worker's: a client may not send them, so it must not get
        // them either - or a handler that defers a broadcast (Rebus copies the headers) is
        // refused for setting dbote-topic.
        foreach (var key in boteMsg.Headers.Keys
                     .Where(k => k.StartsWith("dbote-", StringComparison.OrdinalIgnoreCase)).ToList())
            boteMsg.Headers.Remove(key);

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

        await senderCache.GetSender(serviceBusOptions.Value.Queues.Cloud)
            .SendMessageAsync(readyMessage);
        await messageActions.CompleteMessageAsync(message);
    }

    private Uri CreateAttachmentInboxSasUri(BlobContainerClient container,
        string tenantId, string attachmentId)
    {
        var blobPath = $"{tenantId}/{attachmentId}";
        var blobClient = container.GetBlobClient(blobPath);
        // Short-lived: this SAS travels inside the message (ASB application property) and would
        // otherwise sit in the DLQ / telemetry as a long-lived delete capability. It is minted
        // only once the attachment is Ready and is consumed by the cloud's DataBusIncomingStep
        // shortly after; two hours is generous headroom. The H4c TTL policy reaps the blob.
        var sasUri = blobClient.GenerateSasUri(
            BlobSasPermissions.Read | BlobSasPermissions.Delete,
            DateTimeOffset.UtcNow.AddHours(2));
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

            await senderCache.GetSender(queueName)
                .ScheduleMessageAsync(newMessage, DateTimeOffset.UtcNow.AddSeconds(delay));
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

        var fwdStart = Stopwatch.GetTimestamp();
        var queueClient = await tenantStorageResolver.ResolveQueueClient(tenantId, clientId);
        var ser = Stopwatch.GetTimestamp();
        // Serialize the envelope straight into a pooled UTF-8 buffer (no intermediate payload
        // string, no separate GetByteCount pass - the written length is the byte count).
        using var buffer = new PooledByteBufferWriter(boteMessage.Body.Length + 256);
        using (var writer = new Utf8JsonWriter(buffer))
            JsonSerializer.Serialize(writer, boteMessage);
        workerMetrics.OutSerialize.Record(WorkerMetrics.MsSince(ser));

        // Storage Queue messages are capped at 64 KiB. A too-large message would otherwise
        // fail deep in the send with an opaque storage error; surface a defined error naming
        // the limit and the DataBus (attachment) alternative for large payloads instead.
        if (buffer.WrittenCount > MaxStorageQueueMessageBytes)
            throw new InvalidOperationException(
                $"Message for {tenantId}/{clientId} is {buffer.WrittenCount} bytes, exceeding the " +
                $"{MaxStorageQueueMessageBytes}-byte Storage Queue limit. Send large payloads as a " +
                "DataBus attachment instead of inline.");

        // Wraps the pooled buffer without copying; it stays valid until the send completes
        // because the 'using' scope outlives the awaits below.
        var payload = BinaryData.FromBytes(buffer.WrittenMemory);

        Azure.Response<Azure.Storage.Queues.Models.SendReceipt> receipt;
        var send = Stopwatch.GetTimestamp();
        try
        {
            // The client's queue is created at connect time (GetQueueMetadata), so it almost
            // always exists here. Send optimistically and create only on the rare 404 miss,
            // instead of a create round-trip on every delivery (incl. every broadcast subscriber).
            receipt = await queueClient.SendMessageAsync(payload);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            await queueClient.CreateIfNotExistsAsync();
            receipt = await queueClient.SendMessageAsync(payload);
        }
        workerMetrics.OutStorageSend.Record(WorkerMetrics.MsSince(send));

        // Notify specific client via SignalR
        var notify = Stopwatch.GetTimestamp();
        await Clients.User($"{tenantId}-{clientId}").NewMessage(receipt.Value.MessageId);
        workerMetrics.OutSignalRNotify.Record(WorkerMetrics.MsSince(notify));
        workerMetrics.OutForwardTotal.Record(WorkerMetrics.MsSince(fwdStart));
    }

}