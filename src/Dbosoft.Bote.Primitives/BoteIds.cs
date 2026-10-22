namespace Dbosoft.Bote.Primitives;

/// <summary>
/// Tenant, client and attachment ids end up as segments of blob paths, and the storage SDK
/// resolves <c>..</c> in blob names (<c>tenant-a/../tenant-b/x</c> is <c>tenant-b/x</c>). So an id
/// from outside may only be a single path segment.
/// </summary>
public static class BoteIds
{
    public static bool IsPathSegment(string? id) =>
        !string.IsNullOrEmpty(id)
        && id is not ("." or "..")
        && !id.Any(c => c is '/' or '\\' || char.IsControl(c));
}
