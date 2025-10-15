using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Dbosoft.Bote.BasicIdentityProvider;

public class TokenIssuer(
    ITokenCredentialsProvider tokenCredentialsProvider,
    IOptions<TokenIssuerOptions> options,
    IClientRepository clientRepository,
    ILogger<TokenIssuer> logger)
{
    [Function("token")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> GetToken(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "{tenantId}/token")]
        HttpRequest req,
        string tenantId)
    {
        if (string.IsNullOrEmpty(tenantId))
        {
            logger.LogWarning("Tenant ID not provided in route");
            return new BadRequestResult();
        }

        var form = await req.ReadFormAsync();
        if (!form.TryGetValue(OpenIdConnectParameterNames.GrantType, out var grantType)
            || grantType != OpenIdConnectGrantTypes.ClientCredentials
            || !form.TryGetValue(OpenIdConnectParameterNames.ClientAssertionType, out var clientAssertionType)
            || clientAssertionType != "urn:ietf:params:oauth:client-assertion-type:jwt-bearer"
            || !form.TryGetValue(OpenIdConnectParameterNames.ClientAssertion, out var clientAssertion))
        {
            return new BadRequestResult();
        }

        var handler = new JsonWebTokenHandler();
        var assertionToken = handler.ReadJsonWebToken(clientAssertion);

        // Extract client ID from standard sub claim - tenant comes from URL route
        if (!assertionToken.TryGetValue("sub", out string clientId) || string.IsNullOrEmpty(clientId)
            || assertionToken.Subject != clientId)
        {
            logger.LogWarning("Invalid client assertion: missing or invalid sub claim");
            return new BadRequestResult();
        }

        var client = await clientRepository.GetById(tenantId, clientId);
        if (client == null)
        {
            logger.LogWarning("Client with tenant ID '{TenantId}' and client ID '{ClientId}' not found.", tenantId, clientId);
            return new BadRequestResult();
        }

        var clientEcdsa = ECDsa.Create();
        clientEcdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(client.PublicKey), out _);
        var clientKey = new ECDsaSecurityKey(clientEcdsa)
        {
            KeyId = clientId,
        };

        logger.LogInformation("Expected audience: {Audience}", options.Value.Authority);

        // Validate client assertion: issuer must be client  ID, audience must be authority
        var assertionTokenResult = await handler.ValidateTokenAsync(assertionToken, new TokenValidationParameters()
        {
            ValidIssuer = clientId,
            ValidAudience = options.Value.Authority,
            IssuerSigningKey = clientKey,
        });

        if (!assertionTokenResult.IsValid)
        {
            logger.LogInformation(assertionTokenResult.Exception,
                "Validation of client assertion token for client with tenant ID '{TenantId}' and client ID '{ClientId}' failed:",
                tenantId, clientId);
            return new BadRequestResult();
        }

        var accessToken = CreateAccessToken(tenantId, clientId);
        return new JsonResult(new Dictionary<string, object>()
        {
            [OpenIdConnectParameterNames.AccessToken] = accessToken,
            [OpenIdConnectParameterNames.TokenType] = "Bearer",
            [OpenIdConnectParameterNames.ExpiresIn] = 900,
            [OpenIdConnectParameterNames.Scope] = "bote",
        });
    }

    private string CreateAccessToken(string tenantId, string clientId)
    {
        var tokenCredentials = tokenCredentialsProvider.GetSigningCredentials();
        var handler = new JsonWebTokenHandler();

        var subject = new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, clientId),
            new Claim("tid", tenantId), 
            new Claim("scope", "bote"),
        ]);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = subject,
            Expires = DateTime.UtcNow.AddMinutes(15),
            SigningCredentials = tokenCredentials,
            Audience = options.Value.Audience,
            Issuer = options.Value.Authority,
        };

        return handler.CreateToken(tokenDescriptor);
    }
}

public class TokenIssuerOptions
{
    public string Authority { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
}
