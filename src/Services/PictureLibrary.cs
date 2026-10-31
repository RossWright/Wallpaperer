namespace Wallpaperer.Services;

public static class PictureLibrary
{
    public static readonly HashSet<string> SkippedDirectoryNames = new(StringComparer.Ordinal)
    {
        "node_modules",
        "bin",
        "obj",
        "target",
        "__pycache__",
        "venv",
    };

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".gif",
        ".bmp",
        ".webp",
        ".tif",
        ".tiff",
    };

    public static bool IsSupportedImage(string path) => Extensions.Contains(Path.GetExtension(path));

    public static IEnumerable<string> Enumerate(string root, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            List<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(directory).ToList();
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException or FileNotFoundException or IOException)
                {
                    continue;
                }

                if (IsSymlink(entry) || (attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                var name = Path.GetFileName(entry);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (name.StartsWith('.'))
                        continue;
                    if (SkippedDirectoryNames.Contains(name))
                        continue;

                    pending.Push(entry);
                    continue;
                }

                if (IsSupportedImage(name))
                    yield return entry;
            }
        }
    }

    private static bool IsSymlink(string path)
    {
        try
        {
            return File.ResolveLinkTarget(path, returnFinalTarget: false) is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
