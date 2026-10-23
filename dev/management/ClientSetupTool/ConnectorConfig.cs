namespace ClientSetupTool;

public class ClientConfig
{
    public string ClientId { get; set; }

    public string TenantId { get; set; }

    public string PublicKey { get; set; }

    /// <summary>Scopes besides bote, e.g. bote.peer for a client that talks to other clients.</summary>
    public string[]? Scopes { get; set; }

    /// <summary>With the peer scope, the clients of the tenant it may send to; <c>*</c> for all.</summary>
    public string[]? Peers { get; set; }
}
