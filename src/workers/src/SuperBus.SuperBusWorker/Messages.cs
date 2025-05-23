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

namespace SuperBus.SuperBusWorker;

internal class Messages(
    ILogger<Messages> logger,
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

        var handler = new JsonWebTokenHandler();
        var validationResult = await handler.ValidateTokenAsync(jwt, new TokenValidationParameters()
        {
            // TODO Fix issuer and audience
            ValidIssuer = "http://localhost",
            ValidAudience = "http://localhost",
            IssuerSigningKeyResolver = (foo, securityToken, _, _) =>
            {
                var t = (JsonWebToken)securityToken;
                if (!t.TryGetClaim("tenant_id", out var tidClaim)
                    || !t.TryGetClaim("agent_id", out var aidClaim))
                    return [];

                logger.LogInformation("Claims {TenantId} {AgentId}", tidClaim.Value, aidClaim.Value);

                var tenantOptions = superBusOptions.Value.Tenants
                    .FirstOrDefault(to => to.TenantId == tidClaim.Value && to.AgentId == aidClaim.Value);
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
        if (!token.TryGetClaim("tenant_id", out var tenantIdClaim)
            || !token.TryGetClaim("agent_id", out var agentIdClaim))
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }

        var negotiateResponse = await NegotiateAsync(new NegotiationOptions
        {
            UserId = $"{tenantIdClaim.Value}-{agentIdClaim.Value}" ,
            Claims = 
            [
                tenantIdClaim,
                agentIdClaim,
            ]
        });
        var response = req.CreateResponse();
        await response.WriteBytesAsync(negotiateResponse.ToArray());
        return response;
    }

    [Function(nameof(ServiceBusReceivedMessageFunction))]
    public async Task ServiceBusReceivedMessageFunction(
        [ServiceBusTrigger("%SuperBus:QueuePrefix%-tenant", Connection = "ServiceBusConnection")]
        ServiceBusReceivedMessage message)
    {
        if (!message.ApplicationProperties.TryGetValue(Headers.TenantId, out var tenantId)
           || !message.ApplicationProperties.TryGetValue(Headers.AgentId, out var agentId))
            // TODO fix error handling
            throw new InvalidOperationException("Missing tenant_id or agent_id in message properties.");

        var storageConnection = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
        var queue = new QueueClient(storageConnection, $"{superBusOptions.Value.QueuePrefix}-{tenantId}-{agentId}");
        await queue.CreateAsync();

        // TODO Fix performance https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/use-utf8jsonwriter#write-raw-json
        var superBusMessage = new SuperBusMessage()
        {
            Headers = message.ApplicationProperties
                .Where(kvp => kvp.Key != Headers.TenantId && kvp.Key != Headers.AgentId)
                .Select(kvp => new KeyValuePair<string, string>(kvp.Key, (string)kvp.Value))
                .ToDictionary(),
            Body = message.Body.ToString(),
        };
        
        var receipt = await queue.SendMessageAsync(JsonSerializer.Serialize(superBusMessage));

        await Clients.All.NewMessage(receipt.Value.MessageId);
    }

    [Function(nameof(GetQueueMetadata))]
    public Task<SuperBusQueueMetadata> GetQueueMetadata(
        [SignalRTrigger("Messages", "messages", nameof(this.GetQueueMetadata), ConnectionStringSetting = "AzureSignalRConnectionString")]
        SignalRInvocationContext invocationContext)
    {
        invocationContext.Claims.TryGetValue("tenant_id", out var tenantId);
        invocationContext.Claims.TryGetValue("agent_id", out var agentId);

        var storageConnection = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
        QueueClient queue = new QueueClient(storageConnection, $"{superBusOptions.Value.QueuePrefix}-{tenantId}-{agentId}");
        var uri = queue.GenerateSasUri(
            QueueSasPermissions.Read | QueueSasPermissions.Process | QueueSasPermissions.Update,
            DateTimeOffset.UtcNow.AddHours(1));

        var metaData = new SuperBusQueueMetadata()
        {
            Connection = uri.ToString(),
        };

        return Task.FromResult(metaData);
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
        await using var serviceBusSender = serviceBusClient.CreateSender(queue);

        // TODO Add whitelist for headers

        var serviceBusMessage = new ServiceBusMessage
        {
            Body = new BinaryData(message.Body),
            ContentType = "application/json",
        };

        foreach (var header in message.Headers)
        {
            serviceBusMessage.ApplicationProperties.Add(header.Key, header.Value);
        }

        invocationContext.Claims.TryGetValue("tenant_id", out var tenantId);
        invocationContext.Claims.TryGetValue("agent_id", out var agentId);
        serviceBusMessage.ApplicationProperties.Add(Headers.TenantId, tenantId.ToString());
        serviceBusMessage.ApplicationProperties.Add(Headers.AgentId, agentId.ToString());

        await serviceBusSender.SendMessageAsync(serviceBusMessage);
    }
}
