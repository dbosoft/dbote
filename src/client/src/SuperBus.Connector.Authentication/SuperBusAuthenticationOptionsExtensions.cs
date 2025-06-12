using Microsoft.IdentityModel.Tokens;
using SuperBus.Connector.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace SuperBus.Connector.Authentication;

public static class SuperBusAuthenticationOptionsExtensions
{
    public static SecurityKey GetSigningKey(
        this SuperBusOptions options)
    {
        if (options.Authentication.AuthenticationType != SuperBusAuthenticationType.Value)
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
            KeyId = $"{options.TenantId}-{options.ConnectorId}",
        };
    }
}
