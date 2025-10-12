using Dbosoft.Bote.AppConfiguration;
using Dbosoft.Bote.Benchmark.Messages;
using Dbosoft.Bote.Benchmark.Service;
using Dbosoft.Bote.Options;
using Dbosoft.Bote.Rebus.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rebus.Config;
using Rebus.Retry.Simple;
using Rebus.Routing.TypeBased;
using Rebus.Serialization.Json;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddDboteAzureAppConfiguration();

builder.Services.Configure<ServiceBusOptions>(builder.Configuration.GetSection("dbote:Service:ServiceBus"));

builder.Services.AddApplicationInsightsTelemetryWorkerService();
builder.Services.AddLogging(c => c.AddSimpleConsole());

builder.Services.AddRebus((configure, serviceProvider) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
    var serviceQueueName = builder.Configuration["dbote:Service:ServiceBus:Queues:Service"];
    return configure
        .Options(b => b.RetryStrategy(errorQueueName: options.Queues.Error))
        .Options(o => o.EnableBote(options.Queues.Connectors))
        .Transport(t => t.UseAzureServiceBus(
            builder.Configuration.GetSection("dbote:Service:ServiceBus:Connection"),
            serviceQueueName!))
        .Serialization(s => s.UseSystemTextJson())
        .Logging(l => l.MicrosoftExtensionsLogging(serviceProvider.GetRequiredService<ILoggerFactory>()))
        .Routing(r => r.TypeBased()
            .Map<ServiceResponse>(options.Queues.Cloud));
});

builder.Services.AddRebusHandler<ServiceRequestHandler>();

var host = builder.Build();

await host.RunAsync();
