using System.IO.Hashing;
using System.Text;

namespace SuperBus.SuperBusWorker;

/// <summary>
/// Formats partition and row keys for subscription storage with proper sanitization and hashing
/// to comply with Azure Table Storage constraints.
/// </summary>
internal static class SubscriptionKeyFormatter
{
    private static readonly char[] DisallowedChars = ['/', '\\', '#', '?'];
    private const int MaxKeyLength = 200; // Safe for emulator (256 limit) with room for combinations

    /// <summary>
    /// Creates a partition key for subscription storage: {sanitized_tenantId}_{hashed_topic}
    /// </summary>
    public static string CreatePartitionKey(string tenantId, string topicName)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("TenantId cannot be null or whitespace", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(topicName))
            throw new ArgumentException("TopicName cannot be null or whitespace", nameof(topicName));

        var sanitizedTenant = SanitizeAndNormalize(tenantId);
        var topicHash = HashTopic(topicName);

        return $"{sanitizedTenant}_{topicHash}";
    }

    /// <summary>
    /// Creates a row key for subscription storage: {sanitized_connectorId}
    /// </summary>
    public static string CreateRowKey(string connectorId)
    {
        if (string.IsNullOrWhiteSpace(connectorId))
            throw new ArgumentException("ConnectorId cannot be null or whitespace", nameof(connectorId));

        return SanitizeAndNormalize(connectorId);
    }

    /// <summary>
    /// Sanitizes a key component by removing disallowed characters and normalizing to uppercase.
    /// </summary>
    private static string SanitizeAndNormalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException($"Input cannot be null or whitespace. Value: '{input}'", nameof(input));

        // Remove disallowed characters
        var sanitized = DisallowedChars
            .Aggregate(input, (current, c) => current.Replace(c, '-'));

        // Normalize to uppercase for consistency
        sanitized = sanitized.ToUpperInvariant();

        // Trim whitespace
        sanitized = sanitized.Trim();

        // Ensure not empty after sanitization
        if (string.IsNullOrEmpty(sanitized))
            throw new ArgumentException($"Input '{input}' resulted in empty string after sanitization", nameof(input));

        // Truncate if too long
        if (sanitized.Length > MaxKeyLength)
        {
            sanitized = sanitized[..MaxKeyLength];
        }

        return sanitized;
    }

    /// <summary>
    /// Creates a deterministic hash of a topic name using xxHash64.
    /// Returns 16-character lowercase hexadecimal string.
    /// </summary>
    private static string HashTopic(string topic)
    {
        var bytes = Encoding.UTF8.GetBytes(topic);
        var hash = XxHash64.Hash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant(); // 16 chars
    }
}
