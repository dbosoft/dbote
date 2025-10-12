namespace Dbosoft.Bote.Samples.Chat.Messages;

public class ChatResponse
{
    public required Guid Id { get; set; }

    public required string Author { get; set; }

    public required string Message { get; set; }
}
