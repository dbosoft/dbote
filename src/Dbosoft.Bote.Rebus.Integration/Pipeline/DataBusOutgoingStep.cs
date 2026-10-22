using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Dbosoft.Bote.Primitives;
using Rebus.DataBus;
using Rebus.Logging;
using Rebus.Messages;
using Rebus.Pipeline;

namespace Dbosoft.Bote.Rebus.Integration.Pipeline;

[StepDocumentation("Handles DataBus attachments for outgoing messages to clients")]
internal class DataBusOutgoingStep(
    IDataBusStorage dataBusStorage,
    IRebusLoggerFactory loggerFactory,
    BlobServiceClient blobServiceClient,
    string outboxContainerName
    )
    : IOutgoingStep
{
    private readonly ILog _log = loggerFactory.GetLogger<DataBusOutgoingStep>();
    public async Task Process(OutgoingStepContext context, Func<Task> next)
    {

        var message = context.Load<Message>();

        if (!message.Headers.TryGetValue(BoteHeaders.TenantId, out var tenantId) 
            || !message.Headers.TryGetValue(BoteHeaders.AttachmentId, out var attachmentId))
        {
            await next();
            return;
        }

        _log.Debug("Bote Databus: copying attachment {attachmentId} to outbox",
            attachmentId);

        // The attachment MUST be fully staged in the outbox before the message that
        // references it is sent - otherwise the message can outrun (or entirely lose) its
        // attachment, and the worker downstream just reports "not ready" and reschedules
        // until it dead-letters, with the real cause hidden. So upload synchronously and let
        // a failure propagate (fails the send, so Rebus retries) instead of swallowing it.
        try
        {
            _log.Debug("Bote Databus: reading attachment {attachmentId}", attachmentId);
            await using var stream = await dataBusStorage.Read(attachmentId);
            var metadata = await dataBusStorage.ReadMetadata(attachmentId)
                           ?? new Dictionary<string, string>();

            var outboxPath = $"{tenantId}/{BoteAttachmentIds.Validate(attachmentId)}";

            var outboxBlob = blobServiceClient.GetBlobContainerClient(
                    outboxContainerName)
                .GetBlobClient(outboxPath);

            if (stream.CanSeek)
                stream.Position = 0;

            // BlobUploadOptions overload overwrites, so a retried send re-stages idempotently.
            await outboxBlob.UploadAsync(stream, new BlobUploadOptions
            {
                Metadata = new Dictionary<string, string>(metadata)
                {
                    [BoteStorageConstants.Metadata.TenantId] = tenantId,
                }
            });

            _log.Debug("Bote Databus: uploaded attachment {attachmentId}",
                attachmentId);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to upload attachment {attachmentId} to outbox",
                attachmentId);
            throw;
        }

        await next();
    }

}
