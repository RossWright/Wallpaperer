namespace Wallpaperer.Services;

public sealed class WallpaperApplyException(string message) : Exception(message);

public sealed class GnomeWallpaperApplier(IProcessRunner processes)
{
    public async Task ApplyAsync(string imagePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(imagePath))
            throw new WallpaperApplyException($"Could not read {imagePath}.");

        var quotedUri = Quote(new Uri(imagePath).AbsoluteUri);
        await SetAsync("picture-uri", quotedUri, cancellationToken);
        await SetAsync("picture-uri-dark", quotedUri, cancellationToken);
        await SetAsync("picture-options", Quote("spanned"), cancellationToken);
        DeleteOlder(imagePath);
    }

    private async Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        var result = await processes.RunAsync(
            "gsettings",
            ["set", "org.gnome.desktop.background", key, value],
            cancellationToken);

        if (result.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? $"gsettings exited with code {result.ExitCode} while setting {key}."
                : result.StandardError.Trim();
            throw new WallpaperApplyException(detail);
        }
    }

    private static void DeleteOlder(string imagePath)
    {
        var directory = Path.GetDirectoryName(imagePath);
        if (string.IsNullOrEmpty(directory))
            return;

        var current = Path.GetFullPath(imagePath);
        foreach (var old in Directory.EnumerateFiles(directory, "span-*.jpg"))
        {
            if (!string.Equals(Path.GetFullPath(old), current, StringComparison.Ordinal))
                File.Delete(old);
        }
    }

    internal static string Quote(string value) => "'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
}
