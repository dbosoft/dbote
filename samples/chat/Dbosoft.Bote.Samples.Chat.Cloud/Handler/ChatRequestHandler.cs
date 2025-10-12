using Dbosoft.Bote.Rebus.Integration;
using Dbosoft.Bote.Samples.Chat.Messages;
using Microsoft.Extensions.Logging;
using Rebus.Bus;
using Rebus.Handlers;
using Rebus.Pipeline;

namespace Dbosoft.Bote.Samples.Chat.Cloud.Handler;

public class ChatRequestHandler(
    IBus bus,
    ILogger<ChatRequestHandler> logger)
    : IHandleMessages<ChatRequest>
{
    public async Task Handle(ChatRequest message)
    {
        var tenantId = MessageContext.Current.Headers[BoteHeaders.TenantId];
        var senderConnectorId = MessageContext.Current.Headers[BoteHeaders.ConnectorId];

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
            [BoteHeaders.TenantId] = tenantId,
            [BoteHeaders.Topic] = "chat",
        });
    }
}
