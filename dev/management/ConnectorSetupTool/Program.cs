using ConnectorSetupTool;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLogging(c => c.AddSimpleConsole());
builder.Services.AddHttpClient();

using var host = builder.Build();

var hostEnvironment = host.Services.GetRequiredService<IHostEnvironment>();
var importPath = hostEnvironment.IsDevelopment()
    ? Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "..", "..", "config", "connectors.json")
    : "/import/connectors.json";

var configuration = host.Services.GetRequiredService<IConfiguration>();
var logger = host.Services.GetRequiredService<ILogger<Program>>();
var httpClientFactory = host.Services.GetRequiredService<IHttpClientFactory>();

var identityProviderUrl = configuration["IdentityProviderUrl"] ?? "http://basicidentityprovider";

var json = File.ReadAllText(importPath);
var connectors = JsonSerializer.Deserialize<IReadOnlyList<ConnectorConfig>>(json) ?? [];

logger.LogInformation("Registering {Count} connectors with BasicIdentityProvider at {Url}", connectors.Count, identityProviderUrl);

var httpClient = httpClientFactory.CreateClient();
var successCount = 0;

foreach (var connector in connectors)
{
    try
    {
        var payload = new
        {
            TenantId = connector.TenantId,
            Id = connector.ConnectorId,
            PublicKey = connector.PublicKey
        };

        var response = await httpClient.PostAsJsonAsync($"{identityProviderUrl}/api/register-connector", payload);

        if (response.IsSuccessStatusCode)
        {
            logger.LogInformation("Registered connector {TenantId}/{ConnectorId}", connector.TenantId, connector.ConnectorId);
            successCount++;
        }
        else
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            logger.LogError("Failed to register connector {TenantId}/{ConnectorId}: {StatusCode} - {Error}",
                connector.TenantId, connector.ConnectorId, response.StatusCode, errorContent);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error registering connector {TenantId}/{ConnectorId}", connector.TenantId, connector.ConnectorId);
    }
}

logger.LogInformation("Successfully registered {SuccessCount}/{TotalCount} connectors", successCount, connectors.Count);

if (successCount < connectors.Count)
{
    Environment.ExitCode = 1;
}