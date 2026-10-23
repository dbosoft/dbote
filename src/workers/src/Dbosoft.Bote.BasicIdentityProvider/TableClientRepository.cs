using Azure;
using Azure.Data.Tables;

namespace Dbosoft.Bote.BasicIdentityProvider;

/// <summary>
/// A shared client registry backed by Azure Table Storage, so registrations made against one
/// instance are visible to every instance. The in-memory repository only works single-instance: on
/// Flex Consumption a client could register on one instance and request a token on another, where
/// the registration (and its public key) would be unknown. Accessed via managed identity.
/// </summary>
public class TableClientRepository : IClientRepository
{
    private readonly TableClient _table;

    public TableClientRepository(TableServiceClient tableService)
    {
        _table = tableService.GetTableClient("clients");
        _table.CreateIfNotExists();
    }

    public async Task<ClientInfo?> GetById(string tenantId, string clientId)
    {
        try
        {
            var entity = await _table.GetEntityAsync<ClientEntity>(tenantId, clientId);
            return new ClientInfo
            {
                TenantId = entity.Value.PartitionKey,
                Id = entity.Value.RowKey,
                PublicKey = entity.Value.PublicKey,
                Scopes = entity.Value.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries),
                Peers = (entity.Value.Peers ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries),
            };
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public Task Register(ClientInfo client) =>
        _table.UpsertEntityAsync(new ClientEntity
        {
            PartitionKey = client.TenantId,
            RowKey = client.Id,
            PublicKey = client.PublicKey,
            Scopes = string.Join(' ', client.Scopes),
            Peers = string.Join('\n', client.Peers),
        });

    private class ClientEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = string.Empty;
        public string RowKey { get; set; } = string.Empty;
        public string PublicKey { get; set; } = string.Empty;
        public string Scopes { get; set; } = string.Empty;
        public string? Peers { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}
