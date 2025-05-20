using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Rebus.Bus;
using Rebus.Config;
using SuperBus.Samples.Simple.Messages;

namespace SuperBus.Samples.Simple.Tenant
{
    internal class BeatService(IBus bus)
        : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            int i = 1;
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(2000, stoppingToken);
                await bus.Advanced.Routing.Send("sample-simple-cloud-queue", new PingMessage()
                {
                    Message = $"Hello {i}",
                    Counter = i,
                });
                i++;
            }
        }
    }
}
