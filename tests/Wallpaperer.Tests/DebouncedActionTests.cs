using Wallpaperer.Services;

namespace Wallpaperer.Tests;

public class DebouncedActionTests
{
    [Fact]
    public async Task A_later_schedule_replaces_the_pending_one()
    {
        var debounce = new DebouncedAction(TimeSpan.FromMilliseconds(60));
        var count = 0;
        debounce.Schedule(() =>
        {
            Interlocked.Increment(ref count);
            return Task.CompletedTask;
        });
        await Task.Delay(20);
        debounce.Schedule(() =>
        {
            Interlocked.Increment(ref count);
            return Task.CompletedTask;
        });

        await Task.Delay(140);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Cancel_drops_the_pending_action()
    {
        var debounce = new DebouncedAction(TimeSpan.FromMilliseconds(40));
        var count = 0;
        debounce.Schedule(() =>
        {
            Interlocked.Increment(ref count);
            return Task.CompletedTask;
        });
        debounce.Cancel();

        await Task.Delay(80);
        Assert.Equal(0, count);
    }
}
