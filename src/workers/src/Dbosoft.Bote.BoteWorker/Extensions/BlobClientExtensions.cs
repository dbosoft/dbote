using Azure.Storage.Blobs;
using Azure.Storage.Sas;

namespace Dbosoft.Bote.BoteWorker.Extensions;

public static class BlobClientExtensions
{
    /// <summary>
    /// Generates a SAS URI for blob access.
    /// </summary>
    /// <param name="blobClient">The blob client for which to generate the SAS URI.</param>
    /// <param name="permissions">The permissions to grant (Read, Write, Create, Delete, etc.).</param>
    /// <param name="expiresOn">The expiration time for the SAS token.</param>
    /// <returns>A URI with SAS token.</returns>
    public static Uri GenerateBlobSasUri(
        this BlobClient blobClient,
        BlobSasPermissions permissions,
        DateTimeOffset expiresOn)
    {
        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = blobClient.BlobContainerName,
            BlobName = blobClient.Name,
            Resource = "b",
            StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresOn = expiresOn,
        };
        sasBuilder.SetPermissions(permissions);

        var sasUri = blobClient.GenerateSasUri(sasBuilder);

        return sasUri;
    }
}
