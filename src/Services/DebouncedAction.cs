namespace Wallpaperer.Services;

public sealed class DebouncedAction
{
    private readonly TimeSpan _delay;
    private CancellationTokenSource? _pending;
    private int _generation;

    public DebouncedAction(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delay));
        _delay = delay;
    }

    public void Schedule(Func<Task> action)
    {
        _pending?.Cancel();
        var generation = ++_generation;
        var pending = new CancellationTokenSource();
        _pending = pending;
        _ = RunAsync(action, generation, pending);
    }

    public void Cancel()
    {
        _generation++;
        _pending?.Cancel();
    }

    private async Task RunAsync(Func<Task> action, int generation, CancellationTokenSource pending)
    {
        try
        {
            await Task.Delay(_delay, pending.Token);
            if (generation != _generation)
                return;

            await action();
        }
        catch (OperationCanceledException)
        {
            // A newer change replaced this one.
        }
    }
}
