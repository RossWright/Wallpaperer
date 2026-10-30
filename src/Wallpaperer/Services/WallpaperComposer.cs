using SkiaSharp;
using Wallpaperer.Models;

namespace Wallpaperer.Services;

public sealed class WallpaperComposeException(string message) : Exception(message);

public static class WallpaperComposer
{
    public static string DefaultDirectory()
    {
        var share = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(share, "wallpaperer");
    }

    public static SKBitmap Render(
        IReadOnlyList<MonitorDevice> monitors,
        IReadOnlyDictionary<string, string> imagePaths,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var path in imagePaths.Values)
        {
            if (!File.Exists(path))
                throw new WallpaperComposeException($"Could not read {path}.");
        }

        var canvas = SpanLayout.Measure(monitors);
        if (canvas.Width <= 0 || canvas.Height <= 0)
            throw new WallpaperComposeException("The monitor layout has no area.");

        var bitmap = new SKBitmap();
        var info = new SKImageInfo(canvas.Width, canvas.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        if (!bitmap.TryAllocPixels(info))
        {
            bitmap.Dispose();
            throw new WallpaperComposeException("Not enough memory to build the wallpaper.");
        }

        try
        {
            using var drawing = new SKCanvas(bitmap);
            drawing.Clear(SKColors.Black);
            foreach (var rectangle in canvas.Rectangles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (rectangle.Width <= 0 || rectangle.Height <= 0)
                    continue;
                if (!imagePaths.TryGetValue(rectangle.Connector, out var path))
                    continue;

                using var source = OrientedBitmap.Decode(path, null);
                if (source is null)
                    throw new WallpaperComposeException($"Could not read {path}.");

                DrawCover(drawing, source, rectangle);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    public static string SaveJpeg(SKBitmap bitmap, string directory, DateTimeOffset? timestamp = null)
    {
        Directory.CreateDirectory(directory);
        var stamp = (timestamp ?? DateTimeOffset.Now).DateTime;
        var path = Path.Combine(directory, $"span-{stamp:yyyyMMdd-HHmmss}.jpg");
        var suffix = 2;
        while (File.Exists(path))
        {
            path = Path.Combine(directory, $"span-{stamp:yyyyMMdd-HHmmss}-{suffix}.jpg");
            suffix++;
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 92)
            ?? throw new WallpaperComposeException("Could not encode the wallpaper.");
        using (var stream = File.Create(path))
            data.SaveTo(stream);

        return path;
    }

    private static void DrawCover(SKCanvas canvas, SKBitmap source, DestinationRect destination)
    {
        var cover = SpanLayout.Cover(
            source.Width,
            source.Height,
            destination.Left,
            destination.Top,
            destination.Width,
            destination.Height);

        canvas.Save();
        canvas.ClipRect(new SKRect(destination.Left, destination.Top, destination.Right, destination.Bottom));
        using var image = SKImage.FromBitmap(source);
        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawImage(
            image,
            new SKRect(cover.X, cover.Y, cover.X + cover.ScaledWidth, cover.Y + cover.ScaledHeight),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
            paint);
        canvas.Restore();
    }
}
