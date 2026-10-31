namespace Wallpaperer.Services;

public sealed record WallpaperPicture(string? Path, string? Options)
{
    public bool ShowsAsOneSpan(int monitorCount) =>
        monitorCount > 1 && string.Equals(Options, "spanned", StringComparison.OrdinalIgnoreCase);
}

public sealed record CurrentWallpapers(WallpaperPicture Desktop);

public interface ICurrentWallpaperSource
{
    Task<CurrentWallpapers> ReadAsync(CancellationToken cancellationToken);
}

public sealed class GnomeCurrentWallpaperSource(IProcessRunner processes) : ICurrentWallpaperSource
{
    public async Task<CurrentWallpapers> ReadAsync(CancellationToken cancellationToken)
    {
        var scheme = await GetAsync("org.gnome.desktop.interface", "color-scheme", cancellationToken);
        var picture = await GetAsync("org.gnome.desktop.background", "picture-uri", cancellationToken);
        var pictureDark = await GetAsync("org.gnome.desktop.background", "picture-uri-dark", cancellationToken);
        var options = await GetAsync("org.gnome.desktop.background", "picture-options", cancellationToken);

        var preferDark = string.Equals(
            WallpaperSettingsText.ParseString(scheme),
            "prefer-dark",
            StringComparison.OrdinalIgnoreCase);
        var lightPath = WallpaperSettingsText.ParseFileUri(picture);
        var darkPath = WallpaperSettingsText.ParseFileUri(pictureDark);
        var desktopPath = preferDark && darkPath is not null ? darkPath : lightPath;

        return new CurrentWallpapers(
            new WallpaperPicture(desktopPath, WallpaperSettingsText.ParseString(options)));
    }

    private async Task<string> GetAsync(string schema, string key, CancellationToken cancellationToken)
    {
        var result = await processes.RunAsync("gsettings", ["get", schema, key], cancellationToken);
        return result.ExitCode == 0 ? result.StandardOutput : "";
    }
}

public static class WallpaperSettingsText
{
    public static string? ParseString(string? gsettingsOutput)
    {
        if (string.IsNullOrWhiteSpace(gsettingsOutput))
            return null;

        var text = gsettingsOutput.Trim();
        if (text.Length >= 2 && text[0] == '\'' && text[^1] == '\'')
            text = text[1..^1].Replace("\\'", "'", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);

        return text.Length == 0 ? null : text;
    }

    public static string? ParseFileUri(string? gsettingsOutput)
    {
        var text = ParseString(gsettingsOutput);
        if (text is null || !text.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var path = new Uri(text).LocalPath;
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch (UriFormatException)
        {
            return null;
        }
    }
}
