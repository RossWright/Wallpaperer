using SkiaSharp;
using Wallpaperer.Models;
using Wallpaperer.Services;

namespace Wallpaperer.Tests;

public class SpanPreviewTests
{
    [Fact]
    public void Slices_show_each_monitors_portion_of_the_picture()
    {
        var root = Path.Combine(Path.GetTempPath(), "wallpaperer-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "stripes.png");
        try
        {
            using (var bitmap = new SKBitmap(30, 10))
            {
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
            }

            var monitors = new[]
            {
                new MonitorDevice("A", "A", 0, 0, 10, 10, 1, false, 10, 10),
                new MonitorDevice("B", "B", 10, 0, 10, 10, 1, false, 10, 10),
                new MonitorDevice("C", "C", 20, 0, 10, 10, 1, false, 10, 10),
            };
            var slices = SpanPreview.PngSlices(path, monitors, ["A", "B", "C"]);
            Assert.NotNull(slices);

            AssertColor(SKColors.Red, Center(slices[0]));
            AssertColor(SKColors.Green, Center(slices[1]));
            AssertColor(SKColors.Blue, Center(slices[2]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static SKColor Center(byte[] png)
    {
        using var bitmap = SKBitmap.Decode(png);
        return bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
    }

    private static void AssertColor(SKColor expected, SKColor actual)
    {
        Assert.Equal(expected.Red, actual.Red);
        Assert.Equal(expected.Green, actual.Green);
        Assert.Equal(expected.Blue, actual.Blue);
    }
}
