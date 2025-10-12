using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SuperBus.SuperBusWorker;

public interface ITokenValidationService
{
    Task<TokenValidationResult> ValidateAccessToken(string jwt, CancellationToken cancellationToken = default);
}

public class TokenValidationService : ITokenValidationService
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<OpenIdOptions> _options;
    private readonly ILogger<TokenValidationService> _logger;
    private JsonWebKeySet? _cachedKeySet;
    private DateTime _cacheExpiration = DateTime.MinValue;
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
        var signingKeys = await GetSigningKeys(cancellationToken);

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

    private async Task<ICollection<SecurityKey>> GetSigningKeys(CancellationToken cancellationToken)
    {
        // Check cache
        if (_cachedKeySet != null && DateTime.UtcNow < _cacheExpiration)
        {
            _logger.LogTrace("Using cached JWKS");
            return _cachedKeySet.GetSigningKeys();
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_cachedKeySet != null && DateTime.UtcNow < _cacheExpiration)
            {
                return _cachedKeySet.GetSigningKeys();
            }

            var jwksUri = _options.Value.JwksUri;
            if (string.IsNullOrEmpty(jwksUri))
            {
                throw new InvalidOperationException("JwksUri is not configured in OpenIdOptions.");
            }

            _logger.LogDebug("Fetching JWKS from {JwksUri}", jwksUri);

            // Fetch JWKS using standard Microsoft.IdentityModel library
            var jwksJson = await _httpClient.GetStringAsync(jwksUri, cancellationToken);
            _cachedKeySet = new JsonWebKeySet(jwksJson);
            _cacheExpiration = DateTime.UtcNow.AddHours(1); // Cache for 1 hour to allow key rotation

            _logger.LogInformation("JWKS refreshed from {JwksUri}, found {KeyCount} keys", jwksUri, _cachedKeySet.Keys.Count);

            return _cachedKeySet.GetSigningKeys();
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
