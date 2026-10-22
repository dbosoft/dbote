using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Dbosoft.Bote.Primitives;

namespace Dbosoft.Bote.Cloud;

/// <summary>
/// A client message as the worker put it on the cloud queue, for a receiver that takes it from
/// the queue itself (e.g. an Azure Functions Service Bus trigger) instead of through Rebus.
/// The tenant and client come from the worker, which sets them from the client's verified
/// token and refuses any the client sets itself.
/// </summary>
public sealed class BoteIncomingMessage
{
    private BoteIncomingMessage(
        string tenantId,
        string? clientId,
        string messageId,
        string? correlationId,
        string? messageType,
        string? contentType,
        IReadOnlyDictionary<string, string> headers,
        BinaryData body,
        BoteIncomingAttachment? attachment)
    {
        TenantId = tenantId;
        ClientId = clientId;
        MessageId = messageId;
        CorrelationId = correlationId;
        MessageType = messageType;
        ContentType = contentType;
        Headers = headers;
        Body = body;
        Attachment = attachment;
    }

    public string TenantId { get; }

    /// <summary>The sending client; set on every message a client sent through the worker.</summary>
    public string? ClientId { get; }

    public string MessageId { get; }

    public string? CorrelationId { get; }

    /// <summary>The Rebus type name of the body, see <see cref="RebusWireFormat.TypeName"/>.</summary>
    public string? MessageType { get; }

    public string? ContentType { get; }

    /// <summary>All headers of the message, the Rebus and bote ones included.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    public BinaryData Body { get; }

    /// <summary>The DataBus attachment the client sent with the message, if any.</summary>
    public BoteIncomingAttachment? Attachment { get; }

    /// <summary>
    /// Reads a message from the cloud queue.
    /// </summary>
    /// <param name="message">The message as the Service Bus trigger or receiver hands it over.</param>
    /// <param name="storage">
    /// The gateway storage. Needed only for messages with an attachment: the worker hands the
    /// attachment over as a SAS URI into this account, and one into any other account is refused.
    /// </param>
    /// <exception cref="InvalidOperationException">The message has no tenant, or an attachment that cannot be accepted.</exception>
    public static BoteIncomingMessage From(ServiceBusReceivedMessage message, BlobServiceClient? storage = null) =>
        From(message, storage?.Uri);

    /// <inheritdoc cref="From(ServiceBusReceivedMessage, BlobServiceClient?)"/>
    /// <param name="message">The message as the Service Bus trigger or receiver hands it over.</param>
    /// <param name="trustedStorageEndpoint">The blob endpoint of the gateway storage.</param>
    public static BoteIncomingMessage From(ServiceBusReceivedMessage message, Uri? trustedStorageEndpoint)
    {
        var headers = message.ApplicationProperties
            .Where(p => p.Value is not null)
            .ToDictionary(p => p.Key, p => Convert.ToString(p.Value, System.Globalization.CultureInfo.InvariantCulture)!);

        // Rebus keeps these as native Service Bus properties; present them as headers, too.
        headers[RebusWireFormat.MessageIdHeader] = message.MessageId;
        if (!string.IsNullOrEmpty(message.ContentType))
            headers[RebusWireFormat.ContentTypeHeader] = message.ContentType;
        if (!string.IsNullOrEmpty(message.CorrelationId))
            headers[RebusWireFormat.CorrelationIdHeader] = message.CorrelationId;

        if (!headers.TryGetValue(BoteHeaders.TenantId, out var tenantId) || string.IsNullOrEmpty(tenantId))
            throw new InvalidOperationException($"The message {message.MessageId} has no tenant ({BoteHeaders.TenantId}).");

        return new BoteIncomingMessage(
            tenantId,
            headers.GetValueOrDefault(BoteHeaders.ClientId),
            message.MessageId,
            headers.GetValueOrDefault(RebusWireFormat.CorrelationIdHeader),
            headers.GetValueOrDefault(RebusWireFormat.TypeHeader),
            headers.GetValueOrDefault(RebusWireFormat.ContentTypeHeader),
            headers,
            message.Body,
            ReadAttachment(headers, trustedStorageEndpoint));
    }

    /// <summary>Whether the body is a <typeparamref name="TMessage"/>, by the type name the client sent.</summary>
    public bool Is<TMessage>() => MessageType == RebusWireFormat.TypeName(typeof(TMessage));

    /// <summary>Reads the body as <typeparamref name="TMessage"/>.</summary>
    /// <param name="options">Defaults to the settings of Rebus' System.Text.Json serializer.</param>
    public TMessage Deserialize<TMessage>(JsonSerializerOptions? options = null) =>
        (TMessage)Deserialize(typeof(TMessage), options);

    /// <summary>Reads the body as <paramref name="type"/>.</summary>
    /// <param name="type">The type to read.</param>
    /// <param name="options">Defaults to the settings of Rebus' System.Text.Json serializer.</param>
    public object Deserialize(Type type, JsonSerializerOptions? options = null)
    {
        if (ContentType is not null && !ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The message {MessageId} is not JSON but '{ContentType}'.");

        return JsonSerializer.Deserialize(Body.ToMemory().Span, type, options ?? RebusWireFormat.DefaultSerializerOptions)
               ?? throw new InvalidOperationException($"The message {MessageId} has an empty body.");
    }

    private static BoteIncomingAttachment? ReadAttachment(Dictionary<string, string> headers, Uri? trustedStorageEndpoint)
    {
        // The worker sets the SAS header only once the attachment is in the inbox; a message
        // still waiting for its copy never reaches the cloud queue.
        if (!headers.TryGetValue(BoteHeaders.DataBusInboxSas, out var sas))
            return null;

        if (!headers.TryGetValue(BoteHeaders.AttachmentId, out var attachmentId))
            throw new InvalidOperationException("The message has an attachment SAS but no attachment id.");

        if (trustedStorageEndpoint is null)
            throw new InvalidOperationException(
                "The message has an attachment; pass the gateway storage to accept it.");

        var sasUri = new Uri(sas);
        if (!DataBusSasUriValidator.IsTrustedEndpoint(sasUri, trustedStorageEndpoint))
            throw new InvalidOperationException(
                $"The attachment SAS points at an untrusted host: {sasUri.Host}");

        return new BoteIncomingAttachment(attachmentId, new BlobClient(sasUri));
    }
}
