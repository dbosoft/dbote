using Dbosoft.Bote.Primitives;

namespace Dbosoft.Bote.BoteWorker;

/// <summary>
/// Where the worker keeps attachments. What a client writes lies under its own key, so a client
/// that knows another's attachment id still cannot overwrite it: neither what the cloud sent to
/// a client (<see cref="Delivered"/>) nor what another client sent to the cloud (<see cref="Inbox"/>).
/// </summary>
internal static class DataBusPaths
{
    /// <summary>Client datastore: an attachment the cloud sent, readable by the tenant's clients.</summary>
    public static string Delivered(string tenantId, string attachmentId) =>
        $"{tenantId}/{BoteAttachmentIds.Validate(attachmentId)}";

    /// <summary>Client datastore: where a client uploads an attachment for the cloud.</summary>
    public static string Upload(string tenantId, string clientId, string attachmentId) =>
        $"{tenantId}/upload/{BoteClientNames.ClientKey(tenantId, clientId)}/{BoteAttachmentIds.Validate(attachmentId)}";

    /// <summary>Inbox: a client's attachment, copied for the cloud.</summary>
    public static string Inbox(string tenantId, string clientId, string attachmentId) =>
        $"{tenantId}/{BoteClientNames.ClientKey(tenantId, clientId)}/{BoteAttachmentIds.Validate(attachmentId)}";
}
