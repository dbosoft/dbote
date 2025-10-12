namespace Dbosoft.Bote.Samples.Chat.Messages;

public class ChatRequest
{
    public required Guid Id { get; set; }

    public required string Message { get; set; }
}
