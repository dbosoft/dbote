using System.Collections.Concurrent;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Dbosoft.Bote.Options;
using Dbosoft.Bote.Primitives;
using Microsoft.Extensions.Options;

namespace Dbosoft.Bote.BoteWorker.Services;

/// <summary>
/// Default implementation that uses centralized storage accounts for all tenants.
/// Azure Storage clients are thread-safe and hold a connection pool, so they are cached and
/// reused rather than re-created on every resolve (N2) - at 1000+ tenants with a high call
/// rate, constructing a fresh client per call is avoidable overhead.
/// </summary>
public class DefaultTenantStorageResolver(IOptions<ClientStorageOptions> options) : ITenantStorageResolver
{
    private readonly Lazy<BlobServiceClient> _blobServiceClient =
        new(() => new BlobServiceClient(options.Value.Connection));

    private readonly ConcurrentDictionary<string, QueueClient> _queueClients = new();

    public ValueTask<(BlobServiceClient, string)> ResolveClientDataStore(string tenantId) =>
        ValueTask.FromResult((_blobServiceClient.Value, $"{options.Value.Prefix}-ds"));

    public ValueTask<QueueClient> ResolveQueueClient(string tenantId, string clientId) =>
        GetQueueClient(BoteClientNames.ClientQueueName(options.Value.Prefix, tenantId, clientId));

    public ValueTask<QueueClient> ResolveErrorQueueClient(string tenantId, string clientId) =>
        GetQueueClient(BoteClientNames.ClientErrorQueueName(options.Value.Prefix, tenantId, clientId));

    private ValueTask<QueueClient> GetQueueClient(string queueName) =>
        ValueTask.FromResult(_queueClients.GetOrAdd(queueName,
            name => new QueueClient(options.Value.Connection, name)));
}