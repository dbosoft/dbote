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

namespace SuperBus.BasicIdentityProvider;

public class TokenIssuer(
    ITokenCredentialsProvider tokenCredentialsProvider,
    IOptions<TokenIssuerOptions> options,
    IConnectorRepository connectorRepository,
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

        // Extract connector ID from standard sub claim - tenant comes from URL route
        if (!assertionToken.TryGetValue("sub", out string connectorId) || string.IsNullOrEmpty(connectorId)
            || assertionToken.Subject != connectorId)
        {
            logger.LogWarning("Invalid client assertion: missing or invalid sub claim");
            return new BadRequestResult();
        }

        var connector = await connectorRepository.GetById(tenantId, connectorId);
        if (connector == null)
        {
            logger.LogWarning("Connector with tenant ID '{TenantId}' and connector ID '{ConnectorId}' not found.", tenantId, connectorId);
            return new BadRequestResult();
        }

        var connectorEcdsa = ECDsa.Create();
        connectorEcdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(connector.PublicKey), out _);
        var connectorKey = new ECDsaSecurityKey(connectorEcdsa)
        {
            KeyId = connectorId,
        };

        logger.LogInformation("Expected audience: {Audience}", options.Value.Authority);

        // Validate client assertion: issuer must be connector ID, audience must be authority
        var assertionTokenResult = await handler.ValidateTokenAsync(assertionToken, new TokenValidationParameters()
        {
            ValidIssuer = connectorId,
            ValidAudience = options.Value.Authority,
            IssuerSigningKey = connectorKey,
        });

        if (!assertionTokenResult.IsValid)
        {
            logger.LogInformation(assertionTokenResult.Exception,
                "Validation of client assertion token for connector with tenant ID '{TenantId}' and connector ID '{ConnectorId}' failed:",
                tenantId, connectorId);
            return new BadRequestResult();
        }

        var accessToken = CreateAccessToken(tenantId, connectorId);
        return new JsonResult(new Dictionary<string, object>()
        {
            [OpenIdConnectParameterNames.AccessToken] = accessToken,
            [OpenIdConnectParameterNames.TokenType] = "Bearer",
            [OpenIdConnectParameterNames.ExpiresIn] = 900,
            [OpenIdConnectParameterNames.Scope] = "superbus",
        });
    }

    private string CreateAccessToken(string tenantId, string connectorId)
    {
        var tokenCredentials = tokenCredentialsProvider.GetSigningCredentials();
        var handler = new JsonWebTokenHandler();

        var subject = new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, connectorId),
            new Claim("tid", tenantId),  // Azure AD standard tenant ID claim
            new Claim("scope", "superbus"),
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
