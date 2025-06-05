using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SuperBus.AppConfiguration;
using SuperBus.SuperBusWorker;
using SuperBus.SuperBusWorker.Converters;
using SuperBus.Management.Persistence;
using SuperBus.Options;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Configuration.AddSuperBusAzureAppConfiguration();

builder.ConfigureFunctionsWebApplication();
builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Services.AddLogging(c => c.AddSimpleConsole());

builder.Services.Configure<ServiceBusOptions>(builder.Configuration.GetSection("SuperBus:Worker:ServiceBus"));
builder.Services.Configure<OpenIdOptions>(builder.Configuration.GetSection("SuperBus:Worker:OpenId"));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("SuperBus:Worker:Storage"));

builder.Services.AddServerlessHub<Messages>(serviceManagerBuilder =>
{
    serviceManagerBuilder.WithOptions(options =>
    {
        options.ConnectionString = builder.Configuration["SuperBus:Worker:SignalR:Connection"];
    });
});

builder.Services.AddSingleton<IMessageConverter, MessageConverter>();
builder.Services.AddSingleton<ITokenCredentialsProvider, TokenCredentialsProvider>();

builder.Services.AddAzureClients(clientBuilder =>
{
    clientBuilder.UseCredential(new DefaultAzureCredential());

    clientBuilder.AddServiceBusClient(builder.Configuration["SuperBus:Worker:ServiceBus:Connection"]);

    clientBuilder.AddTableServiceClient(builder.Configuration["SuperBus:Worker:Storage:Connection"]);
    clientBuilder.AddQueueServiceClient(builder.Configuration["SuperBus:Worker:Storage:Connection"]);
});

builder.Services.AddTableStorage(builder.Configuration["SuperBus:Worker:Storage:Prefix"]);

builder.Build().Run();
