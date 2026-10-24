using System.Text;
using AwesomeAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Dbosoft.Bote.BoteWorker.Tests;

public class TokenValidationServiceTests
{
    private readonly TestIdentityProvider _idp = new();

    [Fact]
    public async Task Accepts_a_token_signed_with_a_published_key()
    {
        var result = await _idp.CreateValidator().ValidateAccessToken(_idp.Issue());

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Accepts_an_RS256_token_from_a_production_issuer()
    {
        // The production identity provider (OpenIddict) signs with RS256; the worker must accept it, not only the
        // development BasicIdentityProvider's ES256.
        var token = _idp.Issue(signingCredentials: new SigningCredentials(_idp.RsaKey, SecurityAlgorithms.RsaSha256));

        var result = await _idp.CreateValidator().ValidateAccessToken(token);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Rejects_a_token_of_another_issuer()
    {
        var result = await _idp.CreateValidator().ValidateAccessToken(_idp.Issue(issuer: "https://evil.test/api"));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Rejects_a_token_for_another_audience()
    {
        var result = await _idp.CreateValidator().ValidateAccessToken(_idp.Issue(audience: "http://other-app/api"));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Rejects_a_token_expired_beyond_the_clock_skew()
    {
        var result = await _idp.CreateValidator().ValidateAccessToken(_idp.Issue(expiresIn: TimeSpan.FromMinutes(-3)));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Rejects_a_token_signed_with_an_unpublished_key_under_a_published_kid()
    {
        var forged = TestIdentityProvider.NewKey(_idp.SigningKey.KeyId);
        var token = _idp.Issue(signingCredentials: new SigningCredentials(forged, SecurityAlgorithms.EcdsaSha256));

        var result = await _idp.CreateValidator().ValidateAccessToken(token);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Rejects_an_unsigned_token()
    {
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = TestIdentityProvider.Authority,
            Audience = TestIdentityProvider.Audience,
            Expires = DateTime.UtcNow.AddMinutes(15),
            Claims = new Dictionary<string, object> { ["sub"] = "client-a", ["tid"] = "tenant-a", ["scope"] = "bote" },
        });

        var result = await _idp.CreateValidator().ValidateAccessToken(token);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Rejects_a_token_signed_with_HMAC_over_the_public_key()
    {
        // Algorithm confusion: an attacker knows the public key and uses it as an HMAC secret.
        // Only EC keys are published, so this fails on the key as well as on ValidAlgorithms.
        var publicKey = _idp.SigningKey.ECDsa.ExportSubjectPublicKeyInfo();
        var hmacKey = new SymmetricSecurityKey(publicKey) { KeyId = _idp.SigningKey.KeyId };
        var token = _idp.Issue(signingCredentials: new SigningCredentials(hmacKey, SecurityAlgorithms.HmacSha256));

        var result = await _idp.CreateValidator().ValidateAccessToken(token);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Rejects_garbage()
    {
        var garbage = Convert.ToBase64String(Encoding.UTF8.GetBytes("not a token"));

        var result = await _idp.CreateValidator().ValidateAccessToken(garbage);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Accepts_a_rotated_key_before_the_cache_expires()
    {
        var validator = _idp.CreateValidator();
        (await validator.ValidateAccessToken(_idp.Issue())).IsValid.Should().BeTrue();

        _idp.Rotate("key-2");
        _idp.Time.Advance(TimeSpan.FromMinutes(1));

        (await validator.ValidateAccessToken(_idp.Issue())).IsValid.Should().BeTrue();
        _idp.JwksRequests.Should().Be(2);
    }

    [Fact]
    public async Task Unknown_kids_refresh_the_keys_at_most_every_30_seconds()
    {
        var validator = _idp.CreateValidator();
        await validator.ValidateAccessToken(_idp.Issue());

        _idp.Time.Advance(TimeSpan.FromSeconds(31));
        for (var i = 0; i < 5; i++)
        {
            var unknown = TestIdentityProvider.NewKey($"unknown-{i}");
            var token = _idp.Issue(signingCredentials: new SigningCredentials(unknown, SecurityAlgorithms.EcdsaSha256));
            (await validator.ValidateAccessToken(token)).IsValid.Should().BeFalse();
        }

        _idp.JwksRequests.Should().Be(2);
    }

    [Fact]
    public async Task Keeps_validating_with_the_cached_keys_while_the_provider_is_unreachable()
    {
        var validator = _idp.CreateValidator();
        await validator.ValidateAccessToken(_idp.Issue());

        _idp.Unreachable = true;
        _idp.Time.Advance(TimeSpan.FromHours(2));

        (await validator.ValidateAccessToken(_idp.Issue())).IsValid.Should().BeTrue();
    }
}
