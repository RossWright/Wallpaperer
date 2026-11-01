using System.Diagnostics;
using Velopack;
using Velopack.Sources;

namespace Wallpaperer.Services;

public sealed class VelopackUpdateCheck : IUpdateCheck
{
    public const string RepositoryUrl = "https://github.com/RossWright/Wallpaperer";

    private UpdateManager? _manager;
    private UpdateInfo? _update;

    public async Task<bool> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var manager = new UpdateManager(new GithubSource(RepositoryUrl, null, false));
            if (!manager.IsInstalled)
                return false;

            var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update?.TargetFullRelease is null)
                return false;

            _manager = manager;
            _update = update;
            return true;
        }
        catch (Exception exception)
        {
            Trace.WriteLine("Update check failed: " + exception.Message);
            return false;
        }
    }

    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        if (_manager is null || _update is null)
            throw new InvalidOperationException("No update is available.");

        await _manager.DownloadUpdatesAsync(_update, _ => { }, cancellationToken).ConfigureAwait(false);
        _manager.ApplyUpdatesAndRestart(_update.TargetFullRelease);
    }
}
