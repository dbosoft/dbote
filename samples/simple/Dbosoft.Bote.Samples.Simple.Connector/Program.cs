using Dbosoft.Bote.Connector.Authentication;
using Dbosoft.Bote.Connector.Options;
using Dbosoft.Bote.Rebus.Config;
using Dbosoft.Bote.Samples.Simple.Connector;
using Dbosoft.Bote.Samples.Simple.Connector.Handlers;
using Dbosoft.Bote.Samples.Simple.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rebus.Config;
using Rebus.Retry.Simple;
using Rebus.Routing.TypeBased;
using Rebus.Serialization.Json;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<BoteOptions>(builder.Configuration.GetSection("dbote:Connector"));

builder.Services.AddHttpClient();
builder.Services.AddApplicationInsightsTelemetryWorkerService();
builder.Services.AddLogging(c => c.AddSimpleConsole());

builder.Services.AddRebus((configure, serviceProvider) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<BoteOptions>>().Value;

    if (string.IsNullOrEmpty(options.Authentication.Authority))
        throw new InvalidOperationException("dbote:Connector:Authentication:Authority is not configured");
    if (string.IsNullOrEmpty(options.Authentication.TokenEndpoint))
        throw new InvalidOperationException("dbote:Connector:Authentication:TokenEndpoint is not configured");
    if (string.IsNullOrEmpty(options.Authentication.Scope))
        throw new InvalidOperationException("dbote:Connector:Authentication:Scope is not configured");

    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

    return configure
        .Options(b => b.RetryStrategy(errorQueueName: options.Queues.Error))
        .Transport(t => t.UseBote(
            new Uri(options.Endpoint),
            $"{options.Queues.Connectors}-{options.ConnectorId}",
            new BoteCredentials
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