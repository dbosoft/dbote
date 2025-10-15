using Rebus.DataBus;

namespace Dbosoft.Bote.Samples.Chat.Messages;

public class FileShareMessage
{
    public required Guid Id { get; set; }

    public required string FileName { get; set; }

    public required string Author { get; set; }

    public required DataBusAttachment FileData { get; set; }
}
