using SuperBus.Samples.Chat.Messages;

namespace SuperBus.Samples.Chat.Connector.Services;

public interface IChatService
{
   public event Func<ChatResponse, Task>? OnMessageReceived;

   public Task NotifyMessageReceivedAsync(ChatResponse chatMessage);
}

public class ChatService : IChatService
{
    public event Func<ChatResponse, Task>? OnMessageReceived;

    public async Task NotifyMessageReceivedAsync(ChatResponse chatMessage)
    {
        if (OnMessageReceived is null)
            return;

        var handlers = OnMessageReceived.GetInvocationList();
        var tasks = handlers
            .Cast<Func<ChatResponse, Task>>()
            .Select(h => h(chatMessage));
        
        await Task.WhenAll(tasks);
    }
}
