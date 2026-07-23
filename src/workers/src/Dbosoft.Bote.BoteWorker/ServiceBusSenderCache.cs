using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;

namespace Dbosoft.Bote.BoteWorker;

/// <summary>
/// Caches one long-lived <see cref="ServiceBusSender"/> per queue. Senders are thread-safe and hold
/// an AMQP link over the shared connection; creating and disposing one per message forces a link
/// attach/detach on every send. Reusing them keeps the link open. Singleton-scoped because the
/// hub is instantiated per invocation.
/// </summary>
public sealed class ServiceBusSenderCache(ServiceBusClient client) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ServiceBusSender> _senders = new();

    public ServiceBusSender GetSender(string queue) =>
        _senders.GetOrAdd(queue, q => client.CreateSender(q));

    public async ValueTask DisposeAsync()
    {
        foreach (var sender in _senders.Values)
            await sender.DisposeAsync();
        _senders.Clear();
    }
}
