using System.Text.Json.Serialization;

namespace Dbosoft.Bote.BasicIdentityProvider.Oidc;

public class JwksKeyModel
{
    [JsonPropertyName("kty")]
    public string? Kty { get; set; }

    [JsonPropertyName("crv")]
    public string? Crv { get; set; }

    [JsonPropertyName("x")]
    public string? X { get; set; }

    [JsonPropertyName("y")]
    public string? Y { get; set; }

    [JsonPropertyName("use")]
    public string? Use { get; set; }

    [JsonPropertyName("kid")]
    public string? Kid { get; set; }

    [JsonPropertyName("alg")]
    public string? Alg { get; set; }
}
