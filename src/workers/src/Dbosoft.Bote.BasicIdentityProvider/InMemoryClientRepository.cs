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

    /// <summary>Scopes the client gets besides <c>bote</c>, e.g. the worker's peer scope.</summary>
    public string[] Scopes { get; set; } = [];
}

public class InMemoryClientRepository : IClientRepository
{
    private readonly ConcurrentDictionary<(string TenantId, string ClientId), ClientInfo> _clients = new();

    public Task<ClientInfo?> GetById(string tenantId, string clientId)
    {
        _clients.TryGetValue((tenantId, clientId), out var client);
        return Task.FromResult(client);
    }

    public Task Register(ClientInfo client)
    {
        _clients[(client.TenantId, client.Id)] = client;
        return Task.CompletedTask;
    }
}
