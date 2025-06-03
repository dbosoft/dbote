using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rebus.AzureServiceBus.NameFormat;

namespace SuperBus.Rebus.Integration;

public class SuperBusNameFormatter(
    INameFormatter nameFormatter,
    string storagePrefix)
    : INameFormatter
{
    public string FormatQueueName(string queueName)
    {
        var formattedName = nameFormatter.FormatQueueName(queueName);
        return formattedName.StartsWith($"{storagePrefix}-connectors-")
            ? $"{storagePrefix}-connectors"
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
