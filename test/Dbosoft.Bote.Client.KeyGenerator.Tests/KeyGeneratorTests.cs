using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Microsoft.IdentityModel.Tokens;

namespace Dbosoft.Bote.Client.KeyGenerator.Tests;

public class KeyGeneratorTests
{
    [Fact]
    public void GenerateKey_ReturnsValidKeyPair()
    {
        var keyGenerator = new KeyGenerator();
        var keyInfo = keyGenerator.GenerateKey();

        keyInfo.KeyId.Should().NotBeNullOrEmpty();
        keyInfo.PublicKey.Should().NotBeNullOrEmpty();
        keyInfo.PrivateKey.Should().NotBeNullOrEmpty();

        using var privateKey = ECDsa.Create();
        privateKey.ImportPkcs8PrivateKey(Convert.FromBase64String(keyInfo.PrivateKey), out _);

        var publicJwk = new JsonWebKey(Encoding.UTF8.GetString(Convert.FromBase64String(keyInfo.PublicKey)));
        publicJwk.HasPrivateKey.Should().BeFalse();

        var testMessage = Encoding.UTF8.GetBytes("Test message");
        var signature = privateKey.SignData(testMessage, HashAlgorithmName.SHA256);

        JsonWebKeyConverter.TryConvertToSecurityKey(publicJwk, out var publicKey).Should().BeTrue();
        publicKey.KeyId.Should().Be(keyInfo.KeyId);
        var publicECDsKey = publicKey.Should().BeOfType<ECDsaSecurityKey>().Subject;

        publicECDsKey.ECDsa.VerifyData(testMessage, signature, HashAlgorithmName.SHA256).Should().BeTrue();
    }
}
