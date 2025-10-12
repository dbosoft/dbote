using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SuperBus.BasicIdentityProvider;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services.Configure<TokenIssuerOptions>(builder.Configuration.GetSection("BasicIdentityProvider"));

builder.Services.AddSingleton<ITokenCredentialsProvider, TokenCredentialsProvider>();
builder.Services.AddSingleton<IConnectorRepository, InMemoryConnectorRepository>();

builder.Build().Run();
