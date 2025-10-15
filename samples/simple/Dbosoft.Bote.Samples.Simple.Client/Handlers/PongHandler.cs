using Dbosoft.Bote.Samples.Simple.Messages;
using Microsoft.Extensions.Logging;
using Rebus.Bus;
using Rebus.Handlers;
using Rebus.Messages;
using Rebus.Pipeline;

namespace Dbosoft.Bote.Samples.Simple.Client.Handlers;

public class PongHandler(
    IBus bus,
    IMessageContext messageContext,
    ILogger<PongHandler> logger) : IHandleMessages<PongMessage>
{
    public async Task Handle(PongMessage message)
    {
        if (!messageContext.Headers.ContainsKey(Headers.DeferCount))
        {
            logger.LogInformation("Deferred: {Message} - {Counter}", message.Message, message.Counter);
            await bus.Advanced.TransportMessage.Defer(TimeSpan.FromSeconds(1));
            return;
        }

        logger.LogInformation("PONG!: {Message} - {Counter}", message.Message, message.Counter);
    }
}
