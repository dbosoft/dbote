using System.Security.Cryptography;
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

    public TokenCredentialsProvider()
    {
        // Generate ephemeral ECDSA key (P-256)
        _ecdsa = ECDsa.Create(ECCurve.CreateFromFriendlyName("secp256r1"));
        var securityKey = new ECDsaSecurityKey(_ecdsa);
        _credentials = new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256);
    }

    public SigningCredentials GetSigningCredentials() => _credentials;

    public ECDsa GetECDsa() => _ecdsa;
}
