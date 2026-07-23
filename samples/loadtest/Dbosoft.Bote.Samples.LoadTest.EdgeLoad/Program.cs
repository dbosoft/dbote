using System.Net.Http.Json;
using System.Security.Cryptography;
using Dbosoft.Bote.Samples.LoadTest.EdgeLoad;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// This load generator runs N independent Rebus buses in a single process. The Bote transport
// blocks a thread per bus (sync-over-async SignalR start/receive via AsyncHelpers.RunSync); under
// real (Azure) network latency those blocked threads pile up and starve the thread pool at ~#cores,
// hanging further bus starts. Raise the pool floor so hundreds of buses can coexist here. A real
// edge system runs a single bus and never hits this.
ThreadPool.SetMinThreads(512, 512);

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHttpClient();
builder.Logging.AddSimpleConsole(o => o.SingleLine = true);

var host = builder.Build();
var cfg = host.Services.GetRequiredService<IConfiguration>();
var httpClientFactory = host.Services.GetRequiredService<IHttpClientFactory>();
var log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("EdgeLoad");

var config = new EdgeConfig(
    EdgeCount: cfg.GetValue("LoadTest:EdgeCount", 100),
    Tenant: cfg["LoadTest:Tenant"] ?? "load",
    Endpoint: cfg["LoadTest:Endpoint"] ?? "http://dbote-worker/api",
    IdpUrl: cfg["LoadTest:IdpUrl"] ?? "http://basicidentityprovider",
    ControllerUrl: cfg["LoadTest:ControllerUrl"] ?? "http://controller:8080",
    Authority: cfg["LoadTest:Authority"] ?? "http://basicidentityprovider",
    Scope: cfg["LoadTest:Scope"] ?? "bote",
    CloudQueue: cfg["LoadTest:Queues:Cloud"] ?? "bote-sample-loadtest-cloud",
    ClientsQueue: cfg["LoadTest:Queues:Clients"] ?? "bote-sample-loadtest-clients",
    ErrorQueue: cfg["LoadTest:Queues:Error"] ?? "bote-sample-loadtest-error",
    SmallIntervalMs: cfg.GetValue("LoadTest:SmallIntervalMs", 1000),
    SmallBytes: cfg.GetValue("LoadTest:SmallBytes", 2048),
    LargeEveryN: cfg.GetValue("LoadTest:LargeEveryN", 10),
    LargeBytes: cfg.GetValue("LoadTest:LargeBytes", 1024 * 1024),
    StartupStaggerMs: cfg.GetValue("LoadTest:StartupStaggerMs", 50),
    NoisyEdges: cfg.GetValue("LoadTest:NoisyEdges", 0),
    NoisyIntervalMs: cfg.GetValue("LoadTest:NoisyIntervalMs", 50));

var stats = new LoadStats();
var http = httpClientFactory.CreateClient();

// 1) Generate a key pair per edge and bulk-register the public keys with the IdP.
log.LogInformation("Registering {N} edge clients with the identity provider at {Idp}", config.EdgeCount, config.IdpUrl);
var keys = new Dictionary<string, ECDsa>();
for (var i = 0; i < config.EdgeCount; i++)
{
    var clientId = $"edge-{i:D4}";
    var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    keys[clientId] = signingKey;

    var publicKey = Convert.ToBase64String(signingKey.ExportSubjectPublicKeyInfo());
    // The identity provider's ClientInfo DTO uses "Id" (not "ClientId").
    var response = await http.PostAsJsonAsync($"{config.IdpUrl}/api/register-client",
        new { Id = clientId, TenantId = config.Tenant, PublicKey = publicKey });
    if (!response.IsSuccessStatusCode)
    {
        Interlocked.Increment(ref stats.Errors);
        log.LogError("Registering {ClientId} failed: {Status}", clientId, response.StatusCode);
    }
}

// 2) Start one independent Rebus bus (over the Bote transport) per edge. The first NoisyEdges
// clients are the noisy neighbours (they flood at NoisyIntervalMs); the rest stay well-behaved.
log.LogInformation("Starting {N} edge buses ({Noisy} noisy, stagger {Stagger} ms)...",
    config.EdgeCount, config.NoisyEdges, config.StartupStaggerMs);
var edges = new List<EdgeClient>();
var edgeIndex = 0;
foreach (var (clientId, signingKey) in keys)
{
    var isNoisy = edgeIndex++ < config.NoisyEdges;
    try
    {
        edges.Add(await EdgeClient.StartAsync(config, clientId, signingKey, httpClientFactory, stats, isNoisy));
    }
    catch (Exception ex)
    {
        Interlocked.Increment(ref stats.Errors);
        log.LogError(ex, "Failed to start edge {ClientId}", clientId);
    }

    if (config.StartupStaggerMs > 0)
        await Task.Delay(config.StartupStaggerMs);
}
log.LogInformation("{Started}/{N} edge buses started; generating load. Ctrl+C to stop.", edges.Count, config.EdgeCount);

// 3) Drive load from every edge, and print aggregate stats.
// Not `using`: the CancelKeyPress handler below captures cts and can fire during shutdown; the
// process exits right after WhenAny returns, so there is nothing to dispose.
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var reporterId = Environment.MachineName;
var loadTasks = edges.Select(edge => RunLoad(edge, config, stats, cts.Token)).ToList();
var printer = PrintStats(stats, edges.Count, cts.Token);
var reporter = ReportMetrics(httpClientFactory, config, stats, reporterId, edges.Count, cts.Token);

await Task.WhenAny(Task.WhenAll(loadTasks), printer, reporter);

foreach (var edge in edges)
    edge.Dispose();

return;

static async Task RunLoad(EdgeClient edge, EdgeConfig config, LoadStats stats, CancellationToken ct)
{
    // Noisy neighbours hammer their queue at NoisyIntervalMs; well-behaved edges use SmallIntervalMs.
    var intervalMs = edge.IsNoisy ? config.NoisyIntervalMs : config.SmallIntervalMs;
    var count = 0;
    while (!ct.IsCancellationRequested)
    {
        try
        {
            await edge.SendSmallAsync(config.SmallBytes);
            count++;
            // Noisy neighbours flood small messages only; the large path stays with the quiet edges
            // so the scenario isolates small-message queue overload (what the fairness dampener guards).
            if (!edge.IsNoisy && config.LargeEveryN > 0 && (count + edge.LargeOffset) % config.LargeEveryN == 0)
                await edge.SendLargeAsync(config.LargeBytes);
        }
        catch
        {
            Interlocked.Increment(ref stats.Errors);
        }

        try { await Task.Delay(intervalMs, ct); }
        catch (OperationCanceledException) { break; }
    }
}

static async Task ReportMetrics(IHttpClientFactory httpClientFactory, EdgeConfig config,
    LoadStats stats, string reporterId, int edges, CancellationToken ct)
{
    var client = httpClientFactory.CreateClient();
    while (!ct.IsCancellationRequested)
    {
        try { await Task.Delay(1000, ct); }
        catch (OperationCanceledException) { break; }

        try
        {
            await client.PostAsJsonAsync($"{config.ControllerUrl}/report", stats.ToReport(reporterId, edges), ct);
        }
        catch
        {
            // The dashboard is best-effort; keep generating load regardless.
        }
    }
}

static async Task PrintStats(LoadStats s, int edges, CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        try { await Task.Delay(5000, ct); }
        catch (OperationCanceledException) { break; }

        var quiet = s.QuietRoundtrip.Snapshot();
        var noisy = s.NoisyRoundtrip.Snapshot();
        Console.WriteLine(
            $"[edges={edges}] registered={Interlocked.Read(ref s.Registered)} " +
            $"small sent/ack={Interlocked.Read(ref s.SmallSent)}/{Interlocked.Read(ref s.SmallAcked)} " +
            $"large sent/echo={Interlocked.Read(ref s.LargeSent)}/{Interlocked.Read(ref s.LargeEchoed)} " +
            $"quiet p95={quiet.P95Ms}ms noisy p95={noisy.P95Ms}ms " +
            $"notifications={Interlocked.Read(ref s.Notifications)} errors={Interlocked.Read(ref s.Errors)}");
    }
}
