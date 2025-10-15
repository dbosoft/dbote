using Dbosoft.Bote.Benchmark.Messages;
using Dbosoft.Bote.Options;
using Dbosoft.Bote.Rebus.Integration;
using DFrame;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Rebus.Config;
using Rebus.Retry.Simple;
using Rebus.Routing.TypeBased;
using Rebus.Serialization.Json;


var builder = DFrameApp.CreateBuilder(portWeb: 7312, portListenWorker: 7313);

builder.WorkerBuilder.ConfigureAppConfiguration((context, config) =>
{
    config.AddJsonFile("appsettings.json");
    config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true);
    config.AddEnvironmentVariables();
});

builder.WorkerBuilder.ConfigureServices((context, services) =>
{
    services.Configure<ServiceBusOptions>(context.Configuration.GetSection("dbote:Runner:ServiceBus"));
    services.AddRebus((configure, serviceProvider) =>
    {
        var options = serviceProvider.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
        var benchmarkQueueName = context.Configuration["dbote:Runner:ServiceBus:Queues:Runner"];
        return configure
            .Options(o => o.EnableSynchronousRequestReply())
            .Options(b => b.RetryStrategy(errorQueueName: options.Queues.Error))
            .Transport(t => t.UseAzureServiceBus(
                context.Configuration.GetSection("dbote:Runner:ServiceBus:Connection"),
                benchmarkQueueName!))
            .Serialization(s => s.UseSystemTextJson())
            .Routing(r => r.TypeBased()
                .Map<BenchmarkRequest>(options.Queues.Cloud));
    });
});

await builder.RunAsync();
