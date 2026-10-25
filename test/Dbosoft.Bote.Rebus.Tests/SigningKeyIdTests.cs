using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Dbosoft.Bote.Client.Authentication;
using Dbosoft.Bote.Client.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Dbosoft.Bote.Rebus.Tests;

// A fixed key id per client (tenant and client) let a client hold one key only: the identity
// provider takes a key id once, so a new key could not stand beside the old one.
public class SigningKeyIdTests
{
    private static BoteOptions Options(ECDsa key, string? keyId = null) => new()
    {
        TenantId = "tenant",
        ClientId = "client",
        Authentication = new BoteAuthenticationOptions
        {
            AuthenticationType = BoteAuthenticationType.Value,
            SigningKey = Convert.ToBase64String(key.ExportPkcs8PrivateKey()),
            KeyId = keyId!,
        },
    };

    [Fact]
    public void Signs_under_the_configured_key_id()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Options(key, "key-2").GetSigningKey().KeyId.Should().Be("key-2");
    }

    [Fact]
    public void Signs_under_the_keys_rfc_7638_thumbprint_when_none_is_configured()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = key.ExportParameters(false);
        var canonical = $$"""{"crv":"P-256","kty":"EC","x":"{{Base64UrlEncoder.Encode(p.Q.X)}}","y":"{{Base64UrlEncoder.Encode(p.Q.Y)}}"}""";
        var expected = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        Options(key).GetSigningKey().KeyId.Should().Be(expected);
    }

    [Fact]
    public void Names_two_keys_of_one_client_apart()
    {
        using var old = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var next = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Options(old).GetSigningKey().KeyId.Should().NotBe(Options(next).GetSigningKey().KeyId);
    }
}
