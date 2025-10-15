using System.Collections.Concurrent;
using Rebus.DataBus;

namespace Dbosoft.Bote.Samples.Chat.Cloud;

public class InMemoryDataBusStorage : IDataBusStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _storage = new();
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _metadata = new();

    public Task Save(string id, Stream source, Dictionary<string, string>? metadata)
    {
        using var memoryStream = new MemoryStream();
        source.CopyTo(memoryStream);
        _storage[id] = memoryStream.ToArray();
        _metadata[id] = metadata != null ? new Dictionary<string, string>(metadata) : new Dictionary<string, string>();
        return Task.CompletedTask;
    }

    public Task<Stream> Read(string id)
    {
        if (!_storage.TryGetValue(id, out var data))
        {
            throw new FileNotFoundException($"Attachment {id} not found in data bus");
        }

        return Task.FromResult<Stream>(new MemoryStream(data));
    }

    public Task<Dictionary<string, string>> ReadMetadata(string id)
    {
        if (!_metadata.TryGetValue(id, out var metadata))
        {
            throw new FileNotFoundException($"Metadata for attachment {id} not found");
        }

        return Task.FromResult(new Dictionary<string, string>(metadata));
    }
}
