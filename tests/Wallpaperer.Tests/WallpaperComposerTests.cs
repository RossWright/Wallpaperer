using SkiaSharp;
using Wallpaperer.Models;
using Wallpaperer.Services;

namespace Wallpaperer.Tests;

public class WallpaperComposerTests
{
    [Fact]
    public void Places_each_image_in_its_monitor_and_leaves_gaps_black()
    {
        var root = TempDirectory();
        try
        {
            var red = WriteSolid(root, "red.png", SKColors.Red, 4, 4);
            var blue = WriteSolid(root, "blue.png", SKColors.Blue, 4, 2);
            var monitors = new[]
            {
                Device("A", 0, 0, 10, 10),
                Device("B", 20, 0, 10, 10),
            };

            using var bitmap = WallpaperComposer.Render(
                monitors,
                new Dictionary<string, string> { ["A"] = red, ["B"] = blue },
                CancellationToken.None);

            Assert.Equal(30, bitmap.Width);
            Assert.Equal(10, bitmap.Height);
            AssertColor(SKColors.Red, bitmap.GetPixel(5, 5));
            AssertColor(SKColors.Blue, bitmap.GetPixel(25, 5));
            AssertColor(SKColors.Black, bitmap.GetPixel(15, 5));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Unassigned_monitors_stay_black_and_missing_files_fail_before_saving()
    {
        var root = TempDirectory();
        try
        {
            var red = WriteSolid(root, "red.png", SKColors.Red, 4, 4);
            var monitors = new[]
            {
                Device("A", 0, 0, 10, 10),
                Device("B", 10, 0, 10, 10),
            };

            using var bitmap = WallpaperComposer.Render(
                monitors,
                new Dictionary<string, string> { ["A"] = red },
                CancellationToken.None);
            AssertColor(SKColors.Red, bitmap.GetPixel(5, 5));
            AssertColor(SKColors.Black, bitmap.GetPixel(15, 5));

            var missing = Path.Combine(root, "gone.jpg");
            var error = Assert.Throws<WallpaperComposeException>(() => WallpaperComposer.Render(
                monitors,
                new Dictionary<string, string> { ["A"] = missing },
                CancellationToken.None));
            Assert.Contains(missing, error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveJpeg_uses_the_span_timestamp_name()
    {
        var root = TempDirectory();
        try
        {
            using var bitmap = new SKBitmap(2, 2);
            var path = WallpaperComposer.SaveJpeg(
                bitmap,
                root,
                new DateTimeOffset(2026, 10, 29, 23, 15, 4, TimeSpan.Zero));
            Assert.Equal("span-20261029-231504.jpg", Path.GetFileName(path));
            var header = File.ReadAllBytes(path)[..2];
            Assert.Equal(new byte[] { 0xFF, 0xD8 }, header);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertColor(SKColor expected, SKColor actual)
    {
        Assert.Equal(expected.Red, actual.Red);
        Assert.Equal(expected.Green, actual.Green);
        Assert.Equal(expected.Blue, actual.Blue);
        Assert.Equal(255, actual.Alpha);
    }

    private static MonitorDevice Device(string connector, int x, int y, int width, int height) =>
        new(connector, connector, x, y, width, height, 1, false, width, height);

    private static string WriteSolid(string directory, string name, SKColor color, int width, int height)
    {
        var path = Path.Combine(directory, name);
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data!.SaveTo(stream);
        return path;
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "wallpaperer-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return path;
    }
}
