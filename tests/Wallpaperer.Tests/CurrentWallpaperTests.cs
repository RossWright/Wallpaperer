using Wallpaperer.Services;

namespace Wallpaperer.Tests;

public class CurrentWallpaperTests
{
    [Fact]
    public void File_uris_become_local_paths_and_empty_values_do_not()
    {
        Assert.Equal("/home/ross/My File.jpg", WallpaperSettingsText.ParseFileUri("'file:///home/ross/My%20File.jpg'\n"));
        Assert.Null(WallpaperSettingsText.ParseFileUri("''"));
        Assert.Null(WallpaperSettingsText.ParseFileUri("'spanned'"));
        Assert.Equal("prefer-dark", WallpaperSettingsText.ParseString("'prefer-dark'\n"));
    }

    [Fact]
    public void Spanned_pictures_cover_every_monitor_together()
    {
        var spanned = new WallpaperPicture("/tmp/span.jpg", "spanned");
        var zoom = new WallpaperPicture("/tmp/lock.jpg", "zoom");

        Assert.True(spanned.ShowsAsOneSpan(4));
        Assert.False(spanned.ShowsAsOneSpan(1));
        Assert.False(zoom.ShowsAsOneSpan(4));
    }

    [Fact]
    public async Task Prefer_dark_uses_the_dark_uri()
    {
        var runner = new ScriptedRunner(new Dictionary<string, string>
        {
            ["org.gnome.desktop.interface color-scheme"] = "'prefer-dark'\n",
            ["org.gnome.desktop.background picture-uri"] = "'file:///tmp/light.jpg'\n",
            ["org.gnome.desktop.background picture-uri-dark"] = "'file:///tmp/dark%20one.jpg'\n",
            ["org.gnome.desktop.background picture-options"] = "'spanned'\n",
        });

        var current = await new GnomeCurrentWallpaperSource(runner).ReadAsync(CancellationToken.None);

        Assert.Equal("/tmp/dark one.jpg", current.Desktop.Path);
        Assert.Equal("spanned", current.Desktop.Options);
    }

    private sealed class ScriptedRunner(Dictionary<string, string> values) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            var key = string.Join(' ', arguments.Skip(1));
            return Task.FromResult(new ProcessResult(0, values[key], ""));
        }
    }
}
