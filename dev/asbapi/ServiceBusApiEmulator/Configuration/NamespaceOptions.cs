namespace ServiceBusApiEmulator.Configuration;

public class NamespaceOptions
{
    public string Name { get; set; } = string.Empty;

    public List<QueueOptions> Queues { get; set; } = new();

    public List<TopicModel> Topics { get; set; } = new();
}
