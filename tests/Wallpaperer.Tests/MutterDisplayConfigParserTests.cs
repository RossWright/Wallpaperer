using Wallpaperer.Services;

namespace Wallpaperer.Tests;

public class MutterDisplayConfigParserTests
{
    [Fact]
    public void Parses_the_captured_four_monitor_layout()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "mutter-current.json");
        var devices = MutterDisplayConfigParser.Parse(File.ReadAllText(path));
        var byConnector = devices.ToDictionary(device => device.Connector);

        Assert.Equal(4, devices.Count);
        Assert.Equal((3840, 0, 3840, 2160), Box(byConnector["HDMI-2"]));
        Assert.Equal((0, 2160, 3840, 2160), Box(byConnector["DP-4"]));
        Assert.Equal((3840, 2160, 3840, 2160), Box(byConnector["DP-3"]));
        Assert.Equal((7680, 2160, 3840, 2160), Box(byConnector["DP-5"]));
        Assert.True(byConnector["DP-3"].IsPrimary);
        Assert.False(byConnector["HDMI-2"].IsPrimary);
        Assert.Contains("Samsung", byConnector["DP-3"].DisplayName);
        Assert.All(devices, device => Assert.Equal(1, device.Scale));

        var canvas = SpanLayout.Measure(devices);
        Assert.Equal(11520, canvas.Width);
        Assert.Equal(4320, canvas.Height);
    }

    [Fact]
    public void Scale_and_rotation_come_from_the_current_mode()
    {
        var scaled = MutterDisplayConfigParser.Parse(Layout(scale: 2, transform: 0, currentAsObject: false));
        var monitor = Assert.Single(scaled);
        Assert.Equal(1920, monitor.Width);
        Assert.Equal(1080, monitor.Height);
        Assert.Equal(2, monitor.Scale);
        Assert.Equal("Test Monitor", monitor.DisplayName);

        var rotated = MutterDisplayConfigParser.Parse(Layout(scale: 1, transform: 1, currentAsObject: true));
        var portrait = Assert.Single(rotated);
        Assert.Equal(2160, portrait.Width);
        Assert.Equal(3840, portrait.Height);
    }

    private static (int X, int Y, int Width, int Height) Box(Models.MonitorDevice device) =>
        (device.X, device.Y, device.Width, device.Height);

    private static string Layout(double scale, int transform, bool currentAsObject)
    {
        var current = currentAsObject
            ? """{"is-current":{"type":"b","data":true}}"""
            : """{"is-current":true}""";
        var scaleText = scale.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return "{\"type\":\"u\",\"data\":[1,[[[\"DP-1\",\"V\",\"P\",\"S\"],[[\"3840x2160@60\",3840,2160,60.0,1.0,[1.0,2.0],"
            + current
            + "]],{\"display-name\":{\"type\":\"s\",\"data\":\"Test Monitor\"}}]],[[0,0,"
            + scaleText + "," + transform
            + ",true,[[\"DP-1\",\"V\",\"P\",\"S\"]],{}]],{}]}";
    }
}
