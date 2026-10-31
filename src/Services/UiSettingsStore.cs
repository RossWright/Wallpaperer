using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wallpaperer.Services;

public sealed class UiSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public UiSettingsStore(string path, string? sizeFilterId, bool autoApply = false)
    {
        _path = path;
        SizeFilterId = sizeFilterId;
        AutoApply = autoApply;
    }

    public string? SizeFilterId { get; set; }

    public bool AutoApply { get; set; }

    public static string DefaultPath()
    {
        var share = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(share, "wallpaperer", "ui-settings.json");
    }

    public static UiSettingsStore Load(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new UiSettingsStore(path, null);

            var file = JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(path), JsonOptions);
            return new UiSettingsStore(path, file?.SizeFilterId, file?.AutoApply ?? false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new UiSettingsStore(path, null);
        }
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(new SettingsFile(SizeFilterId, AutoApply), JsonOptions);
            File.WriteAllText(_path, json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The filter still applies for this session.
        }
    }

    private sealed record SettingsFile(
        [property: JsonPropertyName("sizeFilterId")] string? SizeFilterId,
        [property: JsonPropertyName("autoApply")] bool AutoApply = false);
}
