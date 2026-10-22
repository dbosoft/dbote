using AwesomeAssertions;
using Dbosoft.Bote.Primitives;

namespace Dbosoft.Bote.Rebus.IntegrationTests;

public class BoteQueuePolicyTests
{
    private static ClientSendRoute? Resolve(string queue, bool mayAddressPeers = false) =>
        BoteQueuePolicy.ResolveClientSendRoute(queue, "bote-cloud", "bote-clients", "client-a", mayAddressPeers);

    [Theory]
    [InlineData("bote-clients-client-b")] // another client's queue
    [InlineData("bote-clients")]
    [InlineData("bote-error")]
    [InlineData("bote-cloud-incoming")]
    public void Client_sends_to_the_cloud_or_itself_only(string queue)
    {
        Resolve("bote-cloud").Should().Be(new ClientSendRoute(ClientSendTarget.Cloud));
        // Rebus defers to the client's own queue
        Resolve("bote-clients-client-a").Should().Be(new ClientSendRoute(ClientSendTarget.Self, "client-a"));
        Resolve(queue).Should().BeNull();
    }

    [Fact]
    public void Client_with_the_peer_scope_sends_to_another_client()
    {
        Resolve("bote-clients-client-b", mayAddressPeers: true)
            .Should().Be(new ClientSendRoute(ClientSendTarget.Peer, "client-b"));
        Resolve("bote-clients-client-a", mayAddressPeers: true)
            .Should().Be(new ClientSendRoute(ClientSendTarget.Self, "client-a"));
    }

    [Theory]
    [InlineData("bote-clients-")]
    [InlineData("bote-clients-..")]
    [InlineData("bote-clients-x/../y")]
    [InlineData("bote-error")]
    [InlineData("bote-cloud-incoming")]
    public void Peers_are_only_client_queues(string queue) =>
        Resolve(queue, mayAddressPeers: true).Should().BeNull();

    [Fact]
    public void Client_without_id_has_no_own_queue() =>
        BoteQueuePolicy.ResolveClientSendRoute("bote-clients-", "bote-cloud", "bote-clients", "")
            .Should().BeNull();
}
