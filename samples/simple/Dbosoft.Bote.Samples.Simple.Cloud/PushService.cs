using Dbosoft.Bote.Rebus.Integration;
using Dbosoft.Bote.Samples.Simple.Messages;
using Microsoft.Extensions.Hosting;
using Rebus.Bus;

namespace Dbosoft.Bote.Samples.Simple.Cloud;

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
                [BoteHeaders.TenantId] = "tenant-a",
                [BoteHeaders.ConnectorId] = "connector-a",
            });

            await bus.Send(new PushMessage()
            {
                Message = $"Push {i}",
                Counter = i,
            }, new Dictionary<string, string>()
            {
                [BoteHeaders.TenantId] = "tenant-b",
                [BoteHeaders.ConnectorId] = "connector-a",
            });
            i++;
        }
    }
}
