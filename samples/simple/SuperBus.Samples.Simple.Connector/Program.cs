using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rebus.Config;
using Rebus.Retry.Simple;
using Rebus.Routing.TypeBased;
using Rebus.Serialization.Json;
using SuperBus.Connector.Authentication;
using SuperBus.Connector.Options;
using SuperBus.Rebus.Config;
using SuperBus.Samples.Simple.Connector;
using SuperBus.Samples.Simple.Connector.Handlers;
using SuperBus.Samples.Simple.Messages;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<SuperBusOptions>(builder.Configuration.GetSection("SuperBus:Connector"));

builder.Services.AddHttpClient();
builder.Services.AddApplicationInsightsTelemetryWorkerService();
builder.Services.AddLogging(c => c.AddSimpleConsole());

builder.Services.AddRebus((configure, serviceProvider) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<SuperBusOptions>>().Value;

    if (string.IsNullOrEmpty(options.Authentication.Authority))
        throw new InvalidOperationException("SuperBus:Connector:Authentication:Authority is not configured");
    if (string.IsNullOrEmpty(options.Authentication.TokenEndpoint))
        throw new InvalidOperationException("SuperBus:Connector:Authentication:TokenEndpoint is not configured");
    if (string.IsNullOrEmpty(options.Authentication.Scope))
        throw new InvalidOperationException("SuperBus:Connector:Authentication:Scope is not configured");

    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

    return configure
        .Options(b => b.RetryStrategy(errorQueueName: options.Queues.Error))
        .Transport(t => t.UseSuperBus(
            new Uri(options.Endpoint),
            $"{options.Queues.Connectors}-{options.ConnectorId}",
            new SuperBusCredentials
            {
                ConnectorId = options.ConnectorId,
                SigningKey = options.GetSigningKey(),
                TenantId = options.TenantId,
                Authority = options.Authentication.Authority,
                TokenEndpoint = options.Authentication.TokenEndpoint,
                Scope = options.Authentication.Scope,
            },
            httpClientFactory))
        .Serialization(s => s.UseSystemTextJson())
        .Logging(l => l.MicrosoftExtensionsLogging(serviceProvider.GetRequiredService<ILoggerFactory>()))
        .Routing(r => r.TypeBased().Map<PingMessage>(options.Queues.Cloud));
});
builder.Services.AddRebusHandler<PongHandler>();
builder.Services.AddRebusHandler<PushHandler>();

builder.Services.AddHostedService<BeatService>();

var host = builder.Build();

await host.RunAsync();