using System.Text.RegularExpressions;
using AwesomeAssertions;
using Dbosoft.Bote.Primitives;

namespace Dbosoft.Bote.BoteWorker.Tests;

public partial class BoteClientNamesTests
{
    // Azure storage queue names: 3-63 lowercase letters, digits and single hyphens, starting and
    // ending with a letter or digit.
    [GeneratedRegex("^(?=.{3,63}$)[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex QueueName();

    private const string LongestPrefix = "abcdefghijklmnopqrstuvwx";

    [Fact]
    public void The_key_is_stable()
    {
        // Deployed queues are named after it: a change orphans every client's queue.
        BoteClientNames.ClientKey("tenant-a", "client-a").Should().Be("b9b878ea10d2339b4eb1401841ff3e13");
    }

    [Theory]
    [InlineData("a-b", "c", "a", "b-c")]
    [InlineData("ab", "c", "a", "bc")]
    [InlineData("tenant-a", "client-a", "Tenant-A", "Client-A")]
    [InlineData("tenant-a", "client-a", "tenant-a", "client-a-error")]
    [InlineData("tenant/a", "client-a", "tenant-a", "client-a")]
    public void Different_clients_get_different_names(string tenant1, string client1, string tenant2, string client2)
    {
        var first = Names("bote", tenant1, client1);
        var second = Names("bote", tenant2, client2);

        first.Should().NotIntersectWith(second);
    }

    [Fact]
    public void A_clients_queue_is_never_another_clients_error_queue()
    {
        var errorQueueOfC = BoteClientNames.ClientErrorQueueName("bote", "tenant-a", "c");

        BoteClientNames.ClientQueueName("bote", "tenant-a", "c-error").Should().NotBe(errorQueueOfC);
        BoteClientNames.ClientQueueName("bote", "tenant-a", "c").Should().NotBe(errorQueueOfC);
    }

    [Theory]
    [InlineData("tenant-a", "client-a")]
    [InlineData("3f2c9a52-6f0e-4a8e-9a3b-0f5d2c7e1b44", "8d1e5c3a-2b7f-4c9d-8e6a-1f0b3c5d7e92")]
    [InlineData("Tenant A/ü", "client\\..")]
    public void Names_are_valid_queue_names_for_any_ids(string tenantId, string clientId)
    {
        foreach (var prefix in new[] { "bote", "bote-sample-simple", LongestPrefix })
        {
            BoteClientNames.ClientQueueName(prefix, tenantId, clientId).Should().MatchRegex(QueueName());
            BoteClientNames.ClientErrorQueueName(prefix, tenantId, clientId).Should().MatchRegex(QueueName());
        }
    }

    [Fact]
    public void Names_of_very_long_ids_fit()
    {
        var id = new string('x', 1000);

        BoteClientNames.ClientErrorQueueName(LongestPrefix, id, id).Should().MatchRegex(QueueName());
    }

    [Fact]
    public void Topic_keys_are_per_tenant()
    {
        BoteClientNames.TopicKey("tenant-a", "chat").Should().NotBe(BoteClientNames.TopicKey("tenant-b", "chat"));
        BoteClientNames.TopicKey("a-b", "c").Should().NotBe(BoteClientNames.TopicKey("a", "b-c"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Bote")]
    [InlineData("-bote")]
    [InlineData("bote-")]
    [InlineData("bo--te")]
    [InlineData("bote_x")]
    [InlineData(LongestPrefix + "y")]
    public void Refuses_prefixes_that_make_invalid_queue_names(string prefix)
    {
        var act = () => BoteClientNames.ClientQueueName(prefix, "tenant-a", "client-a");

        act.Should().Throw<ArgumentException>();
    }

    private static string[] Names(string prefix, string tenantId, string clientId) =>
    [
        BoteClientNames.ClientQueueName(prefix, tenantId, clientId),
        BoteClientNames.ClientErrorQueueName(prefix, tenantId, clientId),
        BoteClientNames.SignalRUserId(tenantId, clientId),
    ];
}
