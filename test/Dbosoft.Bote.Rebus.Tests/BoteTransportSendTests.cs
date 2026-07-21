using AwesomeAssertions;
using Dbosoft.Bote.Rebus.Config;
using Dbosoft.Bote.Rebus.Transport;
using Dbosoft.Bote.Transport.Abstractions;
using Rebus.Logging;
using Rebus.Messages;
using Rebus.Threading;
using Rebus.Transport;

namespace Dbosoft.Bote.Rebus.Tests;

// Guards the H1 contract: the client->cloud path must be honest about send failures
// (so Rebus retries instead of silently dropping) and must carry a stable message id
// (so Azure Service Bus duplicate detection can suppress the retries' duplicates).
public class BoteTransportSendTests
{
    [Fact]
    public async Task Commit_propagates_send_failure_instead_of_swallowing_it()
    {
        var signalR = new FakeSignalRClient
        {
            OnSend = (_, _) => throw new InvalidOperationException("server rejected the message")
        };
        var transport = CreateTransport(signalR);

        using var scope = new RebusTransactionScope();
        await transport.Send("some-queue", NewMessage("msg-1"), scope.TransactionContext);

        var commit = async () => await scope.CompleteAsync();

        await commit.Should().ThrowAsync<InvalidOperationException>();
        signalR.SendCount.Should().Be(1);
    }

    [Fact]
    public async Task Commit_sends_the_message_exactly_once_on_success()
    {
        var signalR = new FakeSignalRClient();
        var transport = CreateTransport(signalR);

        using var scope = new RebusTransactionScope();
        await transport.Send("some-queue", NewMessage("msg-1"), scope.TransactionContext);
        await scope.CompleteAsync();

        signalR.SendCount.Should().Be(1);
        signalR.LastQueue.Should().Be("some-queue");
    }

    [Fact]
    public async Task Commit_carries_the_rebus_message_id_for_duplicate_detection()
    {
        var signalR = new FakeSignalRClient();
        var transport = CreateTransport(signalR);

        using var scope = new RebusTransactionScope();
        await transport.Send("some-queue", NewMessage("msg-1"), scope.TransactionContext);
        await scope.CompleteAsync();

        signalR.LastMessage!.Headers.Should().ContainKey(Headers.MessageId)
            .WhoseValue.Should().Be("msg-1");
    }

    private static BoteTransport CreateTransport(ISignalRClient signalR) =>
        new(
            queueName: "client-queue",
            options: new BoteTransportOptions(),
            signalRClient: signalR,
            pendingMessagesIndicator: new FakePendingMessagesIndicator(),
            loggerFactory: new ConsoleLoggerFactory(colored: false),
            asyncTaskFactory: new FakeAsyncTaskFactory());

    private static TransportMessage NewMessage(string messageId) =>
        new(new Dictionary<string, string> { [Headers.MessageId] = messageId }, "body"u8.ToArray());

    private sealed class FakeSignalRClient : ISignalRClient
    {
        public int SendCount { get; private set; }
        public string? LastQueue { get; private set; }
        public BoteMessage? LastMessage { get; private set; }
        public Func<string, BoteMessage, Task>? OnSend { get; set; }

        public Task SendMessage(string queue, BoteMessage message)
        {
            SendCount++;
            LastQueue = queue;
            LastMessage = message;
            return OnSend?.Invoke(queue, message) ?? Task.CompletedTask;
        }

        public Task<string> GetQueueConnection() => throw new NotSupportedException();
        public Task<string> GetErrorQueueConnection() => throw new NotSupportedException();
        public Task SubscribeToTopic(string topicName) => throw new NotSupportedException();
        public Task UnsubscribeFromTopic(string topicName) => throw new NotSupportedException();
        public Task<string> GetDataBusAttachmentUri(string attachmentId) => throw new NotSupportedException();
        public Task<string> GetDataBusAttachmentUploadUri(string attachmentId) => throw new NotSupportedException();
        public Task NotifyAttachmentUploaded(string attachmentId) => throw new NotSupportedException();
        public Task<Dictionary<string, string>> GetAttachmentMetadata(string attachmentId) => throw new NotSupportedException();
    }

    private sealed class FakePendingMessagesIndicator : IPendingMessagesIndicators
    {
        public Task<bool> GetAsync() => Task.FromResult(false);
        public Task SetAsync() => Task.CompletedTask;
        public Task ClearAsync() => Task.CompletedTask;
    }

    private sealed class FakeAsyncTaskFactory : IAsyncTaskFactory
    {
        public IAsyncTask Create(string description, Func<Task> action,
            bool prettyInsignificant = false, int intervalSeconds = 10) => new FakeAsyncTask();
    }

    private sealed class FakeAsyncTask : IAsyncTask
    {
        public void Start() { }
        public void Dispose() { }
    }
}
