using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Dbosoft.Bote.BoteWorker;

public interface ITokenValidationService
{
    Task<TokenValidationResult> ValidateAccessToken(string jwt, CancellationToken cancellationToken = default);
}

public class TokenValidationService : ITokenValidationService
{
    // Minimum spacing between forced (unknown-kid) refreshes, so a flood of tokens carrying
    // unknown kids cannot be used to hammer the identity provider's JWKS endpoint.
    private static readonly TimeSpan MinForcedRefreshInterval = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient;
    private readonly IOptions<OpenIdOptions> _options;
    private readonly ILogger<TokenValidationService> _logger;
    private JsonWebKeySet? _cachedKeySet;
    private DateTime _cacheExpiration = DateTime.MinValue;
    private DateTime _lastFetch = DateTime.MinValue;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public TokenValidationService(
        IHttpClientFactory httpClientFactory,
        IOptions<OpenIdOptions> options,
        ILogger<TokenValidationService> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _options = options;
        _logger = logger;
    }

    public async Task<TokenValidationResult> ValidateAccessToken(string jwt, CancellationToken cancellationToken = default)
    {
        var signingKeys = await GetSigningKeys(forceRefresh: false, cancellationToken);

        // Key rotation may introduce a kid we haven't cached yet (within the 1h window).
        // If the token references an unknown kid, refresh once (rate-limited) before rejecting,
        // so a rotation doesn't lock every client out until the cache expires.
        var kid = TryGetKid(jwt);
        if (kid != null
            && signingKeys.All(k => !string.Equals(k.KeyId, kid, StringComparison.Ordinal))
            && DateTime.UtcNow - _lastFetch > MinForcedRefreshInterval)
        {
            _logger.LogInformation("Token kid {Kid} not in cached JWKS; forcing a refresh", kid);
            signingKeys = await GetSigningKeys(forceRefresh: true, cancellationToken);
        }

        var handler = new JsonWebTokenHandler();
        var validationParameters = new TokenValidationParameters()
        {
            ValidIssuer = _options.Value.Authority,
            ValidAudience = _options.Value.Audience,
            IssuerSigningKeys = signingKeys,

            // Explicit validation flags for security
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidateIssuer = true,
            ValidateAudience = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,

            // CRITICAL: Restrict allowed algorithms to prevent algorithm confusion attacks
            ValidAlgorithms = new[] { SecurityAlgorithms.EcdsaSha256 },

            // Reduce clock skew for tighter security (default is 5 minutes)
            ClockSkew = TimeSpan.FromMinutes(2)
        };

        return await handler.ValidateTokenAsync(jwt, validationParameters);
    }

    private async Task<ICollection<SecurityKey>> GetSigningKeys(bool forceRefresh, CancellationToken cancellationToken)
    {
        // Check cache
        if (!forceRefresh && _cachedKeySet != null && DateTime.UtcNow < _cacheExpiration)
        {
            _logger.LogTrace("Using cached JWKS");
            return _cachedKeySet.GetSigningKeys();
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (!forceRefresh && _cachedKeySet != null && DateTime.UtcNow < _cacheExpiration)
            {
                return _cachedKeySet.GetSigningKeys();
            }

            var jwksUri = _options.Value.JwksUri;
            if (string.IsNullOrEmpty(jwksUri))
            {
                throw new InvalidOperationException("JwksUri is not configured in OpenIdOptions.");
            }

            _logger.LogDebug("Fetching JWKS from {JwksUri}", jwksUri);

            try
            {
                // Fetch JWKS using standard Microsoft.IdentityModel library
                var jwksJson = await _httpClient.GetStringAsync(jwksUri, cancellationToken);
                _cachedKeySet = new JsonWebKeySet(jwksJson);
                _cacheExpiration = DateTime.UtcNow.AddHours(1); // Cache for 1 hour to allow key rotation
                _lastFetch = DateTime.UtcNow;

                _logger.LogInformation("JWKS refreshed from {JwksUri}, found {KeyCount} keys", jwksUri, _cachedKeySet.Keys.Count);
            }
            catch (Exception ex) when (_cachedKeySet != null)
            {
                // The identity provider is unreachable but we still hold a (possibly stale) key
                // set. Serve it rather than failing every login until the IdP recovers - a stale
                // but still-valid key is far better than a total authentication outage.
                _logger.LogWarning(ex, "Failed to refresh JWKS from {JwksUri}; falling back to the cached key set", jwksUri);
            }

            return _cachedKeySet.GetSigningKeys();
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static string? TryGetKid(string jwt)
    {
        try
        {
            return new JsonWebToken(jwt).Kid;
        }
        catch
        {
            return null;
        }
    }
}
