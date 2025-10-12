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
        var tenantId = MessageContext.Current.Headers[SuperBusHeaders.TenantId];
        var senderConnectorId = MessageContext.Current.Headers[SuperBusHeaders.ConnectorId];

        logger.LogInformation("Broadcasting chat from connector {ConnectorId} of tenant {TenantId}",
            senderConnectorId, tenantId);

        // Send ONE message with Topic header
        // Worker will store it and notify SignalR group
        // Active connectors in group will request and receive the message
        await bus.Send(new ChatResponse()
        {
            Id = message.Id,
            Author = senderConnectorId,
            Message = message.Message,
        }, new Dictionary<string, string>()
        {
            [SuperBusHeaders.TenantId] = tenantId,
            [SuperBusHeaders.Topic] = "chat",
        });
    }
}
