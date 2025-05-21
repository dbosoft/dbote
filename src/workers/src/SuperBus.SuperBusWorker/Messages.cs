using Azure.Messaging.ServiceBus;
using Azure.Storage.Queues;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.SignalRService;
using Microsoft.Identity.Client.Platforms.Features.DesktopOs.Kerberos;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using SuperBus.Abstractions.SignalR;
using SuperBus.Transport.Abstractions;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Azure.Storage.Sas;
using Microsoft.Azure.SignalR.Management;

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
        [ServiceBusTrigger("sample-simple-tenant-queue", Connection = "ServiceBusConnection")] ServiceBusReceivedMessage message)
    {
        QueueClient queue = new QueueClient("UseDevelopmentStorage=true", "outbox");
        await queue.CreateAsync();

        // TODO Fix performance https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/use-utf8jsonwriter#write-raw-json
        var superBusMessage = new SuperBusMessage()
        {
            Headers = message.ApplicationProperties
                .Select(kvp => new KeyValuePair<string, string>(kvp.Key, (string)kvp.Value))
                .ToDictionary(),
            Body = message.Body.ToString(),
        };
        

        var receipt = await queue.SendMessageAsync(JsonSerializer.Serialize(superBusMessage));

        await Clients.All.NewMessage(receipt.Value.MessageId);
    }

    [Function(nameof(GetQueueMetadata))]
    public async Task<SuperBusQueueMetadata?> GetQueueMetadata(
        [SignalRTrigger("Messages", "messages", nameof(this.GetQueueMetadata), ConnectionStringSetting = "AzureSignalRConnectionString")]
        SignalRInvocationContext invocationContext)
    {
        QueueClient queue = new QueueClient("UseDevelopmentStorage=true", "outbox");
        var uri = queue.GenerateSasUri(
            QueueSasPermissions.Read | QueueSasPermissions.Process | QueueSasPermissions.Update,
            DateTimeOffset.UtcNow.AddHours(1));
        await Task.Yield();

        return new SuperBusQueueMetadata()
        {
            Connection = uri.ToString(),
        };
    }
}

