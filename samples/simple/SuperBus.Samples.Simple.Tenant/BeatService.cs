using Microsoft.Extensions.Hosting;
using Rebus.Bus;
using SuperBus.Samples.Simple.Messages;

namespace SuperBus.Samples.Simple.Tenant;

public class BeatService(IBus bus)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int i = 1;
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(5000, stoppingToken);
            await bus.Send(new PingMessage()
            {
                Message = $"Hello {i}",
                Counter = i,
            });
            i++;
        }
    }
}
