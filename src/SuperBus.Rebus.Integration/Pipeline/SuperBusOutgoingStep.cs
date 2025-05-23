using Rebus.Messages;
using Rebus.Pipeline;
using Rebus.Transport;

namespace SuperBus.Rebus.Integration.Pipeline;

[StepDocumentation("Assigns and validates the SuperBus tenant context for outgoing messages")]
internal class SuperBusOutgoingStep : IOutgoingStep
{
    public async Task Process(OutgoingStepContext context, Func<Task> next)
    {
        var outgoingMessage = context.Load<Message>();
        if (!outgoingMessage.Headers.ContainsKey(SuperBusHeaders.TenantId))
        {
            var transactionContext = context.Load<ITransactionContext>();
            var incomingStepContext = transactionContext.GetOrNull<IncomingStepContext>(StepContext.StepContextKey);
            var incomingMessage = incomingStepContext?.Load<Message>();
            var incomingTenantId = incomingMessage?.Headers.GetValueOrDefault(SuperBusHeaders.TenantId);
            if (string.IsNullOrEmpty(incomingTenantId))
                throw new InvalidOperationException($"The outgoing message has no tenant information.");

            outgoingMessage.Headers[SuperBusHeaders.TenantId] = incomingTenantId;
        }

        await next();
    }
}
