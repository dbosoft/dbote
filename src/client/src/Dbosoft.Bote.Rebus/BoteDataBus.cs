using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Rebus.DataBus;

namespace Dbosoft.Bote.Rebus;

internal class BoteDataBus(
    ISignalRClient signalRClient)
    : IDataBusStorage
{
    public async Task Save(string id, Stream source, Dictionary<string, string>? metadata)
    {
        // Get upload URI for Client
        var uploadUri = await signalRClient.GetDataBusAttachmentUploadUri(id);

        var blobClient = new BlobClient(new Uri(uploadUri));

        var blobMetadata = metadata != null
            ? new Dictionary<string, string>(metadata)
            : new Dictionary<string, string>();

        await blobClient.UploadAsync(source, new BlobUploadOptions
        {
            Metadata = blobMetadata
        });

        await signalRClient.NotifyAttachmentUploaded(id);
    }

    public async Task<Stream> Read(string id)
    {
        // Get read URI for Client
        var readUri = await signalRClient.GetDataBusAttachmentUri(id);

        var blobClient = new BlobClient(new Uri(readUri));
        return await blobClient.OpenReadAsync();
    }

    public async Task<Dictionary<string, string>> ReadMetadata(string id)
    {
        // Get filtered metadata from BoteWorker (internal metadata removed)
        return await signalRClient.GetAttachmentMetadata(id);
    }
}
