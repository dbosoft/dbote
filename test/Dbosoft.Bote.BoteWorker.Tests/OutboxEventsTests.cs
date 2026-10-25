using AwesomeAssertions;

namespace Dbosoft.Bote.BoteWorker.Tests;

public class OutboxEventsTests
{
    // As Event Grid wrote it to the queue on staging, for an attachment the cloud put into the outbox.
    private const string Created = """
        {"topic":"/subscriptions/s/resourceGroups/g/providers/Microsoft.Storage/storageAccounts/a",
         "subject":"/blobServices/default/containers/bote-outbox/blobs/6f086007-9250-4547-8c83-dcbefaa92f2a/00ce1a07-c818-4799-87cb-bcf0af38f38f",
         "eventType":"Microsoft.Storage.BlobCreated","id":"09e21a04-001e-00a9-1167-64ff8206c8da",
         "data":{"api":"PutBlob","blobType":"BlockBlob","url":"https://a.blob.core.windows.net/bote-outbox/x/y"},
         "dataVersion":"","metadataVersion":"1","eventTime":"2026-10-25T09:59:43.9381117Z"}
        """;

    [Fact]
    public void Reads_the_tenant_and_attachment_of_a_blob_created_in_the_outbox()
    {
        OutboxEvents.TryRead(Created, out var tenantId, out var attachmentId).Should().BeTrue();

        tenantId.Should().Be("6f086007-9250-4547-8c83-dcbefaa92f2a");
        attachmentId.Should().Be("00ce1a07-c818-4799-87cb-bcf0af38f38f");
    }

    [Theory]
    [InlineData("Microsoft.Storage.BlobDeleted", "/blobServices/default/containers/bote-outbox/blobs/t/a")]
    [InlineData("Microsoft.Storage.BlobCreated", "/blobServices/default/containers/bote-inbox/blobs/t/c/a")]
    [InlineData("Microsoft.Storage.BlobCreated", "/blobServices/default/containers/bote-outbox/blobs/t")]
    [InlineData("Microsoft.Storage.BlobCreated", "/blobServices/default/containers/bote-outbox/blobs/t/a/b")]
    [InlineData("Microsoft.Storage.BlobCreated", "/blobServices/default/containers/bote-outbox/blobs/t/..")]
    [InlineData("Microsoft.Storage.BlobCreated", "/blobServices/default/containers/bote-outbox/blobs//a")]
    public void Takes_no_other_event(string type, string subject)
    {
        var message = $$"""{"eventType":"{{type}}","subject":"{{subject}}"}""";

        OutboxEvents.TryRead(message, out _, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"eventType":1,"subject":2}""")]
    public void Takes_no_message_that_is_no_event(string message) =>
        OutboxEvents.TryRead(message, out _, out _).Should().BeFalse();
}
