using Microsoft.Extensions.Hosting;
using Rebus.Bus;
using SuperBus.Rebus.Integration;
using SuperBus.Samples.Simple.Messages;

namespace SuperBus.Samples.Simple.Cloud;

public class PushService(IBus bus) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int i = 1;
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(5000, stoppingToken);
            await bus.Send(new PushMessage()
            {
                Message = $"Push {i}",
                Counter = i,
            }, new Dictionary<string, string>()
            {
                [SuperBusHeaders.TenantId] = "tenant-a",
                [SuperBusHeaders.ConnectorId] = "connector-a",
            });

            await bus.Send(new PushMessage()
            {
                Message = $"Push {i}",
                Counter = i,
            }, new Dictionary<string, string>()
            {
                [SuperBusHeaders.TenantId] = "tenant-b",
                [SuperBusHeaders.ConnectorId] = "connector-a",
            });
            i++;
        }
    }
}
