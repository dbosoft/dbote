using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace SuperBus.BasicIdentityProvider;

public class ConnectorRegistration(
    IConnectorRepository connectorRepository,
    ILogger<ConnectorRegistration> logger)
{
    [Function("register-connector")]
    public async Task<IActionResult> RegisterConnector(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")]
        HttpRequest req)
    {
        var connector = await req.ReadFromJsonAsync<ConnectorInfo>();

        if (connector == null
            || string.IsNullOrEmpty(connector.Id)
            || string.IsNullOrEmpty(connector.TenantId)
            || string.IsNullOrEmpty(connector.PublicKey))
        {
            return new BadRequestObjectResult(new { error = "Invalid connector data. Id, TenantId, and PublicKey are required." });
        }

        await connectorRepository.Register(connector);

        logger.LogInformation("Registered connector {ConnectorId} for tenant {TenantId}", connector.Id, connector.TenantId);

        return new OkObjectResult(new { message = $"Connector {connector.Id} registered successfully" });
    }
}
