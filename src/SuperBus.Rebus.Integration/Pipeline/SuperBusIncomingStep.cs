using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rebus.Messages;
using Rebus.Pipeline;

namespace SuperBus.Rebus.Integration.Pipeline;

[StepDocumentation("Assigns and validates the SuperBus tenant context for incoming messages")]
internal class SuperBusIncomingStep : IIncomingStep
{
    public async Task Process(IncomingStepContext context, Func<Task> next)
    {
        // TODO do we need to change messages such that they appear to be from a dedicated connector queue?

        var message = context.Load<TransportMessage>();
        if (!message.Headers.ContainsKey(SuperBusHeaders.TenantId))
            throw new InvalidOperationException("The incoming message has no tenant information.");

        await next();
    }
}

