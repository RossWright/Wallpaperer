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
    public void One_image_spans_the_union_and_leaves_the_gap_black()
    {
        var root = TempDirectory();
        try
        {
            var stripes = WriteStripes(root);
            var monitors = new[]
            {
                Device("A", 0, 0, 10, 10),
                Device("B", 10, 0, 10, 10),
                Device("C", 20, 0, 10, 10),
            };

            using var spanned = WallpaperComposer.Render(
                monitors,
                [new ImagePlacement(stripes, ["A", "B", "C"])],
                CancellationToken.None);
            AssertColor(SKColors.Red, spanned.GetPixel(5, 5));
            AssertColor(SKColors.Green, spanned.GetPixel(15, 5));
            AssertColor(SKColors.Blue, spanned.GetPixel(25, 5));

            var split = new[]
            {
                Device("A", 0, 0, 10, 10),
                Device("C", 20, 0, 10, 10),
            };
            using var gapped = WallpaperComposer.Render(
                split,
                [new ImagePlacement(stripes, ["A", "C"])],
                CancellationToken.None);
            Assert.Equal(30, gapped.Width);
            AssertColor(SKColors.Red, gapped.GetPixel(5, 5));
            AssertColor(SKColors.Black, gapped.GetPixel(15, 5));
            AssertColor(SKColors.Blue, gapped.GetPixel(25, 5));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_new_span_paints_over_the_full_wallpaper_without_moving_it()
    {
        var root = TempDirectory();
        try
        {
            var wallpaper = WriteDesk(root);
            var white = WriteSolid(root, "white.png", SKColors.White, 8, 8);
            var monitors = new[]
            {
                Device("T", 10, 0, 10, 10),
                Device("L", 0, 10, 10, 10),
                Device("M", 10, 10, 10, 10),
                Device("R", 20, 10, 10, 10),
            };

            using var layered = WallpaperComposer.Render(
                monitors,
                [
                    new ImagePlacement(wallpaper, ["T", "L", "M", "R"]),
                    new ImagePlacement(white, ["L", "M"]),
                ],
                CancellationToken.None);

            AssertColor(SKColors.Magenta, layered.GetPixel(15, 5));
            AssertColor(SKColors.White, layered.GetPixel(5, 15));
            AssertColor(SKColors.White, layered.GetPixel(15, 15));
            AssertColor(SKColors.Blue, layered.GetPixel(25, 15));

            using var cleared = WallpaperComposer.Render(
                monitors,
                [new ImagePlacement(wallpaper, ["T", "L", "M", "R"], ["T", "R"])],
                CancellationToken.None);
            AssertColor(SKColors.Magenta, cleared.GetPixel(15, 5));
            AssertColor(SKColors.Black, cleared.GetPixel(5, 15));
            AssertColor(SKColors.Blue, cleared.GetPixel(25, 15));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_covered_picture_is_left_out_and_new_paint_matches_a_full_compose()
    {
        var root = TempDirectory();
        try
        {
            var wallpaper = WriteDesk(root);
            var white = WriteSolid(root, "white.png", SKColors.White, 8, 8);
            var monitors = new[]
            {
                Device("T", 10, 0, 10, 10),
                Device("L", 0, 10, 10, 10),
                Device("M", 10, 10, 10, 10),
                Device("R", 20, 10, 10, 10),
            };
            var hidden = new ImagePlacement(Path.Combine(root, "missing.png"), ["L", "M"]);
            var overlay = new ImagePlacement(white, ["L", "M"]);

            using var skipped = WallpaperComposer.Render(
                monitors,
                [new ImagePlacement(wallpaper, ["T", "L", "M", "R"]), hidden, overlay],
                CancellationToken.None);
            AssertColor(SKColors.Magenta, skipped.GetPixel(15, 5));
            AssertColor(SKColors.White, skipped.GetPixel(5, 15));
            AssertColor(SKColors.Blue, skipped.GetPixel(25, 15));

            using var underlay = WallpaperComposer.Render(
                monitors,
                [new ImagePlacement(wallpaper, ["T", "L", "M", "R"])],
                CancellationToken.None);
            using var painted = WallpaperComposer.PaintOn(underlay, monitors, [overlay], CancellationToken.None);
            Assert.NotNull(painted);
            AssertColor(SKColors.Magenta, painted.GetPixel(15, 5));
            AssertColor(SKColors.White, painted.GetPixel(5, 15));
            AssertColor(SKColors.White, painted.GetPixel(15, 15));
            AssertColor(SKColors.Blue, painted.GetPixel(25, 15));
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

    private static string WriteStripes(string directory)
    {
        var path = Path.Combine(directory, "stripes.png");
        using var bitmap = new SKBitmap(30, 10);
        for (var x = 0; x < bitmap.Width; x++)
        {
            var color = x < 10 ? SKColors.Red : x < 20 ? SKColors.Green : SKColors.Blue;
            for (var y = 0; y < bitmap.Height; y++)
                bitmap.SetPixel(x, y, color);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data!.SaveTo(stream);
        return path;
    }

    private static string WriteDesk(string directory)
    {
        var path = Path.Combine(directory, "desk.png");
        using var bitmap = new SKBitmap(30, 20);
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(SKColors.Black);

        Fill(bitmap, 10, 0, 10, 10, SKColors.Yellow);
        Fill(bitmap, 12, 2, 6, 6, SKColors.Magenta);
        Fill(bitmap, 0, 10, 10, 10, SKColors.Red);
        Fill(bitmap, 10, 10, 10, 10, SKColors.Green);
        Fill(bitmap, 20, 10, 10, 10, SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data!.SaveTo(stream);
        return path;
    }

    private static void Fill(SKBitmap bitmap, int x, int y, int width, int height, SKColor color)
    {
        for (var px = x; px < x + width; px++)
        {
            for (var py = y; py < y + height; py++)
                bitmap.SetPixel(px, py, color);
        }
    }

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
