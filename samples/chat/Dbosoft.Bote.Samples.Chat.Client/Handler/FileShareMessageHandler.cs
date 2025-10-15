using Dbosoft.Bote.Samples.Chat.Client.Services;
using Dbosoft.Bote.Samples.Chat.Messages;
using Rebus.Handlers;

namespace Dbosoft.Bote.Samples.Chat.Client.Handler;

public class FileShareMessageHandler(
    IChatService chatService,
    IFileStorageService fileStorage,
    ILogger<FileShareMessageHandler> logger
    ) : IHandleMessages<FileShareMessage>
{
    public async Task Handle(FileShareMessage message)
    {
        logger.LogInformation("Received file '{FileName}' from {Author}", message.FileName, message.Author);

        // Store the file for download
        fileStorage.StoreFile(message.Id.ToString(), message.FileData, message.FileName);

        await chatService.NotifyFileReceivedAsync(message);
    }
}
