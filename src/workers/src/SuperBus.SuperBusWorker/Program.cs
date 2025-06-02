using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SuperBus.SuperBusWorker;
using SuperBus.SuperBusWorker.Converters;
using SuperBus.Management.Persistence;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

//builder.Services
//    .AddApplicationInsightsTelemetryWorkerService()
//    .ConfigureFunctionsApplicationInsights();

builder.Services.AddLogging(c => c.AddSimpleConsole());

builder.Services.Configure<SuperBusOptions>(builder.Configuration.GetSection("SuperBus"));
builder.Services.Configure<OpenIdOptions>(builder.Configuration.GetSection("OpenId"));

builder.Services.AddServerlessHub<Messages>();

builder.Services.AddSingleton<IMessageConverter, MessageConverter>();
builder.Services.AddSingleton<ITokenCredentialsProvider, TokenCredentialsProvider>();

builder.Services.AddAzureClients(clientBuilder =>
{
    clientBuilder.UseCredential(new DefaultAzureCredential());

    clientBuilder.AddServiceBusClient(builder.Configuration["ServiceBusConnection"]);

    clientBuilder.AddTableServiceClient(builder.Configuration["AzureWebJobsStorage"]);
    clientBuilder.AddQueueServiceClient(builder.Configuration["AzureWebJobsStorage"]);
});

builder.Services.AddTableStorage(builder.Configuration["SuperBus:QueuePrefix"]);

builder.Build().Run();
