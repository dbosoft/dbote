using Rebus.DataBus;

namespace Dbosoft.Bote.Samples.Chat.Messages;

public class FileShareRequest
{
    public required Guid Id { get; set; }

    public required string FileName { get; set; }

    public required DataBusAttachment FileData { get; set; }
}
