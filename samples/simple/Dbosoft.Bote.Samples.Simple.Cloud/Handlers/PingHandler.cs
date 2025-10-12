using Dbosoft.Bote.Rebus.Integration;
using Dbosoft.Bote.Samples.Simple.Messages;
using Microsoft.Extensions.Logging;
using Rebus.Bus;
using Rebus.Handlers;
using Rebus.Pipeline;

namespace Dbosoft.Bote.Samples.Simple.Cloud.Handlers;

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
            [BoteHeaders.TenantId] = MessageContext.Current.Headers[BoteHeaders.TenantId],
            [BoteHeaders.ConnectorId] = MessageContext.Current.Headers[BoteHeaders.ConnectorId],
        });
    }
}
