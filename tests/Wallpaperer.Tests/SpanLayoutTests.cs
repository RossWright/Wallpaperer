using Wallpaperer.Models;
using Wallpaperer.Services;

namespace Wallpaperer.Tests;

public class SpanLayoutTests
{
    [Fact]
    public void Logical_size_divides_by_scale_and_swaps_rotated_axes()
    {
        Assert.Equal((1920, 1080), SpanLayout.LogicalSize(3840, 2160, 2, 0));
        Assert.Equal((2160, 3840), SpanLayout.LogicalSize(3840, 2160, 1, 1));
        Assert.Equal((2160, 3840), SpanLayout.LogicalSize(3840, 2160, 1, 5));
        Assert.Equal((3, 3), SpanLayout.LogicalSize(5, 5, 2, 0));
    }

    [Fact]
    public void Cover_centers_and_crops_the_overflow_equally()
    {
        var wide = SpanLayout.Cover(200, 100, 0, 0, 100, 100);
        Assert.Equal(1f, wide.Scale);
        Assert.Equal(-50f, wide.X);
        Assert.Equal(0f, wide.Y);
        Assert.Equal(50f, -wide.X);
        Assert.Equal(50f, wide.ScaledWidth - 100 - (-wide.X));

        var tall = SpanLayout.Cover(100, 200, 10, 20, 100, 100);
        Assert.Equal(1f, tall.Scale);
        Assert.Equal(10f, tall.X);
        Assert.Equal(-30f, tall.Y);
        Assert.Equal(50f, 20 - tall.Y);
        Assert.Equal(50f, tall.ScaledHeight - 100 - (20 - tall.Y));
    }

    [Fact]
    public void Negative_origins_are_normalized_and_shared_edges_stay_shared()
    {
        var shifted = SpanLayout.Measure(
        [
            Device("A", -40, -20, 80, 40, 1),
            Device("B", 50, 10, 30, 20, 1),
        ]);
        Assert.Equal(120, shifted.Width);
        Assert.Equal(50, shifted.Height);
        var left = Rect(shifted, "A");
        var right = Rect(shifted, "B");
        Assert.Equal((0, 0, 80, 40), (left.Left, left.Top, left.Right, left.Bottom));
        Assert.Equal((90, 30, 120, 50), (right.Left, right.Top, right.Right, right.Bottom));

        var scaled = SpanLayout.Measure(
        [
            Device("A", 0, 0, 100, 100, 1),
            Device("B", 100, 0, 100, 100, 1.5),
        ]);
        Assert.Equal(300, scaled.Width);
        Assert.Equal(150, scaled.Height);
        Assert.Equal(Rect(scaled, "A").Right, Rect(scaled, "B").Left);
    }

    [Fact]
    public void Arrangement_keeps_relative_positions()
    {
        var arranged = MonitorArrangementLayout.Arrange(
        [
            Device("HDMI-2", 3840, 0, 3840, 2160, 1),
            Device("DP-4", 0, 2160, 3840, 2160, 1),
            Device("DP-3", 3840, 2160, 3840, 2160, 1),
            Device("DP-5", 7680, 2160, 3840, 2160, 1),
        ], 1000, 400, 0);

        var byConnector = arranged.ToDictionary(monitor => monitor.Connector);
        Assert.True(byConnector["HDMI-2"].Y < byConnector["DP-3"].Y);
        Assert.True(byConnector["HDMI-2"].X > byConnector["DP-4"].X);
        Assert.Equal(byConnector["DP-4"].Width, byConnector["HDMI-2"].Width, precision: 3);
        Assert.True(byConnector["DP-4"].X < byConnector["DP-5"].X);
    }

    private static MonitorDevice Device(string connector, int x, int y, int width, int height, double scale) =>
        new(connector, connector, x, y, width, height, scale, false, width, height);

    private static DestinationRect Rect(SpanCanvas canvas, string connector) =>
        canvas.Rectangles.Single(rectangle => rectangle.Connector == connector);
}
