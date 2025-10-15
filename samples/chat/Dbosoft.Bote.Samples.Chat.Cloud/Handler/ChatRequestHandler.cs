using Dbosoft.Bote.Primitives;
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
        var senderClientId = MessageContext.Current.Headers[BoteHeaders.ClientId];

        logger.LogInformation("Broadcasting chat from client {ClientId} of tenant {TenantId}",
            senderClientId, tenantId);

        await bus.Send(new ChatResponse()
        {
            Id = message.Id,
            Author = senderClientId,
            Message = message.Message,
        }, new Dictionary<string, string>()
        {
            [BoteHeaders.TenantId] = tenantId,
            [BoteHeaders.Topic] = "chat",
        });
    }
}
