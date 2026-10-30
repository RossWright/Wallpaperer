using Wallpaperer.Models;
using Wallpaperer.Services;

namespace Wallpaperer.Tests;

public class PictureLibraryTests
{
    [Fact]
    public void Scan_skips_hidden_directories_build_folders_and_symlinks()
    {
        var root = TempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "room.jpg"), "x");
            File.WriteAllText(Path.Combine(root, "notes.txt"), "x");
            Directory.CreateDirectory(Path.Combine(root, "Pictures"));
            File.WriteAllText(Path.Combine(root, "Pictures", "Photo.JPG"), "x");
            Directory.CreateDirectory(Path.Combine(root, ".hidden"));
            File.WriteAllText(Path.Combine(root, ".hidden", "secret.png"), "x");
            foreach (var name in PictureLibrary.SkippedDirectoryNames)
            {
                Directory.CreateDirectory(Path.Combine(root, name));
                File.WriteAllText(Path.Combine(root, name, "skip.webp"), "x");
            }

            var outside = Path.Combine(Path.GetTempPath(), "wallpaperer-tests", Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(outside);
            try
            {
                File.WriteAllText(Path.Combine(outside, "inside.gif"), "x");
                Directory.CreateSymbolicLink(Path.Combine(root, "jump"), outside);

                var found = PictureLibrary.Enumerate(root, CancellationToken.None).Select(Path.GetFileName).Order().ToList();
                Assert.Equal(["Photo.JPG", "room.jpg"], found);
            }
            finally
            {
                Directory.Delete(outside, recursive: true);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Newest_picture_sorts_first_and_paths_break_ties()
    {
        var older = new PictureRecord("/home/a/b.jpg", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newer = new PictureRecord("/home/a/a.jpg", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        var sameTimeLaterPath = new PictureRecord("/home/a/c.jpg", older.LastWriteTimeUtc);
        var records = new[] { older, sameTimeLaterPath, newer };
        Array.Sort(records, PictureRecord.CompareNewest);
        Assert.Equal([newer.Path, older.Path, sameTimeLaterPath.Path], records.Select(record => record.Path));
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "wallpaperer-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return path;
    }
}
