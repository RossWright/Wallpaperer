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
        CancellationToken cancellationToken) =>
        Render(
            monitors,
            imagePaths.Select(pair => new ImagePlacement(pair.Value, [pair.Key])).ToList(),
            cancellationToken);

    public static SKBitmap Render(
        IReadOnlyList<MonitorDevice> monitors,
        IReadOnlyList<ImagePlacement> placements,
        CancellationToken cancellationToken)
    {
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
            using (var drawing = new SKCanvas(bitmap))
                drawing.Clear(SKColors.Black);
            DrawPlacements(bitmap, canvas, placements, cancellationToken);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    public static SKBitmap? PaintOn(
        SKBitmap underlay,
        IReadOnlyList<MonitorDevice> monitors,
        IReadOnlyList<ImagePlacement> placements,
        CancellationToken cancellationToken)
    {
        var canvas = SpanLayout.Measure(monitors);
        if (underlay.Width != canvas.Width || underlay.Height != canvas.Height)
            return null;

        var bitmap = underlay.Copy();
        if (bitmap is null)
            return null;

        try
        {
            DrawPlacements(bitmap, canvas, placements, cancellationToken);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static void DrawPlacements(
        SKBitmap bitmap,
        SpanCanvas canvas,
        IReadOnlyList<ImagePlacement> placements,
        CancellationToken cancellationToken)
    {
        var drawn = Uncovered(placements);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var path in drawn.Select(placement => placement.Path).Distinct())
        {
            if (!File.Exists(path))
                throw new WallpaperComposeException($"Could not read {path}.");
        }

        using var drawing = new SKCanvas(bitmap);
        var byConnector = canvas.Rectangles.ToDictionary(rectangle => rectangle.Connector);
        foreach (var placement in drawn)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var members = placement.Connectors
                .Distinct()
                .Select(connector => byConnector.TryGetValue(connector, out var rectangle) ? rectangle : null)
                .OfType<DestinationRect>()
                .Where(rectangle => rectangle.Width > 0 && rectangle.Height > 0)
                .ToList();
            if (members.Count == 0)
                continue;

            using var source = OrientedBitmap.Decode(placement.Path, null);
            if (source is null)
                throw new WallpaperComposeException($"Could not read {placement.Path}.");

            var visible = placement.Visible ?? placement.Connectors;
            var clip = members
                .Where(rectangle => visible.Contains(rectangle.Connector))
                .ToList();
            if (clip.Count == 0)
                continue;

            DrawPlacement(drawing, source, members, clip);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static List<ImagePlacement> Uncovered(IReadOnlyList<ImagePlacement> placements)
    {
        var covered = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<ImagePlacement>();
        for (var index = placements.Count - 1; index >= 0; index--)
        {
            var placement = placements[index];
            var visible = (placement.Visible ?? placement.Connectors)
                .Where(covered.Add)
                .ToList();
            if (visible.Count == 0)
                continue;

            kept.Add(placement with { Visible = visible });
        }

        kept.Reverse();
        return kept;
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

    private static void DrawPlacement(
        SKCanvas canvas,
        SKBitmap source,
        IReadOnlyList<DestinationRect> fitted,
        IReadOnlyList<DestinationRect> clip)
    {
        var left = fitted.Min(rectangle => rectangle.Left);
        var top = fitted.Min(rectangle => rectangle.Top);
        var right = fitted.Max(rectangle => rectangle.Right);
        var bottom = fitted.Max(rectangle => rectangle.Bottom);
        var cover = SpanLayout.Cover(source.Width, source.Height, left, top, right - left, bottom - top);
        using var image = SKImage.FromBitmap(source);
        using var paint = new SKPaint { IsAntialias = true };
        var placed = new SKRect(cover.X, cover.Y, cover.X + cover.ScaledWidth, cover.Y + cover.ScaledHeight);
        foreach (var member in clip)
        {
            canvas.Save();
            canvas.ClipRect(new SKRect(member.Left, member.Top, member.Right, member.Bottom));
            canvas.DrawImage(image, placed, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), paint);
            canvas.Restore();
        }
    }
}
