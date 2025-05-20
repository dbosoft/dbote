using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rebus.Config;
using Rebus.Routing.TypeBased;
using Rebus.Serialization.Json;
using SuperBus.Samples.Simple.Cloud;
using SuperBus.Samples.Simple.Cloud.Handlers;
using SuperBus.Samples.Simple.Messages;

const string sbConnectionString =
    "Endpoint=sb://127.0.0.1;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddLogging(c => c.AddConsole());
builder.Services.AddRebus(configure => configure
    .Transport(t => t.UseAzureServiceBus(sbConnectionString, "sample-simple-cloud-queue"))
    .Serialization(s => s.UseSystemTextJson())
    .Routing(r => r.TypeBased()
        .Map<PongMessage>("sample-simple-tenant-queue")
        .Map<PushMessage>("sample-simple-tenant-queue")));

builder.Services.AddRebusHandler<PingHandler>();

builder.Services.AddHostedService<PushService>();

var host = builder.Build();

await host.RunAsync();

