namespace Dbosoft.Bote.BoteWorker;

/// <summary>
/// Internal message to remove a client and all its subscriptions.
/// Can be published by management/admin tools.
/// </summary>
internal record RemoveClient
{
    public required string TenantId { get; init; }
    public required string ClientId { get; init; }
}

/// <summary>
/// Internal message to remove specific subscriptions for a client (batch operation).
/// Can be published by management/admin tools or by the client itself.
/// </summary>
internal record RemoveSubscriptions
{
    public required string TenantId { get; init; }
    public required string ClientId { get; init; }
    public required List<string> Topics { get; init; }
}
