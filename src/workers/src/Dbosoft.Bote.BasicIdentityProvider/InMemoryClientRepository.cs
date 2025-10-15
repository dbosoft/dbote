using System.Collections.Concurrent;

namespace Dbosoft.Bote.BasicIdentityProvider;

public interface IClientRepository
{
    Task<ClientInfo?> GetById(string tenantId, string clientId);
    Task Register(ClientInfo client);
}

public class ClientInfo
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty; // Base64 SubjectPublicKeyInfo
}

public class InMemoryClientRepository : IClientRepository
{
    private readonly ConcurrentDictionary<string, ClientInfo> _clients = new();

    public Task<ClientInfo?> GetById(string tenantId, string clientId)
    {
        var key = $"{tenantId}-{clientId}";
        _clients.TryGetValue(key, out var client);
        return Task.FromResult(client);
    }

    public Task Register(ClientInfo client)
    {
        var key = $"{client.TenantId}-{client.Id}";
        _clients[key] = client;
        return Task.CompletedTask;
    }
}
