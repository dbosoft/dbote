using Testcontainers.Azurite;

namespace Dbosoft.Bote.Rebus.IntegrationTests;

/// <summary>
/// Starts a real Azurite storage emulator in a container for the duration of a test class.
///
/// Runs on any Docker-compatible runtime. With Podman (the local dev setup) set, before
/// running the tests:
///   DOCKER_HOST=npipe://./pipe/podman-machine-default   (Windows named pipe)
///   TESTCONTAINERS_RYUK_DISABLED=true
/// CI with Docker needs neither.
/// </summary>
public sealed class AzuriteFixture : IAsyncLifetime
{
    // Pin the same Azurite version the dev stack uses, so its queue API version matches the
    // Azure.Storage.Queues SDK we depend on.
    private const string AzuriteImage = "mcr.microsoft.com/azure-storage/azurite:3.35.0";

    private readonly AzuriteContainer _container = new AzuriteBuilder()
        .WithImage(AzuriteImage)
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
