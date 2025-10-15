using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Dbosoft.Bote.BoteWorker.Extensions;
using Dbosoft.Bote.Primitives;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Dbosoft.Bote.BoteWorker
{
    internal partial class BoteHub
    {
        internal enum AttachmentState
        {
            Failed,
            Pending,
            Ready
        }

        private async Task<BlobContainerClient> GetClientBlobContainer(string tenantId)
        {
            var (storageClient, containerName) = await tenantStorageResolver.ResolveClientDataStore(tenantId);
            var containerClient = storageClient.GetBlobContainerClient(containerName);
            await containerClient.CreateIfNotExistsAsync();
            return containerClient;
        }


        private async Task<AttachmentState> IsAttachmentReady(BlobContainerClient containerClient,
            string tenantId, string attachmentId)
        {
            var blobPath = $"{tenantId}/{attachmentId}";
            var blobClient = containerClient.GetBlobClient(blobPath);

            try
            {
                var exists = await blobClient.ExistsAsync();

                if (!exists.Value)
                {
                    logger.LogInformation("Tenant {TenantId}, Attachment {AttachmentId} not ready. File not found", 
                        tenantId, attachmentId);
                    return AttachmentState.Pending;
                }

                var destProps = await blobClient.GetPropertiesAsync();

                if (string.IsNullOrWhiteSpace(destProps.Value.CopyId))
                {
                    logger.LogInformation("Tenant {TenantId}, Attachment {AttachmentId} is ready.",
                        tenantId, attachmentId);

                }
                
                var result = destProps.Value.BlobCopyStatus.GetValueOrDefault(CopyStatus.Failed) switch
                {
                    CopyStatus.Pending => AttachmentState.Pending,
                    CopyStatus.Success => AttachmentState.Ready,
                    _ => AttachmentState.Failed
                };

                if (result != AttachmentState.Ready)
                {
                    logger.LogInformation("Tenant {TenantId}, Attachment {AttachmentId} not ready. Copy status: {CopyStatus}",
                        tenantId, attachmentId, destProps.Value.BlobCopyStatus);
                }

                return result;

            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Destination blob not found yet - expected if not ready
                return AttachmentState.Pending;
            }
        }

        /// <summary>
        /// Gets a URI for reading an attachment from the client datastore.
        /// Validates that the attachment belongs to the requesting tenant.
        /// </summary>
        [Function("GetDataBusAttachmentUri")]
        public async Task<string> GetDataBusAttachmentUri(
            [SignalRTrigger(
            HubName,
            "messages",
            nameof(this.GetDataBusAttachmentUri),
            nameof(attachmentId),
            ConnectionStringSetting = "dbote:Worker:SignalR:Connection")]
        SignalRInvocationContext invocationContext,
            string attachmentId)
        {
            var tenantId = ExtractTenantId(invocationContext);
            var clientId = ExtractClientId(invocationContext);

            var container = await GetClientBlobContainer(tenantId);
            var blobPath = $"{tenantId}/{attachmentId}";
            var blobClient = container.GetBlobClient(blobPath);

            // Validate blob exists and belongs to tenant
            try
            {
                var properties = await blobClient.GetPropertiesAsync();
                properties.Value.Metadata.TryGetValue(
                    BoteStorageConstants.Metadata.TenantId, out var blobTenantId);

                if (blobTenantId != tenantId)
                {
                    logger.LogWarning("Tenant {TenantId} attempted to access attachment {AttachmentId} belonging to {BlobTenantId}",
                        tenantId, attachmentId, blobTenantId);
                    throw new InvalidOperationException($"Attachment {attachmentId} not found");
                }
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                throw new InvalidOperationException($"Attachment {attachmentId} not found");
            }

            // Generate URI
            var uri = blobClient.GenerateBlobSasUri(
                BlobSasPermissions.Read | BlobSasPermissions.Delete,
                DateTimeOffset.UtcNow.AddDays(30));

            logger.LogInformation("Issued read URI for tenant {TenantId}, client {ClientId}, attachment {AttachmentId}",
                tenantId, clientId, attachmentId);

            return uri.ToString();
        }

        /// <summary>
        /// Gets a URI for uploading an attachment to the client datastore.
        /// </summary>
        [Function("GetDataBusAttachmentUploadUri")]
        public async Task<string> GetDataBusAttachmentUploadUri(
            [SignalRTrigger(
            HubName,
            "messages",
            nameof(this.GetDataBusAttachmentUploadUri),
            nameof(attachmentId),
            ConnectionStringSetting = "dbote:Worker:SignalR:Connection")]
        SignalRInvocationContext invocationContext,
            string attachmentId)
        {
            var tenantId = ExtractTenantId(invocationContext);
            var clientId = ExtractClientId(invocationContext);

            var container = await GetClientBlobContainer(tenantId);
            await container.CreateIfNotExistsAsync();

            var blobPath = $"{tenantId}/{attachmentId}";
            var blobClient = container.GetBlobClient(blobPath);

            // Generate URI for upload
            var uri = blobClient.GenerateBlobSasUri(
                BlobSasPermissions.Write | BlobSasPermissions.Create
                                         | BlobSasPermissions.Read,
                DateTimeOffset.UtcNow.AddHours(1));

            logger.LogDebug("Issued upload URI for tenant {TenantId}, client {ClientId}, attachment {AttachmentId}",
                tenantId, clientId, attachmentId);

            return uri.ToString();
        }

        /// <summary>
        /// Gets attachment metadata with internal metadata filtered out.
        /// Used by clients to retrieve user-defined metadata from tenant storage.
        /// </summary>
        [Function("GetAttachmentMetadata")]
        public async Task<Dictionary<string, string>> GetAttachmentMetadata(
            [SignalRTrigger(
            HubName,
            "messages",
            nameof(this.GetAttachmentMetadata),
            nameof(attachmentId),
            ConnectionStringSetting = "dbote:Worker:SignalR:Connection")]
        SignalRInvocationContext invocationContext,
            string attachmentId)
        {
            var tenantId = ExtractTenantId(invocationContext);

            logger.LogInformation("Getting metadata for attachment: {TenantId}/{AttachmentId}", tenantId, attachmentId);

            // Resolve client storage for this tenant
            var (cloudStorage, cloudInboxContainer) = await tenantStorageResolver.ResolveClientDataStore(tenantId);
            var cloudInbox = cloudStorage.GetBlobContainerClient(cloudInboxContainer);
            var blobPath = $"{tenantId}/{attachmentId}";
            var blob = cloudInbox.GetBlobClient(blobPath);

            try
            {
                var properties = await blob.GetPropertiesAsync();
                return BoteStorageConstants.Metadata.FilterUserMetadata(properties.Value.Metadata);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                logger.LogWarning("Attachment not found: {TenantId}/{AttachmentId}", tenantId, attachmentId);
                throw new InvalidOperationException($"Attachment {attachmentId} not found");
            }
        }

        /// <summary>
        /// Notification from client that an attachment has been uploaded to the outbox.
        /// Initiates Client → Cloud copy flow with inline fast-path processing.
        /// Reads user metadata from source blob and adds internal metadata to destination blob.
        /// </summary>
        [Function("AttachmentUploaded")]
        public async Task AttachmentUploaded(
            [SignalRTrigger(
            HubName,
            "messages",
            nameof(this.AttachmentUploaded),
            nameof(attachmentId),
            ConnectionStringSetting = "dbote:Worker:SignalR:Connection")]
        SignalRInvocationContext invocationContext,
            string attachmentId)
        {
            var tenantId = ExtractTenantId(invocationContext);
            var clientId = ExtractClientId(invocationContext);

            logger.LogInformation("Attachment upload notification: {TenantId}/{ClientId}/{AttachmentId}",
                tenantId, clientId, attachmentId);

            var clientContainer = await GetClientBlobContainer(tenantId);

            var blobPath = $"{tenantId}/{attachmentId}";

            var destinationContainer = blobServiceClient.GetBlobContainerClient(BoteStorageConstants.Inbox);
            var sourceBlob = clientContainer.GetBlobClient(blobPath);
            var destBlob = destinationContainer.GetBlobClient(blobPath);

            // Add internal metadata to source blob
            try
            {
                var sourceProps = await sourceBlob.GetPropertiesAsync();
                // Client uploaded blob with user metadata - now add internal metadata
                var metadata = new Dictionary<string, string>(sourceProps.Value.Metadata)
                {
                    [BoteStorageConstants.Metadata.TenantId] = tenantId
                };

                await sourceBlob.SetMetadataAsync(metadata);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                throw new InvalidOperationException($"Attachment {attachmentId} not found");
            }

            // Generate SAS URIs for copy operation (Client → Cloud)
            var sourceUri = sourceBlob.GenerateBlobSasUri(
                BlobSasPermissions.Read | BlobSasPermissions.Delete,
                DateTimeOffset.UtcNow.AddHours(2));

            var destUri = destBlob.GenerateBlobSasUri(
                BlobSasPermissions.Write | BlobSasPermissions.Create
                                         | BlobSasPermissions.Read | BlobSasPermissions.Delete,
                DateTimeOffset.UtcNow.AddHours(2));

            var copyRequest = new DataBusCopyRequest
            {
                TenantId = tenantId,
                AttachmentId = attachmentId,
                SourceBlobUri = sourceUri,
                DestBlobUri = destUri,
                MonitorCount = 0
            };

            var completed = await copyProcessor.TryProcessCopyAsync(copyRequest);

            if (completed)
            {
                logger.LogInformation("Attachment copy completed inline: {AttachmentId}", attachmentId);
            }
            else
            {
                logger.LogInformation("Attachment copy rescheduled for monitoring: {AttachmentId}", attachmentId);
            }
        }

    }
}
