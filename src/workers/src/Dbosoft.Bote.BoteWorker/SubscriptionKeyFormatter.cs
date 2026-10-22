using Dbosoft.Bote.Primitives;

namespace Dbosoft.Bote.BoteWorker;

/// <summary>
/// Keys of the subscriptions table: one partition per topic of a tenant, one row per client.
/// Both are hashes (<see cref="BoteClientNames"/>), so ids that differ only in case or in
/// characters Table Storage does not allow in keys never share a row, and any id fits.
/// </summary>
internal static class SubscriptionKeyFormatter
{
    public static string CreatePartitionKey(string tenantId, string topicName) =>
        BoteClientNames.TopicKey(tenantId, topicName);

    public static string CreateRowKey(string tenantId, string clientId) =>
        BoteClientNames.ClientKey(tenantId, clientId);
}
