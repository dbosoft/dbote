using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rebus.Config;
using Rebus.Persistence.InMem;
using Rebus.Retry.Simple;
using Rebus.Routing.TypeBased;
using Rebus.Serialization.Json;
using SuperBus.AppConfiguration;
using SuperBus.Benchmark.Cloud.Sagas;
using SuperBus.Benchmark.Messages;
using SuperBus.Options;
using SuperBus.Rebus.Integration;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddSuperBusAzureAppConfiguration();

builder.Services.Configure<ServiceBusOptions>(builder.Configuration.GetSection("SuperBus:Cloud:ServiceBus"));

builder.Services.AddLogging(c => c.AddSimpleConsole());
builder.Services.AddRebus((configure, serviceProvider) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
    // TODO Use proper options
    var serviceQueueName = builder.Configuration["SuperBus:Cloud:ServiceBus:Service"];
    return configure
        .Options(b => b.RetryStrategy(errorQueueName: options.Queues.Error))
        .Options(o => o.EnableSuperBus(options.Queues.Connectors))
        .Transport(t => t.UseAzureServiceBus(options.Connection, options.Queues.Cloud))
        .Serialization(s => s.UseSystemTextJson())
        .Sagas(s => s.StoreInMemory())
        .Logging(l => l.MicrosoftExtensionsLogging(serviceProvider.GetRequiredService<ILoggerFactory>()))
        .Routing(r => r.TypeBased()
            .Map<ConnectorRequest>($"{options.Queues.Connectors}-connector-a")
            .Map<ServiceRequest>(serviceQueueName));
});

builder.Services.AddRebusHandler<BenchmarkSaga>();

var host = builder.Build();

await host.RunAsync();
