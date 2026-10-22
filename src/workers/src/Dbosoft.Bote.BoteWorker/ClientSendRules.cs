using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Dbosoft.Bote.Primitives;
using Dbosoft.Bote.Transport.Abstractions;
using Rebus.Messages;

namespace Dbosoft.Bote.BoteWorker;

/// <summary>
/// What the worker does to a message a client sends, before it goes on Service Bus. Everything
/// that names who sent it, where it goes and where answers go is set here from the token and the
/// route, whatever the client wrote.
/// </summary>
internal static class ClientSendRules
{
    /// <summary>Azure Storage Queue hard limit for a single message (64 KiB).</summary>
    public const int MaxStorageQueueMessageBytes = 64 * 1024;

    /// <summary>
    /// How far ahead a client may schedule a message. The scheduled messages of all tenants wait
    /// in the shared queues; Rebus defers for retries and timeouts, not for weeks.
    /// </summary>
    public static readonly TimeSpan MaxDeferral = TimeSpan.FromDays(7);

    // Room for the headers the worker adds before it delivers to a client.
    private const int DeliveryHeaderAllowance = 1024;

    /// <summary>
    /// Writes the <c>dbote-*</c> header names in lower case, the form the worker reads them in,
    /// so <c>DBOTE-ATTACHMENT-ID</c> is not skipped by an exact-case lookup.
    /// </summary>
    public static void NormalizeHeaderNames(IDictionary<string, string?> headers)
    {
        foreach (var key in headers.Keys
                     .Where(k => k.StartsWith("dbote-", StringComparison.OrdinalIgnoreCase)
                                 && k != k.ToLowerInvariant())
                     .ToList())
        {
            var value = headers[key];
            headers.Remove(key);
            headers[key.ToLowerInvariant()] = value;
        }
    }

    /// <returns>Why the message may not be sent, or <c>null</c>.</returns>
    public static string? Refuse(BoteMessage message, ServiceBusMessage serviceBusMessage, ClientSendRoute route,
        DateTimeOffset now)
    {
        if (serviceBusMessage.ScheduledEnqueueTime > now + MaxDeferral)
            return $"A message cannot be scheduled more than {MaxDeferral.TotalDays} days ahead.";

        // A message to a client ends in a storage queue. Too large, it would be accepted here and
        // fail only on delivery, where the sender never learns of it.
        if (route.Target != ClientSendTarget.Cloud
            && JsonSerializer.SerializeToUtf8Bytes(message).Length + DeliveryHeaderAllowance > MaxStorageQueueMessageBytes)
            return $"A message to a client must stay below {MaxStorageQueueMessageBytes / 1024} KiB; send large payloads as an attachment.";

        return null;
    }

    /// <summary>Sets tenant, client, sender and return address from the sender's identity.</summary>
    public static void Stamp(IDictionary<string, object> properties, string tenantId, string clientId,
        ClientSendRoute route, string clientsQueue)
    {
        // The tenant is always the sender's. For the cloud the client id names the sender; on the
        // clients queue it names the receiver, and a message to a peer names its sender apart.
        properties[BoteHeaders.TenantId] = tenantId;
        properties[BoteHeaders.ClientId] = route.ClientId ?? clientId;
        switch (route.Target)
        {
            case ClientSendTarget.Peer:
                properties[BoteHeaders.SenderClientId] = clientId;
                break;
            case ClientSendTarget.Cloud:
                properties.Remove(BoteHeaders.SenderClientId);
                break;
            // Self: a deferred message keeps the sender of the message it was made from. A client
            // can only mislead itself with it.
        }

        // Answers go to the sender, always. A return address the client chose would let it have
        // the cloud or a peer answer to someone else - in the name of whoever answers.
        properties[Headers.ReturnAddress] = $"{clientsQueue}-{clientId}";
    }
}
