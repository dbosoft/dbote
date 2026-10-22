namespace Dbosoft.Bote.Primitives;

/// <summary>Where a message a client sends goes.</summary>
public enum ClientSendTarget
{
    /// <summary>To the cloud queue.</summary>
    Cloud,

    /// <summary>
    /// Back to the sending client, through the clients queue. This is how a client defers a
    /// message: Rebus sends it to the client's own queue with a delivery time.
    /// </summary>
    Self,
}

/// <summary>
/// Trust-boundary policy for the queue a client names when it sends: the cloud queue, or its
/// own queue <c>{clients}-{clientId}</c>. Any other queue - another client's included - is
/// refused.
/// </summary>
public static class BoteQueuePolicy
{
    /// <returns>The target, or <c>null</c> when the client may not send to <paramref name="queue"/>.</returns>
    public static ClientSendTarget? ResolveClientSendTarget(
        string queue, string cloudQueue, string clientsQueue, string clientId)
    {
        if (string.IsNullOrEmpty(queue) || string.IsNullOrEmpty(clientId))
            return null;

        if (string.Equals(queue, cloudQueue, StringComparison.Ordinal))
            return ClientSendTarget.Cloud;

        if (string.Equals(queue, $"{clientsQueue}-{clientId}", StringComparison.Ordinal))
            return ClientSendTarget.Self;

        return null;
    }
}
