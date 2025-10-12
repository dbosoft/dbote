using Dbosoft.Bote.AppConfiguration;
using Dbosoft.Bote.Options;
using Dbosoft.Bote.Rebus.Integration;
using Dbosoft.Bote.Samples.Chat.Cloud.Handler;
using Dbosoft.Bote.Samples.Chat.Messages;
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

builder.Services.Configure<ServiceBusOptions>(builder.Configuration.GetSection("dbote:Cloud:ServiceBus"));

builder.Services.AddApplicationInsightsTelemetryWorkerService();
builder.Services.AddLogging(c => c.AddSimpleConsole());

builder.Services.AddRebus((configure, serviceProvider) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
    return configure
        .Options(b => b.RetryStrategy(errorQueueName: options.Queues.Error))
        .Options(o => o.EnableBote(options.Queues.Connectors))
        .Transport(t => t.UseAzureServiceBus(builder.Configuration.GetSection("dbote:Cloud:ServiceBus:Connection"), options.Queues.Cloud))
        .Routing(r => r.TypeBased().MapAssemblyOf<ChatResponse>(options.Queues.Connectors))
        .Serialization(s => s.UseSystemTextJson())
        .Logging(l => l.MicrosoftExtensionsLogging(serviceProvider.GetRequiredService<ILoggerFactory>()));
});

builder.Services.AddRebusHandler<ChatRequestHandler>();

var host = builder.Build();

await host.RunAsync();