namespace Dbosoft.Bote.Connector.Options;

public class BoteOptions
{
    public string Endpoint { get; set; } = null!;

    public string TenantId { get; set; } = null!;

    public string ConnectorId { get; set; } = null!;

    public BoteAuthenticationOptions Authentication { get; set; } = new();

    public BoteQueuesOptions Queues { get; set; } = new();
}
