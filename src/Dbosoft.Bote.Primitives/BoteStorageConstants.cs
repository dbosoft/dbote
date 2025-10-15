namespace Dbosoft.Bote.Primitives;

/// <summary>
/// Storage container names and blob metadata constants for Bote DataBus.
/// </summary>
public static class BoteStorageConstants
{
    public const string Inbox = "bote-inbox";
    public const string Outbox = "bote-outbox";

    /// <summary>
    /// Blob metadata keys used for attachment tracking and validation.
    /// </summary>
    public static class Metadata
    {
        public const string TenantId = "tenantid";

        /// <summary>
        /// Checks if a metadata key is an internal Bote key that should be filtered from user-visible metadata.
        /// </summary>
        public static bool IsInternalKey(string key)
        {
            return key.Equals(TenantId, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Filters a metadata dictionary to include only user-defined keys (removes internal Bote keys).
        /// </summary>
        public static Dictionary<string, string> FilterUserMetadata(IDictionary<string, string> metadata)
        {
            var filtered = new Dictionary<string, string>();
            foreach (var kvp in metadata)
            {
                if (!IsInternalKey(kvp.Key))
                {
                    filtered[kvp.Key] = kvp.Value;
                }
            }
            return filtered;
        }
    }
}
