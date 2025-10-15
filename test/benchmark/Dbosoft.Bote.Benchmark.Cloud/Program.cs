using Dbosoft.Bote.AppConfiguration;
using Dbosoft.Bote.Benchmark.Cloud.Sagas;
using Dbosoft.Bote.Benchmark.Messages;
using Dbosoft.Bote.Options;
using Dbosoft.Bote.Rebus.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rebus.Config;
using Rebus.Persistence.InMem;
using Rebus.Retry.Simple;
using Rebus.Routing.TypeBased;
using Rebus.Serialization.Json;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddDboteAzureAppConfiguration();

builder.Services.Configure<ServiceBusOptions>(builder.Configuration.GetSection("dbote:Cloud:ServiceBus"));

builder.Services.AddApplicationInsightsTelemetryWorkerService();
builder.Services.AddLogging(c => c.AddSimpleConsole());

builder.Services.AddRebus((configure, serviceProvider) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
    // TODO Use proper options
    var serviceQueueName = builder.Configuration["dbote:Cloud:ServiceBus:Queues:Service"];
    return configure
        .Options(b => b.RetryStrategy(errorQueueName: options.Queues.Error))
        .Options(o => o.EnableBote(options.Queues.Clients))
        .Transport(t => t.UseAzureServiceBus(
            builder.Configuration.GetSection("dbote:Cloud:ServiceBus:Connection"),
            options.Queues.Cloud))
        .Serialization(s => s.UseSystemTextJson())
        .Sagas(s => s.StoreInMemory())
        .Logging(l => l.MicrosoftExtensionsLogging(serviceProvider.GetRequiredService<ILoggerFactory>()))
        .Routing(r => r.TypeBased()
            .Map<ClientRequest>($"{options.Queues.Clients}-client-a")
            .Map<ServiceRequest>(serviceQueueName));
});

builder.Services.AddRebusHandler<BenchmarkSaga>();

var host = builder.Build();

await host.RunAsync();
