namespace Dbosoft.Bote.BoteWorker;

public class OpenIdOptions
{
    public string Authority { get; set; } = string.Empty;
    public string JwksUri { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string RequiredScope { get; set; } = string.Empty;
}
