using AwesomeAssertions;
using Azure.Storage;
using Azure.Storage.Blobs;
using Dbosoft.Bote.Primitives;

namespace Dbosoft.Bote.BoteWorker.Tests;

public class DataBusPathsTests
{
    [Theory]
    [InlineData("../tenant-b/x")]
    [InlineData("x/../../tenant-b/y")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("a\\b")]
    [InlineData("a\nb")]
    [InlineData("x.")]
    [InlineData("")]
    public void Refuses_attachment_ids_that_are_no_path_segment(string attachmentId)
    {
        BoteAttachmentIds.IsValid(attachmentId).Should().BeFalse();

        var delivered = () => DataBusPaths.Delivered("tenant-a", attachmentId);
        var upload = () => DataBusPaths.Upload("tenant-a", "client-a", attachmentId);
        var inbox = () => DataBusPaths.Inbox("tenant-a", "client-a", attachmentId);
        delivered.Should().Throw<ArgumentException>();
        upload.Should().Throw<ArgumentException>();
        inbox.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("3f2c9a52-6f0e-4a8e-9a3b-0f5d2c7e1b44")]
    [InlineData("report.pdf")]
    [InlineData("..%2Ftenant-b%2Fx")] // stays escaped, see below
    public void Accepts_ids_that_stay_in_the_tenant(string attachmentId)
    {
        BoteAttachmentIds.IsValid(attachmentId).Should().BeTrue();

        // What the worker hands out: the SAS must point into the tenant's folder.
        var container = new BlobContainerClient(new Uri("https://acct.blob.core.windows.net/bote-ds"),
            new StorageSharedKeyCredential("acct", Convert.ToBase64String(new byte[64])));
        container.GetBlobClient(DataBusPaths.Delivered("tenant-a", attachmentId)).Name.Should().StartWith("tenant-a/");
    }

    [Theory]
    [InlineData("report.pdf")]
    [InlineData("3F2C9A52-6F0E-4A8E-9A3B")]
    public void A_client_names_its_uploads_with_guids_only(string attachmentId)
    {
        var upload = () => DataBusPaths.Upload("tenant-a", "client-a", attachmentId);
        var inbox = () => DataBusPaths.Inbox("tenant-a", "client-a", attachmentId);

        upload.Should().Throw<ArgumentException>();
        inbox.Should().Throw<ArgumentException>();
        DataBusPaths.Upload("tenant-a", "client-a", "3f2c9a52-6f0e-4a8e-9a3b-0f5d2c7e1b44").Should().StartWith("tenant-a/upload/");
    }

    [Fact]
    public void Uploads_lie_apart_from_delivered_attachments_and_per_client()
    {
        const string id = "3f2c9a52-6f0e-4a8e-9a3b-0f5d2c7e1b44";
        var uploadA = DataBusPaths.Upload("tenant-a", "client-a", id);
        var uploadB = DataBusPaths.Upload("tenant-a", "client-b", id);

        uploadA.Should().NotBe(uploadB);
        uploadA.Should().NotBe(DataBusPaths.Delivered("tenant-a", id));
        DataBusPaths.Inbox("tenant-a", "client-a", id).Should().NotBe(DataBusPaths.Inbox("tenant-a", "client-b", id));
    }
}
