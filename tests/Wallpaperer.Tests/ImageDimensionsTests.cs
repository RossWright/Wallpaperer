using SkiaSharp;
using Wallpaperer.Services;

namespace Wallpaperer.Tests;

public class ImageDimensionsTests
{
    [Fact]
    public void Reads_pixel_size_from_a_png()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wallpaperer-{Guid.NewGuid():N}.png");
        try
        {
            using (var bitmap = new SKBitmap(32, 16))
            {
                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var stream = File.OpenWrite(path);
                data.SaveTo(stream);
            }

            var size = ImageDimensions.TryRead(path);
            Assert.Equal((32, 16), size);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Unreadable_files_have_no_size()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wallpaperer-{Guid.NewGuid():N}.png");
        File.WriteAllText(path, "not an image");
        try
        {
            Assert.Null(ImageDimensions.TryRead(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
