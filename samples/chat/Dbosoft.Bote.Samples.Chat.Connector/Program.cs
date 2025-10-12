using Dbosoft.Bote.Connector.Authentication;
using Dbosoft.Bote.Connector.Options;
using Dbosoft.Bote.Rebus.Config;
using Dbosoft.Bote.Samples.Chat.Connector.Components;
using Dbosoft.Bote.Samples.Chat.Connector.Handler;
using Dbosoft.Bote.Samples.Chat.Connector.Services;
using Dbosoft.Bote.Samples.Chat.Messages;
using Microsoft.Extensions.Options;
using Rebus.Config;
using Rebus.Retry.Simple;
using Rebus.Routing.TypeBased;
using Rebus.Serialization.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BoteOptions>(builder.Configuration.GetSection("dbote:Connector"));

builder.Services.AddHttpClient();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddApplicationInsightsTelemetry();

builder.Services.AddRebus((configure, serviceProvider) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<BoteOptions>>().Value;

    if (string.IsNullOrEmpty(options.Authentication.Authority))
        throw new InvalidOperationException("dbote:Connector:Authentication:Authority is not configured");
    if (string.IsNullOrEmpty(options.Authentication.TokenEndpoint))
        throw new InvalidOperationException("dbote:Connector:Authentication:TokenEndpoint is not configured");
    if (string.IsNullOrEmpty(options.Authentication.Scope))
        throw new InvalidOperationException("dbote:Connector:Authentication:Scope is not configured");

    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

    return configure
        .Options(b => b.RetryStrategy(errorQueueName: options.Queues.Error))
        .Transport(t => t.UseBote(
            new Uri(options.Endpoint),
            $"{options.Queues.Connectors}-{options.ConnectorId}",
            new BoteCredentials
            {
                ConnectorId = options.ConnectorId,
                SigningKey = options.GetSigningKey(),
                TenantId = options.TenantId,
                Authority = options.Authentication.Authority,
                TokenEndpoint = options.Authentication.TokenEndpoint,
                Scope = options.Authentication.Scope,
            },
            httpClientFactory))
        .Serialization(s => s.UseSystemTextJson())
        .Logging(l => l.MicrosoftExtensionsLogging(serviceProvider.GetRequiredService<ILoggerFactory>()))
        .Routing(r => r.TypeBased().Map<ChatRequest>(options.Queues.Cloud));
});

builder.Services.AddRebusHandler<ChatResponseHandler>();

builder.Services.AddSingleton<IChatService, ChatService>();

var app = builder.Build();

// Subscribe to "chat" topic via Rebus Advanced Topics API
// This triggers BoteSubscriptionStorage to call SignalR's SubscribeToTopic
var bus = app.Services.GetRequiredService<Rebus.Bus.IBus>();
bus.Advanced.Topics.Subscribe("chat").Wait();

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
