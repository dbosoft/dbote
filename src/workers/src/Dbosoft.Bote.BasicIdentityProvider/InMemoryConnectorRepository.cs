using System.Collections.Concurrent;

namespace Dbosoft.Bote.BasicIdentityProvider;

public interface IConnectorRepository
{
    Task<ConnectorInfo?> GetById(string tenantId, string connectorId);
    Task Register(ConnectorInfo connector);
}

public class ConnectorInfo
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty; // Base64 SubjectPublicKeyInfo
}

public class InMemoryConnectorRepository : IConnectorRepository
{
    private readonly ConcurrentDictionary<string, ConnectorInfo> _connectors = new();

    public Task<ConnectorInfo?> GetById(string tenantId, string connectorId)
    {
        var key = $"{tenantId}-{connectorId}";
        _connectors.TryGetValue(key, out var connector);
        return Task.FromResult(connector);
    }

    public Task Register(ConnectorInfo connector)
    {
        var key = $"{connector.TenantId}-{connector.Id}";
        _connectors[key] = connector;
        return Task.CompletedTask;
    }
}
