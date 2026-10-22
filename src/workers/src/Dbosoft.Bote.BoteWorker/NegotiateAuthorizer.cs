using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Dbosoft.Bote.BoteWorker;

/// <summary>
/// Decides whether a negotiate request may open a SignalR connection, and as whom. The identity
/// comes from the validated access token only; it is what every later hub call trusts.
/// </summary>
internal sealed class NegotiateAuthorizer(
    ITokenValidationService tokenValidationService,
    IOptions<OpenIdOptions> openIdOptions,
    ILogger<NegotiateAuthorizer> logger)
{
    public async Task<NegotiateDecision> Authorize(string? authorizationHeader, CancellationToken cancellationToken)
    {
        if (authorizationHeader is null || !authorizationHeader.StartsWith("Bearer ", StringComparison.Ordinal))
            return NegotiateDecision.Deny(HttpStatusCode.Unauthorized);

        var jwt = authorizationHeader["Bearer ".Length..];

        var validationResult = await tokenValidationService.ValidateAccessToken(jwt, cancellationToken);

        if (!validationResult.IsValid)
        {
            logger.LogInformation(validationResult.Exception, "Token validation failed");
            return NegotiateDecision.Deny(HttpStatusCode.Unauthorized);
        }

        var token = (JsonWebToken)validationResult.SecurityToken;

        // Extract client identity from standard JWT claims
        if (!ClientIdentity.TryExtract(token, out var identity) || identity is null)
        {
            logger.LogWarning("Failed to extract client identity from token");
            return NegotiateDecision.Deny(HttpStatusCode.Unauthorized);
        }

        // Validate required scope (no fallback - must be configured)
        if (string.IsNullOrEmpty(openIdOptions.Value.RequiredScope))
        {
            logger.LogError("RequiredScope is not configured in OpenIdOptions");
            return NegotiateDecision.Deny(HttpStatusCode.InternalServerError);
        }

        if (!token.TryGetValue("scope", out string scope) || scope != openIdOptions.Value.RequiredScope)
        {
            logger.LogWarning("Invalid or missing scope. Expected: {ExpectedScope}, Actual: {ActualScope}",
                openIdOptions.Value.RequiredScope, scope);
            return NegotiateDecision.Deny(HttpStatusCode.Forbidden);
        }

        return NegotiateDecision.Allow(identity);
    }
}

internal sealed record NegotiateDecision(ClientIdentity? Identity, HttpStatusCode Status)
{
    public static NegotiateDecision Allow(ClientIdentity identity) => new(identity, HttpStatusCode.OK);

    public static NegotiateDecision Deny(HttpStatusCode status) => new(null, status);
}
