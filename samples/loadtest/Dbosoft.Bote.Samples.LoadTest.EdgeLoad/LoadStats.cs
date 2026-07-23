using System.Collections.Concurrent;
using System.Diagnostics;
using Dbosoft.Bote.Samples.LoadTest.Messages;

namespace Dbosoft.Bote.Samples.LoadTest.EdgeLoad;

/// <summary>Aggregate metrics shared by all simulated edge clients (counters are Interlocked).</summary>
public sealed class LoadStats
{
    public long Registered;
    public long SmallSent;
    public long SmallAcked;
    public long LargeSent;
    public long LargeEchoed;
    public long Notifications;
    public long Errors;
    public long SmallBytes;
    public long LargeBytes;

    // Request id -> send timestamp (Stopwatch ticks), for round-trip latency.
    public readonly ConcurrentDictionary<Guid, long> PendingSmall = new();
    public readonly ConcurrentDictionary<Guid, long> PendingLarge = new();
    public readonly LatencyMeter SmallRoundtrip = new();
    public readonly LatencyMeter LargeRoundtrip = new();
    public readonly LatencyMeter SmallSend = new();
    public readonly LatencyMeter LargeSend = new();

    // Noisy-neighbour split (small messages): quiet = well-behaved clients, noisy = the flooders.
    public long QuietSent;
    public long NoisySent;
    public readonly LatencyMeter QuietRoundtrip = new();
    public readonly LatencyMeter NoisyRoundtrip = new();

    public void MarkSent(ConcurrentDictionary<Guid, long> pending, Guid id) =>
        pending[id] = Stopwatch.GetTimestamp();

    public void MarkReceived(ConcurrentDictionary<Guid, long> pending, LatencyMeter meter, Guid id)
    {
        if (pending.TryRemove(id, out var sentAt))
            meter.Record((Stopwatch.GetTimestamp() - sentAt) * 1000.0 / Stopwatch.Frequency);
    }

    /// <summary>Records the round-trip into several meters at once (e.g. aggregate + quiet/noisy group).</summary>
    public void MarkReceived(ConcurrentDictionary<Guid, long> pending, Guid id, params LatencyMeter[] meters)
    {
        if (!pending.TryRemove(id, out var sentAt)) return;
        var ms = (Stopwatch.GetTimestamp() - sentAt) * 1000.0 / Stopwatch.Frequency;
        foreach (var m in meters) m.Record(ms);
    }

    public EdgeReport ToReport(string reporterId, int edges) => new()
    {
        ReporterId = reporterId,
        Edges = edges,
        SmallSent = Interlocked.Read(ref SmallSent),
        SmallAcked = Interlocked.Read(ref SmallAcked),
        LargeSent = Interlocked.Read(ref LargeSent),
        LargeEchoed = Interlocked.Read(ref LargeEchoed),
        SmallInFlight = PendingSmall.Count,
        LargeInFlight = PendingLarge.Count,
        Notifications = Interlocked.Read(ref Notifications),
        Errors = Interlocked.Read(ref Errors),
        SmallBytes = Interlocked.Read(ref SmallBytes),
        LargeBytes = Interlocked.Read(ref LargeBytes),
        SmallRoundtrip = SmallRoundtrip.Snapshot(),
        LargeRoundtrip = LargeRoundtrip.Snapshot(),
        SmallSend = SmallSend.Snapshot(),
        LargeSend = LargeSend.Snapshot(),
        QuietSent = Interlocked.Read(ref QuietSent),
        NoisySent = Interlocked.Read(ref NoisySent),
        QuietRoundtrip = QuietRoundtrip.Snapshot(),
        NoisyRoundtrip = NoisyRoundtrip.Snapshot(),
    };
}
