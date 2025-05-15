using System.Collections.Generic;
using System.Security.Claims;
using Azure.Messaging.EventGrid;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Extensions.SignalRService;
using System.Threading.Tasks;
using SuperBus.Workers.BusWorker.Sas;
using System.Text;
using System;
using System.Net;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Azure.Storage.Sas;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SuperBus.Models;
using QueueSasBuilder = SuperBus.Workers.BusWorker.Sas.QueueSasBuilder;
using QueueSasPermissions = SuperBus.Workers.BusWorker.Sas.QueueSasPermissions;

namespace SuperBus.Workers.BusWorker
{

    public class BusHub : ServerlessHub
    {
        [FunctionName("negotiate")]
        public object Negotiate([HttpTrigger(AuthorizationLevel.Anonymous)] HttpRequest req, ILogger logger)
        {

            if (!req.Query.TryGetValue("ac", out var accountValues) && accountValues.Count != 1)
                return new UnauthorizedResult();

            var account = accountValues[0] ?? "";

            // TODO: fancy lookup of account key
            var dummyKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(account));

            var accountKey = new SharedKeyBusCredential(account, dummyKey);

            var sasParameters = SasQueryParametersInternals.Parse(req.QueryString.Value ?? "");
            var sasBuilder = QueueSasBuilder.FromSasQueryParameters(sasParameters);

            if (!string.IsNullOrWhiteSpace(sasBuilder.QueueName) || string.IsNullOrWhiteSpace(sasBuilder.Identifier))
            {
                logger.LogInformation("Failed: {sas}", sasParameters);
                return new UnauthorizedResult();
            }

            if (!sasBuilder.ValidateSignature(sasParameters.Signature, accountKey))
                return new UnauthorizedResult();

            var userId = sasBuilder.Identifier;

            var result = Negotiate(userId, new List<Claim>
            {
                new ("account", account)

            });
            return result;
        }

        [FunctionName(nameof(OnConnected))]
        public async Task OnConnected([SignalRTrigger] InvocationContext invocationContext)
        {
            if (invocationContext.Claims.TryGetValue("account", out var account))
                await Groups.AddToGroupAsync(invocationContext.ConnectionId, $"account/{account}");
        }


        [FunctionName(nameof(SubscribeQueue))]
        public async Task<bool> SubscribeQueue([SignalRTrigger] InvocationContext invocationContext, string queueName)
        {
            if (!invocationContext.Claims.TryGetValue("account", out var account))
                return false;

            await Groups.AddToGroupAsync(invocationContext.ConnectionId, $"/queue/{account}/{queueName}");

            return true;


        }

        [FunctionName(nameof(GetQueueConnection))]
        public async Task<BusConnections?> GetQueueConnection([SignalRTrigger] InvocationContext invocationContext, string queueName)
        {
            if (!invocationContext.Claims.TryGetValue("account", out var account))
                return null;


            // TODO: fancy lookup of account key
            var dummyKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(account));

            var accountKey = new SharedKeyBusCredential(account, dummyKey);
            var sasBuilder = new QueueSasBuilder(QueueSasPermissions.Process,
                DateTimeOffset.UtcNow + TimeSpan.FromHours(1), queueName);

            var inboxToken= Convert.ToBase64String(
                Encoding.UTF8.GetBytes(sasBuilder.ToSasQueryParameters(accountKey).ToString()));

            var blobService =
                new BlobServiceClient(
                    "DefaultEndpointsProtocol=https;AccountName=superbus;AccountKey=8T4aN5SA8oE9/2Jk/ILI8menVLCAhEbfIK4+82aaxdmz0ep71YM+yqQKVfjCmjo4JsChSPF9Wi5H+AStWiCBrQ==;EndpointSuffix=core.windows.net");
            var expire = DateTimeOffset.UtcNow + TimeSpan.FromHours(1);
            var containerName = "inbox-" + account;
            var storageQueueName = "outbox-" + queueName;

            var containerClient = blobService.GetBlobContainerClient(containerName);
            await containerClient.CreateIfNotExistsAsync();
            var inboxSas = containerClient.GenerateSasUri(BlobContainerSasPermissions.Create, expire);

            var queueClient =
                new QueueClient(
                    "DefaultEndpointsProtocol=https;AccountName=superbus;AccountKey=8T4aN5SA8oE9/2Jk/ILI8menVLCAhEbfIK4+82aaxdmz0ep71YM+yqQKVfjCmjo4JsChSPF9Wi5H+AStWiCBrQ==;EndpointSuffix=core.windows.net",
                    storageQueueName);

            await queueClient.CreateIfNotExistsAsync();
            var queueSas = queueClient.GenerateSasUri(Azure.Storage.Sas.QueueSasPermissions.All, expire);

            var connections = new BusConnections(new InboxConnection(inboxSas, "", expire, inboxToken)
                , new QueueConnection(queueSas, queueName, expire));

            return connections;
        }


        // Default URL for triggering event grid function in the local environment.
        // http://localhost:7071/runtime/webhooks/EventGrid?functionName={functionname}
        [FunctionName("Notify")]
        public async Task Notify([EventGridTrigger] EventGridEvent eventGridEvent)
        {
            if (eventGridEvent.EventType != "dbosoft.superbus.queue.newmessage")
                return;


            var groupName = eventGridEvent.Subject;
            var timeStamp = eventGridEvent.EventTime;
            await Clients.Group(groupName).SendAsync("NewMessage", timeStamp);

        }

        [FunctionName(nameof(OnDisconnected))]
        public void OnDisconnected([SignalRTrigger] InvocationContext invocationContext)
        {
        }
    }
}