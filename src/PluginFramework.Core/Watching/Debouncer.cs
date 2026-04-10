namespace PluginFramework.Core.Watching;

public class Debouncer : IDisposable
{
    private readonly TimeSpan _delay;
    private CancellationTokenSource? _cts;
    private readonly object _lock = new();

    public Debouncer(TimeSpan delay) => _delay = delay;

    public async Task DebounceAsync(Func<Task> action)
    {
        CancellationToken token;
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            token = _cts.Token;
        }

        try
        {
            await Task.Delay(_delay, token);
            if (!token.IsCancellationRequested)
                await action();
        }
        catch (TaskCanceledException) { /* nouvel appel en cours de debounce */ }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}
