using System.Security.Cryptography;
using Dbosoft.Bote.Client.Options;
using Microsoft.IdentityModel.Tokens;

namespace Dbosoft.Bote.Client.Authentication;

public static class BoteAuthenticationOptionsExtensions
{
    public static SecurityKey GetSigningKey(
        this BoteOptions options)
    {
        if (options.Authentication.AuthenticationType != BoteAuthenticationType.Value)
            throw new ArgumentException(
                $"The authentication type {options.Authentication.AuthenticationType} is not supported",
                nameof(options));

        if (string.IsNullOrWhiteSpace(options.Authentication.SigningKey))
            throw new ArgumentException(
                $"The signing key is missing",
                nameof(options));

        // TODO Do we need to create new instance of ECDsa every time? (thread safety and dispose)
        var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(options.Authentication.SigningKey), out _);
        var key = new ECDsaSecurityKey(ecdsa);
        // The identity provider finds the key by this id among the client's keys. A fixed id per
        // client (tenant and client) let a client hold one key only, so a new key could not be
        // registered beside the old one while the client still signs with the old.
        key.KeyId = string.IsNullOrWhiteSpace(options.Authentication.KeyId)
            ? Thumbprint(key)
            : options.Authentication.KeyId;
        return key;
    }

    /// <summary>
    /// The key's JWK thumbprint (RFC 7638, SHA-256, base64url): the same for whoever has the
    /// public key, so whoever registers it can name it without being told.
    /// </summary>
    public static string Thumbprint(ECDsaSecurityKey key) =>
        Base64UrlEncoder.Encode(JsonWebKeyConverter.ConvertFromECDsaSecurityKey(key).ComputeJwkThumbprint());
}
