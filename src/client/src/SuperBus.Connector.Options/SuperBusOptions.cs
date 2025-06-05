namespace SuperBus.Connector.Options;

public class SuperBusOptions
{
    public string Endpoint { get; set; } = null!;

    public string TenantId { get; set; } = null!;

    public string ConnectorId { get; set; } = null!;

    public SuperBusAuthenticationOptions Authentication { get; set; } = new();

    public SuperBusQueuesOptions Queues { get; set; } = new();
}
