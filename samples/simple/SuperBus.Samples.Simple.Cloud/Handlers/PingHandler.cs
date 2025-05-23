using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Rebus.Bus;
using Rebus.Handlers;
using Rebus.Pipeline;
using SuperBus.Rebus.Integration;
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
        }, new Dictionary<string, string>()
        {
            [Headers.TenantId] = MessageContext.Current.Headers[Headers.TenantId],
            [Headers.ConnectorId] = MessageContext.Current.Headers[Headers.ConnectorId],
        });
    }
}
