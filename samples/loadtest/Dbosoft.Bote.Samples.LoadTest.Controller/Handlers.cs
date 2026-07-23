using System.Diagnostics;
using Dbosoft.Bote.Options;
using Dbosoft.Bote.Primitives;
using Dbosoft.Bote.Samples.LoadTest.Messages;
using Microsoft.Extensions.Options;
using Rebus.Bus;
using Rebus.Handlers;
using Rebus.Pipeline;

namespace Dbosoft.Bote.Samples.LoadTest.Controller;

/// <summary>
/// Cloud-side handlers. Each reads the edge's authenticated identity (tenant/client) from the
/// trusted headers the worker injected and replies targeted to that specific edge. A targeted
/// send must address the per-client destination <c>{clients}-{clientId}</c> (the Bote name
/// formatter redirects it to the shared clients queue and the worker routes on the header) -
/// sending to the bare shared queue is rejected by the outgoing validation step.
/// </summary>
public class RegisterEdgeHandler(IBus bus, EdgeRegistry registry, IOptions<ServiceBusOptions> options)
    : IHandleMessages<RegisterEdge>
{
    public async Task Handle(RegisterEdge message)
    {
        registry.RegisterEdge(message.EdgeId);
        await SendToSender(bus, options.Value.Queues.Clients, new EdgeRegistered { EdgeId = message.EdgeId });
    }

    internal static Task SendToSender(IBus bus, string clientsQueue, object message,
        Dictionary<string, string>? extraHeaders = null)
    {
        var tenantId = MessageContext.Current.Headers[BoteHeaders.TenantId];
        var clientId = MessageContext.Current.Headers[BoteHeaders.ClientId];

        var headers = new Dictionary<string, string>
        {
            [BoteHeaders.TenantId] = tenantId,
            [BoteHeaders.ClientId] = clientId,
        };
        if (extraHeaders is not null)
            foreach (var header in extraHeaders)
                headers[header.Key] = header.Value;

        return bus.Advanced.Routing.Send($"{clientsQueue}-{clientId}", message, headers);
    }
}

public class SmallRequestHandler(IBus bus, EdgeRegistry registry, ClientFairness fairness, IOptions<ServiceBusOptions> options)
    : IHandleMessages<SmallRequest>
{
    public async Task Handle(SmallRequest message)
    {
        var clientId = MessageContext.Current.Headers[BoteHeaders.ClientId];
        if (!fairness.TryAdmit(clientId, out var deferBy))
        {
            // Over its fair rate: re-schedule for later instead of processing now, so this client
            // cannot monopolise the controller at the expense of well-behaved ones.
            await bus.Advanced.TransportMessage.Defer(deferBy);
            registry.RecordDeferred();
            return;
        }

        var sw = Stopwatch.StartNew();
        await RegisterEdgeHandler.SendToSender(bus, options.Value.Queues.Clients,
            new SmallResponse { Id = message.Id, Payload = $"ack:{message.Payload.Length}" });
        registry.RecordSmall(message.Payload.Length, sw.Elapsed.TotalMilliseconds);
    }
}

public class LargeRequestHandler(IBus bus, EdgeRegistry registry, ClientFairness fairness, IOptions<ServiceBusOptions> options)
    : IHandleMessages<LargeRequest>
{
    public async Task Handle(LargeRequest message)
    {
        var clientId = MessageContext.Current.Headers[BoteHeaders.ClientId];
        if (!fairness.TryAdmit(clientId, out var deferBy))
        {
            await bus.Advanced.TransportMessage.Defer(deferBy);
            registry.RecordDeferred();
            return;
        }

        var sw = Stopwatch.StartNew();

        // Read the attachment (proves the claim-check round-trip), then echo a copy back as a
        // new attachment targeted at the sending edge.
        await using var source = await message.Data.OpenRead();
        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer);
        var bytes = buffer.Length;

        buffer.Position = 0;
        var copy = await bus.Advanced.DataBus.CreateAttachment(buffer);

        await RegisterEdgeHandler.SendToSender(bus, options.Value.Queues.Clients,
            new LargeResponse { Id = message.Id, FileName = $"echo_{message.FileName}", Data = copy },
            new Dictionary<string, string> { [BoteHeaders.AttachmentId] = copy.Id });

        registry.RecordLarge(bytes, sw.Elapsed.TotalMilliseconds);
    }
}
