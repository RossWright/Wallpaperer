namespace Wallpaperer.Services;

public sealed class WallpaperApplyException(string message) : Exception(message);

public sealed class GnomeWallpaperApplier(IProcessRunner processes)
{
    public async Task ApplyAsync(string imagePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(imagePath))
            throw new WallpaperApplyException($"Could not read {imagePath}.");

        var quotedUri = Quote(new Uri(imagePath).AbsoluteUri);
        var spanned = Quote("spanned");
        await SetAsync("org.gnome.desktop.background", "picture-uri", quotedUri, cancellationToken);
        await SetAsync("org.gnome.desktop.background", "picture-uri-dark", quotedUri, cancellationToken);
        await SetAsync("org.gnome.desktop.background", "picture-options", spanned, cancellationToken);
        DeleteOlderSpanFiles(imagePath);
    }

    private async Task SetAsync(string schema, string key, string value, CancellationToken cancellationToken)
    {
        var result = await processes.RunAsync(
            "gsettings",
            ["set", schema, key, value],
            cancellationToken);

        if (result.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? $"gsettings exited with code {result.ExitCode} while setting {key}."
                : result.StandardError.Trim();
            throw new WallpaperApplyException(detail);
        }
    }

    private static void DeleteOlderSpanFiles(string imagePath)
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
