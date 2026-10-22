namespace Dbosoft.Bote.BoteWorker;

public class OpenIdOptions
{
    public string Authority { get; set; } = string.Empty;
    public string JwksUri { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string RequiredScope { get; set; } = string.Empty;

    /// <summary>
    /// A client whose token carries this scope as well may send to the other clients of its
    /// tenant. Unset, clients only talk to the cloud.
    /// </summary>
    public string? PeerScope { get; set; }
}
