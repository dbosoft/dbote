using Dbosoft.Bote.Primitives;
using Rebus.Messages;
using Rebus.Pipeline;
using Rebus.Pipeline.Send;

namespace Dbosoft.Bote.Rebus.Integration.Pipeline;

[StepDocumentation("Enforces Bote client ID header")]
internal class BoteOutgoingValidationsStep(string clientsQueue) : IOutgoingStep
{
    public async Task Process(OutgoingStepContext context, Func<Task> next)
    {
        var outgoingMessage = context.Load<TransportMessage>();

        // Skip client validation for topic broadcasts
        if (outgoingMessage.Headers.ContainsKey(BoteHeaders.Topic))
        {
            await next();
            return;
        }

        var destinationAddresses = context.Load<DestinationAddresses>().ToList();

        var clientId = GetClientId(destinationAddresses);
        if (clientId is not null)
        {
            if (outgoingMessage.Headers.TryGetValue(BoteHeaders.ClientId, out var headerClientId))
            {
                if (clientId != headerClientId)
                    throw new InvalidOperationException(
                        $"Mismatch between Client ID in message address ('{clientId}') and header ('{headerClientId}').");
            }
            else
            {
                outgoingMessage.Headers[BoteHeaders.ClientId] = clientId;
            }
        }

        await next();
    }

    private string? GetClientId(IList<string> destinationAddresses)
    {
        if (destinationAddresses.Contains(clientsQueue))
            throw new InvalidOperationException("The message has the shared client queue as its destination which is not supported. Did you setup a proper queue for the client?");

        var clientAddresses = destinationAddresses
            .Where(address => address.StartsWith($"{clientsQueue}-"))
            .ToList();

        switch (clientAddresses.Count)
        {
            case > 1:
                throw new InvalidOperationException("The message has multiple client addresses as its destination which is not supported. Client messages must be exclusive.");
            case 1 when destinationAddresses.Count > 1:
                throw new InvalidOperationException("The message has additional addresses besides a client address as its destination which is not supported. Client messages must be exclusive.");
            case 0:
                return null;
            default:
            {
                var clientAddress = clientAddresses.First();
                return clientAddress[$"{clientsQueue}-".Length..];
            }
        }
    }
}
