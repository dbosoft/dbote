namespace Dbosoft.Bote.Transport.Abstractions;

public static class ClaimNames
{
    public static readonly string TenantId = "bote_tenant_id";
    public static readonly string ClientId = "bote_client_id";
    /// <summary>The clients of its tenant the client may send to, as <c>BotePeers.Format</c> writes them.</summary>
    public static readonly string Peers = "bote_peers";
}
