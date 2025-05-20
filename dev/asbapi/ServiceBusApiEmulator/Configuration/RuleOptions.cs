namespace ServiceBusApiEmulator.Configuration;

public class RuleOptions
{
    public string Name { get; set; } = string.Empty;

    public RulePropertiesOptions Properties { get; set; } = new();
}
