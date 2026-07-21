using AwesomeAssertions;
using Dbosoft.Bote.Primitives;

namespace Dbosoft.Bote.Rebus.IntegrationTests;

// Guards the H5a trust-boundary allow-list: a client may only set rbs2-*/non-dbote headers
// plus dbote-attachment-id; every other worker-controlled dbote-* header is a spoof attempt.
public class BoteHeaderPolicyTests
{
    [Theory]
    [InlineData("rbs2-msg-id", true)]
    [InlineData("rbs2-msg-type", true)]
    [InlineData("content-type", true)]
    [InlineData("dbote-attachment-id", true)]
    [InlineData("DBOTE-ATTACHMENT-ID", true)] // case-insensitive
    [InlineData("dbote-tenant-id", false)]
    [InlineData("dbote-client-id", false)]
    [InlineData("dbote-cloud-inbox-sas", false)]
    [InlineData("dbote-topic", false)]
    [InlineData("dbote-reschedule-cnt", false)]
    [InlineData("dbote-signature", false)]
    public void IsClientAllowed_enforces_the_dbote_allowlist(string header, bool allowed) =>
        BoteHeaderPolicy.IsClientAllowed(header).Should().Be(allowed);

    [Fact]
    public void DisallowedClientHeaders_returns_only_the_spoof_attempts()
    {
        var headers = new[]
        {
            "rbs2-msg-id", "dbote-attachment-id",       // allowed
            "dbote-tenant-id", "dbote-cloud-inbox-sas", // spoof attempts
        };

        BoteHeaderPolicy.DisallowedClientHeaders(headers)
            .Should().BeEquivalentTo("dbote-tenant-id", "dbote-cloud-inbox-sas");
    }
}
