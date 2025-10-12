using Azure.Messaging.ServiceBus;
using Azure.Storage.Queues;
using System.Resources;
using Microsoft.Azure.Amqp;

namespace Dbosoft.Bote.Client.Core
{
    public class BoteClient
    {
        public async Task Peng()
        {
            var message = new BoteMessage("Test");


            //var queueClient = new QueueClient(new Uri(""));
            //queueClient.SendMessageAsync();


            //var serviceBusClient = new ServiceBusClient("");
            //var sender = serviceBusClient.CreateSender("", new ServiceBusSenderOptions());
            //var batch = await sender.CreateMessageBatchAsync();

            //var amqpMessage = AmqpMessage.Create();
            //var msg = new Azure.Messaging.ServiceBus.ServiceBusMessage();
            //amqpMessage.ToStream();


        }
    }
}
