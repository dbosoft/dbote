using Dbosoft.Bote.Primitives;
using Dbosoft.Bote.Samples.Chat.Messages;
using Microsoft.Extensions.Logging;
using Rebus.Bus;
using Rebus.Handlers;
using Rebus.Pipeline;

namespace Dbosoft.Bote.Samples.Chat.Cloud.Handler;

public class FileShareRequestHandler(
    IBus bus,
    ILogger<FileShareRequestHandler> logger)
    : IHandleMessages<FileShareRequest>
{
    public async Task Handle(FileShareRequest message)
    {
        var tenantId = MessageContext.Current.Headers[BoteHeaders.TenantId];
        var senderClientId = MessageContext.Current.Headers[BoteHeaders.ClientId];

        // Log all headers for debugging
        logger.LogInformation("FileShareRequest received. Headers: {Headers}",
            string.Join(", ", MessageContext.Current.Headers.Select(h => $"{h.Key}={h.Value}")));

        logger.LogInformation("Broadcasting file '{FileName}' from client {ClientId} of tenant {TenantId}",
            message.FileName, senderClientId, tenantId);

        // Read the attachment, create a copy with new ID, and send it back to demonstrate bidirectional flow
        logger.LogInformation("Creating copy of file '{FileName}' to demonstrate bi-directional messaging", message.FileName);

        await using var fileStream = await message.FileData.OpenRead();
        using var memoryStream = new MemoryStream();
        await fileStream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        var copiedAttachment = await bus.Advanced.DataBus.CreateAttachment(memoryStream);

        await bus.Send(new FileShareMessage()
        {
            Id = Guid.NewGuid(),
            Author = "Cloud (copy)",
            FileName = $"Copy_of_{message.FileName}",
            FileData = copiedAttachment,
        }, new Dictionary<string, string>()
        {
            [BoteHeaders.TenantId] = tenantId,
            [BoteHeaders.Topic] = "chat",
            [BoteHeaders.AttachmentId] = copiedAttachment.Id,
        });

        logger.LogInformation("Sent copy of file '{FileName}' with new attachment ID {AttachmentId}", message.FileName,
            copiedAttachment.Id);
    }
}
