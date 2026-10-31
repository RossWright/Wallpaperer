using Wallpaperer.Services;

namespace Wallpaperer.Tests;

internal sealed class EmptyCurrentWallpaperSource : ICurrentWallpaperSource
{
    public static readonly EmptyCurrentWallpaperSource Instance = new();

    public Task<CurrentWallpapers> ReadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new CurrentWallpapers(new WallpaperPicture(null, null)));
}
