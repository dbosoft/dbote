using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rebus.Config;
using Rebus.Persistence.InMem;
using Rebus.Retry.Simple;
using Rebus.Routing.TypeBased;
using Rebus.Serialization.Json;
using SuperBus.Benchmark.Cloud;
using SuperBus.Benchmark.Cloud.Sagas;
using SuperBus.Benchmark.Messages;
using SuperBus.Rebus.Integration;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<SuperBusOptions>(builder.Configuration.GetSection("SuperBus"));

builder.Services.AddLogging(c => c.AddSimpleConsole());
builder.Services.AddRebus((configure, serviceProvider) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<SuperBusOptions>>().Value;
    return configure
//         .Options(o => o.EnableSynchronousRequestReply())
        .Options(b => b.RetryStrategy(errorQueueName: "superbus-benchmark-error"))
        .Options(o => o.EnableSuperBus(options.StoragePrefix))
        .Transport(t => t.UseAzureServiceBus(options.Connection, $"{options.StoragePrefix}-cloud"))
        .Serialization(s => s.UseSystemTextJson())
        .Sagas(s => s.StoreInMemory())
        .Logging(l => l.MicrosoftExtensionsLogging(serviceProvider.GetRequiredService<ILoggerFactory>()))
        .Routing(r => r.TypeBased()
            .Map<ConnectorRequest>($"{options.StoragePrefix}-connectors-connector-a")
            .Map<ServiceRequest>($"{options.StoragePrefix}-service"));
});

builder.Services.AddRebusHandler<BenchmarkSaga>();

var host = builder.Build();

await host.RunAsync();
