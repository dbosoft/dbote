namespace ServiceBusApiEmulator.Configuration;

public class TopicModel
{
    public string Name { get; set; } = string.Empty;

    public TopicPropertiesModel? Properties { get; set; }

    public List<SubscriptionOptions> Subscriptions { get; set; } = new();
}
