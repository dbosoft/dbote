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
    /// Resolves the storage queue a client receives on. Name it after
    /// <see cref="Dbosoft.Bote.Primitives.BoteClientNames"/>, so no two clients share one.
    /// </summary>
    ValueTask<QueueClient> ResolveQueueClient(string tenantId, string clientId);

    /// <summary>
    /// Resolves the storage queue a client moves its poison messages to.
    /// </summary>
    ValueTask<QueueClient> ResolveErrorQueueClient(string tenantId, string clientId);

}