using Rebus.AzureServiceBus.NameFormat;

namespace Dbosoft.Bote.Rebus.Integration;

public class BoteNameFormatter(
    INameFormatter nameFormatter,
    string connectorsQueue)
    : INameFormatter
{
    public string FormatQueueName(string queueName)
    {
        var formattedName = nameFormatter.FormatQueueName(queueName);
        return formattedName.StartsWith($"{connectorsQueue}-")
            ? connectorsQueue
            : formattedName;
    }

    public string FormatSubscriptionName(string subscriptionName)
    {
        return nameFormatter.FormatSubscriptionName(subscriptionName);
    }

    public string FormatTopicName(string topicName)
    {
        return nameFormatter.FormatTopicName(topicName);
    }
}
