using Wallpaperer.Services;

namespace Wallpaperer.Tests;

public class UiSettingsStoreTests
{
    [Fact]
    public void Size_filter_id_round_trips_and_missing_files_load_empty()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wallpaperer-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "ui-settings.json");
        try
        {
            var missing = UiSettingsStore.Load(path);
            Assert.Null(missing.SizeFilterId);

            missing.SizeFilterId = "3840x2160";
            missing.Save();

            var loaded = UiSettingsStore.Load(path);
            Assert.Equal("3840x2160", loaded.SizeFilterId);
            Assert.False(loaded.AutoApply);

            loaded.AutoApply = true;
            loaded.Save();
            var again = UiSettingsStore.Load(path);
            Assert.True(again.AutoApply);
            Assert.Equal("3840x2160", again.SizeFilterId);

            File.WriteAllText(path, """{ "sizeFilterId": "1920x1080", "sameLockScreen": false }""");
            var olderFile = UiSettingsStore.Load(path);
            Assert.Equal("1920x1080", olderFile.SizeFilterId);
            Assert.False(olderFile.AutoApply);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
