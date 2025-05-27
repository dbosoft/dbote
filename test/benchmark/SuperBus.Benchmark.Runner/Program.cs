using DFrame;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Rebus;
using Rebus.Bus;
using Rebus.Config;
using Rebus.Config;
using Rebus.Retry.Simple;
using Rebus.Routing.TypeBased;
using Rebus.Serialization.Json;
using SuperBus.Benchmark.Messages;
using SuperBus.Benchmark.Runner;
using SuperBus.Benchmark.Service;
using System;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;


var builder = DFrameApp.CreateBuilder(portWeb: 7312, portListenWorker: 7313);

builder.WorkerBuilder.ConfigureAppConfiguration((context, config) =>
{
    config.AddJsonFile("appsettings.json");
    config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true);
    config.AddEnvironmentVariables();
});

builder.WorkerBuilder.ConfigureServices((context, services) =>
{
    services.Configure<SuperBusOptions>(context.Configuration.GetSection("SuperBus"));
    services.AddRebus((configure, serviceProvider) =>
    {
        var options = serviceProvider.GetRequiredService<IOptions<SuperBusOptions>>().Value;
        return configure
            .Options(o => o.EnableSynchronousRequestReply())
            .Options(b => b.RetryStrategy(errorQueueName: $"{options.QueuePrefix}-error"))
            .Transport(t => t.UseAzureServiceBus(options.Connection, $"{options.QueuePrefix}-runner"))
            .Serialization(s => s.UseSystemTextJson())
            .Routing(r => r.TypeBased()
                .Map<BenchmarkRequest>($"{options.QueuePrefix}-cloud"));
    });
});

await builder.RunAsync();
