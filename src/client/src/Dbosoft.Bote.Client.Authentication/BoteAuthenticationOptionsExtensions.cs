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
        return new ECDsaSecurityKey(ecdsa)
        {
            // TODO improve KID?
            KeyId = $"{options.TenantId}-{options.ClientId}",
        };
    }
}
