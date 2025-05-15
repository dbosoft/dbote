using System.Text;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using SuperBus.Models;
using SuperBus.Workers.BusWorker;
using SuperBus.Workers.BusWorker.Sas;

namespace SuperBus.BusWorker.Tests
{
    public class UnitTest1
    {

        [Fact]
        public async Task Test1()
        {
            await using var function = await TemporaryAzureFunctionsApplication.StartNewAsync(
                new DirectoryInfo(@"..\..\..\..\..\src\SuperBus.Workers.BusWorker"));

            var accountName = "tenanta";
            var dummyKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(accountName));


            var sasBuilder =
                new QueueSasBuilder(QueueAccountSasPermissions.Process, DateTimeOffset.UtcNow + TimeSpan.FromHours(1), "main");

            var queryString = sasBuilder.ToSasQueryParameters(new SharedKeyBusCredential(accountName, dummyKey)).ToString();

            var connection = new HubConnectionBuilder()
            .WithUrl(
                $"http://localhost:7071/api/?ac=tenanta&{queryString}"
                //$"https://superbusworker.azurewebsites.net/api/?ac=tenanta&{queryString}"
                )

            .ConfigureLogging(logging =>
            {
                // Log to the Console
                //logging.AddConsole();

                // This will set ALL logging to Debug level
                logging.SetMinimumLevel(LogLevel.Debug);
            })
            .WithAutomaticReconnect()
            .Build();


            using var handler = connection.On("NewMessage", (DateTimeOffset timestamp) =>
            {
                Console.WriteLine(timestamp);

            });

            await connection.StartAsync();

            var subscribed = await connection.InvokeAsync<bool>("SubscribeQueue", "test");

            Assert.True(subscribed);

            var busConnection = await connection.InvokeAsync<BusConnections>("GetQueueConnection", "test");
            Assert.NotNull(busConnection);
            Assert.NotNull(busConnection.Inbox);
            Assert.NotNull(busConnection.Inbox.Token);

            await Task.Delay(Timeout.Infinite);
        }
    }
}