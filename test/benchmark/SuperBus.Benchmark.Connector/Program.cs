using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rebus.Config;
using Rebus.Persistence.InMem;
using Rebus.Retry.Simple;
using Rebus.Routing.TypeBased;
using Rebus.Serialization.Json;
using SuperBus.Benchmark.Connector;
using SuperBus.Benchmark.Messages;
using SuperBus.Connector.Authentication;
using SuperBus.Connector.Options;
using SuperBus.Rebus.Config;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<SuperBusOptions>(builder.Configuration.GetSection("SuperBus:Connector"));

builder.Services.AddApplicationInsightsTelemetryWorkerService();
builder.Services.AddLogging(c => c.AddSimpleConsole());

builder.Services.AddRebus((configure, serviceProvider) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<SuperBusOptions>>().Value;
    return configure
        .Options(b => b.RetryStrategy(errorQueueName: options.Queues.Error))
        .Transport(t => t.UseSuperBus(
            new Uri(options.Endpoint),
            $"{options.Queues.Connectors}-{options.ConnectorId}",
            new SuperBusCredentials
            {
                ConnectorId = options.ConnectorId,
                SigningKey = options.Authentication.GetSigningKey(),
                TenantId = options.TenantId,
            }))
        .Serialization(s => s.UseSystemTextJson())
        .Logging(l => l.MicrosoftExtensionsLogging(serviceProvider.GetRequiredService<ILoggerFactory>()))
        .Routing(r => r.TypeBased().Map<ConnectorResponse>(options.Queues.Cloud));
});

builder.Services.AddRebusHandler<ConnectorRequestHandler>();

var host = builder.Build();

await host.RunAsync();
