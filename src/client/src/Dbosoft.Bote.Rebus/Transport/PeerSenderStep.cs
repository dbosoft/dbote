using Dbosoft.Bote.Primitives;
using Rebus.Exceptions;
using Rebus.Messages;
using Rebus.Pipeline;

namespace Dbosoft.Bote.Rebus.Transport;

/// <summary>
/// Refuses messages from other clients this client does not take messages from, before any
/// handler sees them. The worker names the sender of a message from another client
/// (<see cref="BoteHeaders.SenderClientId"/>); messages from the cloud have none and pass.
/// A refused message goes to the error queue at once.
/// </summary>
[StepDocumentation("Refuses messages from other clients the Bote transport does not accept.")]
internal sealed class PeerSenderStep(Func<string, bool> acceptPeer) : IIncomingStep
{
    public Task Process(IncomingStepContext context, Func<Task> next)
    {
        var message = context.Load<TransportMessage>();
        if (message.Headers.TryGetValue(BoteHeaders.SenderClientId, out var sender) && !acceptPeer(sender))
            throw new FailFastException($"Messages from client '{sender}' are not accepted.");

        return next();
    }
}
