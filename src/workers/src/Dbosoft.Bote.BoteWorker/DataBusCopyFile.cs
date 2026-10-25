using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Dbosoft.Bote.BoteWorker.Extensions;
using Dbosoft.Bote.BoteWorker.Services;
using Dbosoft.Bote.Primitives;
using JetBrains.Annotations;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Dbosoft.Bote.BoteWorker;


/// <summary>
/// Represents a storage-agnostic copy request using only blob URIs.
/// The copy monitor doesn't need to know about storage accounts or containers.
/// </summary>
public class DataBusCopyRequest
{
    public required string TenantId { get; set; }
    public required string AttachmentId { get; set; }

    public required Uri SourceBlobUri { get; set; }
    public required Uri DestBlobUri { get; set; }

    public bool DeleteSource { get; set; }

    public int MonitorCount { get; set; }

}

internal class DataBusCopyFile(
    ILogger<DataBusCopyFile> logger,
    DataBusCopyProcessor copyProcessor,
    ITenantStorageResolver tenantStorageResolver,
    BlobServiceClient outbox)
{

    /// <summary>
    /// A blob the cloud put into its outbox, as Event Grid reports it: starts its copy to where the
    /// clients of its tenant read it. Event Grid, not the blob trigger below, is what Azure runs on:
    /// the polling scan of the blob extension misses blobs (extension 5.3.8 finds only those written
    /// before the host's first poll), and a message to a client waits for its attachment.
    /// </summary>
    [PublicAPI]
    [Function("CloudOutboxBlobCreated")]
    public async Task CloudOutboxBlobCreated(
        [QueueTrigger(BoteStorageConstants.OutboxCreatedQueue, Connection = "dbote:Worker:Storage:Connection")] string message)
    {
        if (!OutboxEvents.TryRead(message, out var tenantId, out var attachmentId))
        {
            logger.LogWarning("An outbox event that names no attachment of the outbox: {Message}", message);
            return;
        }

        await CopyToClients(outbox.GetBlobContainerClient(BoteStorageConstants.Outbox)
            .GetBlobClient($"{tenantId}/{attachmentId}"), tenantId, attachmentId);
    }

    /// <summary>
    /// The same for a storage without Event Grid, as Azurite is in development. Turned off in Azure
    /// (AzureWebJobs.CloudOutboxBlobDetected.Disabled), where both would copy every attachment.
    /// </summary>
    [PublicAPI]
    [Function("CloudOutboxBlobDetected")]
    public Task CloudOutboxBlobDetected(
        // The outbox lives in the gateway storage next to the inbox, not in the Functions host
        // storage (AzureWebJobsStorage), so the cloud only needs access to one account.
        [BlobTrigger("bote-outbox/{tenantId}/{attachmentId}", Connection = "dbote:Worker:Storage:Connection")] BlobClient sourceBlob,
        string tenantId,
        string attachmentId) =>
        CopyToClients(sourceBlob, tenantId, attachmentId);

    private async Task CopyToClients(BlobClient sourceBlob, string tenantId, string attachmentId)
    {
        logger.LogInformation("Cloud outbox blob detected: {TenantId}/{AttachmentId}", tenantId, attachmentId);

        // Resolve destination storage for this tenant's client
        var (clientStorageClient, clientContainerName) = await tenantStorageResolver.ResolveClientDataStore(tenantId);
        var containerClient = clientStorageClient.GetBlobContainerClient(clientContainerName);
        await containerClient.CreateIfNotExistsAsync();

        var destBlob = containerClient.GetBlobClient(DataBusPaths.Delivered(tenantId, attachmentId));

        // Generate SAS URIs for both source and destination (for copy operation)
        var sourceUri = sourceBlob.GenerateBlobSasUri(
            BlobSasPermissions.Read | BlobSasPermissions.Delete,
            DateTimeOffset.UtcNow.AddHours(2));

        var destUri = destBlob.GenerateBlobSasUri(
            BlobSasPermissions.Write | BlobSasPermissions.Create
                                     | BlobSasPermissions.Read
                                     | BlobSasPermissions.Delete,
            DateTimeOffset.UtcNow.AddHours(2));

        // Enqueue copy request with URIs
        var copyRequest = new DataBusCopyRequest
        {
            TenantId = tenantId,
            AttachmentId = attachmentId,
            SourceBlobUri = sourceUri,
            DestBlobUri = destUri,
            MonitorCount = 0,
            DeleteSource = true
        };

        await copyProcessor.TryProcessCopyAsync(copyRequest);
    }

    /// <summary>
    /// Queue-based copy monitor - delegates to DataBusCopyProcessor for all processing and rescheduling.
    /// </summary>
    [PublicAPI]
    [Function("DataBusCopyFileMonitor")]
    public async Task DataBusCopyFileMonitor(
        [QueueTrigger("databus-copy-monitor", Connection = "AzureWebJobsStorage")] string? queueMessage)
    {
        logger.LogInformation("DataBusCopyFileMonitor triggered with message: {MessageLength} bytes", queueMessage?.Length ?? 0);

        var request = JsonSerializer.Deserialize<DataBusCopyRequest>(queueMessage ?? throw new ArgumentNullException(nameof(queueMessage)));
        if (request == null)
        {
            logger.LogError("Failed to deserialize DataBusCopyRequest");
            return;
        }

        logger.LogInformation("Processing copy: {AttachmentId}, Monitor: {Count}",
            request.AttachmentId, request.MonitorCount);

        // Processor handles everything including rescheduling
        // Returns: true = completed, false = rescheduled, exception = permanent failure
        await copyProcessor.TryProcessCopyAsync(request);
    }
}
