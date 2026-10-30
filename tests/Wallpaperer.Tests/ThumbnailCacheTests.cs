using SkiaSharp;
using Wallpaperer.Services;

namespace Wallpaperer.Tests;

public class ThumbnailCacheTests
{
    [Fact]
    public async Task Second_request_reuses_the_cached_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "wallpaperer-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "photo.png");
            using (var bitmap = new SKBitmap(8, 4))
            {
                bitmap.Erase(SKColors.Orange);
                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var stream = File.Create(source);
                data!.SaveTo(stream);
            }

            var cache = new ThumbnailCache(Path.Combine(root, "cache"));
            var first = await cache.GetJpegAsync(source, 32, CancellationToken.None);
            var second = await cache.GetJpegAsync(source, 32, CancellationToken.None);
            Assert.False(string.IsNullOrEmpty(first));
            Assert.Equal(first, second);
            Assert.True(new FileInfo(first!).Length > 0);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
