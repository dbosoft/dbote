using Microsoft.Extensions.Logging;
using Rebus.Handlers;
using SuperBus.Samples.Simple.Messages;

namespace SuperBus.Samples.Simple.Connector.Handlers;

public class PongHandler(
    ILogger<PongHandler> logger) : IHandleMessages<PongMessage>
{
    public Task Handle(PongMessage message)
    {
        logger.LogInformation("PONG!: {Message} - {Counter}", message.Message, message.Counter);
        return Task.CompletedTask;
    }
}
