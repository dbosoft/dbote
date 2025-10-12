using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Dbosoft.Bote.AppConfiguration;
using Dbosoft.Bote.BoteWorker;
using Dbosoft.Bote.BoteWorker.Converters;
using Dbosoft.Bote.Options;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.SignalR;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
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
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("dbote:Worker:Storage"));

builder.Services.AddServerlessHub<Messages>();

builder.Services.AddSingleton<IMessageConverter, MessageConverter>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ITokenValidationService, TokenValidationService>();

builder.Services.AddAzureClients(clientBuilder =>
{
    clientBuilder.UseCredential(new DefaultAzureCredential());
    clientBuilder.AddServiceBusClient(builder.Configuration.GetSection("dbote:Worker:ServiceBus:Connection"));
    clientBuilder.AddQueueServiceClient(builder.Configuration.GetSection("dbote:Worker:Storage:Connection"));
    clientBuilder.AddTableServiceClient(builder.Configuration.GetSection("dbote:Worker:Storage:Connection"));
});

builder.Build().Run();
