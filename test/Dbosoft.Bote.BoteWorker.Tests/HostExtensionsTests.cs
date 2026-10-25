using AwesomeAssertions;

namespace Dbosoft.Bote.BoteWorker.Tests;

public class HostExtensionsTests
{
    /// <summary>
    /// Before 5.3.10 the outbox trigger finds only blobs written before the host's first poll: an
    /// attachment from the cloud then waits for a restart of the worker (Azure/azure-sdk-for-net#61660).
    /// </summary>
    [Fact]
    public void The_host_loads_a_blob_extension_that_finds_new_blobs()
    {
        var file = Path.Combine(AppContext.BaseDirectory, ".azurefunctions", "Microsoft.Azure.WebJobs.Extensions.Storage.Blobs.dll");
        File.Exists(file).Should().BeTrue("the worker's build puts the host's extensions there");

        var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(file).ProductVersion!.Split('+')[0];
        Version.Parse(version).Should().BeGreaterThanOrEqualTo(new Version(5, 3, 10));
    }
}
