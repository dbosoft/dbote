using Dbosoft.Bote.Samples.Chat.Connector.Services;
using Dbosoft.Bote.Samples.Chat.Messages;
using Rebus.Handlers;

namespace Dbosoft.Bote.Samples.Chat.Connector.Handler;

public class ChatResponseHandler(
    IChatService chatService
    ) : IHandleMessages<ChatResponse>
{
    public async Task Handle(ChatResponse message)
    {
        await chatService.NotifyMessageReceivedAsync(message);
    }
}
