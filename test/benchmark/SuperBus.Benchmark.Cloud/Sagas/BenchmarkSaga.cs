using Rebus.Bus;
using Rebus.Handlers;
using Rebus.Messages;
using Rebus.Pipeline;
using Rebus.Sagas;
using SuperBus.Benchmark.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rebus.Extensions;

namespace SuperBus.Benchmark.Cloud.Sagas;

public class BenchmarkSaga(
    IBus bus,
    IMessageContext messageContext)
    : Saga<BenchmarkSagaData>,
    IAmInitiatedBy<BenchmarkRequest>,
    IHandleMessages<ServiceResponse>,
    IHandleMessages<ConnectorResponse>
{
    public async Task Handle(BenchmarkRequest message)
    {
        Data.RequestMessageId = messageContext.TransportMessage.GetMessageId();
        Data.RequestSenderAddress = messageContext.TransportMessage.Headers.GetValue(Headers.ReturnAddress);
        
        if (message.Type is BenchmarkType.Simple)
        {
            await Reply(new BenchmarkResponse()
            {
                RequestId = message.RequestId,
            });
            MarkAsComplete();
            return;
        }

        if (message.Type is BenchmarkType.Connector)
        {
            await bus.Send(new ConnectorRequest()
            {
                RequestId = message.RequestId,
            });
            return;
        }

        if (message.Type is BenchmarkType.Service)
        {
            await bus.Send(new ServiceRequest()
            {
                RequestId = message.RequestId,
            });
            return;
        }

        throw new ArgumentException($"The message benchmark type '{message.Type}' is not supported", nameof(message));
    }

    public async Task Handle(ConnectorResponse message)
    {
        await Reply(new BenchmarkResponse()
        {
            RequestId = message.RequestId,
        });

        MarkAsComplete();
    }


    public async Task Handle(ServiceResponse message)
    {
        await Reply(new BenchmarkResponse()
        {
            RequestId = message.RequestId,
        });

        MarkAsComplete();
    }

    protected override void CorrelateMessages(
        ICorrelationConfig<BenchmarkSagaData> config)
    {
        config.Correlate<BenchmarkRequest>(
            request => request.RequestId,
            sagaData => sagaData.RequestId);
        config.Correlate<ServiceResponse>(
            response => response.RequestId,
            sagaData => sagaData.RequestId);
        config.Correlate<ConnectorResponse>(
            response => response.RequestId,
            sagaData => sagaData.RequestId);
    }

    private async Task Reply(BenchmarkResponse response)
    {
        await bus.Advanced.Routing.Send(
            Data.RequestSenderAddress,
            response,
            new Dictionary<string, string>()
            {
                [Headers.InReplyTo] = Data.RequestMessageId,
            });
    }
}
