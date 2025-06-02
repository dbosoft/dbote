using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using SuperBus.Transport.Abstractions;
using SuperBus.Management.Persistence.Entities;
using SuperBus.Management.Persistence.Repositories;

namespace SuperBus.SuperBusWorker;

public class OpenId(
    ITokenCredentialsProvider tokenCredentialsProvider,
    IOptions<OpenIdOptions> openIdOptions,
    IConnectorRepository connectorRepository)
{
    // TODO use HttRequest (aspnet core integration) or HttpRequestData (azure functions integration) as parameter type
    [Function("token")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> GetToken(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")]
        HttpRequest req)
    {
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

        if (!assertionToken.TryGetValue(ClaimNames.TenantId, out string tenantId)
            || !assertionToken.TryGetValue(ClaimNames.ConnectorId, out string connectorId)
            || assertionToken.Subject != $"{tenantId}-{connectorId}")
        {
            return new BadRequestResult();
        }
        
        var result = await connectorRepository.GetById(tenantId, connectorId);
        var optionalConnectorEntity = result.Match(
            Right: r => r,
            Left: e => e.ToException().Rethrow<Option<ConnectorEntity>>());
        if (optionalConnectorEntity.IsNone)
            return new BadRequestResult();

        var connectorEntity = optionalConnectorEntity.ValueUnsafe();

        var connectorEcdsa = ECDsa.Create();
        connectorEcdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(connectorEntity.PublicKey), out _);
        var connectorKey = new ECDsaSecurityKey(connectorEcdsa);

        var assertionTokenResult = await handler.ValidateTokenAsync(assertionToken, new TokenValidationParameters()
        {
            // TODO Fix issuer and audience
            ValidIssuer = assertionToken.Subject,
            ValidAudience = openIdOptions.Value.Authority,
            IssuerSigningKey = connectorKey,
        });

        if (!assertionTokenResult.IsValid)
            return new BadRequestResult();

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
        var tokenCredentials = tokenCredentialsProvider.GetCredentials();
        var handler = new JsonWebTokenHandler();

        var subject = new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, $"{tenantId}-{connectorId}"),
            new Claim(ClaimNames.TenantId, tenantId),
            new Claim(ClaimNames.ConnectorId, connectorId),
            new Claim("scope", "superbus"),
        ]);

        // TODO define authority and audience
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = subject,
            Expires = DateTime.UtcNow.AddMinutes(15),
            SigningCredentials = tokenCredentials,
            Audience = openIdOptions.Value.Authority,
            Issuer = openIdOptions.Value.Authority,
        };

        return handler.CreateToken(tokenDescriptor);
    }
}
