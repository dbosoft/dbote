namespace ServiceBusApiEmulator.Configuration;

public class TopicPropertiesModel
{
    public string? DefaultMessageTimeToLive { get; set; } = "PT1H";

    public string? DuplicateDetectionHistoryTimeWindow { get; set; } = "PT20S";
    
    public bool? RequiresDuplicateDetection { get; set; } = false;
}
