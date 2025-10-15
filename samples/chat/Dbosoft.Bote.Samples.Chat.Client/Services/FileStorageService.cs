using Rebus.DataBus;

namespace Dbosoft.Bote.Samples.Chat.Client.Services;

public interface IFileStorageService
{
    void StoreFile(string fileId, DataBusAttachment attachment, string fileName);
    (DataBusAttachment? Attachment, string? FileName) GetFile(string fileId);
}

public class FileStorageService : IFileStorageService
{
    private readonly Dictionary<string, (DataBusAttachment Attachment, string FileName)> _files = new();

    public void StoreFile(string fileId, DataBusAttachment attachment, string fileName)
    {
        _files[fileId] = (attachment, fileName);
    }

    public (DataBusAttachment? Attachment, string? FileName) GetFile(string fileId)
    {
        if (_files.TryGetValue(fileId, out var file))
        {
            return (file.Attachment, file.FileName);
        }
        return (null, null);
    }
}
