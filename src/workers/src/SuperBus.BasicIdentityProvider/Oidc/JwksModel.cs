using System.Text.Json.Serialization;

namespace SuperBus.BasicIdentityProvider.Oidc;

public class JwksModel
{
    [JsonPropertyName("keys")]
    public ICollection<JwksKeyModel> Keys { get; set; } = new List<JwksKeyModel>();
}
