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
        CancellationToken cancellationToken,
        float outputScale = 1) =>
        RenderScaled(monitors, placements, cancellationToken, outputScale);

    public static SKBitmap? PaintOn(
        SKBitmap underlay,
        IReadOnlyList<MonitorDevice> monitors,
        IReadOnlyList<ImagePlacement> placements,
        CancellationToken cancellationToken,
        float outputScale = 1)
    {
        var canvas = Scaled(SpanLayout.Measure(monitors), outputScale);
        if (underlay.Width != canvas.Width || underlay.Height != canvas.Height)
            return null;

        var bitmap = underlay.Copy();
        if (bitmap is null)
            return null;

        try
        {
            DrawPlacements(bitmap, canvas, placements, cancellationToken, outputScale);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    public static IReadOnlyDictionary<string, byte[]> PreviewPngs(
        IReadOnlyList<MonitorDevice> monitors,
        IReadOnlyList<ImagePlacement> placements,
        SKBitmap? underlay,
        int maxEdge,
        CancellationToken cancellationToken)
    {
        var full = SpanLayout.Measure(monitors);
        var longest = Math.Max(full.Width, full.Height);
        var scale = longest <= maxEdge || longest <= 0 ? 1f : maxEdge / (float)longest;
        var canvas = Scaled(full, scale);
        using var bitmap = Compose(monitors, placements, underlay, canvas, scale, cancellationToken);
        var slices = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var rectangle in canvas.Rectangles)
        {
            if (rectangle.Width <= 0 || rectangle.Height <= 0)
                continue;

            var png = SlicePng(bitmap, rectangle);
            if (png is not null)
                slices[rectangle.Connector] = png;
        }

        return slices;
    }

    private static SKBitmap RenderScaled(
        IReadOnlyList<MonitorDevice> monitors,
        IReadOnlyList<ImagePlacement> placements,
        CancellationToken cancellationToken,
        float outputScale)
    {
        var canvas = Scaled(SpanLayout.Measure(monitors), outputScale);
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
            DrawPlacements(bitmap, canvas, placements, cancellationToken, outputScale);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static SKBitmap Compose(
        IReadOnlyList<MonitorDevice> monitors,
        IReadOnlyList<ImagePlacement> placements,
        SKBitmap? underlay,
        SpanCanvas canvas,
        float scale,
        CancellationToken cancellationToken)
    {
        if (underlay is null)
            return RenderScaled(monitors, placements, cancellationToken, scale);

        var scaled = scale >= 0.999f
            ? underlay.Copy()
            : underlay.Resize(
                new SKSizeI(Math.Max(1, canvas.Width), Math.Max(1, canvas.Height)),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        if (scaled is null)
            throw new WallpaperComposeException("Could not build the wallpaper preview.");

        try
        {
            DrawPlacements(scaled, canvas, placements, cancellationToken, scale);
            return scaled;
        }
        catch
        {
            scaled.Dispose();
            throw;
        }
    }

    private static void DrawPlacements(
        SKBitmap bitmap,
        SpanCanvas canvas,
        IReadOnlyList<ImagePlacement> placements,
        CancellationToken cancellationToken,
        float outputScale)
    {
        var drawn = Uncovered(placements);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var path in drawn.Select(placement => placement.Path).Where(path => path is not null).Distinct())
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

            var visible = placement.Visible ?? placement.Connectors;
            var clip = members.Where(rectangle => visible.Contains(rectangle.Connector)).ToList();
            if (clip.Count == 0)
                continue;

            if (placement.Color is uint color && placement.Path is null)
            {
                DrawColor(drawing, color, clip);
                continue;
            }

            if (placement.Path is null)
                continue;

            var source = Load(placement.Path, outputScale, out var pixelScale);
            if (source is null)
                throw new WallpaperComposeException($"Could not read {placement.Path}.");

            using (source)
                DrawPlacement(drawing, source, placement.Fit, members, clip, outputScale, pixelScale);
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
                .Where(connector => !covered.Contains(connector))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (visible.Count == 0)
                continue;

            kept.Add(placement with { Visible = visible });
            if (Fills(placement))
            {
                foreach (var connector in visible)
                    covered.Add(connector);
            }
        }

        kept.Reverse();
        return kept;
    }

    private static bool Fills(ImagePlacement placement)
    {
        if (placement.Fit is ImageFit.Center or ImageFit.Fit)
            return false;

        if (placement.Path is null)
            return placement.Color is not null;

        return !HasAlpha(placement.Path);
    }

    private static bool HasAlpha(string path)
    {
        try
        {
            using var codec = SKCodec.Create(path);
            return codec is null || codec.Info.AlphaType != SKAlphaType.Opaque;
        }
        catch (Exception)
        {
            return true;
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

    private static void DrawColor(SKCanvas canvas, uint argb, IReadOnlyList<DestinationRect> clip)
    {
        var color = new SKColor(
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF),
            (byte)((argb >> 24) & 0xFF));
        using var paint = new SKPaint { Color = color, BlendMode = SKBlendMode.Src };
        foreach (var member in clip)
            canvas.DrawRect(new SKRect(member.Left, member.Top, member.Right, member.Bottom), paint);
    }

    private static void DrawPlacement(
        SKCanvas canvas,
        SKBitmap source,
        ImageFit fit,
        IReadOnlyList<DestinationRect> fitted,
        IReadOnlyList<DestinationRect> clip,
        float outputScale,
        float pixelScale)
    {
        var left = fitted.Min(rectangle => rectangle.Left);
        var top = fitted.Min(rectangle => rectangle.Top);
        var right = fitted.Max(rectangle => rectangle.Right);
        var bottom = fitted.Max(rectangle => rectangle.Bottom);
        var destWidth = right - left;
        var destHeight = bottom - top;
        if (destWidth <= 0 || destHeight <= 0)
            return;

        var geometry = pixelScale > 0 ? outputScale / pixelScale : 1f;
        var sourceWidth = Math.Max(1, (int)Math.Round(source.Width * geometry));
        var sourceHeight = Math.Max(1, (int)Math.Round(source.Height * geometry));
        using var image = SKImage.FromBitmap(source);
        using var paint = new SKPaint { IsAntialias = false, BlendMode = SKBlendMode.SrcOver };
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None);
        var placed = fit switch
        {
            ImageFit.Fit => SpanLayout.Contain(sourceWidth, sourceHeight, left, top, destWidth, destHeight),
            ImageFit.Center => SpanLayout.Center(sourceWidth, sourceHeight, left, top, destWidth, destHeight),
            ImageFit.Stretch => new CoverPlacement(1, left, top, destWidth, destHeight),
            ImageFit.Tile => default,
            _ => SpanLayout.Cover(sourceWidth, sourceHeight, left, top, destWidth, destHeight),
        };

        foreach (var member in clip)
        {
            canvas.Save();
            canvas.ClipRect(new SKRect(member.Left, member.Top, member.Right, member.Bottom));
            if (fit == ImageFit.Tile)
            {
                var stepX = Math.Max(1, sourceWidth);
                var stepY = Math.Max(1, sourceHeight);
                for (var y = top; y < bottom; y += stepY)
                {
                    for (var x = left; x < right; x += stepX)
                    {
                        canvas.DrawImage(
                            image,
                            new SKRect(x, y, x + stepX, y + stepY),
                            sampling,
                            paint);
                    }
                }
            }
            else
            {
                canvas.DrawImage(
                    image,
                    new SKRect(placed.X, placed.Y, placed.X + placed.ScaledWidth, placed.Y + placed.ScaledHeight),
                    sampling,
                    paint);
            }

            canvas.Restore();
        }
    }

    private static SKBitmap? Load(string path, float scale, out float pixelScale)
    {
        pixelScale = 1f;
        var origin = SKEncodedOrigin.Default;
        var originalWidth = 1;
        SKBitmap? bitmap = null;
        using (var codec = SKCodec.Create(path))
        {
            if (codec is null)
                return null;

            origin = codec.EncodedOrigin;
            var swaps = origin is SKEncodedOrigin.LeftTop
                or SKEncodedOrigin.RightTop
                or SKEncodedOrigin.RightBottom
                or SKEncodedOrigin.LeftBottom;
            originalWidth = Math.Max(1, swaps ? codec.Info.Height : codec.Info.Width);
            if (scale < 0.999f)
            {
                var dimensions = codec.GetScaledDimensions(scale);
                if (dimensions.Width > 0 && dimensions.Height > 0
                    && (dimensions.Width < codec.Info.Width || dimensions.Height < codec.Info.Height))
                {
                    var info = new SKImageInfo(
                        dimensions.Width,
                        dimensions.Height,
                        SKColorType.Rgba8888,
                        SKAlphaType.Premul);
                    bitmap = SKBitmap.Decode(codec, info);
                }
            }
        }

        bitmap ??= SKBitmap.Decode(path);
        if (bitmap is null)
            return null;

        bitmap = OrientedBitmap.ApplyOrigin(bitmap, origin);
        var measured = bitmap.Width / (float)originalWidth;
        if (measured > 0 && !float.IsNaN(measured) && !float.IsInfinity(measured))
            pixelScale = measured;

        return bitmap;
    }

    private static SpanCanvas Scaled(SpanCanvas canvas, float scale)
    {
        if (scale >= 0.999f)
            return canvas;

        var rectangles = canvas.Rectangles.Select(rectangle => new DestinationRect(
            rectangle.Connector,
            Scale(rectangle.Left, scale),
            Scale(rectangle.Top, scale),
            Scale(rectangle.Right, scale),
            Scale(rectangle.Bottom, scale))).ToList();
        return new SpanCanvas(Math.Max(1, Scale(canvas.Width, scale)), Math.Max(1, Scale(canvas.Height, scale)), canvas.Scale, rectangles);
    }

    private static int Scale(int value, float scale) =>
        (int)Math.Round(value * scale, MidpointRounding.AwayFromZero);

    private static byte[]? SlicePng(SKBitmap bitmap, DestinationRect rectangle)
    {
        using var slice = new SKBitmap(rectangle.Width, rectangle.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(slice))
        {
            canvas.Clear(SKColors.Black);
            canvas.DrawBitmap(
                bitmap,
                new SKRect(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom),
                new SKRect(0, 0, rectangle.Width, rectangle.Height));
        }

        using var image = SKImage.FromBitmap(slice);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data?.ToArray();
    }
}
