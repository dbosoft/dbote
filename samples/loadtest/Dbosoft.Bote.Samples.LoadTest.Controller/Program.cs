using Dbosoft.Bote.Options;
using Dbosoft.Bote.Primitives;
using Dbosoft.Bote.Rebus.Integration;
using Dbosoft.Bote.Samples.LoadTest.Controller;
using Dbosoft.Bote.Samples.LoadTest.Messages;
using Microsoft.Extensions.Options;
using Rebus.Config;
using Rebus.Retry.Simple;
using Rebus.Routing.TypeBased;
using Rebus.Serialization.Json;

var builder = WebApplication.CreateBuilder(args);

// Larger request bodies (SignalR/HTTP) for the large-message path.
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 512 * 1024 * 1024);

builder.Services.Configure<ServiceBusOptions>(builder.Configuration.GetSection("dbote:Cloud:ServiceBus"));
builder.Services.AddLogging(c => c.AddSimpleConsole(o => o.SingleLine = true));
builder.Services.AddSingleton<EdgeRegistry>();
builder.Services.AddSingleton<ClientFairness>();
builder.Services.AddHttpClient();

builder.Services.AddRebus((configure, serviceProvider) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
    var configuration = builder.Configuration;

    return configure
        .Options(b => b.RetryStrategy(errorQueueName: options.Queues.Error))
        .Options(o =>
        {
            // Each replica processes up to 5 messages concurrently (default is 1 worker), so one
            // instance handles more before we need to scale out horizontally.
            o.SetNumberOfWorkers(5);
            o.SetMaxParallelism(5);
        })
        .Options(o =>
        {
            o.EnableBote(options.Queues.Clients);
            o.EnableBoteDataBus(
                configuration["dbote:Cloud:Storage:Connection"]!,
                BoteStorageConstants.Outbox);
        })
        .Transport(t => t.UseAzureServiceBus(
            configuration.GetSection("dbote:Cloud:ServiceBus:Connection"),
            options.Queues.Cloud))
        // Only the broadcast Notification is routed type-based to the shared clients queue
        // (the Topic header makes it a broadcast). Targeted responses are sent explicitly to
        // {clients}-{clientId} from the handlers, so they need no static route.
        .Routing(r => r.TypeBased().Map<Notification>(options.Queues.Clients))
        // Real blob-backed DataBus (Azurite locally / Azure Blob in production): the controller
        // scales horizontally and handles MB attachments under load, so attachments must not live
        // in a single instance's memory (unshared + unbounded growth -> OOM).
        .DataBus(d => d.Register(_ => new BlobDataBusStorage(
            configuration["dbote:Cloud:Storage:Connection"]!,
            "loadtest-databus")))
        .Serialization(s => s.UseSystemTextJson())
        .Logging(l => l.MicrosoftExtensionsLogging(serviceProvider.GetRequiredService<ILoggerFactory>()));
});

builder.Services.AddRebusHandler<RegisterEdgeHandler>();
builder.Services.AddRebusHandler<SmallRequestHandler>();
builder.Services.AddRebusHandler<LargeRequestHandler>();
builder.Services.AddHostedService<NotificationBroadcaster>();
// Push this replica's handling metrics to the consolidator (dashboard); it sums across replicas.
builder.Services.AddHostedService<ControllerReporter>();

var app = builder.Build();

// A scaled, headless message handler - a simple liveness endpoint only; the dashboard lives in the
// separate consolidator web app.
app.MapGet("/", () => Results.Text("bote load-test controller (message handler)"));

app.Run();
