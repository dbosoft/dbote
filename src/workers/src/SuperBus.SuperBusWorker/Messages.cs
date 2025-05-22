using Azure.Messaging.ServiceBus;
using Azure.Storage.Queues;
using Azure.Storage.Sas;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.SignalRService;
using Microsoft.Azure.SignalR.Management;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client.Platforms.Features.DesktopOs.Kerberos;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using SuperBus.Abstractions.SignalR;
using SuperBus.Rebus.Integration;
using SuperBus.Transport.Abstractions;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace SuperBus.SuperBusWorker;

internal class Messages(
    IServiceProvider serviceProvider)
    : ServerlessHub<IMessages>(serviceProvider)
{
    private const string PublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEwXc4qiEaVIFztUPrEWDqAO0h8+g5p84nzUW8rKr1DlCAscbWHJuGEgdXLcTo5a0FCHH+/q4SXxxv/bE0+HnX8g==";

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

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKey), out _);
        var securityKey = new ECDsaSecurityKey(ecdsa);

        var handler = new JsonWebTokenHandler();
        handler.ReadJsonWebToken(jwt);
        var validationResult = await handler.ValidateTokenAsync(jwt, new TokenValidationParameters()
        {
            IssuerSigningKey = securityKey,
            ValidIssuer = "http://localhost",
            ValidAudience = "http://localhost",
        });

        if (!validationResult.IsValid)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        var token = (JsonWebToken)validationResult.SecurityToken;
        if(!token.TryGetClaim("tenant_id", out var tenantIdClaim)
           || !token.TryGetClaim("agent_id", out var agentIdClaim))
            return req.CreateResponse(HttpStatusCode.Unauthorized);

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

    // TODO use %settingsName% for queue name
    [Function(nameof(ServiceBusReceivedMessageFunction))]
    public async Task ServiceBusReceivedMessageFunction(
        [ServiceBusTrigger("sample-simple-tenant", Connection = "ServiceBusConnection")] ServiceBusReceivedMessage message)
    {
        if(!message.ApplicationProperties.TryGetValue(Headers.TenantId, out var tenantId)
           || !message.ApplicationProperties.TryGetValue(Headers.AgentId, out var agentId))
            throw new InvalidOperationException("Missing tenant_id or agent_id in message properties.");

        QueueClient queue = new QueueClient("UseDevelopmentStorage=true", $"{tenantId}-{agentId}");
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

        QueueClient queue = new QueueClient("UseDevelopmentStorage=true", $"{tenantId}-{agentId}");
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
