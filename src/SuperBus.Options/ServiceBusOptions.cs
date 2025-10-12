namespace SuperBus.Options;

public class ServiceBusOptions
{
    public ServiceBusQueuesOptions Queues { get; set; } = new();
}
