using SkiaSharp;

namespace Wallpaperer.Services;

public static class ImageDimensions
{
    public static (int Width, int Height)? TryRead(string path)
    {
        try
        {
            using var codec = SKCodec.Create(path);
            if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
                return null;

            var width = codec.Info.Width;
            var height = codec.Info.Height;
            if (SwapsAxes(codec.EncodedOrigin))
                (width, height) = (height, width);

            return (width, height);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool SwapsAxes(SKEncodedOrigin origin) =>
        origin is SKEncodedOrigin.LeftTop
            or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom
            or SKEncodedOrigin.LeftBottom;
}
