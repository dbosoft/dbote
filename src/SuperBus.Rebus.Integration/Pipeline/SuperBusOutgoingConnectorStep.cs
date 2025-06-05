using Rebus.Messages;
using Rebus.Pipeline;
using Rebus.Pipeline.Send;
using Rebus.Transport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace SuperBus.Rebus.Integration.Pipeline;

[StepDocumentation("Enforces SuperBus connector ID header")]
internal class SuperBusOutgoingConnectorStep(string connectorsQueue) : IOutgoingStep
{
    public async Task Process(OutgoingStepContext context, Func<Task> next)
    {
        var outgoingMessage = context.Load<TransportMessage>();
        var destinationAddresses = context.Load<DestinationAddresses>().ToList();

        var connectorId = GetConnectorId(destinationAddresses);
        if (connectorId is not null)
        {
            if (outgoingMessage.Headers.TryGetValue(SuperBusHeaders.ConnectorId, out var headerConnectorId))
            {
                if (connectorId != headerConnectorId)
                    throw new InvalidOperationException(
                        $"Mismatch between connector ID in message address ('{connectorId}') and header ('{headerConnectorId}').");
            }
            else
            {
                outgoingMessage.Headers[SuperBusHeaders.ConnectorId] = connectorId;
            }
        }

        await next();
    }

    private string? GetConnectorId(IList<string> destinationAddresses)
    {
        if (destinationAddresses.Contains(connectorsQueue))
            throw new InvalidOperationException("The message has the shared connector queue as its destination which is not supported. Did you setup a proper queue for the connector?");

        var connectorAddresses = destinationAddresses
            .Where(address => address.StartsWith($"{connectorsQueue}-"))
            .ToList();

        if (connectorAddresses.Count > 1)
            throw new InvalidOperationException("The message has multiple connector addresses as its destination which is not supported. Connector messages must be exclusive.");

        if (connectorAddresses.Count == 1 && destinationAddresses.Count > 1)
            throw new InvalidOperationException("The message has additional addresses besides a connector address as its destination which is not supported. Connector messages must be exclusive.");

        if (connectorAddresses.Count == 0)
            return null;

        var connectorAddress = connectorAddresses.First();
        return connectorAddress.Substring($"{connectorsQueue}-".Length);
    }
}
