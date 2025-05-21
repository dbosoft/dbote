using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rebus.Config;
using Rebus.Routing.TypeBased;
using Rebus.Serialization.Json;
using SuperBus.Samples.Simple.Messages;
using SuperBus.Samples.Simple.Tenant;
using SuperBus.Samples.Simple.Tenant.Handlers;
using SuperBus.Transport.Config;

const string sbConnectionString =
    "Endpoint=sb://127.0.0.1;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=SAS_KEY_VALUE;UseDevelopmentEmulator=true;";

const string privateKey =
    "MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgsYY7t/48ZYSZ9kKBkerQdxJy0GeIhDzgzAUyufUPqhqhRANCAATBdziqIRpUgXO1Q+sRYOoA7SHz6DmnzifNRbysqvUOUICxxtYcm4YSB1ctxOjlrQUIcf7+rhJfHG/9sTT4edfy";

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddLogging(c => c.AddConsole());
builder.Services.AddRebus(configure => configure
    .Transport(t => t.UseSuperBus(
        new Uri("http://localhost:7249/api"),
        new SuperBusCredentials()
        {
            AgentId = "agent-1",
            SigningKey = privateKey,
            TenantId = "tenant-1",
        }))
    .Serialization(s => s.UseSystemTextJson())
    .Routing(r => r.TypeBased().Map<PingMessage>("sample-simple-cloud-queue")));
builder.Services.AddRebusHandler<PongHandler>();
builder.Services.AddRebusHandler<PushHandler>();

builder.Services.AddHostedService<BeatService>();
var host = builder.Build();

await host.RunAsync();