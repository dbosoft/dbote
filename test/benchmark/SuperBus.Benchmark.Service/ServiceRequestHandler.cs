using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rebus.Bus;
using Rebus.Handlers;
using SuperBus.Benchmark.Messages;

namespace SuperBus.Benchmark.Service;

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
