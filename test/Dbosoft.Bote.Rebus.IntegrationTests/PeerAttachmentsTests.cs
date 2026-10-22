using AwesomeAssertions;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Dbosoft.Bote.BoteWorker;
using Dbosoft.Bote.BoteWorker.Services;
using Dbosoft.Bote.Primitives;
using Azure.Storage.Queues;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dbosoft.Bote.Rebus.IntegrationTests;

// An attachment one client sends to another lands where the tenant's attachments from the cloud
// land as well: neither may take the other's place. The copy itself is not run here - Azurite in
// its container cannot reach a source SAS on the test host's mapped port - but in the sample
// stacks, where worker and storage share a network.
[Collection(AzuriteCollection.Name)]
public class PeerAttachmentsTests(AzuriteFixture azurite)
{
    private const string Tenant = "tenant-a";

    private async Task<(BlobClient Inbox, BlobContainerClient DataStore, string Id)> SetupAsync()
    {
        var service = new BlobServiceClient(azurite.ConnectionString);
        var inbox = service.GetBlobContainerClient("test-peer-inbox");
        var dataStore = service.GetBlobContainerClient("test-peer-ds");
        await inbox.CreateIfNotExistsAsync();
        await dataStore.CreateIfNotExistsAsync();

        var id = Guid.NewGuid().ToString();
        var inboxCopy = inbox.GetBlobClient(DataBusPaths.Inbox(Tenant, "instance", id));
        await inboxCopy.UploadAsync(new BinaryData("from the instance"), new BlobUploadOptions
        {
            Metadata = new Dictionary<string, string> { ["name"] = "query.txt" },
        });
        return (inboxCopy, dataStore, id);
    }

    private static async Task PlaceAsync(BlobContainerClient dataStore, string id, Dictionary<string, string> metadata) =>
        await dataStore.GetBlobClient(DataBusPaths.Delivered(Tenant, id))
            .UploadAsync(new BinaryData("already there"), new BlobUploadOptions { Metadata = metadata });

    [Fact]
    public async Task Never_takes_the_place_of_an_attachment_from_the_cloud()
    {
        var (inbox, dataStore, id) = await SetupAsync();
        await PlaceAsync(dataStore, id, new Dictionary<string, string> { [BoteStorageConstants.Metadata.TenantId] = Tenant });

        var deliver = () => PeerAttachments.DeliverAsync(inbox, dataStore, Tenant, "instance", id);

        await deliver.Should().ThrowAsync<InvalidOperationException>();
        (await dataStore.GetBlobClient(DataBusPaths.Delivered(Tenant, id)).DownloadContentAsync())
            .Value.Content.ToString().Should().Be("already there");
    }

    [Fact]
    public async Task Never_takes_the_place_of_another_clients_attachment()
    {
        var (inbox, dataStore, id) = await SetupAsync();
        await PlaceAsync(dataStore, id, new Dictionary<string, string>
        {
            [BoteStorageConstants.Metadata.SenderKey] = BoteClientNames.ClientKey(Tenant, "someone-else"),
        });

        var deliver = () => PeerAttachments.DeliverAsync(inbox, dataStore, Tenant, "instance", id);

        await deliver.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task A_redelivery_finds_its_own_copy()
    {
        var (inbox, dataStore, id) = await SetupAsync();
        await PlaceAsync(dataStore, id, new Dictionary<string, string>
        {
            [BoteStorageConstants.Metadata.SenderKey] = BoteClientNames.ClientKey(Tenant, "instance"),
        });

        var deliver = () => PeerAttachments.DeliverAsync(inbox, dataStore, Tenant, "instance", id);

        await deliver.Should().NotThrowAsync();
    }

    [Fact]
    public async Task The_cloud_copy_refuses_an_id_a_client_has_taken()
    {
        var service = new BlobServiceClient(azurite.ConnectionString);
        var outbox = service.GetBlobContainerClient("test-peer-outbox");
        var dataStore = service.GetBlobContainerClient("test-peer-ds");
        await outbox.CreateIfNotExistsAsync();
        await dataStore.CreateIfNotExistsAsync();
        var id = Guid.NewGuid().ToString();
        var source = outbox.GetBlobClient($"{Tenant}/{id}");
        await source.UploadAsync(new BinaryData("from the cloud"));
        await PlaceAsync(dataStore, id, new Dictionary<string, string>
        {
            [BoteStorageConstants.Metadata.SenderKey] = BoteClientNames.ClientKey(Tenant, "instance"),
        });
        var destination = dataStore.GetBlobClient(DataBusPaths.Delivered(Tenant, id));
        var services = new ServiceCollection();
        services.AddAzureClients(b =>
            b.AddQueueServiceClient(azurite.ConnectionString).WithName("AzureWebJobsStorage"));
        var processor = new DataBusCopyProcessor(NullLogger<DataBusCopyProcessor>.Instance,
            services.BuildServiceProvider().GetRequiredService<IAzureClientFactory<QueueServiceClient>>());

        var copy = () => processor.TryProcessCopyAsync(new DataBusCopyRequest
        {
            TenantId = Tenant,
            AttachmentId = id,
            SourceBlobUri = source.GenerateSasUri(BlobSasPermissions.Read | BlobSasPermissions.Delete, DateTimeOffset.UtcNow.AddHours(1)),
            DestBlobUri = destination.GenerateSasUri(BlobSasPermissions.All, DateTimeOffset.UtcNow.AddHours(1)),
        });

        await copy.Should().ThrowAsync<InvalidOperationException>();
    }
}
