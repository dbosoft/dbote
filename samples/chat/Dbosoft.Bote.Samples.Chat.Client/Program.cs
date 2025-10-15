using Dbosoft.Bote.Client.Authentication;
using Dbosoft.Bote.Client.Options;
using Dbosoft.Bote.Rebus.Config;
using Dbosoft.Bote.Samples.Chat.Client.Handler;
using Dbosoft.Bote.Samples.Chat.Client.Services;
using Dbosoft.Bote.Samples.Chat.Client.Components;
using Dbosoft.Bote.Samples.Chat.Messages;
using Microsoft.Extensions.Options;
using Rebus.Config;
using Rebus.Retry.Simple;
using Rebus.Routing.TypeBased;
using Rebus.Serialization.Json;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 500 * 1024 * 1024;
});

builder.Services.Configure<BoteOptions>(builder.Configuration.GetSection("dbote:Client"));

builder.Services.AddHttpClient();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddApplicationInsightsTelemetry();

builder.Services.AddRebus((configure, serviceProvider) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<BoteOptions>>().Value;

    if (string.IsNullOrEmpty(options.Authentication.Authority))
        throw new InvalidOperationException("dbote:Client:Authentication:Authority is not configured");
    if (string.IsNullOrEmpty(options.Authentication.TokenEndpoint))
        throw new InvalidOperationException("dbote:Client:Authentication:TokenEndpoint is not configured");
    if (string.IsNullOrEmpty(options.Authentication.Scope))
        throw new InvalidOperationException("dbote:Client:Authentication:Scope is not configured");

    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

    return configure
        .Options(b => b.RetryStrategy(errorQueueName: options.Queues.Error))
        .Transport(t => t.UseBote(
            new Uri(options.Endpoint),
            $"{options.Queues.Clients}-{options.ClientId}",
            new BoteCredentials
            {
                ClientId = options.ClientId,
                SigningKey = options.GetSigningKey(),
                TenantId = options.TenantId,
                Authority = options.Authentication.Authority,
                TokenEndpoint = options.Authentication.TokenEndpoint,
                Scope = options.Authentication.Scope,
            },
            httpClientFactory))
        .Serialization(s => s.UseSystemTextJson())
        .Logging(l => l.MicrosoftExtensionsLogging(serviceProvider.GetRequiredService<ILoggerFactory>()))
        .DataBus(d => d.UseBoteDataBus())
        .Routing(r => r.TypeBased()
            .Map<ChatRequest>(options.Queues.Cloud)
            .Map<FileShareRequest>(options.Queues.Cloud));
});

builder.Services.AddRebusHandler<ChatResponseHandler>();
builder.Services.AddRebusHandler<FileShareMessageHandler>();

builder.Services.AddSingleton<IChatService, ChatService>();
builder.Services.AddSingleton<IFileStorageService, FileStorageService>();

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

app.MapGet("/download/{fileId}", async (string fileId, IFileStorageService fileStorage) =>
{
    var (attachment, fileName) = fileStorage.GetFile(fileId);

    if (attachment == null || fileName == null)
    {
        return Results.NotFound();
    }

    var stream = await attachment.OpenRead();
    return Results.File(stream, "application/octet-stream", fileName);
});

app.Run();
