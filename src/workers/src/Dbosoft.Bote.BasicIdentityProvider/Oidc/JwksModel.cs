using System.Text.Json.Serialization;

namespace Dbosoft.Bote.BasicIdentityProvider.Oidc;

public class JwksModel
{
    [JsonPropertyName("keys")]
    public ICollection<JwksKeyModel> Keys { get; set; } = new List<JwksKeyModel>();
}
