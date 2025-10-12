using Rebus.Bus;
using Rebus.Handlers;
using SuperBus.Benchmark.Messages;

namespace SuperBus.Benchmark.Connector;

internal class ConnectorRequestHandler(
    IBus bus)
    : IHandleMessages<ConnectorRequest>
{
    public async Task Handle(ConnectorRequest message)
    {
        await bus.Reply(new ConnectorResponse()
        {
            RequestId = message.RequestId
        });
    }
}
