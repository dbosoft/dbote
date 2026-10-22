using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Dbosoft.Bote.Cloud;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// The cloud side of the simple sample as Azure Functions, without Rebus: the clients are the
// same Rebus clients as with the Rebus cloud service.
var builder = FunctionsApplication.CreateBuilder(args);

var serviceBus = builder.Configuration.GetSection("dbote:Cloud:ServiceBus");

// The same connection the Service Bus trigger uses: a connection string locally, the
// namespace with a managed identity in Azure.
builder.Services.AddSingleton(_ => serviceBus["Connection"] is { Length: > 0 } connectionString
    ? new ServiceBusClient(connectionString)
    : new ServiceBusClient(serviceBus["Connection:fullyQualifiedNamespace"], new DefaultAzureCredential()));
builder.Services.AddSingleton(sp => BoteCloudSender.Create(
    sp.GetRequiredService<ServiceBusClient>(),
    serviceBus["Queues:Clients"]!,
    serviceBus["Queues:Cloud"]!));

builder.Build().Run();
