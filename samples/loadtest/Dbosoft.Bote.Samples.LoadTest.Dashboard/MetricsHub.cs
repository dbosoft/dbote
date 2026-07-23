using System.Collections.Concurrent;
using Dbosoft.Bote.Samples.LoadTest.Messages;

namespace Dbosoft.Bote.Samples.LoadTest.Dashboard;

/// <summary>
/// Aggregates metrics pushed by every load-test component so the dashboard shows one consolidated
/// view no matter how many instances run. Edge load generators and controller replicas each push a
/// report keyed by their own ReporterId; the hub sums the active ones (reported recently). The
/// number of active controller reporters is the live replica count - the scale-out signal.
/// </summary>
public sealed class MetricsHub
{
    // A reporter is "active" if it has pushed within this window (stale replicas/generators drop off).
    private static readonly TimeSpan ActiveWindow = TimeSpan.FromSeconds(12);

    private readonly ConcurrentDictionary<string, (EdgeReport Report, DateTimeOffset At)> _edges = new();
    private readonly ConcurrentDictionary<string, (ControllerReport Report, DateTimeOffset At)> _controllers = new();
    private volatile StorageMetrics? _storage;

    public void StoreEdgeReport(EdgeReport report) => _edges[report.ReporterId] = (report, DateTimeOffset.UtcNow);
    public void StoreControllerReport(ControllerReport report) => _controllers[report.ReporterId] = (report, DateTimeOffset.UtcNow);
    public void SetStorageMetrics(StorageMetrics metrics) => _storage = metrics;

    public object MetricsSnapshot()
    {
        var cutoff = DateTimeOffset.UtcNow - ActiveWindow;
        var edges = _edges.Values.Where(v => v.At >= cutoff).Select(v => v.Report).ToList();
        var controllers = _controllers.Values.Where(v => v.At >= cutoff).Select(v => v.Report).ToList();

        return new
        {
            serverTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            controller = new
            {
                replicas = controllers.Count,
                edges = controllers.Sum(c => c.Edges),
                smallHandled = controllers.Sum(c => c.SmallHandled),
                largeHandled = controllers.Sum(c => c.LargeHandled),
                smallBytes = controllers.Sum(c => c.SmallBytes),
                largeBytes = controllers.Sum(c => c.LargeBytes),
                notificationsSent = controllers.Sum(c => c.NotificationsSent),
                deferred = controllers.Sum(c => c.Deferred),
                smallLatency = Merge(controllers.Select(c => c.SmallLatency)),
                largeLatency = Merge(controllers.Select(c => c.LargeLatency)),
            },
            storage = _storage,
            edgeGenerators = edges.Count,
            edgeReports = edges,
        };
    }

    /// <summary>Combine latency snapshots across reporters (sum count, weighted avg, max of p95/max).</summary>
    private static LatencySnapshot Merge(IEnumerable<LatencySnapshot> snapshots)
    {
        long count = 0;
        double weightedAvg = 0, p95 = 0, max = 0;
        foreach (var s in snapshots)
        {
            if (s.Count == 0) continue;
            count += s.Count;
            weightedAvg += s.AvgMs * s.Count;
            p95 = Math.Max(p95, s.P95Ms);
            max = Math.Max(max, s.MaxMs);
        }
        return count == 0
            ? LatencySnapshot.Empty
            : new LatencySnapshot(count, Math.Round(weightedAvg / count, 1), Math.Round(p95, 1), Math.Round(max, 1));
    }
}
