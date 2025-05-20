using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Rebus.Handlers;
using SuperBus.Samples.Simple.Messages;

namespace SuperBus.Samples.Simple.Tenant.Handlers;

public class PongHandler(
    ILogger<PongHandler> logger) : IHandleMessages<PongMessage>
{
    public Task Handle(PongMessage message)
    {
        logger.LogInformation("PONG!: {Message} - {Counter}", message.Message, message.Counter);
        return Task.CompletedTask;
    }
}
