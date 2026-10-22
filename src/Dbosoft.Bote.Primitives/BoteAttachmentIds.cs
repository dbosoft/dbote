namespace Dbosoft.Bote.Primitives;

/// <summary>
/// An attachment id names the last segment of a blob path, and a client chooses the ids of what
/// it uploads; every id from outside is checked before it becomes part of a path
/// (<see cref="BoteIds.IsPathSegment"/>).
/// </summary>
public static class BoteAttachmentIds
{
    public const int MaxLength = 128;

    public static bool IsValid(string? id) => BoteIds.IsPathSegment(id) && id!.Length <= MaxLength;

    /// <exception cref="ArgumentException">The id is no single path segment.</exception>
    public static string Validate(string? id) =>
        IsValid(id)
            ? id!
            : throw new ArgumentException(
                $"'{id}' is not a valid attachment id: at most {MaxLength} characters, no '/', '\\', " +
                "control characters, '.' or '..'.", nameof(id));
}
