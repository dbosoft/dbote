namespace ServiceBusApiEmulator.Configuration;

public class QueuePropertiesOptions
{
    public bool? DeadLetteringOnMessageExpiration { get; set; } = false;

    public string? DefaultMessageTimeToLive { get; set; } = "PT1H";

    public string? DuplicateDetectionHistoryTimeWindow { get; set; } = "PT20S";

    public string? ForwardDeadLetteredMessagesTo { get; set; } = null;

    public string? ForwardTo { get; set; } = null;

    public string? LockDuration { get; set; } = "PT1M";

    public int? MaxDeliveryCount { get; set; } = 3;

    public bool? RequiresDuplicateDetection { get; set; } = false;

    public bool? RequiresSession { get; set; } = false;
}
