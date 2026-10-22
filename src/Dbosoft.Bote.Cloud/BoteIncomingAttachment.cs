using Azure.Storage.Blobs;
using Dbosoft.Bote.Primitives;

namespace Dbosoft.Bote.Cloud;

/// <summary>
/// A DataBus attachment a client sent, waiting in the gateway's inbox. Read it, keep it where
/// the application keeps its data, then <see cref="DeleteAsync"/> it: until then a retried
/// message can still read it, and the inbox lifecycle policy removes what is left behind.
/// </summary>
public sealed class BoteIncomingAttachment
{
    private readonly BlobClient _blob;

    internal BoteIncomingAttachment(string id, BlobClient blob)
    {
        Id = id;
        _blob = blob;
    }

    public string Id { get; }

    public async Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default) =>
        await _blob.OpenReadAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

    /// <summary>The metadata the client gave the attachment, without the gateway's own.</summary>
    public async Task<IReadOnlyDictionary<string, string>> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        var properties = await _blob.GetPropertiesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return BoteStorageConstants.Metadata.FilterUserMetadata(properties.Value.Metadata);
    }

    public Task DeleteAsync(CancellationToken cancellationToken = default) =>
        _blob.DeleteIfExistsAsync(cancellationToken: cancellationToken);
}
