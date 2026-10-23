using System.Net;
using AwesomeAssertions;
using Dbosoft.Bote.Primitives;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dbosoft.Bote.BoteWorker.Tests;

public class NegotiateAuthorizerTests
{
    private readonly TestIdentityProvider _idp = new();

    private NegotiateAuthorizer CreateAuthorizer(string requiredScope = TestIdentityProvider.Scope,
        string? peerScope = null)
    {
        var options = TestIdentityProvider.Options(requiredScope, peerScope);
        return new NegotiateAuthorizer(
            _idp.CreateValidator(options),
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<NegotiateAuthorizer>.Instance);
    }

    private Task<NegotiateDecision> Authorize(string? header, string requiredScope = TestIdentityProvider.Scope,
        string? peerScope = null) =>
        CreateAuthorizer(requiredScope, peerScope).Authorize(header, CancellationToken.None);

    [Fact]
    public async Task Takes_the_identity_from_the_token()
    {
        var decision = await Authorize($"Bearer {_idp.Issue(tenantId: "tenant-b", clientId: "client-x")}");

        decision.Status.Should().Be(HttpStatusCode.OK);
        decision.Identity.Should().Be(new ClientIdentity("tenant-b", "client-x"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("bearer abc")]
    public async Task Refuses_requests_without_a_bearer_token(string? header)
    {
        var decision = await Authorize(header);

        decision.Should().Be(NegotiateDecision.Deny(HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task Refuses_an_invalid_token()
    {
        var decision = await Authorize($"Bearer {_idp.Issue(audience: "http://other-app/api")}");

        decision.Should().Be(NegotiateDecision.Deny(HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task Refuses_a_token_without_tenant()
    {
        var decision = await Authorize($"Bearer {_idp.Issue(tenantId: null)}");

        decision.Should().Be(NegotiateDecision.Deny(HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task Refuses_a_token_without_client()
    {
        var decision = await Authorize($"Bearer {_idp.Issue(clientId: null)}");

        decision.Should().Be(NegotiateDecision.Deny(HttpStatusCode.Unauthorized));
    }

    [Theory]
    [InlineData("tenant/a", "client-a")]
    [InlineData("..", "client-a")]
    [InlineData("tenant-a", "client\\a")]
    [InlineData("tenant-a", ".")]
    [InlineData("tenant-a", "client\na")]
    public async Task Refuses_ids_that_are_no_path_segment(string tenantId, string clientId)
    {
        var decision = await Authorize($"Bearer {_idp.Issue(tenantId: tenantId, clientId: clientId)}");

        decision.Should().Be(NegotiateDecision.Deny(HttpStatusCode.Unauthorized));
    }

    [Theory]
    [InlineData("a-b", "c-error")]
    [InlineData("3f2c9a52-6f0e-4a8e-9a3b-0f5d2c7e1b44", "Client A.1")]
    public async Task Accepts_ids_the_names_no_longer_depend_on(string tenantId, string clientId)
    {
        var decision = await Authorize($"Bearer {_idp.Issue(tenantId: tenantId, clientId: clientId)}");

        decision.Identity.Should().Be(new ClientIdentity(tenantId, clientId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("other")]
    [InlineData("botex")]
    [InlineData("bote.peer")]
    public async Task Forbids_a_token_without_the_required_scope(string? scope)
    {
        var decision = await Authorize($"Bearer {_idp.Issue(scope: scope)}");

        decision.Should().Be(NegotiateDecision.Deny(HttpStatusCode.Forbidden));
    }

    [Fact]
    public async Task Accepts_the_required_scope_among_others()
    {
        var decision = await Authorize($"Bearer {_idp.Issue(scope: "other bote")}");

        decision.Status.Should().Be(HttpStatusCode.OK);
        decision.Peers.Should().BeSameAs(BotePeers.None);
    }

    [Theory]
    [InlineData("bote bote.peer", "bote.peer", true)]
    [InlineData("bote", "bote.peer", false)]
    [InlineData("bote bote.peer", null, false)] // the worker allows no peers
    [InlineData("bote bote.peerx", "bote.peer", false)]
    public async Task Only_the_peer_scope_lets_a_client_address_its_peers(string scope, string? peerScope, bool expected)
    {
        var decision = await Authorize($"Bearer {_idp.Issue(scope: scope, peers: new[] { "*" })}", peerScope: peerScope);

        decision.Status.Should().Be(HttpStatusCode.OK);
        decision.Peers.Allows("client-b").Should().Be(expected);
    }

    [Fact]
    public async Task The_token_names_the_peers()
    {
        var decision = await Authorize($"Bearer {_idp.Issue(scope: "bote bote.peer", peers: new[] { "agent-1", "agent-2", "../x" })}",
            peerScope: "bote.peer");

        decision.Peers.Allows("agent-1").Should().BeTrue();
        decision.Peers.Allows("agent-2").Should().BeTrue();
        decision.Peers.Allows("agent-3").Should().BeFalse();
        decision.Peers.Allows("../x").Should().BeFalse();
    }

    [Fact]
    public async Task A_single_peer_may_be_a_string()
    {
        var decision = await Authorize($"Bearer {_idp.Issue(scope: "bote bote.peer", peers: "instance")}",
            peerScope: "bote.peer");

        decision.Peers.Allows("instance").Should().BeTrue();
        decision.Peers.Allows("other").Should().BeFalse();
    }

    [Fact]
    public async Task The_peer_scope_without_a_list_reaches_no_one()
    {
        var decision = await Authorize($"Bearer {_idp.Issue(scope: "bote bote.peer")}", peerScope: "bote.peer");

        decision.Status.Should().Be(HttpStatusCode.OK);
        decision.Peers.IsNone.Should().BeTrue();
    }

    [Fact]
    public async Task Fails_closed_without_a_configured_scope()
    {
        var decision = await Authorize($"Bearer {_idp.Issue()}", requiredScope: "");

        decision.Should().Be(NegotiateDecision.Deny(HttpStatusCode.InternalServerError));
    }
}
