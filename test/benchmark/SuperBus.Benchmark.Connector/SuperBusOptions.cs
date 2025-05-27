namespace SuperBus.Benchmark.Connector;

public class SuperBusOptions
{
    public string Endpoint { get; set; }

    public string ConnectorId { get; set; }

    public string TenantId { get; set; }

    public string SigningKey { get; set; }

    public string QueuePrefix { get; set; }
}
