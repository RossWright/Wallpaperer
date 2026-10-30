using SkiaSharp;

namespace Wallpaperer.Services;

internal static class OrientedBitmap
{
    public static SKBitmap? Decode(string path, int? maxEdge)
    {
        try
        {
            using var codec = SKCodec.Create(path);
            if (codec is null)
                return null;

            var origin = codec.EncodedOrigin;
            SKBitmap? bitmap = null;
            var longest = Math.Max(codec.Info.Width, codec.Info.Height);
            if (maxEdge is int edge && edge > 0 && longest > edge)
            {
                var scale = edge / (float)longest;
                var dimensions = codec.GetScaledDimensions(scale);
                var info = new SKImageInfo(
                    Math.Max(1, dimensions.Width),
                    Math.Max(1, dimensions.Height),
                    SKColorType.Rgba8888,
                    SKAlphaType.Premul);
                bitmap = SKBitmap.Decode(codec, info);
            }

            bitmap ??= SKBitmap.Decode(path);
            if (bitmap is null)
                return null;

            if (maxEdge is int limit && Math.Max(bitmap.Width, bitmap.Height) > limit)
            {
                var scale = limit / (float)Math.Max(bitmap.Width, bitmap.Height);
                var resized = bitmap.Resize(
                    new SKSizeI(
                        Math.Max(1, (int)Math.Round(bitmap.Width * scale)),
                        Math.Max(1, (int)Math.Round(bitmap.Height * scale))),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                bitmap.Dispose();
                if (resized is null)
                    return null;
                bitmap = resized;
            }

            return ApplyOrigin(bitmap, origin);
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static SKBitmap ApplyOrigin(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default)
            return source;

        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var width = swap ? source.Height : source.Width;
        var height = swap ? source.Width : source.Height;
        var oriented = new SKBitmap(width, height, source.ColorType, source.AlphaType);
        using (var canvas = new SKCanvas(oriented))
        {
            switch (origin)
            {
                case SKEncodedOrigin.TopRight:
                    canvas.Translate(width, 0);
                    canvas.Scale(-1, 1);
                    break;
                case SKEncodedOrigin.BottomRight:
                    canvas.Translate(width, height);
                    canvas.RotateDegrees(180);
                    break;
                case SKEncodedOrigin.BottomLeft:
                    canvas.Translate(0, height);
                    canvas.Scale(1, -1);
                    break;
                case SKEncodedOrigin.RightTop:
                    canvas.Translate(width, 0);
                    canvas.RotateDegrees(90);
                    break;
                case SKEncodedOrigin.LeftBottom:
                    canvas.Translate(0, height);
                    canvas.RotateDegrees(270);
                    break;
                case SKEncodedOrigin.LeftTop:
                    canvas.Translate(0, height);
                    canvas.RotateDegrees(270);
                    canvas.Translate(source.Width, 0);
                    canvas.Scale(-1, 1);
                    break;
                case SKEncodedOrigin.RightBottom:
                    canvas.Translate(width, 0);
                    canvas.RotateDegrees(90);
                    canvas.Translate(source.Width, 0);
                    canvas.Scale(-1, 1);
                    break;
            }

            canvas.DrawBitmap(source, 0, 0);
        }

        source.Dispose();
        return oriented;
    }
}
