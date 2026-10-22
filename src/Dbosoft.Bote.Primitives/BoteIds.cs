namespace Dbosoft.Bote.Primitives;

/// <summary>
/// Tenant, client and attachment ids end up as segments of blob paths, and the storage SDK
/// resolves <c>..</c> in blob names (<c>tenant-a/../tenant-b/x</c> is <c>tenant-b/x</c>). So an id
/// from outside may only be a single path segment. Not ending in a dot either: Azure drops
/// trailing dots of blob names, so <c>x.</c> would name the blob of <c>x</c>.
/// </summary>
public static class BoteIds
{
    public static bool IsPathSegment(string? id) =>
        !string.IsNullOrEmpty(id)
        && !id.EndsWith('.')
        && !id.Any(c => c is '/' or '\\' || char.IsControl(c));
}
