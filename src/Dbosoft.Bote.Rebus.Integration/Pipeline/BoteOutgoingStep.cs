using Rebus.Messages;
using Rebus.Pipeline;
using Rebus.Transport;

namespace Dbosoft.Bote.Rebus.Integration.Pipeline;

[StepDocumentation("Assigns and validates the Bote tenant context for outgoing messages")]
internal class BoteOutgoingStep : IOutgoingStep
{
    public async Task Process(OutgoingStepContext context, Func<Task> next)
    {
        var outgoingMessage = context.Load<Message>();
        if (!outgoingMessage.Headers.ContainsKey(BoteHeaders.TenantId))
        {
            var transactionContext = context.Load<ITransactionContext>();
            var incomingStepContext = transactionContext.GetOrNull<IncomingStepContext>(StepContext.StepContextKey);
            var incomingMessage = incomingStepContext?.Load<Message>();
            var incomingTenantId = incomingMessage?.Headers.GetValueOrDefault(BoteHeaders.TenantId);
            if (string.IsNullOrEmpty(incomingTenantId))
                throw new InvalidOperationException($"The outgoing message has no tenant information.");

            outgoingMessage.Headers[BoteHeaders.TenantId] = incomingTenantId;
        }

        await next();
    }
}
