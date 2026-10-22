using AwesomeAssertions;
using Dbosoft.Bote.Primitives;

namespace Dbosoft.Bote.Rebus.IntegrationTests;

// Guards the broadcast fan-out: bounded parallelism, per-subscriber failure isolation,
// and no aborting the whole broadcast when a single subscriber fails.
public class BroadcastFanOutTests
{
    [Fact]
    public async Task Delivers_to_every_subscriber_on_success()
    {
        var delivered = new System.Collections.Concurrent.ConcurrentBag<int>();

        var result = await BroadcastFanOut.RunAsync(
            Enumerable.Range(0, 50).ToList(),
            i => { delivered.Add(i); return Task.CompletedTask; },
            maxConcurrency: 8);

        result.Delivered.Should().Be(50);
        result.Failures.Should().BeEmpty();
        delivered.Should().BeEquivalentTo(Enumerable.Range(0, 50));
    }

    [Fact]
    public async Task Isolates_per_subscriber_failures_and_keeps_going()
    {
        var result = await BroadcastFanOut.RunAsync(
            Enumerable.Range(0, 10).ToList(),
            i => i % 2 == 0
                ? Task.CompletedTask
                : Task.FromException(new InvalidOperationException($"fail {i}")),
            maxConcurrency: 4);

        result.Delivered.Should().Be(5);
        result.Failures.Should().HaveCount(5);
        result.Failures.Select(f => f.Item).Should().BeEquivalentTo(new[] { 1, 3, 5, 7, 9 });
        result.Failures.Should().OnlyContain(f => f.Error is InvalidOperationException);
    }

    [Fact]
    public async Task Never_exceeds_the_concurrency_limit()
    {
        var current = 0;
        var peak = 0;
        var sync = new object();

        var result = await BroadcastFanOut.RunAsync(
            Enumerable.Range(0, 40).ToList(),
            async _ =>
            {
                lock (sync) { current++; peak = Math.Max(peak, current); }
                await Task.Delay(15);
                lock (sync) { current--; }
            },
            maxConcurrency: 5);

        result.Delivered.Should().Be(40);
        peak.Should().BeLessThanOrEqualTo(5);
    }

    [Fact]
    public async Task Empty_subscriber_list_delivers_nothing()
    {
        var result = await BroadcastFanOut.RunAsync(
            new List<int>(),
            _ => Task.CompletedTask,
            maxConcurrency: 8);

        result.Delivered.Should().Be(0);
        result.Failures.Should().BeEmpty();
        result.AnyDelivered.Should().BeFalse();
    }
}
