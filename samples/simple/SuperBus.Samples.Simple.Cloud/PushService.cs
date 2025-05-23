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
                [Headers.TenantId] = "tenant-1",
                [Headers.AgentId] = "agent-1",
            });

            await bus.Send(new PushMessage()
            {
                Message = $"Push {i}",
                Counter = i,
            }, new Dictionary<string, string>()
            {
                [Headers.TenantId] = "tenant-2",
                [Headers.AgentId] = "agent-2",
            });
            i++;
        }
    }
}
