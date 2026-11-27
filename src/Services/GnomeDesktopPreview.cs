namespace Wallpaperer.Services;

public interface IDesktopPreview
{
    Task ShowUntilDismissedAsync(CancellationToken cancellationToken);

    Task RestoreAsync();
}

public sealed class DesktopPreviewException(string message) : Exception(message);

public sealed class UnavailableDesktopPreview : IDesktopPreview
{
    public Task ShowUntilDismissedAsync(CancellationToken cancellationToken) =>
        Task.FromException(new DesktopPreviewException("Desktop preview is only available on GNOME."));

    public Task RestoreAsync() => Task.CompletedTask;
}

public sealed class GnomeDesktopPreview(IProcessRunner processes, string? helperPath = null, string? portalTokenPath = null) : IDesktopPreview
{
    private readonly string _helperPath = helperPath ?? DefaultHelperPath();
    private readonly string _portalTokenPath = portalTokenPath ?? Path.Combine(WallpaperComposer.DefaultDirectory(), "preview-portal.token");

    public async Task ShowUntilDismissedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_helperPath))
            throw new DesktopPreviewException("Wallpaperer is missing its desktop preview helper.");

        var result = await processes.RunAsync("python3", [_helperPath, _portalTokenPath], cancellationToken);
        if (cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException(cancellationToken);

        if (result.ExitCode == 2)
            throw new DesktopPreviewException("Desktop preview needs permission to send the show-desktop shortcut.");

        if (result.ExitCode != 0)
            throw new DesktopPreviewException(Failure(result));
    }

    public Task RestoreAsync() => Task.CompletedTask;

    internal static string DefaultHelperPath() =>
        Path.Combine(AppContext.BaseDirectory, "GnomeShell", "preview-desktop.py");

    private static string Failure(ProcessResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        var line = detail.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(item => !item.Contains("DeprecationWarning", StringComparison.Ordinal)
                && !item.StartsWith("GLib.unix_signal_add", StringComparison.Ordinal)
                && !item.StartsWith("GLibUnix.signal_add", StringComparison.Ordinal));
        return string.IsNullOrWhiteSpace(line)
            ? "Could not preview the desktop."
            : $"Could not preview the desktop. {line}";
    }
}
