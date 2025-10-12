using Azure.Data.Tables;
using Azure.Identity;
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

Console.WriteLine("Storage initialization complete");
