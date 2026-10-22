using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Dbosoft.Bote.Primitives;

/// <summary>
/// Names of what the gateway keeps per client: its storage queues, its SignalR user and its topic
/// subscriptions. They are derived from a hash of tenant and client instead of joining the two, so
/// no two clients share a name (<c>a-b</c>/<c>c</c> and <c>a</c>/<c>b-c</c>, or a client <c>c-error</c> and the error
/// queue of <c>c</c>), whatever characters the identity provider allows, and every name fits the
/// 63 characters of a storage queue.
/// </summary>
public static class BoteClientNames
{
    /// <summary>Characters of <see cref="ClientKey"/>: 128 bits of SHA-256 as lowercase hex.</summary>
    public const int ClientKeyLength = 32;

    private const string ErrorSuffix = "-error";
    private const int MaxQueueNameLength = 63;

    /// <summary>The longest queue prefix: room for <c>-{key}-error</c>.</summary>
    public const int MaxPrefixLength = MaxQueueNameLength - 1 - ClientKeyLength - 6;

    /// <summary>
    /// Identifies a client of a tenant. Cryptographic, because tenant and client ids may come
    /// from whoever registers a client, and a chosen collision would share a queue.
    /// </summary>
    public static string ClientKey(string tenantId, string clientId)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        ArgumentException.ThrowIfNullOrEmpty(clientId);
        return Hash(tenantId, clientId);
    }

    /// <summary>Identifies a topic of a tenant, for the subscriptions of its clients.</summary>
    public static string TopicKey(string tenantId, string topic)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        ArgumentException.ThrowIfNullOrEmpty(topic);
        return Hash(tenantId, topic);
    }

    /// <summary>The storage queue the client receives on: <c>{prefix}-{key}</c>.</summary>
    public static string ClientQueueName(string prefix, string tenantId, string clientId)
    {
        ValidatePrefix(prefix);
        return $"{prefix}-{ClientKey(tenantId, clientId)}";
    }

    /// <summary>The storage queue the client moves its poison messages to: <c>{prefix}-{key}-error</c>.</summary>
    public static string ClientErrorQueueName(string prefix, string tenantId, string clientId) =>
        ClientQueueName(prefix, tenantId, clientId) + ErrorSuffix;

    /// <summary>The SignalR user of the client's connections.</summary>
    public static string SignalRUserId(string tenantId, string clientId) => ClientKey(tenantId, clientId);

    private static string Hash(string tenantId, string name)
    {
        // Length-prefixed, so the boundary between the two is part of what is hashed.
        var tenant = Encoding.UTF8.GetBytes(tenantId);
        var other = Encoding.UTF8.GetBytes(name);
        var input = new byte[4 + tenant.Length + other.Length];
        BinaryPrimitives.WriteInt32BigEndian(input, tenant.Length);
        tenant.CopyTo(input, 4);
        other.CopyTo(input, 4 + tenant.Length);

        return Convert.ToHexStringLower(SHA256.HashData(input), 0, ClientKeyLength / 2);
    }

    /// <summary>
    /// A queue prefix must itself be valid at the start of a queue name: lowercase letters, digits
    /// and single hyphens, beginning with a letter or digit, at most <see cref="MaxPrefixLength"/>.
    /// </summary>
    public static void ValidatePrefix(string prefix)
    {
        if (string.IsNullOrEmpty(prefix)
            || prefix.Length > MaxPrefixLength
            || !char.IsAsciiLetterLower(prefix[0]) && !char.IsAsciiDigit(prefix[0])
            || prefix[^1] == '-'
            || prefix.Contains("--", StringComparison.Ordinal)
            || prefix.Any(c => !char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '-'))
            throw new ArgumentException(
                $"'{prefix}' is not a valid queue prefix: lowercase letters, digits and single hyphens, " +
                $"starting with a letter or digit, at most {MaxPrefixLength} characters.", nameof(prefix));
    }
}
