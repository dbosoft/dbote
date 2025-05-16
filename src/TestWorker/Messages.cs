using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.SignalRService;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using SuperBus.Abstractions.SignalR;
using Azure.Storage.Queues;

namespace TestWorker;

public class Messages : ServerlessHub<IMessages>
{
    public Messages(IServiceProvider serviceProvider) : base(serviceProvider)
    {



    }

    [Function("negotiate")]
    public async Task<HttpResponseData> Negotiate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
    {
        var negotiateResponse = await NegotiateAsync(new() { UserId = "test" });
        var response = req.CreateResponse();
        await response.WriteBytesAsync(negotiateResponse.ToArray());
        return response;
    }


    //[Function("timer")]
    //[FixedDelayRetry(5, "00:00:10")]
    //public async Task Run([TimerTrigger("*/2 * * * * *")] TimerInfo timerInfo,
    //    FunctionContext context)
    //{
    //    var logger = context.GetLogger(nameof(Messages));
    //    logger.LogInformation($"Function Ran. Next timer schedule = {timerInfo.ScheduleStatus?.Next}");
    //    await Clients.All.NewMessage("Ping!");
    //}

    [Function(nameof(ServiceBusReceivedMessageFunction))]
    public async Task ServiceBusReceivedMessageFunction(
        [ServiceBusTrigger("queue.1", Connection = "ServiceBusConnection")] ServiceBusReceivedMessage message)
    {
        QueueClient queue = new QueueClient("UseDevelopmentStorage=true", "outbox");
        await queue.CreateAsync();
        var receipt = await queue.SendMessageAsync(message.Body.ToString());

        await Clients.All.NewMessage(receipt.Value.MessageId);
    }
}
