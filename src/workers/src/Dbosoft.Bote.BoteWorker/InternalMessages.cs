namespace Dbosoft.Bote.BoteWorker;

/// <summary>
/// Internal message to remove a connector and all its subscriptions.
/// Can be published by management/admin tools.
/// </summary>
internal record RemoveConnector
{
    public required string TenantId { get; init; }
    public required string ConnectorId { get; init; }
}

/// <summary>
/// Internal message to remove specific subscriptions for a connector (batch operation).
/// Can be published by management/admin tools or by the connector itself.
/// </summary>
internal record RemoveSubscriptions
{
    public required string TenantId { get; init; }
    public required string ConnectorId { get; init; }
    public required List<string> Topics { get; init; }
}
