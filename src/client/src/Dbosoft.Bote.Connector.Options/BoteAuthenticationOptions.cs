namespace Dbosoft.Bote.Connector.Options;

public class BoteAuthenticationOptions
{
    public BoteAuthenticationType AuthenticationType { get; set; }

    public string KeyId { get; set; }

    public string? SigningKey { get; set; }

    public string? Authority { get; set; }

    public string? TokenEndpoint { get; set; }

    public string? Scope { get; set; }
}
