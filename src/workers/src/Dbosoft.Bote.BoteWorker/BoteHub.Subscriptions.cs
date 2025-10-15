using Azure.Data.Tables;
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
                filter: $"PartitionKey eq '{partitionKey}'");

            var subscriberCount = 0;
            var staleSubscriptions = new List<string>();

            foreach (var subscriber in subscribers)
            {
                var clientId = subscriber.GetString("ClientId");
                try
                {
                    await ForwardToClientQueue(tenantId, clientId, boteMessage);
                    subscriberCount++;
                }
                catch (Azure.RequestFailedException ex) when (ex.Status == 404)
                {
                    logger.LogInformation(
                        "Removing stale subscription for {TenantId}/{ClientId} on topic {Topic} - queue not found",
                        tenantId, clientId, topic);

                    try
                    {
                        await subscriptionsTable.DeleteEntityAsync(subscriber.PartitionKey, subscriber.RowKey);
                        staleSubscriptions.Add(clientId);
                    }
                    catch (Exception deleteEx)
                    {
                        logger.LogWarning(deleteEx,
                            "Failed to delete stale subscription for {TenantId}/{ClientId}",
                            tenantId, clientId);
                    }
                }
            }

            logger.LogInformation(
                "Broadcast to topic {Topic} in tenant {TenantId} delivered to {SubscriberCount} subscribers, removed {StaleCount} stale subscriptions",
                topic, tenantId, subscriberCount, staleSubscriptions.Count);
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
