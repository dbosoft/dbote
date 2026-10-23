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

    /// <summary>
    /// To another client of the same tenant, through the clients queue. Only to the clients the
    /// identity provider lets the sender reach (<see cref="BotePeers"/>).
    /// </summary>
    Peer,
}

/// <param name="Target">Where the message goes.</param>
/// <param name="ClientId">The receiving client for <see cref="ClientSendTarget.Self"/> and
/// <see cref="ClientSendTarget.Peer"/>; always of the sender's tenant.</param>
public sealed record ClientSendRoute(ClientSendTarget Target, string? ClientId = null);

/// <summary>
/// Trust-boundary policy for the queue a client names when it sends: the cloud queue, its own
/// queue <c>{clients}-{clientId}</c>, or the queue <c>{clients}-{otherClientId}</c> of another
/// client among its <see cref="BotePeers"/>. The tenant is never part of the address: the
/// worker always delivers within the sender's tenant.
/// </summary>
public static class BoteQueuePolicy
{
    /// <returns>The route, or <c>null</c> when the client may not send to <paramref name="queue"/>.</returns>
    public static ClientSendRoute? ResolveClientSendRoute(
        string queue, string cloudQueue, string clientsQueue, string clientId, BotePeers? peers = null)
    {
        if (string.IsNullOrEmpty(queue) || string.IsNullOrEmpty(clientId))
            return null;

        if (string.Equals(queue, cloudQueue, StringComparison.Ordinal))
            return new ClientSendRoute(ClientSendTarget.Cloud);

        var clientPrefix = $"{clientsQueue}-";
        if (!queue.StartsWith(clientPrefix, StringComparison.Ordinal))
            return null;

        var receiver = queue[clientPrefix.Length..];
        if (string.Equals(receiver, clientId, StringComparison.Ordinal))
            return new ClientSendRoute(ClientSendTarget.Self, clientId);

        // The receiver's id ends up in names and paths; the same rule as for a token's ids.
        return peers is not null && peers.Allows(receiver) && BoteIds.IsPathSegment(receiver)
            ? new ClientSendRoute(ClientSendTarget.Peer, receiver)
            : null;
    }
}
