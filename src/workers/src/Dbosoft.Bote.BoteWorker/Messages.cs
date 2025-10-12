using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Azure.Data.Tables;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Queues;
using Azure.Storage.Sas;
using Dbosoft.Bote.Abstractions.SignalR;
using Dbosoft.Bote.BoteWorker.Converters;
using Dbosoft.Bote.Options;
using Dbosoft.Bote.Rebus.Integration;
using Dbosoft.Bote.Transport.Abstractions;
using JetBrains.Annotations;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.SignalRService;
using Microsoft.Azure.SignalR.Management;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.Net.Http.Headers;

namespace Dbosoft.Bote.BoteWorker;

[PublicAPI]
[SignalRConnection("dbote:Worker:SignalR:Connection")]
internal class Messages(
    ILogger<Messages> logger,
    IMessageConverter messageConverter,
    IServiceProvider serviceProvider,
    ITokenValidationService tokenValidationService,
    IOptions<OpenIdOptions> openIdOptions,
    IOptions<StorageOptions> storageOptions,
    IOptions<ServiceBusOptions> serviceBusOptions,
    QueueServiceClient queueServiceClient,
    ServiceBusClient serviceBusClient,
    TableServiceClient tableServiceClient)
    : ServerlessHub<IMessages>(serviceProvider)
{
    private const string HubName = nameof(Messages);
    private const string SubscriptionsTableName = "subscriptions";

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

        var validationResult = await tokenValidationService.ValidateAccessToken(jwt, req.FunctionContext.CancellationToken);

        if (!validationResult.IsValid)
        {
            logger.LogDebug(validationResult.Exception, "Token validation failed");
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }

        var token = (JsonWebToken)validationResult.SecurityToken;

        // Extract connector identity from standard JWT claims
        if (!ConnectorIdentity.TryExtract(token, out var identity) || identity is null)
        {
            logger.LogWarning("Failed to extract connector identity from token");
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }

        // Validate required scope (no fallback - must be configured)
        if (string.IsNullOrEmpty(openIdOptions.Value.RequiredScope))
        {
            logger.LogError("RequiredScope is not configured in OpenIdOptions");
            return req.CreateResponse(HttpStatusCode.InternalServerError);
        }

        if (!token.TryGetValue("scope", out string scope) || scope != openIdOptions.Value.RequiredScope)
        {
            logger.LogWarning("Invalid or missing scope. Expected: {ExpectedScope}, Actual: {ActualScope}",
                openIdOptions.Value.RequiredScope, scope);
            return req.CreateResponse(HttpStatusCode.Forbidden);
        }

        var negotiateResponse = await NegotiateAsync(new NegotiationOptions
        {
            UserId = $"{identity.TenantId}-{identity.ConnectorId}",
            Claims =
            [
                new Claim(ClaimNames.TenantId, identity.TenantId),
                new Claim(ClaimNames.ConnectorId, identity.ConnectorId),
            ],
            CloseOnAuthenticationExpiration = true,
            TokenLifetime = TimeSpan.FromMinutes(15),
        });
        var response = req.CreateResponse();
        await response.WriteBytesAsync(negotiateResponse.ToArray());
        return response;
    }

    [Function(nameof(ServiceBusReceivedMessageFunction))]
    public async Task ServiceBusReceivedMessageFunction(
        [ServiceBusTrigger("%dbote:Worker:ServiceBus:Queues:Connectors%", Connection = "dbote:Worker:ServiceBus:Connection")]
        ServiceBusReceivedMessage message)
    {
        if (!message.ApplicationProperties.TryGetValue(BoteHeaders.TenantId, out var tenantId))
            throw new InvalidOperationException("Missing tenant_id in message properties.");

        var boteMessage = messageConverter.ToBote(message);

        // Check if this is a topic broadcast or targeted message
        if (message.ApplicationProperties.TryGetValue(BoteHeaders.Topic, out var topic))
        {
            // TOPIC BROADCAST MODE: Query subscriptions and send to each subscriber
            var subscriptionsTable = tableServiceClient.GetTableClient(SubscriptionsTableName);
            var partitionKey = SubscriptionKeyFormatter.CreatePartitionKey(tenantId.ToString()!, topic.ToString()!);

            var subscribers = subscriptionsTable.Query<TableEntity>(
                filter: $"PartitionKey eq '{partitionKey}'");

            var subscriberCount = 0;
            var staleSubscriptions = new List<string>();

            foreach (var subscriber in subscribers)
            {
                var subConnectorId = subscriber.GetString("ConnectorId");
                try
                {
                    await SendToConnectorQueue(tenantId.ToString()!, subConnectorId, boteMessage);
                    subscriberCount++;
                }
                catch (Azure.RequestFailedException ex) when (ex.Status == 404)
                {
                    // Lazy cleanup: Queue doesn't exist - connector likely deleted
                    logger.LogInformation(
                        "Removing stale subscription for {TenantId}/{ConnectorId} on topic {Topic} - queue not found",
                        tenantId, subConnectorId, topic);

                    try
                    {
                        await subscriptionsTable.DeleteEntityAsync(subscriber.PartitionKey, subscriber.RowKey);
                        staleSubscriptions.Add(subConnectorId);
                    }
                    catch (Exception deleteEx)
                    {
                        logger.LogWarning(deleteEx,
                            "Failed to delete stale subscription for {TenantId}/{ConnectorId}",
                            tenantId, subConnectorId);
                    }
                }
            }

            logger.LogInformation(
                "Broadcast to topic {Topic} in tenant {TenantId} delivered to {SubscriberCount} subscribers, removed {StaleCount} stale subscriptions",
                topic, tenantId, subscriberCount, staleSubscriptions.Count);
        }
        else if (message.ApplicationProperties.TryGetValue(BoteHeaders.ConnectorId, out var connectorId))
        {
            // TARGETED MODE: Direct to specific connector queue
            await SendToConnectorQueue(tenantId.ToString()!, connectorId.ToString()!, boteMessage);
        }
        else
        {
            throw new InvalidOperationException(
                "Message must have either Topic (for broadcast) or ConnectorId (for targeted delivery).");
        }
    }

    private async Task SendToConnectorQueue(string tenantId, string connectorId, BoteMessage boteMessage)
    {
        var queueClient = queueServiceClient.GetQueueClient($"{storageOptions.Value.Prefix}-{tenantId}-{connectorId}");
        await queueClient.CreateAsync();

        var receipt = await queueClient.SendMessageAsync(JsonSerializer.Serialize(boteMessage));

        // Notify specific connector via SignalR
        await Clients.User($"{tenantId}-{connectorId}").NewMessage(receipt.Value.MessageId);
    }

    [Function(nameof(GetQueueMetadata))]
    public async Task<BoteQueueMetadata> GetQueueMetadata(
        [SignalRTrigger(
            HubName,
            "messages",
            nameof(this.GetQueueMetadata),
            ConnectionStringSetting = "dbote:Worker:SignalR:Connection")]
        SignalRInvocationContext invocationContext)
    {
        // Verify claims exist
        if (!invocationContext.Claims.TryGetValue(ClaimNames.TenantId, out var tenantId) ||
            !invocationContext.Claims.TryGetValue(ClaimNames.ConnectorId, out var connectorId) ||
            string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(connectorId))
        {
            logger.LogError("Missing or invalid required claims in SignalR invocation");
            throw new UnauthorizedAccessException("Invalid connection claims");
        }

        var queueClient = queueServiceClient.GetQueueClient($"{storageOptions.Value.Prefix}-{tenantId}-{connectorId}");
        await queueClient.CreateAsync();
        var uri = queueClient.GenerateSasUri(
            QueueSasPermissions.Read | QueueSasPermissions.Process | QueueSasPermissions.Update,
            DateTimeOffset.UtcNow.AddHours(1));

        var metaData = new BoteQueueMetadata()
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
            ConnectionStringSetting = "dbote:Worker:SignalR:Connection")]
        SignalRInvocationContext invocationContext,
        string queue,
        BoteMessage message)
    {
        // Defense-in-depth: Verify claims exist
        if (!invocationContext.Claims.TryGetValue(ClaimNames.TenantId, out var tenantId) ||
            !invocationContext.Claims.TryGetValue(ClaimNames.ConnectorId, out var connectorId) ||
            string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(connectorId))
        {
            logger.LogError("Missing or invalid required claims in SignalR invocation");
            throw new UnauthorizedAccessException("Invalid connection claims");
        }

        // Validate queue name against whitelist
        var actualQueueName = queue.StartsWith($"{serviceBusOptions.Value.Queues.Connectors}-")
            ? $"{serviceBusOptions.Value.Queues.Connectors}"
            : queue;

        // Connectors can only send to Cloud queue or Connectors queue
        var allowedQueues = new[]
        {
            serviceBusOptions.Value.Queues.Cloud,
            serviceBusOptions.Value.Queues.Connectors
        };

        if (!allowedQueues.Contains(actualQueueName))
        {
            logger.LogWarning("Connector {TenantId}-{ConnectorId} attempted to send to unauthorized queue: {Queue}",
                tenantId, connectorId, queue);
            throw new UnauthorizedAccessException($"Queue '{queue}' is not allowed");
        }

        // Validate headers - REJECT if connector attempts to inject security-critical headers
        var blockedHeaders = new[]
        {
            BoteHeaders.TenantId,
            BoteHeaders.ConnectorId
        };

        foreach (var blockedHeader in blockedHeaders)
        {
            if (!message.Headers.ContainsKey(blockedHeader)) continue;
            logger.LogError("Connector {TenantId}-{ConnectorId} attempted to inject blocked header: {Header}",
                tenantId, connectorId, blockedHeader);
            throw new UnauthorizedAccessException($"Attempted to inject blocked header: {blockedHeader}");
        }

        await using var serviceBusSender = serviceBusClient.CreateSender(actualQueueName);

        var serviceBusMessage = messageConverter.ToServiceBus(message);

        // Add authenticated tenant/connector identity to message properties
        serviceBusMessage.ApplicationProperties.Add(BoteHeaders.TenantId, tenantId.ToString());
        serviceBusMessage.ApplicationProperties.Add(BoteHeaders.ConnectorId, connectorId.ToString());

        await serviceBusSender.SendMessageAsync(serviceBusMessage);
    }

    [Function("SubscribeToTopic")]
    public async Task SubscribeToTopic(
        [SignalRTrigger(
            HubName,
            "messages",
            nameof(this.SubscribeToTopic),
            nameof(topicName),
            ConnectionStringSetting = "dbote:Worker:SignalR:Connection")]
        SignalRInvocationContext invocationContext,
        string topicName)
    {
        try
        {
            if (!invocationContext.Claims.TryGetValue(ClaimNames.TenantId, out var tenantIdValues) ||
                !invocationContext.Claims.TryGetValue(ClaimNames.ConnectorId, out var connectorIdValues) ||
                string.IsNullOrEmpty(tenantIdValues) || string.IsNullOrEmpty(connectorIdValues))
            {
                logger.LogError("Missing or invalid required claims in SignalR invocation");
                throw new UnauthorizedAccessException("Invalid connection claims");
            }

            // exception should not happen, just for static check compliance
            var tenantId = tenantIdValues.ToString() ?? throw new NullReferenceException();
            var connectorId = connectorIdValues.ToString() ?? throw new NullReferenceException();



            logger.LogDebug(
                "SubscribeToTopic called with TenantId='{TenantId}', ConnectorId='{ConnectorId}', TopicName='{TopicName}'",
                tenantId, connectorId, topicName);

            // Store subscription in table storage with sanitized and hashed keys
            var subscriptionsTable = tableServiceClient.GetTableClient(SubscriptionsTableName);
            await subscriptionsTable.CreateIfNotExistsAsync();

            var partitionKey = SubscriptionKeyFormatter.CreatePartitionKey(tenantId, topicName);
            var rowKey = SubscriptionKeyFormatter.CreateRowKey(connectorId);

            var entity = new TableEntity(partitionKey, rowKey)
            {
                ["TenantId"] = tenantId,
                ["ConnectorId"] = connectorId,
                ["Topic"] = topicName,
                ["SubscribedAt"] = DateTime.UtcNow
            };

            await subscriptionsTable.UpsertEntityAsync(entity);

            logger.LogDebug(
                "Connector {TenantId}/{ConnectorId} subscribed to topic {Topic} (key: {PartitionKey}/{RowKey})",
                tenantId, connectorId, topicName, partitionKey, rowKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to subscribe to topic. TopicName='{TopicName}', Error: {ErrorMessage}",
                topicName, ex.Message);
            throw;
        }
    }

    [Function("UnsubscribeFromTopic")]
    public async Task UnsubscribeFromTopic(
        [SignalRTrigger(
            HubName,
            "messages",
            nameof(this.UnsubscribeFromTopic),
            nameof(topicName),
            ConnectionStringSetting = "dbote:Worker:SignalR:Connection")]
        SignalRInvocationContext invocationContext,
        string topicName)
    {
        if (!invocationContext.Claims.TryGetValue(ClaimNames.TenantId, out var tenantIdValues) ||
            !invocationContext.Claims.TryGetValue(ClaimNames.ConnectorId, out var connectorIdValues) ||
            string.IsNullOrEmpty(tenantIdValues) || string.IsNullOrEmpty(connectorIdValues))
        {
            logger.LogError("Missing or invalid required claims in SignalR invocation");
            throw new UnauthorizedAccessException("Invalid connection claims");
        }

        // exception should not happen, just for static check compliance
        var tenantId = tenantIdValues.ToString() ?? throw new NullReferenceException();
        var connectorId = connectorIdValues.ToString() ?? throw new NullReferenceException();

        // Remove subscription from table storage
        var subscriptionsTable = tableServiceClient.GetTableClient(SubscriptionsTableName);
        var partitionKey = SubscriptionKeyFormatter.CreatePartitionKey(tenantId, topicName);
        var rowKey = SubscriptionKeyFormatter.CreateRowKey(connectorId);

        try
        {
            await subscriptionsTable.DeleteEntityAsync(partitionKey, rowKey);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            // Subscription already removed, ignore
            logger.LogDebug(
                "Subscription not found for {TenantId}/{ConnectorId} on topic {Topic}",
                tenantId, connectorId, topicName);
        }

        logger.LogDebug(
            "Connector {TenantId}/{ConnectorId} unsubscribed from topic {Topic}",
            tenantId, connectorId, topicName);
    }

}
