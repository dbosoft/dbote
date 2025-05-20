namespace ServiceBusApiEmulator.Configuration;

public class SubscriptionPropertiesOptions
{
    public bool? DeadLetteringOnMessageExpiration { get; set; } = false;

    public string? DefaultMessageTimeToLive { get; set; } = "PT1H";

    public string? LockDuration { get; set; } = "PT1M";

    public int? MaxDeliveryCount { get; set; } = 3;

    public string? ForwardDeadLetteredMessagesTo { get; set; } = null;

    public string? ForwardTo { get; set; } = null;

    public bool? RequiresSession { get; set; } = false;
}
