namespace NEXTShot.Services;

public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = "Global\\NEXTShot_SingleInstance";

    private readonly Mutex _mutex;
    public bool IsFirstInstance { get; }

    public SingleInstanceService()
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
        IsFirstInstance = createdNew;
    }

    public void Dispose()
    {
        if (IsFirstInstance)
        {
            try { _mutex.ReleaseMutex(); }
            catch { /* already released */ }
        }
        _mutex.Dispose();
    }
}
