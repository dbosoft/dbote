using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace SuperBus.SuperBusWorker;

public interface ITokenCredentialsProvider
{
    SigningCredentials GetCredentials();
}

public class TokenCredentialsProvider : ITokenCredentialsProvider
{
    private readonly SigningCredentials _credentials;

    public TokenCredentialsProvider()
    {
        // TODO implement key vault provider
        var ecdsa = ECDsa.Create(ECCurve.CreateFromFriendlyName("secp256r1"));
        var securityKey = new ECDsaSecurityKey(ecdsa);
        _credentials = new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256);
    }

    public SigningCredentials GetCredentials() => _credentials;
}
