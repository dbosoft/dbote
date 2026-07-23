using Dbosoft.Bote.Primitives;
using Dbosoft.Bote.Samples.LoadTest.Messages;
using Rebus.Bus;

namespace Dbosoft.Bote.Samples.LoadTest.Controller;

/// <summary>
/// Periodically broadcasts a short notification to all edges of the load tenant on the
/// "notify" topic - broadcasts are used only for lightweight fan-out, never for bulk data.
/// </summary>
public class NotificationBroadcaster(
    IBus bus,
    EdgeRegistry registry,
    IConfiguration configuration,
    ILogger<NotificationBroadcaster> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tenant = configuration["LoadTest:Tenant"] ?? "load";
        var interval = TimeSpan.FromSeconds(configuration.GetValue("LoadTest:BroadcastIntervalSeconds", 10));
        var sequence = 0L;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (registry.EdgeCount == 0)
                continue;

            sequence++;
            await bus.Send(
                new Notification { Sequence = sequence, Text = $"controller tick {sequence}" },
                new Dictionary<string, string>
                {
                    [BoteHeaders.TenantId] = tenant,
                    [BoteHeaders.Topic] = "notify",
                });
            registry.NotificationSent();
            logger.LogInformation("Broadcast notification {Sequence} to tenant {Tenant} ({Edges} edges)",
                sequence, tenant, registry.EdgeCount);
        }
    }
}
