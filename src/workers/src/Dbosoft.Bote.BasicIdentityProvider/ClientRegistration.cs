using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Dbosoft.Bote.BasicIdentityProvider;

public class ClientRegistration(
    IClientRepository clientRepository,
    ILogger<ClientRegistration> logger)
{
    [Function("register-client")]
    public async Task<IActionResult> RegisterClient(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")]
        HttpRequest req)
    {
        var client = await req.ReadFromJsonAsync<ClientInfo>();

        if (client == null
            || string.IsNullOrEmpty(client.Id)
            || string.IsNullOrEmpty(client.TenantId)
            || string.IsNullOrEmpty(client.PublicKey))
        {
            return new BadRequestObjectResult(new { error = "Invalid client data. Id, TenantId, and PublicKey are required." });
        }

        await clientRepository.Register(client);

        logger.LogInformation("Registered client {ClientId} for tenant {TenantId}", client.Id, client.TenantId);

        return new OkObjectResult(new { message = $"Client {client.Id} registered successfully" });
    }
}
