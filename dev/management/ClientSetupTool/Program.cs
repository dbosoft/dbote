using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using ClientSetupTool;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLogging(c => c.AddSimpleConsole());
builder.Services.AddHttpClient();

using var host = builder.Build();

var hostEnvironment = host.Services.GetRequiredService<IHostEnvironment>();
var importPath = hostEnvironment.IsDevelopment()
    ? Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "..", "..", "config", "clients.json")
    : "/import/clients.json";

var configuration = host.Services.GetRequiredService<IConfiguration>();
var logger = host.Services.GetRequiredService<ILogger<Program>>();
var httpClientFactory = host.Services.GetRequiredService<IHttpClientFactory>();

var identityProviderUrl = configuration["IdentityProviderUrl"] ?? "http://basicidentityprovider";

var json = File.ReadAllText(importPath);
var clients = JsonSerializer.Deserialize<IReadOnlyList<ClientConfig>>(json) ?? [];

logger.LogInformation("Registering {Count} clients with BasicIdentityProvider at {Url}", clients.Count, identityProviderUrl);

var httpClient = httpClientFactory.CreateClient();
var successCount = 0;

foreach (var client in clients)
{
    try
    {
        var payload = new
        {
            TenantId = client.TenantId,
            Id = client.ClientId,
            PublicKey = client.PublicKey
        };

        var response = await httpClient.PostAsJsonAsync($"{identityProviderUrl}/api/register-client", payload);

        if (response.IsSuccessStatusCode)
        {
            logger.LogInformation("Registered client {TenantId}/{ClientId}", client.TenantId, client.ClientId);
            successCount++;
        }
        else
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            logger.LogError("Failed to register client {TenantId}/{ClientId}: {StatusCode} - {Error}",
                client.TenantId, client.ClientId, response.StatusCode, errorContent);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error registering client {TenantId}/{ClientId}", client.TenantId, client.ClientId);
    }
}

logger.LogInformation("Successfully registered {SuccessCount}/{TotalCount} clients", successCount, clients.Count);

if (successCount < clients.Count)
{
    Environment.ExitCode = 1;
}