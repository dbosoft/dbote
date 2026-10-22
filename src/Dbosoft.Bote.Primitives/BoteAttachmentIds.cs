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

    /// <summary>
    /// The ids a client chooses for what it uploads must be GUIDs, as Rebus makes them: an id
    /// nobody can guess cannot be read by guessing, nor taken before its sender uses it.
    /// </summary>
    public static bool IsValidClientId(string? id) => Guid.TryParseExact(id, "D", out _);

    /// <exception cref="ArgumentException">The id is no GUID.</exception>
    public static string ValidateClientId(string? id) =>
        IsValidClientId(id)
            ? id!
            : throw new ArgumentException($"'{id}' is not a valid attachment id of a client: a GUID.", nameof(id));

    /// <exception cref="ArgumentException">The id is no single path segment.</exception>
    public static string Validate(string? id) =>
        IsValid(id)
            ? id!
            : throw new ArgumentException(
                $"'{id}' is not a valid attachment id: at most {MaxLength} characters, no '/', '\\', " +
                "control characters, not ending in '.'.", nameof(id));
}
