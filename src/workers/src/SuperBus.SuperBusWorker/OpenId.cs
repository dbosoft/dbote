using Azure.Core;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SuperBus.Transport.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace SuperBus.SuperBusWorker;

public class OpenId(
    ITokenCredentialsProvider tokenCredentialsProvider,
    IOptions<SuperBusOptions> superBusOptions,
    IOptions<OpenIdOptions> openIdOptions)
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
        
        var tenantOptions = superBusOptions.Value.Tenants
            .FirstOrDefault(to => to.TenantId == tenantId && to.ConnectorId == connectorId);
        if (tenantOptions is null)
            return new BadRequestResult();

        var connectorEcdsa = ECDsa.Create();
        connectorEcdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(tenantOptions.SigningKey), out _);
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
