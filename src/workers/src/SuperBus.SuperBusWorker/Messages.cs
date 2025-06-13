using Azure.Messaging.ServiceBus;
using Azure.Storage.Queues;
using Azure.Storage.Sas;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.SignalRService;
using Microsoft.Azure.SignalR.Management;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using SuperBus.Abstractions.SignalR;
using SuperBus.Rebus.Integration;
using SuperBus.SuperBusWorker.Converters;
using SuperBus.Transport.Abstractions;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using SuperBus.Options;

namespace SuperBus.SuperBusWorker;

[SignalRConnection("SuperBus:Worker:SignalR:Connection")]
internal class Messages(
    ILogger<Messages> logger,
    IMessageConverter messageConverter,
    IServiceProvider serviceProvider,
    ITokenCredentialsProvider tokenCredentialsProvider,
    IOptions<StorageOptions> storageOptions,
    IOptions<OpenIdOptions> openIdOptions,
    IOptions<ServiceBusOptions> serviceBusOptions,
    QueueServiceClient queueServiceClient,
    ServiceBusClient serviceBusClient)
    : ServerlessHub<IMessages>(serviceProvider)
{
    private const string HubName = nameof(Messages);

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

        var tokenCredentials = tokenCredentialsProvider.GetCredentials();
        var handler = new JsonWebTokenHandler();
        var validationResult = await handler.ValidateTokenAsync(jwt, new TokenValidationParameters()
        {
            ValidIssuer = openIdOptions.Value.Authority,
            ValidAudience = openIdOptions.Value.Authority,
            IssuerSigningKey = tokenCredentials.Key,
        });

        if (!validationResult.IsValid)
        {
            logger.LogDebug(validationResult.Exception, "Validation of client assertion token failed: ");
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }

        var token = (JsonWebToken)validationResult.SecurityToken;
        if (!token.TryGetValue(ClaimNames.TenantId, out string tenantId)
            || !token.TryGetValue(ClaimNames.ConnectorId, out string connectorId)
            || !token.TryGetValue("scope", out string scope)
            || scope != "superbus")
        {
            // TODO proper error handling?
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }

        var negotiateResponse = await NegotiateAsync(new NegotiationOptions
        {
            UserId = $"{tenantId}-{connectorId}" ,
            Claims = 
            [
                new Claim(ClaimNames.TenantId, tenantId),
                new Claim(ClaimNames.ConnectorId, connectorId),
            ],
            // TODO define values
            CloseOnAuthenticationExpiration = true,
            TokenLifetime = TimeSpan.FromMinutes(15),
        });
        var response = req.CreateResponse();
        await response.WriteBytesAsync(negotiateResponse.ToArray());
        return response;
    }

    [Function(nameof(ServiceBusReceivedMessageFunction))]
    public async Task ServiceBusReceivedMessageFunction(
        [ServiceBusTrigger("%SuperBus:Worker:ServiceBus:Queues:Connectors%", Connection = "SuperBus:Worker:ServiceBus:Connection")]
        ServiceBusReceivedMessage message)
    {
        if (!message.ApplicationProperties.TryGetValue(SuperBusHeaders.TenantId, out var tenantId)
           || !message.ApplicationProperties.TryGetValue(SuperBusHeaders.ConnectorId, out var connectorId))
            // TODO fix error handling
            throw new InvalidOperationException("Missing tenant_id or connector_id in message properties.");

        var queueClient = queueServiceClient.GetQueueClient($"{storageOptions.Value.Prefix}-{tenantId}-{connectorId}");
        await queueClient.CreateAsync();

        // TODO validate headers?

        var superBusMessage = messageConverter.ToSuperBus(message);

        var receipt = await queueClient.SendMessageAsync(JsonSerializer.Serialize(superBusMessage));

        await Clients.All.NewMessage(receipt.Value.MessageId);
    }

    [Function(nameof(GetQueueMetadata))]
    public async Task<SuperBusQueueMetadata> GetQueueMetadata(
        [SignalRTrigger(
            HubName,
            "messages",
            nameof(this.GetQueueMetadata),
            ConnectionStringSetting = "SuperBus:Worker:SignalR:Connection")]
        SignalRInvocationContext invocationContext)
    {
        invocationContext.Claims.TryGetValue(ClaimNames.TenantId, out var tenantId);
        invocationContext.Claims.TryGetValue(ClaimNames.ConnectorId, out var connectorId);

        var queueClient = queueServiceClient.GetQueueClient($"{storageOptions.Value.Prefix}-{tenantId}-{connectorId}");
        await queueClient.CreateAsync();
        var uri = queueClient.GenerateSasUri(
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
        [SignalRTrigger(
            HubName,
            "messages",
            nameof(this.SendMessage),
            nameof(queue), nameof(message),
            ConnectionStringSetting = "SuperBus:Worker:SignalR:Connection")]
        SignalRInvocationContext invocationContext,
        string queue,
        SuperBusMessage message)
    {
        // TODO use common helper for handling queue names
        var actualQueueName = queue.StartsWith($"{serviceBusOptions.Value.Queues.Connectors}-")
            ? $"{serviceBusOptions.Value.Queues.Connectors}"
            : queue;

        await using var serviceBusSender = serviceBusClient.CreateSender(actualQueueName);

        // TODO Add whitelist for headers

        var serviceBusMessage = messageConverter.ToServiceBus(message);

        invocationContext.Claims.TryGetValue(ClaimNames.TenantId, out var tenantId);
        invocationContext.Claims.TryGetValue(ClaimNames.ConnectorId, out var connectorId);
        serviceBusMessage.ApplicationProperties.Add(SuperBusHeaders.TenantId, tenantId.ToString());
        serviceBusMessage.ApplicationProperties.Add(SuperBusHeaders.ConnectorId, connectorId.ToString());

        await serviceBusSender.SendMessageAsync(serviceBusMessage);
    }
}
