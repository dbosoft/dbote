using System.Globalization;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Dbosoft.Bote.Primitives;

namespace Dbosoft.Bote.Cloud;

/// <summary>
/// Sends messages to bote clients without Rebus: to one client, or to every client of a tenant
/// that subscribed to a topic. Messages go to the gateway's clients queue in the form the
/// worker and the Rebus clients expect.
/// </summary>
public sealed class BoteCloudSender
{
    private readonly ServiceBusSender _clientsQueue;
    private readonly string? _returnAddress;
    private readonly BlobContainerClient? _outbox;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly TimeProvider _clock;

    /// <param name="clientsQueue">A sender for the gateway's clients queue.</param>
    /// <param name="returnAddress">
    /// The gateway's cloud queue, where a client's <c>bus.Reply</c> goes. Without it a client
    /// cannot reply, as Rebus needs the return address of the message it replies to.
    /// </param>
    /// <param name="outbox">The outbox container of the gateway storage; needed only to send attachments.</param>
    /// <param name="serializerOptions">Defaults to the settings of Rebus' System.Text.Json serializer.</param>
    /// <param name="clock">For the sent time.</param>
    public BoteCloudSender(
        ServiceBusSender clientsQueue,
        string? returnAddress = null,
        BlobContainerClient? outbox = null,
        JsonSerializerOptions? serializerOptions = null,
        TimeProvider? clock = null)
    {
        _clientsQueue = clientsQueue;
        _returnAddress = returnAddress;
        _outbox = outbox;
        _serializerOptions = serializerOptions ?? RebusWireFormat.DefaultSerializerOptions;
        _clock = clock ?? TimeProvider.System;
    }

    /// <param name="serviceBus">A client of the gateway's Service Bus namespace.</param>
    /// <param name="clientsQueue">The name of the gateway's clients queue.</param>
    /// <param name="cloudQueue">The name of the gateway's cloud queue, where clients reply to.</param>
    /// <param name="storage">The gateway storage; needed only to send attachments.</param>
    /// <param name="serializerOptions">Defaults to the settings of Rebus' System.Text.Json serializer.</param>
    public static BoteCloudSender Create(
        ServiceBusClient serviceBus,
        string clientsQueue,
        string cloudQueue,
        BlobServiceClient? storage = null,
        JsonSerializerOptions? serializerOptions = null) =>
        new(serviceBus.CreateSender(clientsQueue),
            cloudQueue,
            storage?.GetBlobContainerClient(BoteStorageConstants.Outbox),
            serializerOptions);

    public async Task SendToClientAsync<TMessage>(string tenantId, string clientId, TMessage message,
        BoteSendOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        await SendAsync(tenantId, new Dictionary<string, string> { [BoteHeaders.ClientId] = clientId },
            message, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends to every client of the tenant subscribed to the topic. Only clients subscribed when
    /// the worker delivers receive it; nothing is kept for others.
    /// </summary>
    public async Task BroadcastAsync<TMessage>(string tenantId, string topic, TMessage message,
        BoteSendOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        await SendAsync(tenantId, new Dictionary<string, string> { [BoteHeaders.Topic] = topic },
            message, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Answers the client that sent <paramref name="request"/>, correlated with it.
    /// </summary>
    public async Task ReplyAsync<TMessage>(BoteIncomingMessage request, TMessage message,
        BoteSendOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (request.ClientId is null)
            throw new InvalidOperationException($"The message {request.MessageId} has no client to reply to.");

        // As Rebus does: a reply keeps the conversation's correlation id, which starts with the
        // id of its first message, and counts one up in the conversation.
        var sequence = int.TryParse(request.Headers.GetValueOrDefault(RebusWireFormat.CorrelationSequenceHeader),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out var incoming) ? incoming + 1 : 1;
        var correlated = new BoteSendOptions
        {
            MessageId = options?.MessageId,
            CorrelationId = options?.CorrelationId ?? request.CorrelationId ?? request.MessageId,
            CorrelationSequence = sequence,
            Headers = options?.Headers,
            Attachment = options?.Attachment,
            TimeToLive = options?.TimeToLive,
            DeliverAt = options?.DeliverAt,
        };
        await SendToClientAsync(request.TenantId, request.ClientId, message, correlated, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SendAsync<TMessage>(string tenantId, Dictionary<string, string> routing, TMessage message,
        BoteSendOptions? options, CancellationToken cancellationToken)
    {
        var serviceBusMessage = CreateMessage(tenantId, routing, message, options);

        // The attachment must be in the outbox before the message that refers to it is sent,
        // or the worker finds nothing to copy and reschedules the message until it gives up.
        if (options?.Attachment is { } attachment)
            await StageAttachmentAsync(tenantId, attachment, cancellationToken).ConfigureAwait(false);

        await _clientsQueue.SendMessageAsync(serviceBusMessage, cancellationToken).ConfigureAwait(false);
    }

    internal ServiceBusMessage CreateMessage<TMessage>(string tenantId, Dictionary<string, string> routing,
        TMessage message, BoteSendOptions? options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(message);
        if (tenantId.IndexOfAny(['/', '\\']) >= 0)
            throw new ArgumentException("A tenant id cannot contain a path separator.", nameof(tenantId));

        var type = message.GetType();
        var messageId = options?.MessageId ?? Guid.NewGuid().ToString();
        var serviceBusMessage = new ServiceBusMessage(JsonSerializer.SerializeToUtf8Bytes(message, type, _serializerOptions))
        {
            MessageId = messageId,
            ContentType = RebusWireFormat.JsonContentType,
            // As Rebus does: a message that starts a conversation is correlated with itself.
            CorrelationId = options?.CorrelationId ?? messageId,
        };
        if (options?.TimeToLive is { } timeToLive)
            serviceBusMessage.TimeToLive = timeToLive;
        if (options?.DeliverAt is { } deliverAt)
            serviceBusMessage.ScheduledEnqueueTime = deliverAt;

        var properties = serviceBusMessage.ApplicationProperties;
        if (options?.Headers is { } headers)
        {
            foreach (var (key, value) in headers)
            {
                if (key.StartsWith("dbote-", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"The header '{key}' is set by the sender only.", nameof(options));
                properties[key] = value;
            }
        }

        properties[RebusWireFormat.TypeHeader] = RebusWireFormat.TypeName(type);
        properties[RebusWireFormat.SentTimeHeader] = _clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);
        properties[RebusWireFormat.IntentHeader] = RebusWireFormat.PointToPointIntent;
        properties[RebusWireFormat.CorrelationSequenceHeader] =
            (options?.CorrelationSequence ?? 0).ToString(CultureInfo.InvariantCulture);
        if (_returnAddress is not null)
            properties[RebusWireFormat.ReturnAddressHeader] = _returnAddress;
        properties[BoteHeaders.TenantId] = tenantId;
        foreach (var (key, value) in routing)
            properties[key] = value;
        if (options?.Attachment is { } attachment)
            properties[BoteHeaders.AttachmentId] = attachment.Id;

        return serviceBusMessage;
    }

    private async Task StageAttachmentAsync(string tenantId, BoteOutgoingAttachment attachment,
        CancellationToken cancellationToken)
    {
        if (_outbox is null)
            throw new InvalidOperationException("Sending an attachment needs the gateway storage's outbox.");

        var metadata = attachment.Metadata is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(attachment.Metadata);
        metadata[BoteStorageConstants.Metadata.TenantId] = tenantId;

        if (attachment.Content.CanSeek)
            attachment.Content.Position = 0;

        // Overwrites, so a retried send stages the attachment again.
        await _outbox.GetBlobClient($"{tenantId}/{attachment.Id}")
            .UploadAsync(attachment.Content, new BlobUploadOptions { Metadata = metadata }, cancellationToken)
            .ConfigureAwait(false);
    }
}
