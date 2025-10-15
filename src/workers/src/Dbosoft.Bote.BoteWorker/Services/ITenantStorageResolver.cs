using Azure.Storage.Blobs;
using Azure.Storage.Queues;

namespace Dbosoft.Bote.BoteWorker.Services;

/// <summary>
/// Resolves storage accounts and containers for tenants.
/// </summary>
public interface ITenantStorageResolver
{
    /// <summary>
    /// Resolves the storage account and container for a tenant's datastore.
    /// </summary>
    ValueTask<(BlobServiceClient Storage, string Container)> ResolveClientDataStore(string tenantId);

    /// <summary>
    /// Resolves tenant's queue client
    /// </summary>
    ValueTask<QueueClient> ResolveQueueClient(string tenantId, string id);

}