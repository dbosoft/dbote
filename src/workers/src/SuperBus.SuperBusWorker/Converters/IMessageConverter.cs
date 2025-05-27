using Azure.Messaging.ServiceBus;
using SuperBus.Transport.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperBus.SuperBusWorker.Converters;

public interface IMessageConverter
{
    ServiceBusMessage ToServiceBus(SuperBusMessage superBusMessage);
    SuperBusMessage ToSuperBus(ServiceBusReceivedMessage serviceBusMessage);
}
