using AwesomeAssertions;
using Dbosoft.Bote.Primitives;
using Dbosoft.Bote.Rebus.Transport;
using Rebus.Exceptions;
using Rebus.Messages;
using Rebus.Pipeline;
using Rebus.Transport;

namespace Dbosoft.Bote.Rebus.Tests;

public class PeerSenderStepTests
{
    private static readonly PeerSenderStep Step = new(sender => sender == "instance");

    [Fact]
    public async Task A_message_from_an_accepted_client_reaches_the_handlers()
    {
        var handled = await Process(new() { [BoteHeaders.SenderClientId] = "instance" });

        handled.Should().BeTrue();
    }

    [Fact]
    public async Task A_message_from_another_client_is_refused_before_the_handlers()
    {
        var handled = false;
        var act = () => Step.Process(Context(new() { [BoteHeaders.SenderClientId] = "agent-2" }),
            () => { handled = true; return Task.CompletedTask; });

        // Fail fast: no retries, straight to the error queue.
        await act.Should().ThrowAsync<FailFastException>().WithMessage("*agent-2*");
        handled.Should().BeFalse();
    }

    [Fact]
    public async Task A_message_from_the_cloud_passes() =>
        (await Process([])).Should().BeTrue();

    private static async Task<bool> Process(Dictionary<string, string> headers)
    {
        var handled = false;
        await Step.Process(Context(headers), () => { handled = true; return Task.CompletedTask; });
        return handled;
    }

    private static IncomingStepContext Context(Dictionary<string, string> headers)
    {
        headers[Headers.MessageId] = Guid.NewGuid().ToString();
        return new IncomingStepContext(new TransportMessage(headers, []), new RebusTransactionScope().TransactionContext);
    }
}
