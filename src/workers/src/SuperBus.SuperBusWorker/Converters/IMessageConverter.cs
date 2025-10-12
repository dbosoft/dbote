using Azure.Messaging.ServiceBus;
using SuperBus.Transport.Abstractions;

namespace SuperBus.SuperBusWorker.Converters;

public interface IMessageConverter
{
    ServiceBusMessage ToServiceBus(SuperBusMessage superBusMessage);
    SuperBusMessage ToSuperBus(ServiceBusReceivedMessage serviceBusMessage);
}
