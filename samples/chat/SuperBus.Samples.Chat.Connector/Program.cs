using Microsoft.Extensions.Options;
using Rebus.Config;
using Rebus.Retry.Simple;
using Rebus.Routing.TypeBased;
using Rebus.Serialization.Json;
using SuperBus.Connector.Authentication;
using SuperBus.Connector.Options;
using SuperBus.Rebus.Config;
using SuperBus.Samples.Chat.Connector.Components;
using SuperBus.Samples.Chat.Connector.Handler;
using SuperBus.Samples.Chat.Connector.Services;
using SuperBus.Samples.Chat.Messages;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<SuperBusOptions>(builder.Configuration.GetSection("SuperBus:Connector"));

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddApplicationInsightsTelemetry();

builder.Services.AddRebus((configure, serviceProvider) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<SuperBusOptions>>().Value;
    return configure
        .Options(b => b.RetryStrategy(errorQueueName: options.Queues.Error))
        .Transport(t => t.UseSuperBus(
            new Uri(options.Endpoint),
            $"{options.Queues.Connectors}-{options.ConnectorId}",
            new SuperBusCredentials
            {
                ConnectorId = options.ConnectorId,
                SigningKey = options.GetSigningKey(),
                TenantId = options.TenantId,
            }))
        .Serialization(s => s.UseSystemTextJson())
        .Logging(l => l.MicrosoftExtensionsLogging(serviceProvider.GetRequiredService<ILoggerFactory>()))
        .Routing(r => r.TypeBased().Map<ChatRequest>(options.Queues.Cloud));
});

builder.Services.AddRebusHandler<ChatResponseHandler>();

builder.Services.AddSingleton<IChatService, ChatService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
