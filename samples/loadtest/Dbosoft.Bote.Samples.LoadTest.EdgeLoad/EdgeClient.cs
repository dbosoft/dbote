using System.Diagnostics;
using System.Security.Cryptography;
using Dbosoft.Bote.Primitives;
using Dbosoft.Bote.Rebus.Config;
using Dbosoft.Bote.Samples.LoadTest.Messages;
using Microsoft.IdentityModel.Tokens;
using Rebus.Activation;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Logging;
using Rebus.Routing.TypeBased;
using Rebus.Serialization.Json;

namespace Dbosoft.Bote.Samples.LoadTest.EdgeLoad;

/// <summary>
/// One simulated edge system = one independent Rebus bus over the Bote transport (its own
/// SignalR connection + storage queue + worker loop). N of these run in a single process.
/// </summary>
public sealed class EdgeClient : IDisposable
{
    private readonly BuiltinHandlerActivator _activator;
    private readonly LoadStats _stats;

    public string ClientId { get; }
    public IBus Bus { get; }

    /// <summary>Per-edge phase offset so large sends aren't synchronized across edges.</summary>
    public int LargeOffset { get; }

    /// <summary>True for the noisy-neighbour flooders (reported separately from quiet clients).</summary>
    public bool IsNoisy { get; }

    private EdgeClient(string clientId, BuiltinHandlerActivator activator, IBus bus, LoadStats stats,
        int largeOffset, bool isNoisy)
    {
        ClientId = clientId;
        _activator = activator;
        Bus = bus;
        _stats = stats;
        LargeOffset = largeOffset;
        IsNoisy = isNoisy;
    }

    public static async Task<EdgeClient> StartAsync(
        EdgeConfig config, string clientId, ECDsa signingKey,
        IHttpClientFactory httpClientFactory, LoadStats stats, bool isNoisy)
    {
        var activator = new BuiltinHandlerActivator();

        activator.Handle<EdgeRegistered>(_ =>
        {
            Interlocked.Increment(ref stats.Registered);
            return Task.CompletedTask;
        });
        activator.Handle<SmallResponse>(message =>
        {
            // Record into the aggregate and into this client's group (quiet vs noisy) so the
            // dashboard can show whether the flooders hurt the well-behaved clients' latency.
            stats.MarkReceived(stats.PendingSmall, message.Id, stats.SmallRoundtrip,
                isNoisy ? stats.NoisyRoundtrip : stats.QuietRoundtrip);
            Interlocked.Increment(ref stats.SmallAcked);
            return Task.CompletedTask;
        });
        activator.Handle<LargeResponse>(async message =>
        {
            // Read the echoed attachment back to prove the round-trip.
            await using var stream = await message.Data.OpenRead();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            stats.MarkReceived(stats.PendingLarge, stats.LargeRoundtrip, message.Id);
            Interlocked.Increment(ref stats.LargeEchoed);
        });
        activator.Handle<Notification>(_ =>
        {
            Interlocked.Increment(ref stats.Notifications);
            return Task.CompletedTask;
        });

        var credentials = new BoteCredentials
        {
            ClientId = clientId,
            TenantId = config.Tenant,
            SigningKey = new ECDsaSecurityKey(signingKey) { KeyId = clientId },
            Authority = config.Authority,
            TokenEndpoint = $"{config.IdpUrl}/api/{config.Tenant}/token",
            Scope = config.Scope,
        };

        var bus = Configure.With(activator)
            .Logging(l => l.Console(minLevel: LogLevel.Warn))
            .Transport(t => t.UseBote(
                new Uri(config.Endpoint),
                $"{config.ClientsQueue}-{clientId}",
                credentials,
                httpClientFactory))
            .Serialization(s => s.UseSystemTextJson())
            .DataBus(d => d.UseBoteDataBus())
            .Routing(r => r.TypeBased()
                .Map<RegisterEdge>(config.CloudQueue)
                .Map<SmallRequest>(config.CloudQueue)
                .Map<LargeRequest>(config.CloudQueue))
            .Start();

        await bus.Advanced.Topics.Subscribe("notify");
        await bus.Send(new RegisterEdge { EdgeId = clientId });

        // Phase-shift each edge's large-message cadence so N edges don't all fire a large on the
        // same iteration (a synchronized burst overwhelms the cloud->edge DataBus copy). Real edge
        // systems aren't in lockstep either.
        var largeOffset = config.LargeEveryN > 1 ? Random.Shared.Next(config.LargeEveryN) : 0;
        return new EdgeClient(clientId, activator, bus, stats, largeOffset, isNoisy);
    }

    public async Task SendSmallAsync(int sizeBytes)
    {
        var id = Guid.NewGuid();
        var payload = new string('x', sizeBytes);
        _stats.MarkSent(_stats.PendingSmall, id);
        var sw = Stopwatch.GetTimestamp();
        await Bus.Send(new SmallRequest { Id = id, EdgeId = ClientId, Payload = payload });
        _stats.SmallSend.Record((Stopwatch.GetTimestamp() - sw) * 1000.0 / Stopwatch.Frequency);
        Interlocked.Increment(ref _stats.SmallSent);
        if (IsNoisy) Interlocked.Increment(ref _stats.NoisySent);
        else Interlocked.Increment(ref _stats.QuietSent);
        Interlocked.Add(ref _stats.SmallBytes, sizeBytes);
    }

    public async Task SendLargeAsync(int sizeBytes)
    {
        var id = Guid.NewGuid();
        var bytes = new byte[sizeBytes];
        Random.Shared.NextBytes(bytes);
        var attachment = await Bus.Advanced.DataBus.CreateAttachment(new MemoryStream(bytes));

        _stats.MarkSent(_stats.PendingLarge, id);
        var sw = Stopwatch.GetTimestamp();
        await Bus.Send(
            new LargeRequest { Id = id, EdgeId = ClientId, FileName = "payload.bin", Data = attachment },
            new Dictionary<string, string> { [BoteHeaders.AttachmentId] = attachment.Id });
        _stats.LargeSend.Record((Stopwatch.GetTimestamp() - sw) * 1000.0 / Stopwatch.Frequency);

        Interlocked.Increment(ref _stats.LargeSent);
        Interlocked.Add(ref _stats.LargeBytes, sizeBytes);
    }

    public void Dispose()
    {
        Bus.Dispose();
        _activator.Dispose();
    }
}
