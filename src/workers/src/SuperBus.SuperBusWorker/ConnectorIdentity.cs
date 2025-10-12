using Microsoft.IdentityModel.JsonWebTokens;

namespace SuperBus.SuperBusWorker;

/// <summary>
/// Represents the identity of a connector extracted from standard JWT claims.
/// </summary>
public record ConnectorIdentity(string TenantId, string ConnectorId)
{
    /// <summary>
    /// Extracts connector identity from a validated JWT token using standard claims.
    /// Uses 'sub' claim for connector ID and 'tid' claim for tenant ID.
    /// </summary>
    /// <param name="token">The validated JWT token</param>
    /// <param name="identity">The extracted identity if successful</param>
    /// <returns>True if identity was successfully extracted</returns>
    public static bool TryExtract(JsonWebToken token, out ConnectorIdentity? identity)
    {
        identity = null;

        // Get subject claim (connector ID)
        if (!token.TryGetValue("sub", out string connectorId) || string.IsNullOrEmpty(connectorId))
            return false;

        // Get tenant ID from standard 'tid' claim (Azure AD standard)
        if (!token.TryGetValue("tid", out string tenantId) || string.IsNullOrEmpty(tenantId))
            return false;

        identity = new ConnectorIdentity(tenantId, connectorId);
        return true;
    }
}
