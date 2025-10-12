using Azure.Messaging.ServiceBus;
using Dbosoft.Bote.Transport.Abstractions;

namespace Dbosoft.Bote.BoteWorker.Converters;

public interface IMessageConverter
{
    ServiceBusMessage ToServiceBus(BoteMessage boteMessage);
    BoteMessage ToBote(ServiceBusReceivedMessage serviceBusMessage);
}
