using AwesomeAssertions;
using Dbosoft.Bote.BoteWorker.Converters;
using Dbosoft.Bote.Transport.Abstractions;
using Rebus.Messages;

namespace Dbosoft.Bote.Rebus.IntegrationTests;

public class MessageConverterTests
{
    // Rebus 8.8 writes 7 fractional digits ("o"), Rebus 8.9 writes 6; the worker serves clients
    // on either version.
    [Theory]
    [InlineData("2026-10-22T08:01:46.6234860+00:00")]
    [InlineData("2026-10-22T08:01:46.623486+00:00")]
    public void Deferred_until_of_any_Rebus_version_becomes_the_scheduled_time(string deferredUntil)
    {
        var message = new MessageConverter().ToServiceBus(new BoteMessage
        {
            Headers = new Dictionary<string, string?>
            {
                [Headers.MessageId] = "m",
                [Headers.DeferredUntil] = deferredUntil,
            },
            Body = Array.Empty<byte>(),
        });

        message.ScheduledEnqueueTime.Should().BeCloseTo(
            new DateTimeOffset(2026, 10, 22, 8, 1, 46, 623, TimeSpan.Zero), TimeSpan.FromMilliseconds(1));
        message.ApplicationProperties.Should().NotContainKey(Headers.DeferredUntil);
    }
}
