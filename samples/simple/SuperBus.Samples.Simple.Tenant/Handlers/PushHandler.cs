using Microsoft.Extensions.Logging;
using Rebus.Handlers;
using SuperBus.Samples.Simple.Messages;

namespace SuperBus.Samples.Simple.Tenant.Handlers;

public class PushHandler(ILogger<PushHandler> logger) : IHandleMessages<PushMessage>
{
    public Task Handle(PushMessage message)
    {
        logger.LogInformation("PUSH!: {Message} - {Counter}", message.Message, message.Counter);
        return Task.CompletedTask;
    }
}
