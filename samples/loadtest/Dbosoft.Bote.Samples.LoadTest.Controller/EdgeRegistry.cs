using System.Collections.Concurrent;
using Dbosoft.Bote.Samples.LoadTest.Messages;

namespace Dbosoft.Bote.Samples.LoadTest.Controller;

/// <summary>
/// This replica's in-memory handling metrics. Deliberately not durable - this is a load demo, and
/// the metrics are pushed to the consolidator (dashboard) which sums across all replicas.
/// Everything is thread-safe (the Rebus handlers run concurrently).
/// </summary>
public class EdgeRegistry
{
    private readonly ConcurrentDictionary<string, byte> _edges = new();
    private readonly LatencyMeter _smallHandle = new();
    private readonly LatencyMeter _largeHandle = new();
    private long _smallHandled;
    private long _largeHandled;
    private long _smallBytes;
    private long _largeBytes;
    private long _notificationsSent;
    private long _deferred;

    public int EdgeCount => _edges.Count;

    public void RecordDeferred() => Interlocked.Increment(ref _deferred);

    public void RegisterEdge(string edgeId) => _edges[edgeId] = 0;

    public void RecordSmall(long bytes, double handleMs)
    {
        Interlocked.Increment(ref _smallHandled);
        Interlocked.Add(ref _smallBytes, bytes);
        _smallHandle.Record(handleMs);
    }

    public void RecordLarge(long bytes, double handleMs)
    {
        Interlocked.Increment(ref _largeHandled);
        Interlocked.Add(ref _largeBytes, bytes);
        _largeHandle.Record(handleMs);
    }

    public void NotificationSent() => Interlocked.Increment(ref _notificationsSent);

    public ControllerReport ToReport(string reporterId) => new()
    {
        ReporterId = reporterId,
        Edges = _edges.Count,
        SmallHandled = Interlocked.Read(ref _smallHandled),
        LargeHandled = Interlocked.Read(ref _largeHandled),
        SmallBytes = Interlocked.Read(ref _smallBytes),
        LargeBytes = Interlocked.Read(ref _largeBytes),
        NotificationsSent = Interlocked.Read(ref _notificationsSent),
        Deferred = Interlocked.Read(ref _deferred),
        SmallLatency = _smallHandle.Snapshot(),
        LargeLatency = _largeHandle.Snapshot(),
    };
}
