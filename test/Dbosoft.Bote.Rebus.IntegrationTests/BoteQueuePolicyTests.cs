using AwesomeAssertions;
using Dbosoft.Bote.Primitives;

namespace Dbosoft.Bote.Rebus.IntegrationTests;

public class BoteQueuePolicyTests
{
    private static ClientSendRoute? Resolve(string queue, BotePeers? peers = null) =>
        BoteQueuePolicy.ResolveClientSendRoute(queue, "bote-cloud", "bote-clients", "client-a", peers);

    [Fact]
    public void Client_sends_to_the_cloud_or_itself()
    {
        Resolve("bote-cloud").Should().Be(new ClientSendRoute(ClientSendTarget.Cloud));
        // Rebus defers to the client's own queue
        Resolve("bote-clients-client-a").Should().Be(new ClientSendRoute(ClientSendTarget.Self, "client-a"));
    }

    [Theory]
    [InlineData("bote-clients-client-b")] // another client's queue
    [InlineData("bote-clients")]
    [InlineData("bote-error")]
    [InlineData("bote-cloud-incoming")]
    public void Client_without_the_peer_scope_sends_nowhere_else(string queue) =>
        Resolve(queue).Should().BeNull();

    [Fact]
    public void Client_sends_to_every_client_of_its_tenant_when_allowed_all()
    {
        Resolve("bote-clients-client-b", BotePeers.All)
            .Should().Be(new ClientSendRoute(ClientSendTarget.Peer, "client-b"));
        Resolve("bote-clients-client-a", BotePeers.All)
            .Should().Be(new ClientSendRoute(ClientSendTarget.Self, "client-a"));
    }

    [Fact]
    public void Client_sends_only_to_the_peers_it_is_allowed()
    {
        var peers = BotePeers.Of(["client-b"]);

        Resolve("bote-clients-client-b", peers).Should().Be(new ClientSendRoute(ClientSendTarget.Peer, "client-b"));
        Resolve("bote-clients-client-c", peers).Should().BeNull();
        Resolve("bote-clients-Client-B", peers).Should().BeNull();
        // its own queue needs no permission
        Resolve("bote-clients-client-a", BotePeers.None)
            .Should().Be(new ClientSendRoute(ClientSendTarget.Self, "client-a"));
    }

    [Fact]
    public void Peers_survive_the_signalr_claim()
    {
        var formatted = BotePeers.Of(["client-c", "client-b"]).Format();

        // SignalR hands the claim to the worker as an HTTP header
        formatted.Should().NotContainAny("\r", "\n");
        var peers = BotePeers.Parse(formatted);
        peers.Allows("client-b").Should().BeTrue();
        peers.Allows("client-c").Should().BeTrue();
        peers.Allows("client-d").Should().BeFalse();
        BotePeers.Parse(BotePeers.All.Format()).Allows("client-d").Should().BeTrue();
    }

    [Theory]
    [InlineData("bote-clients-")]
    [InlineData("bote-clients-..")]
    [InlineData("bote-clients-x/../y")]
    [InlineData("bote-error")]
    [InlineData("bote-cloud-incoming")]
    public void Peers_are_only_client_queues(string queue) =>
        Resolve(queue, BotePeers.All).Should().BeNull();

    [Fact]
    public void Client_without_id_has_no_own_queue() =>
        BoteQueuePolicy.ResolveClientSendRoute("bote-clients-", "bote-cloud", "bote-clients", "")
            .Should().BeNull();
}
