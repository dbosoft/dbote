using Dbosoft.Bote.Benchmark.Messages;
using Rebus.Bus;
using Rebus.Handlers;

namespace Dbosoft.Bote.Benchmark.Connector;

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
