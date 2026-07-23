namespace Dbosoft.Bote.Transport.Abstractions;

public class BoteMessage
{
    public IDictionary<string, string?> Headers { get; set; } = new Dictionary<string, string?>();

    /// <summary>
    /// The raw message body (UTF-8). A <see cref="ReadOnlyMemory{T}"/> so the worker hot path can
    /// wrap the incoming Service Bus buffer without copying (and never decodes it to a UTF-16 string
    /// just to re-encode it) - the body is opaque to the gateway.
    /// </summary>
    public ReadOnlyMemory<byte> Body { get; set; }
}
