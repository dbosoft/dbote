using System.Diagnostics;

namespace Dbosoft.Bote.BoteWorker;

/// <summary>
/// Lightweight per-phase timing so we can see, under load, where the worker actually spends time on
/// the message hot path (inbound client->cloud and outbound cloud->client) before optimizing on a
/// hunch. Purely diagnostic; exposed as JSON by the Metrics function.
/// </summary>
public sealed class WorkerMetrics
{
    public sealed class PhaseMeter
    {
        private readonly object _lock = new();
        private long _count;
        private double _sumMs;
        private double _maxMs;

        public void Record(double ms)
        {
            lock (_lock)
            {
                _count++;
                _sumMs += ms;
                if (ms > _maxMs) _maxMs = ms;
            }
        }

        public object Snapshot()
        {
            lock (_lock)
            {
                return new
                {
                    count = _count,
                    avgMs = _count == 0 ? 0 : Math.Round(_sumMs / _count, 3),
                    maxMs = Math.Round(_maxMs, 3),
                };
            }
        }
    }

    // Inbound: client -> SignalR -> worker -> Service Bus.
    public PhaseMeter InToServiceBus { get; } = new();
    public PhaseMeter InAsbSend { get; } = new();
    public PhaseMeter InTotal { get; } = new();

    // Outbound: Service Bus -> worker -> Storage Queue (+ SignalR notify).
    public PhaseMeter OutToBote { get; } = new();
    public PhaseMeter OutSerialize { get; } = new();
    public PhaseMeter OutStorageSend { get; } = new();
    public PhaseMeter OutSignalRNotify { get; } = new();
    public PhaseMeter OutForwardTotal { get; } = new();

    /// <summary>Elapsed milliseconds since a <see cref="Stopwatch.GetTimestamp"/> mark.</summary>
    public static double MsSince(long startTimestamp) =>
        (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;

    public object Snapshot() => new
    {
        inbound = new
        {
            total = InTotal.Snapshot(),
            toServiceBus = InToServiceBus.Snapshot(),
            asbSend = InAsbSend.Snapshot(),
        },
        outbound = new
        {
            forwardTotal = OutForwardTotal.Snapshot(),
            toBote = OutToBote.Snapshot(),
            serialize = OutSerialize.Snapshot(),
            storageSend = OutStorageSend.Snapshot(),
            signalRNotify = OutSignalRNotify.Snapshot(),
        },
    };
}
