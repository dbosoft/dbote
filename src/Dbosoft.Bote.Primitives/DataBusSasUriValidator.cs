namespace Dbosoft.Bote.Primitives;

/// <summary>
/// Defense-in-depth against SSRF: the cloud downloads a DataBus attachment from a SAS URL
/// carried in a message header. That header is worker-issued and the send path already
/// rejects client-set copies of it, but the cloud must still refuse to fetch from anywhere
/// other than its own storage account - otherwise a single bad/forged header would make the
/// cloud issue an authenticated request to an attacker-chosen host.
/// </summary>
public static class DataBusSasUriValidator
{
    /// <summary>
    /// Whether <paramref name="sasUri"/> targets the same storage endpoint as
    /// <paramref name="trustedEndpoint"/> - same scheme, host and port. Both are derived from
    /// the same storage account configuration, so a legitimate inbox SAS always matches; a
    /// mismatched host/port (SSRF) or a downgraded scheme (e.g. a read+delete SAS requested
    /// over plain http when the account is https) is rejected.
    /// </summary>
    public static bool IsTrustedEndpoint(Uri sasUri, Uri trustedEndpoint)
    {
        if (!sasUri.IsAbsoluteUri)
            return false;

        return string.Equals(sasUri.Scheme, trustedEndpoint.Scheme, StringComparison.OrdinalIgnoreCase)
               && string.Equals(sasUri.Host, trustedEndpoint.Host, StringComparison.OrdinalIgnoreCase)
               && sasUri.Port == trustedEndpoint.Port;
    }
}
