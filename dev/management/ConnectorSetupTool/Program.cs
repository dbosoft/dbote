using Azure.Data.Tables;
using Azure.Identity;
using ConnectorSetupTool;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLogging(c => c.AddSimpleConsole());
builder.Services.AddAzureClients(clientBuilder =>
{
    clientBuilder.UseCredential(new DefaultAzureCredential());

    clientBuilder.AddTableServiceClient(builder.Configuration["StorageConnection"]);
});

using var host = builder.Build();

var hostEnvironment = host.Services.GetRequiredService<IHostEnvironment>();
var importPath = hostEnvironment.IsDevelopment()
    ? Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "..", "..", "config", "connectors.json")
    : "/import/connectors.json";

var configuration = host.Services.GetRequiredService<IConfiguration>();

var json = File.ReadAllText(importPath);
var connectors = System.Text.Json.JsonSerializer.Deserialize<IReadOnlyList<ConnectorConfig>>(json) ?? [];

/*
var connectorRepository = host.Services.GetRequiredService<IConnectorRepository>();
var result = await Importer.ImportConnectors(connectorRepository, connectors).Run();
result.ThrowIfFail();
*/

var tableServiceClient = host.Services.GetRequiredService<TableServiceClient>();
var storagePrefix = configuration["StoragePrefix"] ?? "superbus";
var tableName = SanitizeTableName($"{storagePrefix}-connectors");
var tableClient = tableServiceClient.GetTableClient(tableName);
await tableClient.CreateIfNotExistsAsync();

foreach (var connector in connectors)
{
    var entity = new TableEntity(connector.TenantId.ToUpperInvariant(), connector.ConnectorId.ToUpperInvariant())
    {
        ["PublicKey"] = connector.PublicKey,
    };
    await tableClient.UpsertEntityAsync(entity);
}

string SanitizeTableName(string name) =>
    new(name.Where(char.IsLetterOrDigit).ToArray());