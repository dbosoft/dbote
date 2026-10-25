using System.Text.Json;
using Dbosoft.Bote.Primitives;

namespace Dbosoft.Bote.BoteWorker;

/// <summary>
/// The Event Grid events (Event Grid schema) the gateway storage sends to
/// <see cref="BoteStorageConstants.OutboxCreatedQueue"/> for the blobs written to the outbox.
/// </summary>
internal static class OutboxEvents
{
    private const string BlobCreated = "Microsoft.Storage.BlobCreated";
    private const string OutboxBlobs = "/blobServices/default/containers/" + BoteStorageConstants.Outbox + "/blobs/";

    /// <summary>
    /// The tenant and attachment of a blob created in the outbox; false for any other event, and
    /// for a blob whose name is not <c>{tenant}/{attachment}</c>.
    /// </summary>
    public static bool TryRead(string message, out string tenantId, out string attachmentId)
    {
        tenantId = attachmentId = "";
        string? type, subject;
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;
            type = root.TryGetProperty("eventType", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            subject = root.TryGetProperty("subject", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
        }
        catch (JsonException)
        {
            return false;
        }

        if (type != BlobCreated || subject is null || !subject.StartsWith(OutboxBlobs, StringComparison.Ordinal))
            return false;
        var parts = subject[OutboxBlobs.Length..].Split('/');
        if (parts.Length != 2 || !BoteIds.IsPathSegment(parts[0]) || !BoteAttachmentIds.IsValid(parts[1]))
            return false;
        (tenantId, attachmentId) = (parts[0], parts[1]);
        return true;
    }
}
