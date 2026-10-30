using SkiaSharp;
using Wallpaperer.Services;

namespace Wallpaperer.Tests;

public class OrientedBitmapTests
{
    [Fact]
    public void Right_rotation_turns_the_top_left_pixel_with_the_image()
    {
        using var oriented = Orient(SKEncodedOrigin.RightTop);
        Assert.Equal(1, oriented.Width);
        Assert.Equal(2, oriented.Height);
        Assert.Equal(SKColors.Red, oriented.GetPixel(0, 0));
        Assert.Equal(SKColors.Blue, oriented.GetPixel(0, 1));
    }

    [Fact]
    public void Half_turn_and_mirror_swap_the_horizontal_pixels()
    {
        using var turned = Orient(SKEncodedOrigin.BottomRight);
        Assert.Equal(SKColors.Blue, turned.GetPixel(0, 0));
        Assert.Equal(SKColors.Red, turned.GetPixel(1, 0));

        using var mirrored = Orient(SKEncodedOrigin.TopRight);
        Assert.Equal(SKColors.Blue, mirrored.GetPixel(0, 0));
        Assert.Equal(SKColors.Red, mirrored.GetPixel(1, 0));
    }

    [Fact]
    public void Left_rotation_puts_the_right_pixel_on_top()
    {
        using var oriented = Orient(SKEncodedOrigin.LeftBottom);
        Assert.Equal(1, oriented.Width);
        Assert.Equal(2, oriented.Height);
        Assert.Equal(SKColors.Blue, oriented.GetPixel(0, 0));
        Assert.Equal(SKColors.Red, oriented.GetPixel(0, 1));
    }

    private static SKBitmap Orient(SKEncodedOrigin origin)
    {
        var source = new SKBitmap(2, 1);
        source.SetPixel(0, 0, SKColors.Red);
        source.SetPixel(1, 0, SKColors.Blue);
        return OrientedBitmap.ApplyOrigin(source, origin);
    }
}
