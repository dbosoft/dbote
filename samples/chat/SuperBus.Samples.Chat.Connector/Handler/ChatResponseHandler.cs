using Rebus.Handlers;
using SuperBus.Samples.Chat.Connector.Services;
using SuperBus.Samples.Chat.Messages;

namespace SuperBus.Samples.Chat.Connector.Handler;

public class ChatResponseHandler(
    IChatService chatService
    ) : IHandleMessages<ChatResponse>
{
    public async Task Handle(ChatResponse message)
    {
        await chatService.NotifyMessageReceivedAsync(message);
    }
}
