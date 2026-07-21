using Microsoft.AspNetCore.SignalR.Client;

namespace Dbosoft.Bote.Rebus;

/// <summary>
/// Reconnect policy for the Bote SignalR client. An edge client is expected to lose its
/// connection routinely - the worker closes it on token expiration (every ~15 min) and
/// networks drop - so the client must keep trying to reconnect indefinitely rather than
/// giving up after the default handful of attempts and going silent.
/// </summary>
internal sealed class BoteReconnectPolicy : IRetryPolicy
{
    private static readonly TimeSpan[] Schedule =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
    ];

    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(60);

    public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
        retryContext.PreviousRetryCount < Schedule.Length
            ? Schedule[retryContext.PreviousRetryCount]
            : MaxDelay;
}
