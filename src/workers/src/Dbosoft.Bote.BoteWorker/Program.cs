using Azure.Identity;
using Dbosoft.Bote.AppConfiguration;
using Dbosoft.Bote.BoteWorker;
using Dbosoft.Bote.BoteWorker.Converters;
using Dbosoft.Bote.BoteWorker.Services;
using Dbosoft.Bote.Options;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Configuration.AddDboteAzureAppConfiguration();

builder.ConfigureFunctionsWebApplication();
builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Services.AddLogging(loggingBuilder =>
{
    loggingBuilder.AddSimpleConsole();
    loggingBuilder.AddApplicationInsights();
    loggingBuilder.Services.Configure<LoggerFilterOptions>(
        options => options.Rules.Add(
            new LoggerFilterRule(
                "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider",
                null,
                LogLevel.Debug,
                null)));
});

builder.Services.Configure<ServiceBusOptions>(builder.Configuration.GetSection("dbote:Worker:ServiceBus"));
builder.Services.Configure<OpenIdOptions>(builder.Configuration.GetSection("dbote:Worker:OpenId"));
builder.Services.Configure<ClientStorageOptions>(builder.Configuration.GetSection("dbote:Worker:Storage"));

builder.Services.AddServerlessHub<BoteHub>();

builder.Services.AddSingleton<IMessageConverter, MessageConverter>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ITokenValidationService, TokenValidationService>();

// DataBus support - tenant storage resolution
builder.Services.AddSingleton<ITenantStorageResolver, DefaultTenantStorageResolver>();
builder.Services.AddSingleton<DataBusCopyProcessor>();
builder.Services.AddAzureClients(clientBuilder =>
{
    clientBuilder.UseCredential(new DefaultAzureCredential());
    clientBuilder.AddServiceBusClient(builder.Configuration.GetSection("dbote:Worker:ServiceBus:Connection"));
    clientBuilder.AddQueueServiceClient(builder.Configuration.GetSection("dbote:Worker:Storage:Connection"));
    clientBuilder.AddQueueServiceClient(builder.Configuration.GetSection("AzureWebJobsStorage"))
        .WithName("AzureWebJobsStorage")
        .ConfigureOptions(options => options.MessageEncoding = Azure.Storage.Queues.QueueMessageEncoding.Base64);
    clientBuilder.AddTableServiceClient(builder.Configuration.GetSection("dbote:Worker:Storage:Connection"));
    clientBuilder.AddBlobServiceClient(builder.Configuration.GetSection("dbote:Worker:Storage:Connection"));
});

builder.Build().Run();
