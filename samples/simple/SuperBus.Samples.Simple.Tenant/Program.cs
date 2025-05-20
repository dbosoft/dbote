using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rebus.Config;
using Rebus.Routing.TypeBased;
using Rebus.Serialization.Json;
using SuperBus.Samples.Simple.Messages;
using SuperBus.Samples.Simple.Tenant;
using SuperBus.Samples.Simple.Tenant.Handlers;

const string sbConnectionString =
    "Endpoint=sb://127.0.0.1;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddLogging(c => c.AddConsole());
builder.Services.AddRebus(configure => configure
    .Transport(t => t.UseAzureServiceBus(sbConnectionString, "sample-simple-tenant-queue"))
    .Serialization(s => s.UseSystemTextJson())
    .Routing(r => r.TypeBased().Map<PingMessage>("sample-simple-cloud-queue")));
builder.Services.AddRebusHandler<PongHandler>();
builder.Services.AddRebusHandler<PushHandler>();

builder.Services.AddHostedService<BeatService>();
var host = builder.Build();

await host.RunAsync();