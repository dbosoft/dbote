namespace ServiceBusApiEmulator.Configuration;

public class SubscriptionOptions
{
    public string Name { get; set; } = string.Empty;

    public SubscriptionPropertiesOptions? Properties { get; set; }

    public List<RuleOptions> Rules { get; set; } = new();
}
