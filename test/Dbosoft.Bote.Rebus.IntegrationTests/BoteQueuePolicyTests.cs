using AwesomeAssertions;
using Dbosoft.Bote.Primitives;

namespace Dbosoft.Bote.Rebus.IntegrationTests;

public class BoteQueuePolicyTests
{
    [Theory]
    [InlineData("bote-cloud", ClientSendTarget.Cloud)]
    [InlineData("bote-clients-client-a", ClientSendTarget.Self)] // Rebus defers to the client's own queue
    [InlineData("bote-clients-client-b", null)] // another client's queue
    [InlineData("bote-clients", null)]
    [InlineData("bote-error", null)]
    [InlineData("bote-cloud-incoming", null)]
    public void Client_sends_to_the_cloud_or_itself_only(string queue, ClientSendTarget? expected) =>
        BoteQueuePolicy.ResolveClientSendTarget(queue, "bote-cloud", "bote-clients", "client-a")
            .Should().Be(expected);

    [Fact]
    public void Client_without_id_has_no_own_queue() =>
        BoteQueuePolicy.ResolveClientSendTarget("bote-clients-", "bote-cloud", "bote-clients", "")
            .Should().BeNull();
}
