using System.Text.Json;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Azure.Storage.Sas;
using AwesomeAssertions;
using Dbosoft.Bote.Rebus.Config;
using Dbosoft.Bote.Transport.Abstractions;
using Rebus.Activation;
using Rebus.Config;
using Rebus.Messages;
using Rebus.Retry.Simple;
using Rebus.Serialization.Json;

namespace Dbosoft.Bote.Rebus.IntegrationTests;

/// <summary>
/// End-to-end guard for the coupled H2 (local dead-letter queue) and H3 (retry redelivery)
/// fixes, exercised against a real Azure Storage Queue (Azurite):
///
/// A message whose handler always fails must be redelivered until Rebus reaches its max
/// delivery count (proves the transport re-arms polling after a NACK - H3c) and then land
/// in the client's own local error queue with the error details (proves the Bote error
/// handler writes poison to the edge queue instead of the rejected SignalR path - H2).
///
/// The real BoteTransport, BoteErrorHandler and Rebus pipeline run; only the worker's SAS
/// issuance is stood in for by <see cref="StorageQueueSignalRClient"/>, which hands out real
/// Azurite SAS URIs - that issuance is a thin, separate concern.
/// </summary>
[Collection(AzuriteCollection.Name)]
public class PoisonRedeliveryEndToEndTests
{
    private readonly AzuriteFixture _azurite;

    public PoisonRedeliveryEndToEndTests(AzuriteFixture azurite) => _azurite = azurite;

    [Fact]
    public async Task Failing_handler_is_redelivered_then_dead_lettered_to_the_local_error_queue()
    {
        var inbound = new QueueClient(_azurite.ConnectionString, "test-client-inbound");
        var error = new QueueClient(_azurite.ConnectionString, "test-client-inbound-error");
        await inbound.CreateAsync();
        await error.CreateAsync();

        await SeedPoisonMessageAsync(inbound, messageId: "poison-1", text: "boom");

        var signalR = new StorageQueueSignalRClient(inbound, error);
        using var activator = new BuiltinHandlerActivator();

        var handleCount = 0;
        activator.Handle<PoisonPayload>(_ =>
        {
            Interlocked.Increment(ref handleCount);
            throw new InvalidOperationException("this handler always fails");
        });

        using var bus = Configure.With(activator)
            .Logging(l => l.Console())
            .Transport(t => t.UseBote(
                "test-client-inbound",
                signalR,
                // Short reconcile so the receive loop starts polling the seeded message
                // without waiting on a SignalR push (there is none in this test).
                new BoteTransportOptions { PendingMessagesReconcileInterval = TimeSpan.FromSeconds(1) }))
            .Options(o => o.RetryStrategy(maxDeliveryAttempts: 2))
            .Serialization(s => s.UseSystemTextJson())
            .Start();

        var poison = await WaitForMessageAsync(error, TimeSpan.FromSeconds(30));

        poison.Should().NotBeNull("the poison message must be dead-lettered to the local error queue");

        // Redelivered at least once before dead-lettering => the retry loop was not stranded (H3c).
        handleCount.Should().BeGreaterThanOrEqualTo(2);

        using var doc = JsonDocument.Parse(poison!);
        doc.RootElement.GetProperty("Body").GetString().Should().Contain("boom");

        var headers = doc.RootElement.GetProperty("Headers");
        headers.GetProperty(Headers.MessageId).GetString().Should().Be("poison-1");
        headers.GetProperty(Headers.ErrorDetails).GetString().Should().Contain("this handler always fails");
    }

    private static async Task SeedPoisonMessageAsync(QueueClient inbound, string messageId, string text)
    {
        var message = new BoteMessage
        {
            Headers = new Dictionary<string, string?>
            {
                [Headers.MessageId] = messageId,
                [Headers.Type] = $"{typeof(PoisonPayload).FullName}, {typeof(PoisonPayload).Assembly.GetName().Name}",
                [Headers.ContentType] = "application/json;charset=utf-8",
                [Headers.Intent] = Headers.IntentOptions.PointToPoint,
            },
            Body = JsonSerializer.Serialize(new PoisonPayload { Text = text }),
        };

        await inbound.SendMessageAsync(JsonSerializer.Serialize(message));
    }

    private static async Task<string?> WaitForMessageAsync(QueueClient queue, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            PeekedMessage[] peeked = (await queue.PeekMessagesAsync(maxMessages: 1)).Value;
            if (peeked.Length > 0)
                return peeked[0].Body.ToString();

            await Task.Delay(500);
        }

        return null;
    }

    public class PoisonPayload
    {
        public string Text { get; set; } = "";
    }

    /// <summary>
    /// Stands in for the worker: returns real Azurite SAS URIs for the client's inbound and
    /// error queues. Everything else is unused in this test.
    /// </summary>
    private sealed class StorageQueueSignalRClient(QueueClient inbound, QueueClient error) : ISignalRClient
    {
        public Task<string> GetQueueConnection() =>
            Task.FromResult(inbound.GenerateSasUri(
                QueueSasPermissions.Read | QueueSasPermissions.Process | QueueSasPermissions.Update,
                DateTimeOffset.UtcNow.AddHours(1)).ToString());

        public Task<string> GetErrorQueueConnection() =>
            Task.FromResult(error.GenerateSasUri(
                QueueSasPermissions.Add,
                DateTimeOffset.UtcNow.AddHours(1)).ToString());

        public Task SendMessage(string queue, BoteMessage message) => throw new NotSupportedException();
        public Task SubscribeToTopic(string topicName) => throw new NotSupportedException();
        public Task UnsubscribeFromTopic(string topicName) => throw new NotSupportedException();
        public Task<string> GetDataBusAttachmentUri(string attachmentId) => throw new NotSupportedException();
        public Task<string> GetDataBusAttachmentUploadUri(string attachmentId) => throw new NotSupportedException();
        public Task NotifyAttachmentUploaded(string attachmentId) => throw new NotSupportedException();
        public Task<Dictionary<string, string>> GetAttachmentMetadata(string attachmentId) => throw new NotSupportedException();
    }
}
