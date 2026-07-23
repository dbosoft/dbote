namespace Dbosoft.Bote.Samples.LoadTest.Controller;

/// <summary>
/// Pushes this replica's handling metrics to the consolidator (dashboard) periodically, keyed by a
/// per-replica id, so the dashboard can sum across all replicas and show the live replica count.
/// Best-effort: failures never disturb message handling.
/// </summary>
public sealed class ControllerReporter(
    EdgeRegistry registry,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<ControllerReporter> logger) : BackgroundService
{
    // A stable-per-process id so each replica is a distinct reporter (HOSTNAME on Container Apps).
    private readonly string _reporterId =
        Environment.GetEnvironmentVariable("CONTAINER_APP_REPLICA_NAME")
        ?? Environment.MachineName + "-" + Guid.NewGuid().ToString("N")[..6];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consolidatorUrl = configuration["LoadTest:ConsolidatorUrl"];
        if (string.IsNullOrWhiteSpace(consolidatorUrl))
        {
            logger.LogWarning("LoadTest:ConsolidatorUrl not set - controller metrics will not be reported.");
            return;
        }

        var client = httpClientFactory.CreateClient();
        var url = $"{consolidatorUrl.TrimEnd('/')}/controller-report";
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
            catch (OperationCanceledException) { break; }

            try { await client.PostAsJsonAsync(url, registry.ToReport(_reporterId), stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch { /* dashboard is best-effort */ }
        }
    }
}
