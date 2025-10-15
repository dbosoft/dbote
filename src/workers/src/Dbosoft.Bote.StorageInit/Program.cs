using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

// Initialize storage resources
var connectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING")
    ?? throw new InvalidOperationException("AZURE_STORAGE_CONNECTION_STRING not set");

Console.WriteLine("Initializing Bote storage resources...");

// Create subscriptions table
var tableServiceClient = new TableServiceClient(connectionString);
var subscriptionsTable = tableServiceClient.GetTableClient("subscriptions");
await subscriptionsTable.CreateIfNotExistsAsync();
Console.WriteLine("Created subscriptions table: subscriptions");

// Create blob containers from environment variables (optional)
var containersList = Environment.GetEnvironmentVariable("BLOB_CONTAINERS");
if (!string.IsNullOrEmpty(containersList))
{
    var blobServiceClient = new BlobServiceClient(connectionString);
    var containers = containersList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    foreach (var containerName in containers)
    {
        var container = blobServiceClient.GetBlobContainerClient(containerName);
        await container.CreateIfNotExistsAsync();
        Console.WriteLine($"Created blob container: {containerName}");
    }
}

var queueServiceClient = new QueueServiceClient(connectionString);
var queueClient = queueServiceClient.GetQueueClient("databus-copy-monitor");
await queueClient.CreateIfNotExistsAsync();

Console.WriteLine("Storage initialization complete");
