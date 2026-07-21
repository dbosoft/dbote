namespace Dbosoft.Bote.Primitives;

/// <summary>
/// Trust-boundary policy for headers on messages a client sends to the cloud.
///
/// Everything in the <c>dbote-*</c> namespace is worker-controlled - identity
/// (<see cref="BoteHeaders.TenantId"/>/<see cref="BoteHeaders.ClientId"/>), routing
/// (<see cref="BoteHeaders.Topic"/>), the cloud-inbox SAS
/// (<see cref="BoteHeaders.DataBusInboxSas"/>), retry tracking
/// (<see cref="BoteHeaders.ReScheduleCounter"/>), the signature
/// (<see cref="BoteHeaders.Signature"/>) - and a client must not set any of it.
/// The one exception is <see cref="BoteHeaders.AttachmentId"/>, which the client
/// legitimately sets to reference a DataBus upload. Rebus envelope headers (<c>rbs2-*</c>)
/// and any non-<c>dbote-</c> header pass through.
///
/// This is an allow-list, not a block-list, so a newly added <c>dbote-*</c> header is
/// rejected by default rather than silently spoofable until someone remembers to block it.
/// </summary>
public static class BoteHeaderPolicy
{
    private const string BoteHeaderPrefix = "dbote-";

    /// <summary>
    /// Whether a client is allowed to set the given header key on a message to the cloud.
    /// </summary>
    public static bool IsClientAllowed(string headerKey) =>
        !headerKey.StartsWith(BoteHeaderPrefix, StringComparison.OrdinalIgnoreCase)
        || headerKey.Equals(BoteHeaders.AttachmentId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the header keys a client is not allowed to set (i.e. spoof attempts).
    /// </summary>
    public static IReadOnlyList<string> DisallowedClientHeaders(IEnumerable<string> headerKeys) =>
        headerKeys.Where(k => !IsClientAllowed(k)).ToList();
}
