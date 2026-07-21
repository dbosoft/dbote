using Azure.Data.Tables;
using Dbosoft.Bote.Primitives;
using Dbosoft.Bote.Transport.Abstractions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Dbosoft.Bote.BoteWorker
{
    internal partial class BoteHub
    {
        private const string SubscriptionsTableName = "subscriptions";


        private async Task BroadcastToTopic(string tenantId, string topic, BoteMessage boteMessage)
        {
            var subscriptionsTable = tableServiceClient.GetTableClient(SubscriptionsTableName);
            var partitionKey = SubscriptionKeyFormatter.CreatePartitionKey(tenantId, topic);

            logger.LogInformation("Broadcasting message of topic {Topic} to tenant {TenantId}",
                topic, tenantId);


            var subscribers = subscriptionsTable.Query<TableEntity>(
                filter: $"PartitionKey eq '{partitionKey}'").ToList();

            // Fan out in parallel with per-subscriber failure isolation: one slow or failing
            // subscriber must not block the others (head-of-line) or abort the whole broadcast.
            var result = await BroadcastFanOut.RunAsync(
                subscribers,
                subscriber => ForwardToClientQueue(tenantId, subscriber.GetString("ClientId"), boteMessage),
                maxConcurrency: 16);

            var staleCount = 0;
            var hardFailures = new List<Exception>();

            foreach (var (subscriber, error) in result.Failures)
            {
                var clientId = subscriber.GetString("ClientId");
                if (error is Azure.RequestFailedException { Status: 404 })
                {
                    // The client queue is gone - the subscription is stale, drop it.
                    logger.LogInformation(
                        "Removing stale subscription for {TenantId}/{ClientId} on topic {Topic} - queue not found",
                        tenantId, clientId, topic);
                    try
                    {
                        await subscriptionsTable.DeleteEntityAsync(subscriber.PartitionKey, subscriber.RowKey);
                        staleCount++;
                    }
                    catch (Exception deleteEx)
                    {
                        logger.LogWarning(deleteEx,
                            "Failed to delete stale subscription for {TenantId}/{ClientId}",
                            tenantId, clientId);
                    }
                }
                else
                {
                    hardFailures.Add(error);
                    logger.LogWarning(error,
                        "Failed to deliver broadcast to {TenantId}/{ClientId} on topic {Topic}",
                        tenantId, clientId, topic);
                }
            }

            logger.LogInformation(
                "Broadcast to topic {Topic} in tenant {TenantId}: delivered {Delivered}, removed {StaleCount} stale, failed {FailedCount}",
                topic, tenantId, result.Delivered, staleCount, hardFailures.Count);

            // Only retry the whole message (throw -> Service Bus redelivers) when nobody received
            // it and there were real failures. On partial success we complete: re-broadcasting to
            // everyone would duplicate for those already delivered, and broadcast is best-effort to
            // the currently active subscribers.
            if (result.Delivered == 0 && hardFailures.Count > 0)
                throw new AggregateException(
                    $"Broadcast to topic {topic} for tenant {tenantId} failed for all {hardFailures.Count} subscriber(s)",
                    hardFailures);
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
                var tenantId = ExtractTenantId(invocationContext);
                var clientId = ExtractClientId(invocationContext);

                logger.LogDebug(
                    "SubscribeToTopic called with TenantId='{TenantId}', ClientId='{ClientId}', TopicName='{TopicName}'",
                    tenantId, clientId, topicName);

                // Store subscription in table storage with sanitized and hashed keys
                var subscriptionsTable = tableServiceClient.GetTableClient(SubscriptionsTableName);
                await subscriptionsTable.CreateIfNotExistsAsync();

                var partitionKey = SubscriptionKeyFormatter.CreatePartitionKey(tenantId, topicName);
                var rowKey = SubscriptionKeyFormatter.CreateRowKey(clientId);

                var entity = new TableEntity(partitionKey, rowKey)
                {
                    ["TenantId"] = tenantId,
                    ["ClientId"] = clientId,
                    ["Topic"] = topicName,
                    ["SubscribedAt"] = DateTime.UtcNow
                };

                await subscriptionsTable.UpsertEntityAsync(entity);

                logger.LogDebug(
                    "Client {TenantId}/{ClientId} subscribed to topic {Topic} (key: {PartitionKey}/{RowKey})",
                    tenantId, clientId, topicName, partitionKey, rowKey);
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
            var tenantId = ExtractTenantId(invocationContext);
            var clientId = ExtractClientId(invocationContext);

            // Remove subscription from table storage
            var subscriptionsTable = tableServiceClient.GetTableClient(SubscriptionsTableName);
            var partitionKey = SubscriptionKeyFormatter.CreatePartitionKey(tenantId, topicName);
            var rowKey = SubscriptionKeyFormatter.CreateRowKey(clientId);

            try
            {
                await subscriptionsTable.DeleteEntityAsync(partitionKey, rowKey);
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                // Subscription already removed, ignore
                logger.LogDebug(
                    "Subscription not found for {TenantId}/{ClientId} on topic {Topic}",
                    tenantId, clientId, topicName);
            }

            logger.LogDebug(
                "Client {TenantId}/{ClientId} unsubscribed from topic {Topic}",
                tenantId, clientId, topicName);
        }

    }
}
