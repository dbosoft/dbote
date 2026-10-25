using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Dbosoft.Bote.Client.KeyGenerator;

public class KeyGenerator
{
    public KeyInfo GenerateKey()
    {
        using var key = ECDsa.Create(ECCurve.CreateFromFriendlyName("secp256r1"));
        var securityKey = new ECDsaSecurityKey(key);
        // The id the client signs with when none is configured: the key's thumbprint.
        var keyId = Base64UrlEncoder.Encode(JsonWebKeyConverter.ConvertFromECDsaSecurityKey(securityKey).ComputeJwkThumbprint());
        securityKey.KeyId = keyId;

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
