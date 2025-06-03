using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rebus.Config;
using Rebus.Routing.TypeBased;
using Rebus.Serialization.Json;
using SuperBus.Rebus.Integration;
using SuperBus.Samples.Simple.Cloud;
using SuperBus.Samples.Simple.Cloud.Handlers;
using SuperBus.Samples.Simple.Messages;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<SuperBusOptions>(builder.Configuration.GetSection("SuperBus"));

builder.Services.AddLogging(c => c.AddSimpleConsole());
builder.Services.AddRebus((configure, serviceProvider) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<SuperBusOptions>>().Value;
    return configure
        .Options(o => o.EnableSuperBus(options.StoragePrefix))
        .Transport(t => t.UseAzureServiceBus(options.Connection, $"{options.StoragePrefix}-cloud"))
        .Serialization(s => s.UseSystemTextJson())
        .Logging(l => l.MicrosoftExtensionsLogging(serviceProvider.GetRequiredService<ILoggerFactory>()))
        .Routing(r => r.TypeBased()
            .Map<PongMessage>($"{options.StoragePrefix}-connectors-connector-a")
            .Map<PushMessage>($"{options.StoragePrefix}-connectors-connector-a"));
});

builder.Services.AddRebusHandler<PingHandler>();

builder.Services.AddHostedService<PushService>();

var host = builder.Build();

await host.RunAsync();

