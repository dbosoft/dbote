using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rebus.Config;
using Rebus.Routing.TypeBased;
using Rebus.Serialization.Json;
using SuperBus.Rebus.Config;
using SuperBus.Samples.Simple.Messages;
using SuperBus.Samples.Simple.Tenant;
using SuperBus.Samples.Simple.Tenant.Handlers;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<SuperBusOptions>(builder.Configuration.GetSection("SuperBus"));

builder.Services.AddLogging(c => c.AddSimpleConsole());
builder.Services.AddRebus((configure, serviceProvider) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<SuperBusOptions>>().Value;
    return configure
        .Transport(t => t.UseSuperBus(
            new Uri(options.Endpoint),
            $"{options.QueuePrefix}-tenant",
            new SuperBusCredentials
            {
                AgentId = options.AgentId,
                SigningKey = options.SigningKey,
                TenantId = options.TenantId,
            }))
        .Serialization(s => s.UseSystemTextJson())
        .Logging(l => l.MicrosoftExtensionsLogging(serviceProvider.GetRequiredService<ILoggerFactory>()))
        .Routing(r => r.TypeBased().Map<PingMessage>($"{options.QueuePrefix}-cloud"));
});
builder.Services.AddRebusHandler<PongHandler>();
builder.Services.AddRebusHandler<PushHandler>();

builder.Services.AddHostedService<BeatService>();
var host = builder.Build();

await host.RunAsync();