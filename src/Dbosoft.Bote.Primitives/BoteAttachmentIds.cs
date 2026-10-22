namespace Dbosoft.Bote.Primitives;

/// <summary>
/// An attachment id names the last segment of a blob path. A client chooses the ids of what it
/// uploads, and the storage SDK resolves <c>..</c> in blob names (<c>tenant-a/../tenant-b/x</c> is
/// <c>tenant-b/x</c>), so every id from outside is checked before it becomes part of a path.
/// </summary>
public static class BoteAttachmentIds
{
    public const int MaxLength = 128;

    public static bool IsValid(string? id) =>
        !string.IsNullOrEmpty(id)
        && id.Length <= MaxLength
        && id is not ("." or "..")
        && !id.Any(c => c is '/' or '\\' || char.IsControl(c));

    /// <exception cref="ArgumentException">The id is no single path segment.</exception>
    public static string Validate(string? id) =>
        IsValid(id)
            ? id!
            : throw new ArgumentException(
                $"'{id}' is not a valid attachment id: at most {MaxLength} characters, no '/', '\\', " +
                "control characters, '.' or '..'.", nameof(id));
}
