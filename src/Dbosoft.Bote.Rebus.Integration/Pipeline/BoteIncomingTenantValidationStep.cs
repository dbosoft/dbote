using Dbosoft.Bote.Primitives;
using Rebus.Messages;
using Rebus.Pipeline;

namespace Dbosoft.Bote.Rebus.Integration.Pipeline;

[StepDocumentation("Assigns and validates the Bote tenant context for incoming messages")]
internal class BoteIncomingTenantValidationStep : IIncomingStep
{
    public async Task Process(IncomingStepContext context, Func<Task> next)
    {
        // TODO do we need to change messages such that they appear to be from a dedicated client queue?

        var message = context.Load<TransportMessage>();
        if (!message.Headers.ContainsKey(BoteHeaders.TenantId))
            throw new InvalidOperationException("The incoming message has no tenant information.");

        await next();
    }
}

