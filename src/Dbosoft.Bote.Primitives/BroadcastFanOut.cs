using System.Collections.Concurrent;

namespace Dbosoft.Bote.Primitives;

/// <summary>
/// Result of a bounded fan-out: how many deliveries succeeded and the per-item failures.
/// </summary>
public sealed record FanOutResult<T>(int Delivered, IReadOnlyList<(T Item, Exception Error)> Failures)
{
    public bool AnyDelivered => Delivered > 0;
}

/// <summary>
/// Delivers a message to many subscribers with bounded concurrency and per-subscriber failure
/// isolation. Used for topic broadcast fan-out on the shared clients queue: a single slow or
/// failing subscriber must neither block the rest (head-of-line) nor abort the whole broadcast
/// (which, on Service Bus redelivery, would re-deliver to everyone and duplicate messages).
/// </summary>
public static class BroadcastFanOut
{
    public static async Task<FanOutResult<T>> RunAsync<T>(
        IReadOnlyList<T> items,
        Func<T, Task> deliver,
        int maxConcurrency)
    {
        if (items.Count == 0)
            return new FanOutResult<T>(0, Array.Empty<(T, Exception)>());

        var delivered = 0;
        var failures = new ConcurrentBag<(T, Exception)>();
        var gate = new SemaphoreSlim(Math.Max(1, maxConcurrency));

        try
        {
            // Materialize eagerly so all tasks are started before we await, and dispose the
            // semaphore only after they have all completed (below).
            var tasks = items.Select(async item =>
            {
                await gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    await deliver(item).ConfigureAwait(false);
                    Interlocked.Increment(ref delivered);
                }
                catch (Exception ex)
                {
                    failures.Add((item, ex));
                }
                finally
                {
                    gate.Release();
                }
            }).ToList();

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        finally
        {
            gate.Dispose();
        }

        return new FanOutResult<T>(delivered, failures.ToList());
    }
}
