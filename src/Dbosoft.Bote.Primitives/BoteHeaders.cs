namespace Dbosoft.Bote.Primitives;

/// <summary>
/// Message header constants for Bote messaging system.
/// </summary>
public static class BoteHeaders
{
    // Identity Headers
    public const string TenantId = "dbote-tenant-id";
    public const string ClientId = "dbote-client-id";
    public const string Signature = "dbote-signature";
    public const string Topic = "dbote-topic";
    public const string AttachmentId = "dbote-attachment-id";
    public const string ReScheduleCounter = "dbote-reschedule-cnt";
    public const string DataBusInboxSas = "dbote-cloud-inbox-sas";
}
