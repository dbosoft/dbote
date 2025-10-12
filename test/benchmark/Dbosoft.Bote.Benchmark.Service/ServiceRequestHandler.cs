using Dbosoft.Bote.Benchmark.Messages;
using Rebus.Bus;
using Rebus.Handlers;

namespace Dbosoft.Bote.Benchmark.Service;

internal class ServiceRequestHandler(
    IBus bus)
    : IHandleMessages<ServiceRequest>
{
    public async Task Handle(ServiceRequest message)
    {
        await bus.Reply(new ServiceResponse()
        {
            RequestId = message.RequestId,
        });
    }
}
