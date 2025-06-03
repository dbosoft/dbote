using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rebus.Config;
using Rebus.Retry.Simple;
using Rebus.Routing.TypeBased;
using Rebus.Serialization.Json;
using SuperBus.Benchmark.Messages;
using SuperBus.Benchmark.Service;
using SuperBus.Rebus.Integration;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<SuperBusOptions>(builder.Configuration.GetSection("SuperBus"));

builder.Services.AddLogging(c => c.AddSimpleConsole());
builder.Services.AddRebus((configure, serviceProvider) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<SuperBusOptions>>().Value;
    return configure
        //         .Options(o => o.EnableSynchronousRequestReply())
        .Options(b => b.RetryStrategy(errorQueueName: "superbus-benchmark-error"))
        .Options(o => o.EnableSuperBus(options.StoragePrefix))
        .Transport(t => t.UseAzureServiceBus(options.Connection, $"{options.StoragePrefix}-service"))
        .Serialization(s => s.UseSystemTextJson())
        .Logging(l => l.MicrosoftExtensionsLogging(serviceProvider.GetRequiredService<ILoggerFactory>()))
        .Routing(r => r.TypeBased()
            .Map<ServiceResponse>($"{options.StoragePrefix}-cloud"));
});

builder.Services.AddRebusHandler<ServiceRequestHandler>();

var host = builder.Build();

await host.RunAsync();
