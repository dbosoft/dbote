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
    private const int MaxStorageQueueMessageBytes = ClientSendRules.MaxStorageQueueMessageBytes;

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


        // Clients send to the cloud queue, to their own queue to defer a message, or to another
        // client of their tenant the identity provider lets them reach.
        var route = BoteQueuePolicy.ResolveClientSendRoute(queue,
            serviceBusOptions.Value.Queues.Cloud, serviceBusOptions.Value.Queues.Clients, clientId,
            Peers(invocationContext));

        if (route is null)
        {
            if (queue.StartsWith($"{serviceBusOptions.Value.Queues.Clients}-", StringComparison.Ordinal))
                workerMetrics.PeerRefused();
            logger.LogWarning("Client {TenantId}-{ClientId} attempted to send to unauthorized queue: {Queue}",
                tenantId, clientId, queue);
            throw new UnauthorizedAccessException($"Queue '{queue}' is not allowed");
        }

        ClientSendRules.NormalizeHeaderNames(message.Headers);

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

        using var trace = BoteTracing.ContinueClientTrace(message.Headers, nameof(SendMessage));

        var tsb = Stopwatch.GetTimestamp();
        var serviceBusMessage = messageConverter.ToServiceBus(message);
        workerMetrics.InToServiceBus.Record(WorkerMetrics.MsSince(tsb));

        if (ClientSendRules.Refuse(message, serviceBusMessage, route, DateTimeOffset.UtcNow) is { } refusal)
        {
            logger.LogWarning("Refused message of {TenantId}/{ClientId} to {Queue}: {Reason}",
                tenantId, clientId, queue, refusal);
            throw new InvalidOperationException(refusal);
        }

        ClientSendRules.Stamp(serviceBusMessage.ApplicationProperties, tenantId, clientId, route,
            serviceBusOptions.Value.Queues.Clients);

        if (route.Target == ClientSendTarget.Peer)
        {
            // Only to a client that has connected once (its queue is made then), so a client
            // cannot have the worker make queues for any name it likes.
            var receiverQueue = await tenantStorageResolver.ResolveQueueClient(tenantId, route.ClientId!);
            if (!(await receiverQueue.ExistsAsync()).Value)
            {
                workerMetrics.PeerRefused();
                throw new InvalidOperationException($"Client '{route.ClientId}' is not known.");
            }

            // The audit trail of client-to-client traffic.
            workerMetrics.PeerSent();
            logger.LogInformation("Client {TenantId}/{ClientId} sends {MessageId} to {Receiver}",
                tenantId, clientId, serviceBusMessage.MessageId, route.ClientId);
        }

        // Messages to clients - itself or a peer - go through the clients queue, which delivers
        // them at their scheduled time.
        var targetQueueName = route.Target == ClientSendTarget.Cloud
            ? serviceBusOptions.Value.Queues.Cloud
            : serviceBusOptions.Value.Queues.Clients;
        Uri? sasUri = null;

        // Check if this is an attachment message that needs monitoring
        if (serviceBusMessage.ApplicationProperties.TryGetValue(BoteHeaders.AttachmentId,
                out var attachmentIdOb))
        {
            if (route.Target == ClientSendTarget.Self)
                throw new NotSupportedException("A client cannot send an attachment to itself.");

            var attachmentId = attachmentIdOb.ToString()
                               ?? throw new InvalidOperationException(
                                   $"Invalid {BoteHeaders.AttachmentId} header value.");
            var container = blobServiceClient.GetBlobContainerClient(BoteStorageConstants.Inbox);

            // Check attachment readiness
            var inboxPath = DataBusPaths.Inbox(tenantId, clientId, attachmentId);
            var attachmentState = await IsAttachmentReady(container, tenantId, attachmentId, inboxPath);

            switch (attachmentState)
            {
                case AttachmentState.Failed:
                    throw new InvalidDataException($"Failed to copy attachment {BoteHeaders.AttachmentId}");
                case AttachmentState.Ready when route.Target == ClientSendTarget.Peer:
                    // The receiver reads it where clients read what the cloud sent them.
                    await DeliverPeerAttachment(container, inboxPath, tenantId, clientId, attachmentId);
                    break;
                case AttachmentState.Ready:
                    // fall through to forwarding logic
                    sasUri = CreateAttachmentInboxSasUri(container, inboxPath);

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
        BoteTracing.Stamp(serviceBusMessage.ApplicationProperties);

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
            var attachmentState = await IsAttachmentReady(blobContainer, tenantId, attachmentId,
                DataBusPaths.Delivered(tenantId, attachmentId));

            switch (attachmentState)
            {
                case AttachmentState.Failed:
                    await messageActions.DeadLetterMessageAsync(message,
                        new Dictionary<string, object>
                        {
                            ["Reason"] = "Attachment copy failed"
                        });
                    return;
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
        // Except the sender of a message from another client: set by the worker from the
        // sender's token, it tells the receiver who is asking.
        if (message.ApplicationProperties.TryGetValue(BoteHeaders.SenderClientId, out var senderClientId))
            boteMsg.Headers[BoteHeaders.SenderClientId] = senderClientId.ToString()!;
        // The trigger runs in the trace of whoever sent the message; the client continues it.
        BoteTracing.Stamp(boteMsg.Headers);

        logger.LogInformation("Ready to forward message {MessageId} to tenant {TenantId}", 
            message.MessageId, tenantId);

        if (message.ApplicationProperties.TryGetValue(BoteHeaders.Topic, out var msgTopic))
        {
            await BroadcastToTopic(tenantId, msgTopic.ToString()!, boteMsg);
        }
        else if (message.ApplicationProperties.TryGetValue(BoteHeaders.ClientId, out var msgClientId))
        {
            // A message from a peer only goes to a queue that exists; see SendMessage.
            await ForwardToClientQueue(tenantId, msgClientId.ToString()!, boteMsg,
                StorageTimeToLive(message), createQueue: senderClientId is null);
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
        // Only the worker sets the sender, and only on messages from a client to a peer.
        var isPeerMessage = message.ApplicationProperties.ContainsKey(BoteHeaders.SenderClientId);
        Uri? sasUri = null;
        if (message.ApplicationProperties.TryGetValue(BoteHeaders.AttachmentId,
                out var attachmentIdOb))
        {
            var attachmentId = attachmentIdOb.ToString()
                               ?? throw new InvalidOperationException(
                                   $"Invalid {BoteHeaders.AttachmentId} header value.");
            // The client that uploaded it: the sender of a message to a peer, else the client id.
            if (!message.ApplicationProperties.TryGetValue(BoteHeaders.SenderClientId, out var senderObj)
                && !message.ApplicationProperties.TryGetValue(BoteHeaders.ClientId, out senderObj))
                throw new InvalidOperationException($"Missing {BoteHeaders.ClientId} in message properties.");
            var senderClientId = senderObj.ToString()!;

            var container = blobServiceClient.GetBlobContainerClient(BoteStorageConstants.Inbox);
            var inboxPath = DataBusPaths.Inbox(tenantId, senderClientId, attachmentId);

            // Check attachment readiness
            var attachmentState = await IsAttachmentReady(container, tenantId, attachmentId, inboxPath);

            switch (attachmentState)
            {
                case AttachmentState.Failed:
                    await messageActions.DeadLetterMessageAsync(message,
                            new Dictionary<string, object>
                            {
                                ["Reason"] = "Attachment copy failed"
                            });
                    return;
                case AttachmentState.Ready when isPeerMessage:
                    await DeliverPeerAttachment(container, inboxPath, tenantId, senderClientId, attachmentId);
                    break;
                case AttachmentState.Ready:
                    // Attachment is ready! Generate SAS URL and forward to Cloud queue
                    sasUri = CreateAttachmentInboxSasUri(container, inboxPath);
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

        // A message to a peer goes on to the clients queue, which delivers it to the receiver.
        await senderCache.GetSender(isPeerMessage
                ? serviceBusOptions.Value.Queues.Clients
                : serviceBusOptions.Value.Queues.Cloud)
            .SendMessageAsync(readyMessage);
        await messageActions.CompleteMessageAsync(message);
    }

    private static Uri CreateAttachmentInboxSasUri(BlobContainerClient container, string inboxPath)
    {
        var blobClient = container.GetBlobClient(inboxPath);
        // Short-lived: this SAS travels inside the message (ASB application property) and would
        // otherwise sit in the DLQ / telemetry as a long-lived delete capability. It is minted
        // only once the attachment is Ready and is consumed by the cloud's DataBusIncomingStep
        // shortly after; two hours is generous headroom. The containers' TTL policy reaps the blob.
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

    /// <summary>
    /// What is left of the message's time to live, for the storage queue it waits in next; a
    /// request to a client that is offline should not run days later. <c>null</c>: the queue's
    /// default (7 days).
    /// </summary>
    private static TimeSpan? StorageTimeToLive(ServiceBusReceivedMessage message)
    {
        if (message.TimeToLive >= TimeSpan.FromDays(7))
            return null;
        var left = message.ExpiresAt - DateTimeOffset.UtcNow;
        return left > TimeSpan.FromSeconds(1) ? left : TimeSpan.FromSeconds(1);
    }

    private async Task ForwardToClientQueue(string tenantId, string clientId, BoteMessage boteMessage,
        TimeSpan? timeToLive = null, bool createQueue = true)
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
            receipt = await queueClient.SendMessageAsync(payload, timeToLive: timeToLive);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404 && createQueue)
        {
            await queueClient.CreateIfNotExistsAsync();
            receipt = await queueClient.SendMessageAsync(payload, timeToLive: timeToLive);
        }
        workerMetrics.OutStorageSend.Record(WorkerMetrics.MsSince(send));

        // Notify specific client via SignalR
        var notify = Stopwatch.GetTimestamp();
        await Clients.User(BoteClientNames.SignalRUserId(tenantId, clientId)).NewMessage(receipt.Value.MessageId);
        workerMetrics.OutSignalRNotify.Record(WorkerMetrics.MsSince(notify));
        workerMetrics.OutForwardTotal.Record(WorkerMetrics.MsSince(fwdStart));
    }

}