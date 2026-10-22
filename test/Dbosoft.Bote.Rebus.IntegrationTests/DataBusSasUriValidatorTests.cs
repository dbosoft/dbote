using AwesomeAssertions;
using Dbosoft.Bote.Primitives;

namespace Dbosoft.Bote.Rebus.IntegrationTests;

// Guards the H5b SSRF defense: the cloud only fetches an inbox attachment from its own
// storage endpoint, never an arbitrary host carried in the SAS header.
public class DataBusSasUriValidatorTests
{
    private static readonly Uri Trusted = new("https://acct.blob.core.windows.net/");

    [Theory]
    [InlineData("https://acct.blob.core.windows.net/bote-inbox/t/a?sig=x", true)]
    [InlineData("https://acct.blob.core.windows.net:443/bote-inbox/t/a?sig=x", true)]
    [InlineData("http://attacker.example.com/bote-inbox/t/a?sig=x", false)]
    [InlineData("https://acct.blob.core.windows.net.attacker.com/x?sig=x", false)]
    [InlineData("https://other.blob.core.windows.net/bote-inbox/t/a?sig=x", false)]
    [InlineData("http://acct.blob.core.windows.net:443/bote-inbox/t/a?sig=x", false)] // scheme downgrade (same host+port)
    public void IsTrustedEndpoint_only_accepts_the_trusted_host(string uri, bool expected) =>
        DataBusSasUriValidator.IsTrustedEndpoint(new Uri(uri), Trusted).Should().Be(expected);

    [Fact]
    public void IsTrustedEndpoint_matches_the_emulator_endpoint_including_port()
    {
        var trusted = new Uri("http://127.0.0.1:10000/devstoreaccount1");
        DataBusSasUriValidator.IsTrustedEndpoint(
            new Uri("http://127.0.0.1:10000/devstoreaccount1/bote-inbox/t/a?sig=x"), trusted)
            .Should().BeTrue();
        DataBusSasUriValidator.IsTrustedEndpoint(
            new Uri("http://127.0.0.1:10001/devstoreaccount1/bote-inbox/t/a?sig=x"), trusted)
            .Should().BeFalse("a different port is a different endpoint");
    }
}
