namespace Dbosoft.Bote.Rebus;

internal interface IPendingMessagesIndicators
{
    Task<bool> GetAsync();

    Task SetAsync();

    Task ClearAsync();
}

internal sealed class PendingMessagesIndicators : IPendingMessagesIndicators, IDisposable
{
    private SemaphoreSlim _semaphore = new(1, 1);
    private bool _flag;

    public async Task ClearAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            _flag = false;
        }
        finally
        {
            _semaphore.Release();
        }
    }


    public async Task<bool> GetAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            return _flag;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task SetAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            _flag = true;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}
