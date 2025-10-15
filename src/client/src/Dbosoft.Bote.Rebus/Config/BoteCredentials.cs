using Microsoft.IdentityModel.Tokens;

namespace Dbosoft.Bote.Rebus.Config;

public class BoteCredentials
{
    public string? TenantId { get; set; }

    public string? ClientId { get; set; }

    public SecurityKey? SigningKey { get; set; }

    public string? Authority { get; set; }

    public string? TokenEndpoint { get; set; }

    public string? Scope { get; set; }
}
