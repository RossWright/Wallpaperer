using SkiaSharp;
using Wallpaperer.Models;

namespace Wallpaperer.Services;

public static class SpanPreview
{
    public static byte[][]? PngSlices(
        string orientedImagePath,
        IReadOnlyList<MonitorDevice> monitors,
        IReadOnlyList<string> connectors)
    {
        if (connectors.Count == 0)
            return null;

        List<DestinationRect> members;
        try
        {
            var canvas = SpanLayout.Measure(monitors);
            var byConnector = canvas.Rectangles.ToDictionary(rectangle => rectangle.Connector);
            members = [];
            foreach (var connector in connectors)
            {
                if (!byConnector.TryGetValue(connector, out var rectangle))
                    return null;
                members.Add(rectangle);
            }
        }
        catch (ArgumentException)
        {
            return null;
        }

        using var source = SKBitmap.Decode(orientedImagePath);
        if (source is null || source.Width <= 0 || source.Height <= 0)
            return null;

        var left = members.Min(rectangle => rectangle.Left);
        var top = members.Min(rectangle => rectangle.Top);
        var right = members.Max(rectangle => rectangle.Right);
        var bottom = members.Max(rectangle => rectangle.Bottom);
        if (right <= left || bottom <= top)
            return null;

        var cover = SpanLayout.Cover(source.Width, source.Height, left, top, right - left, bottom - top);
        var slices = new byte[members.Count][];
        for (var index = 0; index < members.Count; index++)
        {
            var png = Slice(source, cover, members[index]);
            if (png is null)
                return null;
            slices[index] = png;
        }

        return slices;
    }

    private static byte[]? Slice(SKBitmap source, CoverPlacement cover, DestinationRect member)
    {
        var srcLeft = (member.Left - cover.X) / cover.Scale;
        var srcTop = (member.Top - cover.Y) / cover.Scale;
        var srcRight = (member.Right - cover.X) / cover.Scale;
        var srcBottom = (member.Bottom - cover.Y) / cover.Scale;
        var width = Math.Max(1, (int)Math.Round(srcRight - srcLeft));
        var height = Math.Max(1, (int)Math.Round(srcBottom - srcTop));
        using var slice = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(slice))
        {
            canvas.Clear(SKColors.Black);
            using var image = SKImage.FromBitmap(source);
            canvas.DrawImage(
                image,
                new SKRect(srcLeft, srcTop, srcRight, srcBottom),
                new SKRect(0, 0, width, height),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        }

        using var encoded = SKImage.FromBitmap(slice);
        using var data = encoded.Encode(SKEncodedImageFormat.Png, 100);
        return data?.ToArray();
    }
}
