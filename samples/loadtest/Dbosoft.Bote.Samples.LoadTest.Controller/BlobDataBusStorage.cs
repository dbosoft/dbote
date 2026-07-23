using System.Text.Json;
using Azure.Storage.Blobs;
using Rebus.DataBus;

namespace Dbosoft.Bote.Samples.LoadTest.Controller;

/// <summary>
/// Blob-backed <see cref="IDataBusStorage"/> (Azurite locally / Azure Blob in production).
/// Unlike an in-memory store this is shared across horizontally scaled controller instances and
/// does not grow the process heap under load. Data and metadata are stored as two blobs per
/// attachment (a JSON metadata sidecar, since Rebus metadata keys contain '.' which is not a
/// valid blob-metadata key).
/// </summary>
public class BlobDataBusStorage : IDataBusStorage
{
    private readonly BlobContainerClient _container;

    public BlobDataBusStorage(string connectionString, string containerName)
    {
        _container = new BlobServiceClient(connectionString).GetBlobContainerClient(containerName);
        _container.CreateIfNotExists();
    }

    public async Task Save(string id, Stream source, Dictionary<string, string>? metadata)
    {
        await _container.GetBlobClient(DataName(id)).UploadAsync(source, overwrite: true);
        var json = JsonSerializer.Serialize(metadata ?? new Dictionary<string, string>());
        await _container.GetBlobClient(MetadataName(id)).UploadAsync(BinaryData.FromString(json), overwrite: true);
    }

    public async Task<Stream> Read(string id)
    {
        var response = await _container.GetBlobClient(DataName(id)).DownloadStreamingAsync();
        return response.Value.Content;
    }

    public async Task<Dictionary<string, string>> ReadMetadata(string id)
    {
        var response = await _container.GetBlobClient(MetadataName(id)).DownloadContentAsync();
        return JsonSerializer.Deserialize<Dictionary<string, string>>(response.Value.Content.ToString())
               ?? new Dictionary<string, string>();
    }

    private static string DataName(string id) => $"{id}/data";
    private static string MetadataName(string id) => $"{id}/metadata.json";
}
