using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Dbosoft.Bote.Options;
using Microsoft.Extensions.Options;

namespace Dbosoft.Bote.BoteWorker.Services;

/// <summary>
/// Default implementation that uses centralized storage accounts for all tenants.
/// </summary>
public class DefaultTenantStorageResolver(IOptions<ClientStorageOptions> options) : ITenantStorageResolver
{
    public ValueTask<(BlobServiceClient, string)> ResolveClientDataStore(string tenantId)
    {
        var client = new BlobServiceClient(options.Value.Connection);
        return ValueTask.FromResult((client, $"{options.Value.Prefix}-ds"));

    }

    public ValueTask<QueueClient> ResolveQueueClient(string tenantId, string id)
    {
        var client = new QueueClient(options.Value.Connection,
            $"{options.Value.Prefix}-{tenantId}-{id}");
        return new ValueTask<QueueClient>(client);
    }
}