namespace ServiceBusApiEmulator.Configuration;

public class UserConfigOptions
{
    public List<NamespaceOptions> Namespaces { get; set; } = new();

    public LoggingOptions Logging { get; set; } = new();
}
