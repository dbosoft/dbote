namespace Dbosoft.Bote.Cloud;

public sealed class BoteSendOptions
{
    /// <summary>Defaults to a new id. Set it to make a resend detectable as a duplicate.</summary>
    public string? MessageId { get; init; }

    /// <summary>Defaults to the message id, which starts a conversation.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>The position in the conversation; <see cref="BoteCloudSender.ReplyAsync{TMessage}"/> sets it.</summary>
    public int? CorrelationSequence { get; init; }

    /// <summary>Additional headers; the <c>dbote-*</c> headers are set by the sender only.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>A DataBus attachment to stage in the outbox before the message is sent.</summary>
    public BoteOutgoingAttachment? Attachment { get; init; }

    public TimeSpan? TimeToLive { get; init; }

    /// <summary>Delivers the message no earlier than this.</summary>
    public DateTimeOffset? DeliverAt { get; init; }
}

/// <summary>
/// An attachment for a client. Its <see cref="Id"/> is what the message refers to it by; the
/// client reads it through its bote DataBus with that id. Give it a seekable stream: a retried
/// send uploads the content again from the start.
/// </summary>
public sealed class BoteOutgoingAttachment
{
    public BoteOutgoingAttachment(string id, Stream content, IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(content);
        if (id.IndexOfAny(['/', '\\']) >= 0 || id.Contains(".."))
            throw new ArgumentException("An attachment id cannot contain a path.", nameof(id));
        Id = id;
        Content = content;
        Metadata = metadata;
    }

    /// <summary>An attachment with a new id.</summary>
    public static BoteOutgoingAttachment Create(Stream content, IReadOnlyDictionary<string, string>? metadata = null) =>
        new(Guid.NewGuid().ToString(), content, metadata);

    public string Id { get; }

    public Stream Content { get; }

    public IReadOnlyDictionary<string, string>? Metadata { get; }
}
