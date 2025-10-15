using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Logging;

namespace Dbosoft.Bote.BoteWorker.Services;

/// <summary>
/// Processes blob copy operations between storage containers.
/// Handles both inline (fast path) and deferred (queue-based) monitoring.
/// Encapsulates all rescheduling logic internally.
/// </summary>
public class DataBusCopyProcessor(
    ILogger<DataBusCopyProcessor> logger,
    IAzureClientFactory<QueueServiceClient> queueClientFactory)
{
    private readonly QueueClient _monitorQueue = queueClientFactory
        .CreateClient("AzureWebJobsStorage").GetQueueClient("databus-copy-monitor");

    private async Task RescheduleJobAsync(DataBusCopyRequest request)
    {
        //  First check immediate, then exponential backoff: 0s → 0.5s → 0.75s → 1.1s → 1.7s → ... → 60s (cap)
        var delay = request.MonitorCount == 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(Math.Min(0.5 * Math.Pow(1.5, request.MonitorCount - 1), 60));

        await _monitorQueue.SendMessageAsync(JsonSerializer.Serialize(request), delay);
    }

    private async Task<(bool completed, DataBusCopyRequest Request)> 
        CheckCopyStatusAsync(DataBusCopyRequest request)
    {
        var sourceBlob = new BlobClient(request.SourceBlobUri);
        var destBlob = new BlobClient(request.DestBlobUri);

        BlobProperties? destProps = null;
        BlobProperties? sourceProps;

        try
        {
            if((await destBlob.ExistsAsync()).Value)
                destProps = await destBlob.GetPropertiesAsync();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Destination blob not found yet - expected in Phase 1
            destProps = null;
        }

        if (destProps != null)
        {
            switch (CheckBlobCopyState(destProps))
            {
                case BlobCopyState.Failed:
                    throw new InvalidOperationException($"Destination blob copy failed: {request.AttachmentId}");
                case BlobCopyState.Pending:
                    logger.LogInformation("Destination blob copy in progress: {AttachmentId}", request.AttachmentId);
                    request.MonitorCount++;
                    if (request.MonitorCount > 70)
                    {
                        throw new TimeoutException($":Destination blob copy timeout {request.AttachmentId}");
                    }

                    return (false, request);
                case BlobCopyState.None:
                    return (true, request);
            }

            // If we reach here, something is wrong
            logger.LogError("Destination blob {Path} found, but neither completed or copied. Logic error?", destBlob.Name);
            throw new InvalidOperationException($"Destination blob copy failed: {request.AttachmentId}");
        }

        try
        {
            sourceProps = await sourceBlob.GetPropertiesAsync();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Source blob not found - fail immediately
            throw new InvalidOperationException($"Source Attachment not found: {request.AttachmentId}");
        }

        if(sourceProps == null)
            throw new InvalidOperationException($"Source Attachment not found: {request.AttachmentId}");

        switch (CheckBlobCopyState(sourceProps))
        {
            case BlobCopyState.Failed:
                throw new InvalidOperationException($"Source blob copy failed: {request.AttachmentId}");
            case BlobCopyState.Pending:
                request.MonitorCount++;
                if (request.MonitorCount > 70)
                {
                    throw new TimeoutException($"Source blob copy timeout: {request.AttachmentId}");
                }

                return (false, request);

            case BlobCopyState.None:
                break;
        }

        logger.LogInformation("starting copy for attachment {AttachmentId}: {Source} to {Destination}",
            request.AttachmentId, sourceBlob.Name, destBlob.Name);
        await destBlob.StartCopyFromUriAsync(request.SourceBlobUri);
        request.MonitorCount = 0; // Reset for copy monitoring phase

        return (false, request);
    }

    private enum BlobCopyState
    {
        Failed,
        Pending,
        None
    }

    private static BlobCopyState CheckBlobCopyState(BlobProperties properties)
    {
        if (string.IsNullOrWhiteSpace(properties.CopyId) 
            || properties.BlobCopyStatus == null)
            return BlobCopyState.None;

        switch (properties.BlobCopyStatus)
        {
            case CopyStatus.Pending:
                return BlobCopyState.Pending;
            case CopyStatus.Success:
                return BlobCopyState.None;
            default:
                return BlobCopyState.Failed;
        }

    }

    /// <summary>
    /// Attempts to process a blob copy request (Phase 1: start copy, Phase 2: check completion).
    /// Handles rescheduling internally if operation needs continued monitoring.
    /// </summary>
    /// <param name="request">The copy request to process</param>
    /// <returns>True if copy completed successfully, False if rescheduled for continued monitoring</returns>
    /// <exception cref="TimeoutException">Thrown when copy times out after max retries</exception>
    /// <exception cref="InvalidOperationException">Thrown when copy fails permanently</exception>
    public async Task<bool> TryProcessCopyAsync(DataBusCopyRequest request)
    {
        var internalRetry = 0;
        while (internalRetry < 3)
        {
            internalRetry++;

            var (completed, response) = await CheckCopyStatusAsync(request);
            request = response;

            if (!completed)
            {
                await Task.Delay(200*internalRetry);
                continue;
            }


            if (!request.DeleteSource) return true;

            // Copy completed successfully - clean up source blob
            try
            {
                var sourceBlob = new BlobClient(request.SourceBlobUri);
                await sourceBlob.DeleteIfExistsAsync();
            }
            catch (RequestFailedException ex)
            {
                logger.LogError(ex, "Failed to delete source blob after copy: {AttachmentId}",
                    request.AttachmentId);
            }

            logger.LogInformation("Copy completed successfully: {AttachmentId}", request.AttachmentId);

            return true;

        }

        // Reschedule for continued monitoring
        await RescheduleJobAsync(request);
        logger.LogInformation("Rescheduled copy job for monitoring: {AttachmentId}, MonitorCount: {Count}",
            request.AttachmentId, request.MonitorCount);
        return false;

    }
}
