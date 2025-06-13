using Microsoft.Extensions.Logging;
using Rebus.Bus;
using Rebus.Handlers;
using Rebus.Pipeline;
using SuperBus.Rebus.Integration;
using SuperBus.Samples.Chat.Messages;

namespace SuperBus.Samples.Chat.Cloud.Handler;

public class ChatRequestHandler(
    IBus bus,
    ILogger<ChatRequestHandler> logger)
    : IHandleMessages<ChatRequest>
{
    public async Task Handle(ChatRequest message)
    {
        logger.LogInformation("Handling chat request from connector {ConnectorId} of tenant {TenantId},",
            MessageContext.Current.Headers[SuperBusHeaders.TenantId],
            MessageContext.Current.Headers[SuperBusHeaders.ConnectorId]);
        
        await bus.Reply(new ChatResponse()
        {
            Id = message.Id,
            Author = MessageContext.Current.Headers[SuperBusHeaders.ConnectorId],
            Message = $"ECHO: {message.Message}",
        });
    }
}
