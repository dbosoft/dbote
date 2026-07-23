using Azure.Messaging.ServiceBus.Administration;
using Azure.Storage.Queues;

namespace Dbosoft.Bote.Samples.LoadTest.Dashboard;

/// <summary>A point-in-time sample of where messages sit, measured from queue depths.</summary>
public sealed record StorageMetrics(
    int EdgeQueues, long EdgeQueueDepth, long EdgeQueueMax,
    long? CloudQueueDepth, long? ClientsQueueDepth);

/// <summary>Periodically samples the storage/service-bus queues so the dashboard can show the
/// backlog (cloud queue depth is the scale-out signal).</summary>
public sealed class StorageMetricsCollector : BackgroundService
{
    private readonly MetricsHub _hub;
    private readonly ILogger<StorageMetricsCollector> _logger;
    private readonly QueueServiceClient? _queues;
    private readonly ServiceBusAdministrationClient? _asb;
    private readonly string _edgeQueuePrefix;
    private readonly string _cloudQueue;
    private readonly string _clientsQueue;
    private readonly TimeSpan _interval;

    public StorageMetricsCollector(MetricsHub hub, IConfiguration cfg, ILogger<StorageMetricsCollector> logger)
    {
        _hub = hub;
        _logger = logger;
        var storageConn = cfg["dbote:Cloud:Storage:Connection"];
        if (!string.IsNullOrWhiteSpace(storageConn)) _queues = new QueueServiceClient(storageConn);
        var asbConn = cfg["dbote:Cloud:ServiceBus:Connection"];
        if (!string.IsNullOrWhiteSpace(asbConn)) _asb = new ServiceBusAdministrationClient(asbConn);
        _edgeQueuePrefix = cfg["LoadTest:EdgeQueuePrefix"] ?? "bote-load-";
        _cloudQueue = cfg["dbote:Cloud:ServiceBus:Queues:Cloud"] ?? "bote-sample-loadtest-cloud";
        _clientsQueue = cfg["dbote:Cloud:ServiceBus:Queues:Clients"] ?? "bote-sample-loadtest-clients";
        _interval = TimeSpan.FromSeconds(cfg.GetValue("LoadTest:StorageSampleSeconds", 3));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { _hub.SetStorageMetrics(await SampleAsync(stoppingToken)); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogWarning(ex, "storage sample failed"); }

            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task<StorageMetrics> SampleAsync(CancellationToken ct)
    {
        long total = 0, max = 0;
        var queues = 0;
        if (_queues is not null)
        {
            await foreach (var q in _queues.GetQueuesAsync(prefix: _edgeQueuePrefix, cancellationToken: ct))
            {
                try
                {
                    var props = await _queues.GetQueueClient(q.Name).GetPropertiesAsync(ct);
                    long depth = props.Value.ApproximateMessagesCount;
                    total += depth;
                    if (depth > max) max = depth;
                    queues++;
                }
                catch (OperationCanceledException) { throw; }
                catch { /* a queue can disappear between list and read */ }
            }
        }

        return new StorageMetrics(queues, total, max,
            await AsbDepthAsync(_cloudQueue, ct), await AsbDepthAsync(_clientsQueue, ct));
    }

    private async Task<long?> AsbDepthAsync(string queue, CancellationToken ct)
    {
        if (_asb is null) return null;
        try
        {
            var props = await _asb.GetQueueRuntimePropertiesAsync(queue, ct);
            return props.Value.ActiveMessageCount;
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }
}
