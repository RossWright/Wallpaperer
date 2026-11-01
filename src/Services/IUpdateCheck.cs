namespace Wallpaperer.Services;

public interface IUpdateCheck
{
    Task<bool> CheckAsync(CancellationToken cancellationToken);

    Task ApplyAsync(CancellationToken cancellationToken);
}

public sealed class NoUpdateCheck : IUpdateCheck
{
    public static readonly NoUpdateCheck Instance = new();

    private NoUpdateCheck()
    {
    }

    public Task<bool> CheckAsync(CancellationToken cancellationToken) => Task.FromResult(false);

    public Task ApplyAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
