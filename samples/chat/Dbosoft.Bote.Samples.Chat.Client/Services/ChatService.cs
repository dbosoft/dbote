using Dbosoft.Bote.Samples.Chat.Messages;

namespace Dbosoft.Bote.Samples.Chat.Client.Services;

public interface IChatService
{
   public event Func<ChatResponse, Task>? OnMessageReceived;
   public event Func<FileShareMessage, Task>? OnFileReceived;

   public Task NotifyMessageReceivedAsync(ChatResponse chatMessage);
   public Task NotifyFileReceivedAsync(FileShareMessage fileMessage);
}

public class ChatService : IChatService
{
    public event Func<ChatResponse, Task>? OnMessageReceived;
    public event Func<FileShareMessage, Task>? OnFileReceived;

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

    public async Task NotifyFileReceivedAsync(FileShareMessage fileMessage)
    {
        if (OnFileReceived is null)
            return;

        var handlers = OnFileReceived.GetInvocationList();
        var tasks = handlers
            .Cast<Func<FileShareMessage, Task>>()
            .Select(h => h(fileMessage));

        await Task.WhenAll(tasks);
    }
}
