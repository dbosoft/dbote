using Dbosoft.Bote.Benchmark.Messages;
using Rebus.Bus;
using Rebus.Handlers;

namespace Dbosoft.Bote.Benchmark.Client;

internal class ClientRequestHandler(
    IBus bus)
    : IHandleMessages<ClientRequest>
{
    public async Task Handle(ClientRequest message)
    {
        await bus.Reply(new ClientResponse()
        {
            RequestId = message.RequestId
        });
    }
}
