using Azure.Messaging.ServiceBus;
using Azure.Storage.Queues;
using Azure.Storage.Sas;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.SignalRService;
using Microsoft.Azure.SignalR.Management;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using SuperBus.Abstractions.SignalR;
using SuperBus.Rebus.Integration;
using SuperBus.Transport.Abstractions;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SuperBus.SuperBusWorker.Converters;

namespace SuperBus.SuperBusWorker;

internal class Messages(
    ILogger<Messages> logger,
    IMessageConverter messageConverter,
    IServiceProvider serviceProvider,
    IOptions<SuperBusOptions> superBusOptions)
    : ServerlessHub<IMessages>(serviceProvider)
{
    [Function("negotiate")]
    public async Task<HttpResponseData> Negotiate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
    {
        if (!req.Headers.TryGetValues(HeaderNames.Authorization, out var authHeaders))
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        var authHeader = authHeaders.FirstOrDefault();
        if (authHeader is null || !authHeader.StartsWith("Bearer ", StringComparison.Ordinal))
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        var jwt = authHeader.Substring("Bearer ".Length);

        // TODO Should use a proper token endpoint

        var handler = new JsonWebTokenHandler();
        var validationResult = await handler.ValidateTokenAsync(jwt, new TokenValidationParameters()
        {
            // TODO Fix issuer and audience
            ValidIssuer = "http://localhost",
            ValidAudience = "http://localhost",
            IssuerSigningKeyResolver = (_, securityToken, _, _) =>
            {
                var t = (JsonWebToken)securityToken;
                if (!t.TryGetClaim(ClaimNames.TenantId, out var tidClaim)
                    || !t.TryGetClaim(ClaimNames.ConnectorId, out var cidClaim))
                    return [];

                var tenantOptions = superBusOptions.Value.Tenants
                    .FirstOrDefault(to => to.TenantId == tidClaim.Value && to.ConnectorId == cidClaim.Value);
                if (tenantOptions is null)
                    return [];

                var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(tenantOptions.SigningKey), out _);
                return [new ECDsaSecurityKey(ecdsa)];
            },
        });

        if (!validationResult.IsValid)
        {
            logger.LogDebug(validationResult.Exception, "Validation of client assertion token failed: ");
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }
        
        var token = (JsonWebToken)validationResult.SecurityToken;
        if (!token.TryGetClaim(ClaimNames.TenantId, out var tenantIdClaim)
            || !token.TryGetClaim(ClaimNames.ConnectorId, out var connectorIdClaim))
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }

        var negotiateResponse = await NegotiateAsync(new NegotiationOptions
        {
            UserId = $"{tenantIdClaim.Value}-{connectorIdClaim.Value}" ,
            Claims = 
            [
                tenantIdClaim,
                connectorIdClaim,
            ]
        });
        var response = req.CreateResponse();
        await response.WriteBytesAsync(negotiateResponse.ToArray());
        return response;
    }

    [Function(nameof(ServiceBusReceivedMessageFunction))]
    public async Task ServiceBusReceivedMessageFunction(
        [ServiceBusTrigger("%SuperBus:QueuePrefix%-connectors", Connection = "ServiceBusConnection")]
        ServiceBusReceivedMessage message)
    {
        if (!message.ApplicationProperties.TryGetValue(SuperBusHeaders.TenantId, out var tenantId)
           || !message.ApplicationProperties.TryGetValue(SuperBusHeaders.ConnectorId, out var connectorId))
            // TODO fix error handling
            throw new InvalidOperationException("Missing tenant_id or connector_id in message properties.");

        var storageConnection = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
        var queue = new QueueClient(storageConnection, $"{superBusOptions.Value.QueuePrefix}-{tenantId}-{connectorId}");
        await queue.CreateAsync();

        // TODO validate headers?

        var superBusMessage = messageConverter.ToSuperBus(message);

        var receipt = await queue.SendMessageAsync(JsonSerializer.Serialize(superBusMessage));

        await Clients.All.NewMessage(receipt.Value.MessageId);
    }

    [Function(nameof(GetQueueMetadata))]
    public async Task<SuperBusQueueMetadata> GetQueueMetadata(
        [SignalRTrigger("Messages", "messages", nameof(this.GetQueueMetadata), ConnectionStringSetting = "AzureSignalRConnectionString")]
        SignalRInvocationContext invocationContext)
    {
        invocationContext.Claims.TryGetValue(ClaimNames.TenantId, out var tenantId);
        invocationContext.Claims.TryGetValue(ClaimNames.ConnectorId, out var connectorId);

        var storageConnection = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
        QueueClient queue = new QueueClient(storageConnection, $"{superBusOptions.Value.QueuePrefix}-{tenantId}-{connectorId}");
        await queue.CreateAsync();
        var uri = queue.GenerateSasUri(
            QueueSasPermissions.Read | QueueSasPermissions.Process | QueueSasPermissions.Update,
            DateTimeOffset.UtcNow.AddHours(1));

        var metaData = new SuperBusQueueMetadata()
        {
            Connection = uri.ToString(),
        };

        return metaData;
    }

    [Function(nameof(SendMessage))]
    public async Task SendMessage(
        [SignalRTrigger("Messages", "messages", nameof(this.SendMessage), nameof(queue), nameof(message), ConnectionStringSetting = "AzureSignalRConnectionString")]
        SignalRInvocationContext invocationContext,
        string queue,
        SuperBusMessage message)
    {
        var serviceBusConnection = Environment.GetEnvironmentVariable("ServiceBusConnection");
        
        await using var serviceBusClient = new ServiceBusClient(serviceBusConnection!);

        // TODO use common helper for handling queue names

        var actualQueueNam = queue.StartsWith($"{superBusOptions.Value.QueuePrefix}-connectors-")
            ? $"{superBusOptions.Value.QueuePrefix}-connectors"
            : queue;

        await using var serviceBusSender = serviceBusClient.CreateSender(actualQueueNam);

        // TODO Add whitelist for headers

        var serviceBusMessage = messageConverter.ToServiceBus(message);

        invocationContext.Claims.TryGetValue(ClaimNames.TenantId, out var tenantId);
        invocationContext.Claims.TryGetValue(ClaimNames.ConnectorId, out var connectorId);
        serviceBusMessage.ApplicationProperties.Add(SuperBusHeaders.TenantId, tenantId.ToString());
        serviceBusMessage.ApplicationProperties.Add(SuperBusHeaders.ConnectorId, connectorId.ToString());

        await serviceBusSender.SendMessageAsync(serviceBusMessage);
    }
}
