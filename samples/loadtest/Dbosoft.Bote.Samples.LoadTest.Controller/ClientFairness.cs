using System.Collections.Concurrent;
using System.Diagnostics;

namespace Dbosoft.Bote.Samples.LoadTest.Controller;

/// <summary>
/// Reference "noisy neighbor" dampener: a per-client token bucket. Each client gets a fair rate;
/// a client that floods runs out of tokens and its excess messages are told to defer (re-scheduled
/// on the queue for later) instead of monopolising the shared cloud queue and the controller's
/// worker slots - so well-behaved clients keep being served promptly.
///
/// This is an APPLICATION-level pattern in the cloud service (the framework deliberately funnels all
/// clients through one queue; fairness is the app's job). Buckets are per replica (in-memory), so
/// with multiple replicas the effective global rate is rate x replicas - fine for the demo; a strict
/// global limit would need shared state.
/// </summary>
public sealed class ClientFairness
{
    private readonly bool _enabled;
    private readonly double _ratePerSec;
    private readonly double _burst;
    private readonly ILogger<ClientFairness> _log;
    // One bucket per client id, never evicted: fine for this bounded demo (a fixed EdgeCount set of
    // clients). A real gateway with unbounded/rotating clients would need a TTL/LRU eviction here.
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new();
    private long _admitted;
    private long _deferred;

    public ClientFairness(IConfiguration config, ILogger<ClientFairness> log)
    {
        _log = log;
        _enabled = config.GetValue("LoadTest:Fairness:Enabled", false);
        _ratePerSec = config.GetValue("LoadTest:Fairness:RatePerSec", 5.0);
        _burst = config.GetValue("LoadTest:Fairness:Burst", 10.0);
        _log.LogInformation("ClientFairness init: Enabled={Enabled} RatePerSec={Rate} Burst={Burst}",
            _enabled, _ratePerSec, _burst);
    }

    public bool Enabled => _enabled;

    /// <summary>True = admit and process now; false = over budget, defer by <paramref name="deferBy"/>.</summary>
    public bool TryAdmit(string clientId, out TimeSpan deferBy)
    {
        deferBy = TimeSpan.Zero;
        if (!_enabled) return true;
        var admit = _buckets.GetOrAdd(clientId, _ => new Bucket(_burst)).TryConsume(_ratePerSec, _burst, out deferBy);
        var a = admit ? Interlocked.Increment(ref _admitted) : Interlocked.Read(ref _admitted);
        var d = admit ? Interlocked.Read(ref _deferred) : Interlocked.Increment(ref _deferred);
        if ((a + d) % 200 == 0)
            _log.LogInformation("Fairness clients={Clients} admitted={Admitted} deferred={Deferred} lastClient={Client}",
                _buckets.Count, a, d, clientId);
        return admit;
    }

    private sealed class Bucket(double initialTokens)
    {
        private readonly object _lock = new();
        private double _tokens = initialTokens;
        private long _lastTicks = Stopwatch.GetTimestamp();

        public bool TryConsume(double ratePerSec, double capacity, out TimeSpan deferBy)
        {
            lock (_lock)
            {
                var now = Stopwatch.GetTimestamp();
                var elapsed = (now - _lastTicks) / (double)Stopwatch.Frequency;
                _lastTicks = now;
                _tokens = Math.Min(capacity, _tokens + elapsed * ratePerSec);

                if (_tokens >= 1)
                {
                    _tokens -= 1;
                    deferBy = TimeSpan.Zero;
                    return true;
                }

                // Time until a token is available, plus jitter so a deferred burst doesn't all
                // return at once (thundering herd).
                var seconds = Math.Min(30, (1 - _tokens) / ratePerSec);
                deferBy = TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(Random.Shared.Next(250, 1250));
                return false;
            }
        }
    }
}
