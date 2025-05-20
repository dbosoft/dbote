using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ServiceBusApiEmulator;
using ServiceBusApiEmulator.Configuration;
using ServiceBusApiEmulator.Models;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsProduction())
{
    builder.Configuration.AddJsonFile("/config/config.json");
}

if (builder.Environment.IsDevelopment())
{
    // Add config file using the absolute path as the relative does not seem to work
    builder.Configuration.AddJsonFile(Path.Combine(Environment.CurrentDirectory,"..", "..", "config", "servicebus.json"));
}

builder.Services.Configure<UserConfigOptions>(builder.Configuration.GetSection("UserConfig"));

var app = builder.Build();


app.MapGet("/{entity}", (string entity, [FromServices] IOptions<UserConfigOptions> config) =>
{
    var queueConfig = config.Value.Namespaces[0].Queues.FirstOrDefault(q => q.Name == entity);
    if (queueConfig is null)
        return Results.NotFound();

    var queueDescription = new QueueDescription()
    {
        MaxSizeInMegabytes = 1024,
        AccessedAt = DateTime.Now,
        DeadLetteringOnMessageExpiration = false,
        EnableBatchedOperations = false,
        CreatedAt = DateTime.Now,
        EnableExpress = false,
        EnablePartitioning = false,
        IsAnonymousAccessible = false,
        MaxDeliveryCount = 3,
        EntityAvailabilityStatus = EntityAvailabilityStatus.Available,
        MaxMessageSizeInKilobytes = 256,
        MessageCount = 0,
        RequiresDuplicateDetection = false,
        RequiresSession = false,
        SizeInBytes = 1024,
        SupportOrdering = false,
        Status = EntityStatus.Active,
        UpdatedAt = DateTime.Now,
    };

    return Results.Extensions.AtomXml(entity, queueDescription);
});

await app.RunAsync();
