using System.Text.Json.Serialization;

namespace Wallpaperer.Services;

public sealed record LibraryPin(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("path")] string Path)
{
    public const string Image = "image";
    public const string Folder = "folder";
}

public sealed record PictureArrangementResult(IReadOnlyList<string> Paths, int HiddenCount);

public static class PictureLibraryOrganization
{
    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static string FolderOf(string path)
    {
        var directory = Path.GetDirectoryName(Normalize(path));
        return string.IsNullOrEmpty(directory) ? "" : Normalize(directory);
    }

    public static bool IsHidden(
        string path,
        IReadOnlyList<string> hiddenImages,
        IReadOnlyList<string> hiddenFolders,
        IReadOnlyList<string> shownImages)
    {
        var key = Normalize(path);
        if (hiddenImages.Any(item => Normalize(item) == key))
            return true;
        if (shownImages.Any(item => Normalize(item) == key))
            return false;

        return IsCoveredByHiddenFolder(path, hiddenFolders);
    }

    public static bool IsCoveredByHiddenFolder(string path, IEnumerable<string> hiddenFolders)
    {
        var hidden = hiddenFolders.Select(Normalize).ToHashSet(StringComparer.Ordinal);
        var folder = FolderOf(path);
        while (!string.IsNullOrEmpty(folder))
        {
            if (hidden.Contains(folder))
                return true;

            var parent = Path.GetDirectoryName(folder);
            if (string.IsNullOrEmpty(parent))
                break;

            parent = Normalize(parent);
            if (string.IsNullOrEmpty(parent) || parent == folder)
                break;

            folder = parent;
        }

        return false;
    }

    public static IReadOnlyList<string> AncestorFolders(string imagePath)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var homeKey = string.IsNullOrEmpty(home) ? null : Normalize(home);
        var folders = new List<string>();
        var folder = FolderOf(imagePath);
        while (!string.IsNullOrEmpty(folder))
        {
            folders.Add(folder);
            if (homeKey is not null && folder == homeKey)
                break;

            var parent = Path.GetDirectoryName(folder);
            if (string.IsNullOrEmpty(parent))
                break;

            parent = Normalize(parent);
            if (string.IsNullOrEmpty(parent) || parent == folder)
                break;

            folder = parent;
        }

        return folders;
    }

    public static IReadOnlyList<string> HiddenAncestors(string imagePath, IEnumerable<string> hiddenFolders)
    {
        var hidden = hiddenFolders.Select(Normalize).ToHashSet(StringComparer.Ordinal);
        return AncestorFolders(imagePath).Where(hidden.Contains).ToList();
    }

    public static string DisplayFolder(string folder)
    {
        var normalized = Normalize(folder);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            var homeKey = Normalize(home);
            if (normalized == homeKey)
                return Path.GetFileName(homeKey);
            if (normalized.StartsWith(homeKey + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return Path.GetRelativePath(homeKey, normalized);
        }

        return normalized;
    }

    public static PictureArrangementResult Arrange(
        IReadOnlyList<string> newestFirst,
        IReadOnlyList<LibraryPin> pins,
        IReadOnlyList<string> hiddenImages,
        IReadOnlyList<string> hiddenFolders,
        IReadOnlyList<string> shownImages,
        bool showHidden)
    {
        var byPath = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in newestFirst)
            byPath.TryAdd(Normalize(path), path);

        var visibility = new PictureVisibility(hiddenImages, hiddenFolders, shownImages);
        bool IsPictureHidden(string path) => visibility.IsHidden(path);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var main = new List<string>();

        void Take(string path)
        {
            var key = Normalize(path);
            if (!byPath.ContainsKey(key) || IsPictureHidden(path) || !seen.Add(key))
                return;

            main.Add(byPath[key]);
        }

        foreach (var pin in pins)
        {
            if (pin.Kind == LibraryPin.Image)
            {
                Take(pin.Path);
                continue;
            }

            if (pin.Kind != LibraryPin.Folder)
                continue;

            var folder = Normalize(pin.Path);
            foreach (var path in newestFirst)
            {
                if (FolderOf(path) == folder)
                    Take(path);
            }
        }

        foreach (var path in newestFirst)
            Take(path);

        var hidden = newestFirst.Where(IsPictureHidden).ToList();
        if (!showHidden)
            return new PictureArrangementResult(main, hidden.Count);

        var ordered = new List<string>(main.Count + hidden.Count);
        ordered.AddRange(main);
        ordered.AddRange(hidden);
        return new PictureArrangementResult(ordered, hidden.Count);
    }

    public static void MoveImageToTop(
        List<LibraryPin> pins,
        List<string> hiddenImages,
        List<string> hiddenFolders,
        List<string> shownImages,
        string path)
    {
        var image = Normalize(path);
        pins.RemoveAll(pin => pin.Kind == LibraryPin.Image && Normalize(pin.Path) == image);
        pins.Insert(0, new LibraryPin(LibraryPin.Image, image));
        hiddenImages.RemoveAll(item => Normalize(item) == image);
        if (IsCoveredByHiddenFolder(image, hiddenFolders))
            AddDistinct(shownImages, image);
        else
            shownImages.RemoveAll(item => Normalize(item) == image);
    }

    public static void MoveFolderToTop(
        List<LibraryPin> pins,
        List<string> hiddenImages,
        List<string> hiddenFolders,
        List<string> shownImages,
        string path,
        IEnumerable<string> knownPaths)
    {
        var folder = FolderOf(path);
        pins.RemoveAll(pin =>
            (pin.Kind == LibraryPin.Folder && Normalize(pin.Path) == folder)
            || (pin.Kind == LibraryPin.Image && FolderOf(pin.Path) == folder));
        pins.Insert(0, new LibraryPin(LibraryPin.Folder, folder));
        hiddenFolders.RemoveAll(item => Normalize(item) == folder);
        hiddenImages.RemoveAll(item => FolderOf(item) == folder);
        if (IsCoveredByHiddenFolder(path, hiddenFolders))
        {
            foreach (var other in knownPaths.Where(item => FolderOf(item) == folder))
                AddDistinct(shownImages, Normalize(other));
        }
        else
        {
            shownImages.RemoveAll(item => FolderOf(item) == folder);
        }
    }

    public static void HideImage(List<LibraryPin> pins, List<string> hiddenImages, List<string> shownImages, string path)
    {
        var image = Normalize(path);
        AddDistinct(hiddenImages, image);
        shownImages.RemoveAll(item => Normalize(item) == image);
        pins.RemoveAll(pin => pin.Kind == LibraryPin.Image && Normalize(pin.Path) == image);
    }

    public static void HideFolder(
        List<LibraryPin> pins,
        List<string> hiddenImages,
        List<string> hiddenFolders,
        List<string> shownImages,
        string folder)
    {
        var normalized = Normalize(folder);
        AddDistinct(hiddenFolders, normalized);
        pins.RemoveAll(pin => IsInside(pin.Kind == LibraryPin.Folder ? pin.Path : FolderOf(pin.Path), normalized));
        hiddenImages.RemoveAll(item => IsInside(FolderOf(item), normalized));
        shownImages.RemoveAll(item => IsInside(FolderOf(item), normalized));
    }

    public static void ShowImage(
        List<string> hiddenImages,
        List<string> hiddenFolders,
        List<string> shownImages,
        string path)
    {
        var image = Normalize(path);
        hiddenImages.RemoveAll(item => Normalize(item) == image);
        if (IsCoveredByHiddenFolder(image, hiddenFolders))
            AddDistinct(shownImages, image);
        else
            shownImages.RemoveAll(item => Normalize(item) == image);
    }

    public static void ShowFolder(List<string> hiddenFolders, string folder) =>
        hiddenFolders.RemoveAll(item => Normalize(item) == Normalize(folder));

    private static bool IsInside(string path, string folder)
    {
        var normalized = Normalize(path);
        return normalized == folder
            || normalized.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static void AddDistinct(List<string> paths, string path)
    {
        if (paths.All(item => Normalize(item) != path))
            paths.Add(path);
    }
}

public sealed class PictureVisibility
{
    private readonly HashSet<string> _hiddenImages;
    private readonly HashSet<string> _shownImages;
    private readonly HashSet<string> _hiddenFolders;

    public PictureVisibility(
        IReadOnlyList<string> hiddenImages,
        IReadOnlyList<string> hiddenFolders,
        IReadOnlyList<string> shownImages)
    {
        _hiddenImages = hiddenImages.Select(PictureLibraryOrganization.Normalize).ToHashSet(StringComparer.Ordinal);
        _shownImages = shownImages.Select(PictureLibraryOrganization.Normalize).ToHashSet(StringComparer.Ordinal);
        _hiddenFolders = hiddenFolders.Select(PictureLibraryOrganization.Normalize).ToHashSet(StringComparer.Ordinal);
    }

    public bool IsHidden(string path)
    {
        var key = PictureLibraryOrganization.Normalize(path);
        if (_hiddenImages.Contains(key))
            return true;
        if (_shownImages.Contains(key))
            return false;

        var folder = PictureLibraryOrganization.FolderOf(key);
        while (!string.IsNullOrEmpty(folder))
        {
            if (_hiddenFolders.Contains(folder))
                return true;

            var parent = Path.GetDirectoryName(folder);
            if (string.IsNullOrEmpty(parent))
                break;

            parent = PictureLibraryOrganization.Normalize(parent);
            if (string.IsNullOrEmpty(parent) || parent == folder)
                break;

            folder = parent;
        }

        return false;
    }
}
