using System.Text;
using AwesomeAssertions;
using Azure.Messaging.ServiceBus;
using Dbosoft.Bote.Primitives;
using Dbosoft.Bote.Transport.Abstractions;
using Rebus.Messages;

namespace Dbosoft.Bote.BoteWorker.Tests;

public class ClientSendRulesTests
{
    private const string Clients = "bote-clients";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static Dictionary<string, object> Stamp(ClientSendRoute route, Dictionary<string, object>? properties = null)
    {
        properties ??= new Dictionary<string, object>();
        ClientSendRules.Stamp(properties, "tenant-a", "instance", route, Clients);
        return properties;
    }

    private static Dictionary<string, object> Spoofed() => new()
    {
        [BoteHeaders.TenantId] = "tenant-b",
        [BoteHeaders.ClientId] = "ceo",
        [BoteHeaders.SenderClientId] = "ceo",
        [Headers.ReturnAddress] = "bote-cloud",
    };

    [Fact]
    public void A_message_to_a_peer_names_the_true_sender_and_answers_go_back_to_it()
    {
        var properties = Stamp(new ClientSendRoute(ClientSendTarget.Peer, "agent"), Spoofed());

        properties.Should().Contain(BoteHeaders.TenantId, "tenant-a");
        properties.Should().Contain(BoteHeaders.ClientId, "agent");
        properties.Should().Contain(BoteHeaders.SenderClientId, "instance");
        properties.Should().Contain(Headers.ReturnAddress, $"{Clients}-instance");
    }

    [Fact]
    public void A_message_to_the_cloud_names_the_sender_as_client_and_carries_no_sender()
    {
        var properties = Stamp(new ClientSendRoute(ClientSendTarget.Cloud), Spoofed());

        properties.Should().Contain(BoteHeaders.TenantId, "tenant-a");
        properties.Should().Contain(BoteHeaders.ClientId, "instance");
        properties.Should().NotContainKey(BoteHeaders.SenderClientId);
        // the cloud's reply cannot be steered to another client or queue
        properties.Should().Contain(Headers.ReturnAddress, $"{Clients}-instance");
    }

    [Fact]
    public void A_deferred_message_keeps_the_sender_of_the_message_it_was_made_from()
    {
        var properties = Stamp(new ClientSendRoute(ClientSendTarget.Self, "instance"),
            new Dictionary<string, object> { [BoteHeaders.SenderClientId] = "agent" });

        properties.Should().Contain(BoteHeaders.ClientId, "instance");
        properties.Should().Contain(BoteHeaders.SenderClientId, "agent");
    }

    [Fact]
    public void Header_names_of_any_case_are_normalized()
    {
        var headers = new Dictionary<string, string?>
        {
            ["DBOTE-SENDER-CLIENT-ID"] = "ceo",
            ["Dbote-Attachment-Id"] = "x",
            ["rbs2-msg-id"] = "1",
        };

        ClientSendRules.NormalizeHeaderNames(headers);

        headers.Keys.Should().BeEquivalentTo(BoteHeaders.SenderClientId, BoteHeaders.AttachmentId, "rbs2-msg-id");
    }

    [Fact]
    public void Refuses_a_message_scheduled_too_far_ahead()
    {
        var message = new ServiceBusMessage { ScheduledEnqueueTime = Now + ClientSendRules.MaxDeferral + TimeSpan.FromHours(1) };

        ClientSendRules.Refuse(new BoteMessage(), message, new ClientSendRoute(ClientSendTarget.Self, "instance"), Now)
            .Should().NotBeNull();
        ClientSendRules.Refuse(new BoteMessage(), new ServiceBusMessage { ScheduledEnqueueTime = Now.AddDays(1) },
            new ClientSendRoute(ClientSendTarget.Self, "instance"), Now).Should().BeNull();
    }

    [Theory]
    [InlineData(ClientSendTarget.Peer, true)]
    [InlineData(ClientSendTarget.Self, true)]
    [InlineData(ClientSendTarget.Cloud, false)] // Service Bus takes it; the cloud is no storage queue
    public void Refuses_a_message_too_large_for_a_client_queue(ClientSendTarget target, bool refused)
    {
        var large = new BoteMessage { Body = Encoding.UTF8.GetBytes(new string('x', 50 * 1024)) };

        ClientSendRules.Refuse(large, new ServiceBusMessage(), new ClientSendRoute(target, "agent"), Now)
            .Should().Match(r => (r != null) == refused);
    }
}
