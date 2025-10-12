using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.IdentityModel.Tokens;
using SuperBus.BasicIdentityProvider.Oidc;

namespace SuperBus.BasicIdentityProvider;

[PublicAPI]
public class JwksEndpoint(ITokenCredentialsProvider tokenCredentialsProvider)
{
    [Function("jwks")]
    public IActionResult GetJwks(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = ".well-known/jwks.json")]
        HttpRequest req)
    {
        var ecdsa = tokenCredentialsProvider.GetECDsa();
        var parameters = ecdsa.ExportParameters(false); // Export public key only

        // Convert X and Y coordinates to base64url
        var x = Base64UrlEncoder.Encode(parameters.Q.X!);
        var y = Base64UrlEncoder.Encode(parameters.Q.Y!);

        var jwksModel = new JwksModel
        {
            Keys = new List<JwksKeyModel>
            {
                new()
                {
                    Kty = "EC",
                    Crv = "P-256",
                    X = x,
                    Y = y,
                    Use = "sig",
                    Kid = "superbus-basic-identity-provider",
                    Alg = "ES256"
                }
            }
        };

        return new JsonResult(jwksModel);
    }
}
