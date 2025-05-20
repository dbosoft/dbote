namespace ServiceBusApiEmulator.Configuration;

public class RulePropertiesOptions
{
    public string FilterType { get; set; } = string.Empty;

    public CorrelationFilterOptions? CorrelationFilter { get; set; }

    public SqlFilterOptions? SqlFilter { get; set; }
    
    public SqlActionOptions? Action { get; set; }
}
