using System.Text;
using Azure.Storage.Blobs;
using AwesomeAssertions;
using Dbosoft.Bote.Primitives;
using Dbosoft.Bote.Rebus.Integration.Pipeline;
using Rebus.DataBus;
using Rebus.Logging;
using Rebus.Messages;
using Rebus.Pipeline;
using Rebus.Pipeline.Send;
using Rebus.Transport;

namespace Dbosoft.Bote.Rebus.IntegrationTests;

/// <summary>
/// Guards that the cloud outgoing DataBus step must stage the attachment in the
/// outbox synchronously and fail the send if that upload fails - never let the message
/// (which references the attachment) be sent while the attachment is missing.
/// </summary>
[Collection(AzuriteCollection.Name)]
public class DataBusOutgoingStepTests
{
    private const string OutboxContainer = "test-outbox";
    private readonly AzuriteFixture _azurite;

    public DataBusOutgoingStepTests(AzuriteFixture azurite) => _azurite = azurite;

    [Fact]
    public async Task Stages_the_attachment_in_the_outbox_before_forwarding()
    {
        var blobService = new BlobServiceClient(_azurite.ConnectionString);
        await blobService.GetBlobContainerClient(OutboxContainer).CreateIfNotExistsAsync();

        var storage = new FakeDataBusStorage(
            "the attachment bytes"u8.ToArray(),
            new Dictionary<string, string> { ["filename"] = "hello.txt" });

        var step = new DataBusOutgoingStep(
            storage, new ConsoleLoggerFactory(colored: false), blobService, OutboxContainer);

        var nextCalled = false;
        await step.Process(
            NewContext(tenantId: "tenant-a", attachmentId: "att-1"),
            () => { nextCalled = true; return Task.CompletedTask; });

        nextCalled.Should().BeTrue();

        var blob = blobService.GetBlobContainerClient(OutboxContainer).GetBlobClient("tenant-a/att-1");
        (await blob.ExistsAsync()).Value.Should().BeTrue("the attachment must be staged before the message is forwarded");

        var content = await blob.DownloadContentAsync();
        Encoding.UTF8.GetString(content.Value.Content.ToArray()).Should().Be("the attachment bytes");

        var props = await blob.GetPropertiesAsync();
        props.Value.Metadata.Should().ContainKey(BoteStorageConstants.Metadata.TenantId)
            .WhoseValue.Should().Be("tenant-a");
        props.Value.Metadata.Should().ContainKey("filename").WhoseValue.Should().Be("hello.txt");
    }

    [Fact]
    public async Task Propagates_the_upload_failure_instead_of_forwarding_a_message_without_its_attachment()
    {
        var blobService = new BlobServiceClient(_azurite.ConnectionString);
        await blobService.GetBlobContainerClient(OutboxContainer).CreateIfNotExistsAsync();

        var storage = new FakeDataBusStorage("x"u8.ToArray(), new Dictionary<string, string>())
        {
            ThrowOnRead = true,
        };

        var step = new DataBusOutgoingStep(
            storage, new ConsoleLoggerFactory(colored: false), blobService, OutboxContainer);

        var nextCalled = false;
        var act = async () => await step.Process(
            NewContext(tenantId: "tenant-a", attachmentId: "att-fail"),
            () => { nextCalled = true; return Task.CompletedTask; });

        await act.Should().ThrowAsync<InvalidOperationException>();
        nextCalled.Should().BeFalse("the message must not be forwarded when staging the attachment failed");
    }

    private static OutgoingStepContext NewContext(string tenantId, string attachmentId)
    {
        var message = new Message(
            new Dictionary<string, string>
            {
                [BoteHeaders.TenantId] = tenantId,
                [BoteHeaders.AttachmentId] = attachmentId,
            },
            body: new object());

        return new OutgoingStepContext(
            message,
            new RebusTransactionScope().TransactionContext,
            new DestinationAddresses(new[] { "some-destination" }));
    }

    private sealed class FakeDataBusStorage(byte[] bytes, Dictionary<string, string> metadata) : IDataBusStorage
    {
        public bool ThrowOnRead { get; set; }

        public Task<Stream> Read(string id)
        {
            if (ThrowOnRead)
                throw new InvalidOperationException("attachment read failed");
            return Task.FromResult<Stream>(new MemoryStream(bytes));
        }

        public Task<Dictionary<string, string>> ReadMetadata(string id) => Task.FromResult(metadata);

        public Task Save(string id, Stream source, Dictionary<string, string>? m) => throw new NotSupportedException();
    }
}
