using System.Net;
using Dbosoft.Bote.Primitives;
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

        // OAuth scopes are a space-separated set.
        token.TryGetValue("scope", out string? scope);
        var scopes = (scope ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!scopes.Contains(openIdOptions.Value.RequiredScope, StringComparer.Ordinal))
        {
            logger.LogWarning("Invalid or missing scope. Expected: {ExpectedScope}, Actual: {ActualScope}",
                openIdOptions.Value.RequiredScope, scope);
            return NegotiateDecision.Deny(HttpStatusCode.Forbidden);
        }

        return NegotiateDecision.Allow(identity, Peers(token, scopes, identity));
    }

    // The peer scope lets a client send to other clients, the token's list says to which.
    private BotePeers Peers(JsonWebToken token, string[] scopes, ClientIdentity identity)
    {
        var peerScope = openIdOptions.Value.PeerScope;
        if (string.IsNullOrEmpty(peerScope) || !scopes.Contains(peerScope, StringComparer.Ordinal))
            return BotePeers.None;

        // An array claim comes as one claim per entry.
        var peers = BotePeers.Of(token.Claims
            .Where(c => c.Type == BotePeers.TokenClaim)
            .Select(c => c.Value));
        if (peers.IsNone)
            logger.LogWarning("Client {TenantId}/{ClientId} has the peer scope but no {Claim} claim; it reaches no other client",
                identity.TenantId, identity.ClientId, BotePeers.TokenClaim);
        return peers;
    }
}

/// <param name="Peers">The clients of its tenant the client may send to.</param>
internal sealed record NegotiateDecision(ClientIdentity? Identity, HttpStatusCode Status, BotePeers Peers)
{
    public static NegotiateDecision Allow(ClientIdentity identity, BotePeers? peers = null) =>
        new(identity, HttpStatusCode.OK, peers ?? BotePeers.None);

    public static NegotiateDecision Deny(HttpStatusCode status) => new(null, status, BotePeers.None);
}
