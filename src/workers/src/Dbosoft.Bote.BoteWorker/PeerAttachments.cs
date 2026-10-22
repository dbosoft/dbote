using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Dbosoft.Bote.Primitives;

namespace Dbosoft.Bote.BoteWorker;

/// <summary>
/// Hands a client's attachment to another client of its tenant: copies the sender's inbox copy
/// to where the receiver reads attachments sent to it (<see cref="DataBusPaths.Delivered"/>).
/// </summary>
internal static class PeerAttachments
{
    /// <summary>
    /// Starts the copy; it may still be running on return, and the message waits for it on the
    /// clients queue. Never copies over an attachment that is already there - the path is the
    /// one the tenant's attachments from the cloud use as well - except over the sender's own
    /// earlier copy that failed.
    /// </summary>
    /// <exception cref="InvalidOperationException">The id is taken by another attachment.</exception>
    public static async Task DeliverAsync(BlobClient inboxCopy, BlobContainerClient clientDataStore,
        string tenantId, string senderClientId, string attachmentId)
    {
        var sourceProperties = await inboxCopy.GetPropertiesAsync();
        var senderKey = BoteClientNames.ClientKey(tenantId, senderClientId);
        var metadata = new Dictionary<string, string>(sourceProperties.Value.Metadata)
        {
            [BoteStorageConstants.Metadata.TenantId] = tenantId,
            [BoteStorageConstants.Metadata.SenderKey] = senderKey,
        };
        var destination = clientDataStore.GetBlobClient(DataBusPaths.Delivered(tenantId, attachmentId));
        var sourceUri = inboxCopy.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddHours(2));

        for (var attempt = 0; ; attempt++)
        {
            // Looked at first, not only left to the condition below: the storage emulator fails a
            // conditional copy onto an existing blob with an error instead of 409/412.
            if (await ExistingAsync(destination) is { } existing)
            {
                if (!existing.Metadata.TryGetValue(BoteStorageConstants.Metadata.SenderKey, out var key)
                    || key != senderKey)
                    throw new InvalidOperationException($"Attachment {attachmentId} already exists.");

                // A redelivered message: what the first attempt copied is it, unless that copy
                // failed - then it goes again.
                if (existing.BlobCopyStatus is not (CopyStatus.Failed or CopyStatus.Aborted) || attempt > 0)
                    return;
                await destination.DeleteIfExistsAsync(conditions: new BlobRequestConditions { IfMatch = existing.ETag });
            }

            try
            {
                // The condition covers a second copy that started since the look above.
                await destination.StartCopyFromUriAsync(sourceUri, new BlobCopyFromUriOptions
                {
                    Metadata = metadata,
                    DestinationConditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                });
                return;
            }
            catch (RequestFailedException ex) when (ex.Status is 409 or 412 && attempt == 0)
            {
                // Taken meanwhile: decide on what is there now.
            }
        }
    }

    private static async Task<BlobProperties?> ExistingAsync(BlobClient blob)
    {
        try
        {
            return (await blob.GetPropertiesAsync()).Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    /// <summary>Whether a blob was put there by <see cref="DeliverAsync"/>.</summary>
    public static bool IsPeerCopy(IDictionary<string, string> metadata) =>
        metadata.ContainsKey(BoteStorageConstants.Metadata.SenderKey);
}
