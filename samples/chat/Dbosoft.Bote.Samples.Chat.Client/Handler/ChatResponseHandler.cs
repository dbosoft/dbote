using Dbosoft.Bote.Samples.Chat.Client.Services;
using Dbosoft.Bote.Samples.Chat.Messages;
using Rebus.Handlers;

namespace Dbosoft.Bote.Samples.Chat.Client.Handler;

public class ChatResponseHandler(
    IChatService chatService
    ) : IHandleMessages<ChatResponse>
{
    public async Task Handle(ChatResponse message)
    {
        await chatService.NotifyMessageReceivedAsync(message);
    }
}
