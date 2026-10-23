using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Dbosoft.Bote.BoteWorker.Tests;

/// <summary>
/// Stands in for the identity provider: issues ES256 access tokens like BasicIdentityProvider and
/// serves the published keys as JWKS to the worker's <see cref="TokenValidationService"/>.
/// </summary>
internal sealed class TestIdentityProvider : HttpMessageHandler, IHttpClientFactory
{
    public const string Authority = "https://idp.test/api";
    public const string Audience = "http://dbote-worker/api";
    public const string JwksUri = "https://idp.test/api/.well-known/jwks.json";
    public const string Scope = "bote";

    private ECDsaSecurityKey[] _published;

    public TestIdentityProvider()
    {
        SigningKey = NewKey("key-1");
        _published = [SigningKey];
    }

    public ECDsaSecurityKey SigningKey { get; private set; }

    public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);

    public int JwksRequests { get; private set; }

    public bool Unreachable { get; set; }

    public static ECDsaSecurityKey NewKey(string kid) =>
        new(ECDsa.Create(ECCurve.NamedCurves.nistP256)) { KeyId = kid };

    /// <summary>Signs with a new key from now on and publishes only that one.</summary>
    public void Rotate(string kid)
    {
        SigningKey = NewKey(kid);
        _published = [SigningKey];
    }

    public TokenValidationService CreateValidator(OpenIdOptions? options = null) =>
        new(this, Microsoft.Extensions.Options.Options.Create(options ?? Options()),
            NullLogger<TokenValidationService>.Instance, Time);

    public static OpenIdOptions Options(string requiredScope = Scope, string? peerScope = null) => new()
    {
        Authority = Authority,
        Audience = Audience,
        JwksUri = JwksUri,
        RequiredScope = requiredScope,
        PeerScope = peerScope,
    };

    public string Issue(
        string? tenantId = "tenant-a",
        string? clientId = "client-a",
        string? scope = Scope,
        string issuer = Authority,
        string audience = Audience,
        TimeSpan? expiresIn = null,
        SigningCredentials? signingCredentials = null,
        object? peers = null)
    {
        var claims = new List<Claim>();
        if (clientId is not null) claims.Add(new Claim(JwtRegisteredClaimNames.Sub, clientId));
        if (tenantId is not null) claims.Add(new Claim("tid", tenantId));
        if (scope is not null) claims.Add(new Claim("scope", scope));

        var expires = DateTime.UtcNow.Add(expiresIn ?? TimeSpan.FromMinutes(15));
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            IssuedAt = expires.AddMinutes(-30),
            NotBefore = expires.AddMinutes(-30),
            Expires = expires,
            Issuer = issuer,
            Audience = audience,
            Claims = peers is null ? null : new Dictionary<string, object> { ["bote_peers"] = peers },
            SigningCredentials = signingCredentials
                ?? new SigningCredentials(SigningKey, SecurityAlgorithms.EcdsaSha256),
        });
    }

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        JwksRequests++;
        if (Unreachable || request.RequestUri?.ToString() != JwksUri)
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var keys = _published.Select(k =>
        {
            var p = k.ECDsa.ExportParameters(false);
            return new
            {
                kty = "EC",
                crv = "P-256",
                x = Base64UrlEncoder.Encode(p.Q.X),
                y = Base64UrlEncoder.Encode(p.Q.Y),
                kid = k.KeyId,
                use = "sig",
                alg = "ES256",
            };
        });

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { keys })),
        });
    }
}
