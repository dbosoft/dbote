using System.Text;
using AwesomeAssertions;
using Dbosoft.Bote.Rebus.Transport;
using Dbosoft.Bote.Transport.Abstractions;
using Rebus.Logging;
using Rebus.Messages;
using Rebus.Retry;
using Rebus.Transport;

namespace Dbosoft.Bote.Rebus.Tests;

// Guards the H2 contract: client-side poison messages go to the client's local error
// queue (not back through the SignalR SendMessage whitelist, which would reject the
// error queue and silently drop or infinitely re-loop the message).
public class BoteErrorHandlerTests
{
    [Fact]
    public async Task Poison_message_is_written_to_the_local_error_queue()
    {
        var writer = new FakeErrorQueueWriter();
        var handler = CreateHandler(writer);

        await handler.HandlePoisonMessage(NewMessage("msg-1", "the body"), NewContext(), NewError());

        writer.WriteCount.Should().Be(1);
        writer.LastMessage!.Body.Should().Be("the body");
    }

    [Fact]
    public async Task Poison_message_carries_the_error_details_for_triage()
    {
        var writer = new FakeErrorQueueWriter();
        var handler = CreateHandler(writer);

        await handler.HandlePoisonMessage(
            NewMessage("msg-1", "the body"), NewContext(),
            new ExceptionInfo("System.Exception", "boom", "stack trace details", DateTimeOffset.UtcNow));

        writer.LastMessage!.Headers.Should().ContainKey(Headers.ErrorDetails)
            .WhoseValue.Should().Be("stack trace details");
    }

    [Fact]
    public async Task Poison_message_preserves_the_original_message_id()
    {
        var writer = new FakeErrorQueueWriter();
        var handler = CreateHandler(writer);

        await handler.HandlePoisonMessage(NewMessage("msg-1", "the body"), NewContext(), NewError());

        writer.LastMessage!.Headers.Should().ContainKey(Headers.MessageId)
            .WhoseValue.Should().Be("msg-1");
    }

    private static BoteErrorHandler CreateHandler(IErrorQueueWriter writer) =>
        new(writer, new ConsoleLoggerFactory(colored: false));

    private static TransportMessage NewMessage(string messageId, string body) =>
        new(new Dictionary<string, string> { [Headers.MessageId] = messageId },
            Encoding.UTF8.GetBytes(body));

    private static ITransactionContext NewContext() => new RebusTransactionScope().TransactionContext;

    private static ExceptionInfo NewError() =>
        new("System.Exception", "boom", "details", DateTimeOffset.UtcNow);

    private sealed class FakeErrorQueueWriter : IErrorQueueWriter
    {
        public int WriteCount { get; private set; }
        public BoteMessage? LastMessage { get; private set; }

        public Task WriteAsync(BoteMessage message)
        {
            WriteCount++;
            LastMessage = message;
            return Task.CompletedTask;
        }
    }
}
