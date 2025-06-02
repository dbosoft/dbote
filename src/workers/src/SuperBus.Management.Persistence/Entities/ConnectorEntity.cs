using Dbosoft.Azure.TableStorage;

namespace SuperBus.Management.Persistence.Entities;

public class ConnectorEntity : IndexedTableEntity
{
    public static Func<string, string, (string PartitionKey, string RowKey)>
        PointQuery = (tenantId, connectorId) => (tenantId.AsPartitionKey(), connectorId.AsPartitionKey());

    public string Id { get; set; }

    public string TenantId { get; set; }

    public string PublicKey { get; set; }

    public sealed override string PrimaryPartitionKey => TenantId.AsPartitionKey();

    public sealed override string PrimaryRowKey => Id.AsPartitionKey();

    public override IEnumerable<(string PartitionKey, string RowKey)> GetCurrentIndexKeys()
    {
        yield return ($"Member:{Id.AsPartitionKey()}", TenantId.AsPartitionKey());
    }

    public override IEnumerable<(string PartitionKey, string RowKey)> GetPreviousIndexKeys()
    {
        yield return ($"Member:{Id.AsPartitionKey()}", TenantId.AsPartitionKey());
    }
}
