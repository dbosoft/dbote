using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Azure.Storage.Sas;
using AwesomeAssertions;
using Dbosoft.Bote.BoteWorker;
using Dbosoft.Bote.BoteWorker.Services;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dbosoft.Bote.Rebus.IntegrationTests;

// Covers the DataBus copy state machine's decision logic against a real storage backend
// (Azurite): the idempotency short-circuit when the destination already exists (H4b) and
// the fail-fast when the source is gone.
//
// Note: the actual server-side copy-from-URL is NOT exercised here - Azurite runs in a
// container and cannot reach a source SAS whose host is the test host's mapped port. That
// leg is covered by the live emulator stack (worker and storage share a network). This test
// deliberately drove out a real bug: an IfNoneMatch=* conditional copy made Azurite return
// 500 and broke the DataBus flow, which is why the copy is now unconditional.
[Collection(AzuriteCollection.Name)]
public class DataBusCopyProcessorTests
{
    private const string SourceContainer = "test-copy-source";
    private const string DestContainer = "test-copy-dest";

    private readonly AzuriteFixture _azurite;

    public DataBusCopyProcessorTests(AzuriteFixture azurite) => _azurite = azurite;

    [Fact]
    public async Task Completes_without_recopying_when_the_destination_already_exists()
    {
        var (processor, source, dest) = await SetupAsync("att-idem");
        await source.UploadAsync(new BinaryData("payload"), overwrite: true);
        // Destination already present (e.g. an at-least-once redelivery of the same copy request).
        await dest.UploadAsync(new BinaryData("payload"), overwrite: true);

        var completed = await processor.TryProcessCopyAsync(Request("att-idem", source, dest, deleteSource: false));

        completed.Should().BeTrue("an existing destination means the copy is already done");
        (await dest.ExistsAsync()).Value.Should().BeTrue();
    }

    [Fact]
    public async Task Concurrent_redeliveries_with_an_existing_destination_both_complete()
    {
        var (processor, source, dest) = await SetupAsync("att-concurrent");
        await source.UploadAsync(new BinaryData("payload"), overwrite: true);
        await dest.UploadAsync(new BinaryData("payload"), overwrite: true);

        // Two at-least-once redeliveries of the same copy request racing through the
        // destination-exists short-circuit must both complete without throwing.
        var both = await Task.WhenAll(
            processor.TryProcessCopyAsync(Request("att-concurrent", source, dest, deleteSource: false)),
            processor.TryProcessCopyAsync(Request("att-concurrent", source, dest, deleteSource: false)));

        both.Should().AllBeEquivalentTo(true);
    }

    [Fact]
    public async Task Fails_fast_when_the_source_attachment_is_missing()
    {
        var (processor, source, dest) = await SetupAsync("att-missing");
        await source.DeleteIfExistsAsync();
        await dest.DeleteIfExistsAsync();

        var act = async () => await processor.TryProcessCopyAsync(Request("att-missing", source, dest, deleteSource: false));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static DataBusCopyRequest Request(string attachmentId, BlobClient source, BlobClient dest, bool deleteSource) =>
        new()
        {
            TenantId = "tenant-a",
            AttachmentId = attachmentId,
            SourceBlobUri = Sas(source, BlobSasPermissions.Read | BlobSasPermissions.Delete),
            DestBlobUri = Sas(dest, BlobSasPermissions.Write | BlobSasPermissions.Create
                                    | BlobSasPermissions.Read | BlobSasPermissions.Delete),
            DeleteSource = deleteSource,
        };

    private async Task<(DataBusCopyProcessor Processor, BlobClient Source, BlobClient Dest)> SetupAsync(string attachmentId)
    {
        var blobService = new BlobServiceClient(_azurite.ConnectionString);
        var sourceContainer = blobService.GetBlobContainerClient(SourceContainer);
        var destContainer = blobService.GetBlobContainerClient(DestContainer);
        await sourceContainer.CreateIfNotExistsAsync();
        await destContainer.CreateIfNotExistsAsync();

        var blobPath = $"tenant-a/{attachmentId}";
        var source = sourceContainer.GetBlobClient(blobPath);
        var dest = destContainer.GetBlobClient(blobPath);

        var services = new ServiceCollection();
        services.AddAzureClients(b =>
            b.AddQueueServiceClient(_azurite.ConnectionString).WithName("AzureWebJobsStorage"));
        var factory = services.BuildServiceProvider().GetRequiredService<IAzureClientFactory<QueueServiceClient>>();

        var processor = new DataBusCopyProcessor(NullLogger<DataBusCopyProcessor>.Instance, factory);
        return (processor, source, dest);
    }

    private static Uri Sas(BlobClient blob, BlobSasPermissions permissions) =>
        blob.GenerateSasUri(permissions, DateTimeOffset.UtcNow.AddHours(1));
}
