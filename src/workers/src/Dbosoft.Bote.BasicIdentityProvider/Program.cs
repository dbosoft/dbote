using Azure.Data.Tables;
using Azure.Identity;
using Dbosoft.Bote.BasicIdentityProvider;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services.Configure<TokenIssuerOptions>(builder.Configuration.GetSection("BasicIdentityProvider"));

builder.Services.AddSingleton<ITokenCredentialsProvider, TokenCredentialsProvider>();

// The client registry must be shared across instances (register on one, issue token on another).
// Use Azure Table Storage (via managed identity) when configured; fall back to in-memory for
// single-instance/local development.
var tableServiceUri = builder.Configuration["BasicIdentityProvider:TableServiceUri"];
if (!string.IsNullOrWhiteSpace(tableServiceUri))
{
    builder.Services.AddSingleton(_ =>
        new TableServiceClient(new Uri(tableServiceUri), new DefaultAzureCredential()));
    builder.Services.AddSingleton<IClientRepository, TableClientRepository>();
}
else
{
    builder.Services.AddSingleton<IClientRepository, InMemoryClientRepository>();
}

builder.Build().Run();
