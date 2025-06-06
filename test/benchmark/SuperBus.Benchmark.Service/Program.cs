using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rebus.Config;
using Rebus.Retry.Simple;
using Rebus.Routing.TypeBased;
using Rebus.Serialization.Json;
using SuperBus.AppConfiguration;
using SuperBus.Benchmark.Messages;
using SuperBus.Benchmark.Service;
using SuperBus.Options;
using SuperBus.Rebus.Integration;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddSuperBusAzureAppConfiguration();

builder.Services.Configure<ServiceBusOptions>(builder.Configuration.GetSection("SuperBus:Service:ServiceBus"));

builder.Services.AddApplicationInsightsTelemetryWorkerService();
builder.Services.AddLogging(c => c.AddSimpleConsole());

builder.Services.AddRebus((configure, serviceProvider) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
    var serviceQueueName = builder.Configuration["SuperBus:Service:ServiceBus:Queues:Service"];
    return configure
        //         .Options(o => o.EnableSynchronousRequestReply())
        .Options(b => b.RetryStrategy(errorQueueName: options.Queues.Error))
        .Options(o => o.EnableSuperBus(options.Queues.Connectors))
        .Transport(t => t.UseAzureServiceBus(options.Connection, serviceQueueName))
        .Serialization(s => s.UseSystemTextJson())
        .Logging(l => l.MicrosoftExtensionsLogging(serviceProvider.GetRequiredService<ILoggerFactory>()))
        .Routing(r => r.TypeBased()
            .Map<ServiceResponse>(options.Queues.Cloud));
});

builder.Services.AddRebusHandler<ServiceRequestHandler>();

var host = builder.Build();

await host.RunAsync();
