using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Rebus.Bus;
using Rebus.Handlers;
using SuperBus.Samples.Simple.Messages;

namespace SuperBus.Samples.Simple.Cloud.Handlers;

public class PingHandler(
    IBus bus,
    ILogger<PingHandler> logger)
    : IHandleMessages<PingMessage>
{
    public async Task Handle(PingMessage message)
    {
        logger.LogInformation("PING!: {Message} - {Counter}", message.Message, message.Counter);
        await bus.Reply(new PongMessage()
        {
            Message = $"Hello back {message.Counter}",
            Counter = message.Counter,
        });
    }
}
