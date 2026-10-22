using Dbosoft.Bote.Primitives;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Dbosoft.Bote.BoteWorker;

/// <summary>
/// Represents the identity of a client extracted from standard JWT claims.
/// </summary>
public record ClientIdentity(string TenantId, string ClientId)
{
    /// <summary>
    /// Extracts client identity from a validated JWT token using standard claims.
    /// Uses 'sub' claim for client ID and 'tid' claim for tenant ID.
    /// </summary>
    /// <param name="token">The validated JWT token</param>
    /// <param name="identity">The extracted identity if successful</param>
    /// <returns>True if identity was successfully extracted</returns>
    /// <remarks>
    /// The tenant names a folder of the DataBus containers (<c>{tenant}/{attachment}</c>), so
    /// tenant and client must work as a path segment: no slashes, no control characters, not
    /// <c>.</c> or <c>..</c>.
    /// </remarks>
    public static bool TryExtract(JsonWebToken token, out ClientIdentity? identity)
    {
        identity = null;

        // Get subject claim (client ID)
        if (!token.TryGetValue("sub", out string clientId) || string.IsNullOrEmpty(clientId))
            return false;

        // Get tenant ID from standard 'tid' claim (Azure AD standard)
        if (!token.TryGetValue("tid", out string tenantId) || string.IsNullOrEmpty(tenantId))
            return false;

        if (!BoteIds.IsPathSegment(tenantId) || !BoteIds.IsPathSegment(clientId))
            return false;

        identity = new ClientIdentity(tenantId, clientId);
        return true;
    }
}
