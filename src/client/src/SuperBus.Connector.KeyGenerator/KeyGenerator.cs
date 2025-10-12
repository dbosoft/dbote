using Microsoft.IdentityModel.Tokens;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace SuperBus.Connector.KeyGenerator;

public class KeyGenerator
{
    public KeyInfo GenerateKey()
    {
        var keyId = Guid.NewGuid().ToString();
        using var key = ECDsa.Create(ECCurve.CreateFromFriendlyName("secp256r1"));
        var securityKey = new ECDsaSecurityKey(key)
        {
            KeyId = keyId,
        };

        var jwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(securityKey);
        var publicJwkJson = (string)typeof(JsonWebKey)
            // Unfortunately, the method for serializing a JWK to JSON is not public,
            // but the method is part of shipped internal APIs and hence unlikely to change.
            .GetMethod("RepresentAsAsymmetricPublicJwk", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(jwk, null)!;

        return new KeyInfo
        {
            KeyId = keyId,
            PublicKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(publicJwkJson)),
            PrivateKey = Convert.ToBase64String(key.ExportPkcs8PrivateKey())
        };
    }
}
