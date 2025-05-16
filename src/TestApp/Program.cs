// See https://aka.ms/new-console-template for more information

using Azure.Messaging.ServiceBus;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.SignalR.Client;
using SuperBus.Abstractions.SignalR;


const string sbConnectionString =
    "Endpoint=sb://127.0.0.1;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

Console.WriteLine("Superbus test app!");

Console.Write("Send message?");
Console.ReadKey();

var connection = new HubConnectionBuilder()
    // TODO this must be URL of the Azure function with the negotiate endpoint
    .WithUrl("http://localhost:7083/api")
    .Build();

QueueClient queue = new QueueClient("UseDevelopmentStorage=true", "outbox");
await queue.CreateAsync();

using var _ = connection.On(nameof(IMessages.NewMessage), async (string messageId) =>
{
    Console.WriteLine($"SignalR for message {messageId}");
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
    var message = await queue.ReceiveMessageAsync(TimeSpan.FromSeconds(30), cts.Token);
    Console.WriteLine($"Queue message ({message.Value.MessageId}): {message.Value.Body}");

});

await connection.StartAsync();

var serviceBusClient = new ServiceBusClient(sbConnectionString);
var serviceBusSender = serviceBusClient.CreateSender("queue.1");

while (true)
{
    await serviceBusSender.SendMessageAsync(new ServiceBusMessage("PING!"));
    await Task.Delay(2000);
}
