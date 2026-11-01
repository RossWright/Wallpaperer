using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wallpaperer.Services;

public sealed class UiSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public UiSettingsStore(
        string path,
        string? sizeFilterId,
        bool autoApply = false,
        IReadOnlyList<LibraryPin>? pins = null,
        IReadOnlyList<string>? hiddenImages = null,
        IReadOnlyList<string>? hiddenFolders = null,
        IReadOnlyList<string>? shownImages = null)
    {
        _path = path;
        SizeFilterId = sizeFilterId;
        AutoApply = autoApply;
        Pins = pins?.ToList() ?? [];
        HiddenImages = hiddenImages?.ToList() ?? [];
        HiddenFolders = hiddenFolders?.ToList() ?? [];
        ShownImages = shownImages?.ToList() ?? [];
    }

    public string? SizeFilterId { get; set; }

    public bool AutoApply { get; set; }

    public List<LibraryPin> Pins { get; }

    public List<string> HiddenImages { get; }

    public List<string> HiddenFolders { get; }

    public List<string> ShownImages { get; }

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
            return new UiSettingsStore(
                path,
                file?.SizeFilterId,
                file?.AutoApply ?? false,
                file?.Pins,
                file?.HiddenImages,
                file?.HiddenFolders,
                file?.ShownImages);
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

            var json = JsonSerializer.Serialize(
                new SettingsFile(SizeFilterId, AutoApply, Pins, HiddenImages, HiddenFolders, ShownImages),
                JsonOptions);
            File.WriteAllText(_path, json);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The filter still applies for this session.
        }
    }

    private sealed record SettingsFile(
        [property: JsonPropertyName("sizeFilterId")] string? SizeFilterId,
        [property: JsonPropertyName("autoApply")] bool AutoApply = false,
        [property: JsonPropertyName("pins")] List<LibraryPin>? Pins = null,
        [property: JsonPropertyName("hiddenImages")] List<string>? HiddenImages = null,
        [property: JsonPropertyName("hiddenFolders")] List<string>? HiddenFolders = null,
        [property: JsonPropertyName("shownImages")] List<string>? ShownImages = null);
}
