using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.SignalR.Client;
using Rebus.Bus;
using Rebus.Messages;
using Rebus.Transport;
using SuperBus.Transport.Abstractions;
using SuperBus.Transport.Config;

namespace SuperBus.Transport;

// Based on Rebus.AzureQueues
// https://github.dev/rebus-org/Rebus.AzureQueues/blob/master/Rebus.AzureQueues
// Licensed under MIT License

internal sealed class SuperBusTransport(
    string queueName,
    ISignalRClient signalRClient,
    IPendingMessagesIndicators pendingMessagesIndicator
    ) : ITransport
{
    
    public string Address => queueName;

    public void CreateQueue(string address)
    {
        //throw new NotImplementedException();
    }

    public async Task<TransportMessage?> Receive(ITransactionContext context, CancellationToken cancellationToken)
    {
        var hasPendingMessages = await pendingMessagesIndicator.GetAsync();
        if (!hasPendingMessages)
            return null;

        var queueConnection = await signalRClient.GetQueueConnection();
        var queue = new QueueClient(new Uri(queueConnection));
        var message = await queue.ReceiveMessageAsync(TimeSpan.FromSeconds(30), cancellationToken);
        if (message.Value is null)
        {
            await pendingMessagesIndicator.ClearAsync();
            return null;
        }

        context.OnAck(async _ =>
        {
            await queue.DeleteMessageAsync(message.Value.MessageId, message.Value.PopReceipt, CancellationToken.None);
        });

        // TODO implement ACK NACK similar to the Azure Queues transport

        var superBusMessage = JsonSerializer.Deserialize<SuperBusMessage>(
            message.Value.Body.ToString());

        var transportMessage = new TransportMessage(
            superBusMessage!.Headers.ToDictionary(),
            Encoding.UTF8.GetBytes(superBusMessage.Body));
        return transportMessage;
    }

    public Task Send(string destinationAddress, TransportMessage message, ITransactionContext context)
    {
        return Task.CompletedTask;
    }
}
