namespace Dbosoft.Bote.Samples.LoadTest.Messages;

/// <summary>A latency distribution snapshot (all values in milliseconds).</summary>
public record LatencySnapshot(long Count, double AvgMs, double P95Ms, double MaxMs)
{
    public static readonly LatencySnapshot Empty = new(0, 0, 0, 0);
}

/// <summary>Thread-safe latency accumulator (count/avg/max + p95 over a recent ring sample).</summary>
public sealed class LatencyMeter
{
    private readonly object _lock = new();
    private readonly double[] _ring = new double[1024];
    private long _count;
    private double _sumMs;
    private double _maxMs;
    private int _pos;
    private int _filled;

    public void Record(double ms)
    {
        lock (_lock)
        {
            _count++;
            _sumMs += ms;
            if (ms > _maxMs) _maxMs = ms;
            _ring[_pos] = ms;
            _pos = (_pos + 1) % _ring.Length;
            if (_filled < _ring.Length) _filled++;
        }
    }

    public LatencySnapshot Snapshot()
    {
        lock (_lock)
        {
            if (_count == 0) return LatencySnapshot.Empty;
            var sample = new double[_filled];
            Array.Copy(_ring, sample, _filled);
            Array.Sort(sample);
            var p95 = sample[Math.Min(sample.Length - 1, (int)(sample.Length * 0.95))];
            return new LatencySnapshot(_count, Math.Round(_sumMs / _count, 1), Math.Round(p95, 1), Math.Round(_maxMs, 1));
        }
    }
}

/// <summary>
/// The edge load generator's self-measured metrics, pushed to the controller out-of-band
/// (HTTP) so the dashboard can show both sides without perturbing the message load.
/// </summary>
public record EdgeReport
{
    public string ReporterId { get; init; } = "";
    public int Edges { get; init; }
    public long SmallSent { get; init; }
    public long SmallAcked { get; init; }
    public long LargeSent { get; init; }
    public long LargeEchoed { get; init; }
    // Authoritative outstanding round-trips (outgoing request awaiting its response). Counted from
    // the pending-request map, so it's never negative even when a response is redelivered
    // (at-least-once) - unlike sent-minus-acked.
    public int SmallInFlight { get; init; }
    public int LargeInFlight { get; init; }
    public long Notifications { get; init; }
    public long Errors { get; init; }
    public long SmallBytes { get; init; }
    public long LargeBytes { get; init; }
    public LatencySnapshot SmallRoundtrip { get; init; } = LatencySnapshot.Empty;
    public LatencySnapshot LargeRoundtrip { get; init; } = LatencySnapshot.Empty;
    // Time for Bus.Send to complete = the inbound leg (edge -> SignalR -> worker -> ASB enqueue).
    // Comparing this against the round-trip splits the worker/SignalR transfer into inbound vs the
    // rest (handling + outbound worker->edge).
    public LatencySnapshot SmallSend { get; init; } = LatencySnapshot.Empty;
    public LatencySnapshot LargeSend { get; init; } = LatencySnapshot.Empty;
    // Noisy-neighbour split (small): quiet = well-behaved clients, noisy = the flooders. The point
    // of the dampener is to keep QuietRoundtrip low even while noisy clients flood.
    public long QuietSent { get; init; }
    public long NoisySent { get; init; }
    public LatencySnapshot QuietRoundtrip { get; init; } = LatencySnapshot.Empty;
    public LatencySnapshot NoisyRoundtrip { get; init; } = LatencySnapshot.Empty;
}

/// <summary>
/// One controller replica's self-measured handling metrics, pushed to the consolidator (dashboard)
/// out-of-band so the dashboard can sum across all replicas (keyed by <see cref="ReporterId"/>).
/// </summary>
public record ControllerReport
{
    public string ReporterId { get; init; } = "";
    public int Edges { get; init; }
    public long SmallHandled { get; init; }
    public long LargeHandled { get; init; }
    public long SmallBytes { get; init; }
    public long LargeBytes { get; init; }
    public long NotificationsSent { get; init; }
    // Messages this replica deferred (re-scheduled) because the client was over its fair-rate budget.
    public long Deferred { get; init; }
    public LatencySnapshot SmallLatency { get; init; } = LatencySnapshot.Empty;
    public LatencySnapshot LargeLatency { get; init; } = LatencySnapshot.Empty;
}
