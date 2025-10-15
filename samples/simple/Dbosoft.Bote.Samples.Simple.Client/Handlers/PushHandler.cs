using Dbosoft.Bote.Samples.Simple.Messages;
using Microsoft.Extensions.Logging;
using Rebus.Handlers;

namespace Dbosoft.Bote.Samples.Simple.Client.Handlers;

public class PushHandler(ILogger<PushHandler> logger) : IHandleMessages<PushMessage>
{
    public Task Handle(PushMessage message)
    {
        logger.LogInformation("PUSH!: {Message} - {Counter}", message.Message, message.Counter);
        return Task.CompletedTask;
    }
}
