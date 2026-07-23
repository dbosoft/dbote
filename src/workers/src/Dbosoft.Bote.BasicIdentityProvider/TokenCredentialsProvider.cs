using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Dbosoft.Bote.BasicIdentityProvider;

public interface ITokenCredentialsProvider
{
    SigningCredentials GetSigningCredentials();
    ECDsa GetECDsa();
}

public class TokenCredentialsProvider : ITokenCredentialsProvider
{
    private readonly SigningCredentials _credentials;
    private readonly ECDsa _ecdsa;

    public TokenCredentialsProvider(IConfiguration configuration, ILogger<TokenCredentialsProvider> logger)
    {
        // A stable P-256 signing key (PEM) shared via Key Vault so every instance/cold start signs
        // with the same key - required once the provider runs on more than one instance (Flex
        // Consumption). Without it each instance generates its own ephemeral key, and because the
        // JWKS kid is fixed the validator caches one key and rejects tokens signed by the others.
        var pem = configuration["BasicIdentityProvider:SigningKeyPem"];
        if (!string.IsNullOrWhiteSpace(pem))
        {
            _ecdsa = ECDsa.Create();
            _ecdsa.ImportFromPem(pem);
            logger.LogInformation("Using stable ECDSA signing key from configuration (Key Vault).");
        }
        else
        {
            // Local/dev fallback: ephemeral key. NOT safe across multiple instances.
            _ecdsa = ECDsa.Create(ECCurve.CreateFromFriendlyName("secp256r1"));
            logger.LogWarning("No BasicIdentityProvider:SigningKeyPem configured - using an EPHEMERAL " +
                              "signing key (single-instance/dev only).");
        }

        var securityKey = new ECDsaSecurityKey(_ecdsa);
        _credentials = new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256);
    }

    public SigningCredentials GetSigningCredentials() => _credentials;

    public ECDsa GetECDsa() => _ecdsa;
}
