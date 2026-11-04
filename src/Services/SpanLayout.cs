using Wallpaperer.Models;

namespace Wallpaperer.Services;

public readonly record struct CoverPlacement(float Scale, float X, float Y, float ScaledWidth, float ScaledHeight);

public sealed record DestinationRect(string Connector, int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

public sealed record SpanCanvas(int Width, int Height, double Scale, IReadOnlyList<DestinationRect> Rectangles);

public readonly record struct ArrangedMonitor(string Connector, double X, double Y, double Width, double Height);

public static class SpanLayout
{
    public static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    public static bool SwapsAxes(int transform) => transform is 1 or 3 or 5 or 7;

    public static (int Width, int Height) LogicalSize(int modeWidth, int modeHeight, double scale, int transform)
    {
        if (scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale));

        var width = modeWidth / scale;
        var height = modeHeight / scale;
        if (SwapsAxes(transform))
            (width, height) = (height, width);

        return (Round(width), Round(height));
    }

    public static CoverPlacement Cover(int sourceWidth, int sourceHeight, int destX, int destY, int destWidth, int destHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || destWidth <= 0 || destHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceWidth));

        var scale = Math.Max(destWidth / (float)sourceWidth, destHeight / (float)sourceHeight);
        return Place(scale, sourceWidth, sourceHeight, destX, destY, destWidth, destHeight);
    }

    public static CoverPlacement Contain(int sourceWidth, int sourceHeight, int destX, int destY, int destWidth, int destHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || destWidth <= 0 || destHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceWidth));

        var scale = Math.Min(destWidth / (float)sourceWidth, destHeight / (float)sourceHeight);
        return Place(scale, sourceWidth, sourceHeight, destX, destY, destWidth, destHeight);
    }

    public static CoverPlacement Center(int sourceWidth, int sourceHeight, int destX, int destY, int destWidth, int destHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || destWidth <= 0 || destHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceWidth));

        return new CoverPlacement(
            1,
            destX + (destWidth - sourceWidth) / 2f,
            destY + (destHeight - sourceHeight) / 2f,
            sourceWidth,
            sourceHeight);
    }

    private static CoverPlacement Place(
        float scale,
        int sourceWidth,
        int sourceHeight,
        int destX,
        int destY,
        int destWidth,
        int destHeight)
    {
        var scaledWidth = sourceWidth * scale;
        var scaledHeight = sourceHeight * scale;
        var x = destX + (destWidth - scaledWidth) / 2f;
        var y = destY + (destHeight - scaledHeight) / 2f;
        return new CoverPlacement(scale, x, y, scaledWidth, scaledHeight);
    }

    public static SpanCanvas Measure(IReadOnlyList<MonitorDevice> monitors)
    {
        if (monitors.Count == 0)
            throw new ArgumentException("At least one monitor is required.", nameof(monitors));

        var scale = monitors.Max(monitor => monitor.Scale);
        if (scale <= 0)
            scale = 1;

        var minX = monitors.Min(monitor => monitor.X);
        var minY = monitors.Min(monitor => monitor.Y);
        var maxX = monitors.Max(monitor => monitor.X + monitor.Width);
        var maxY = monitors.Max(monitor => monitor.Y + monitor.Height);
        var rectangles = monitors.Select(monitor => new DestinationRect(
            monitor.Connector,
            Round((monitor.X - minX) * scale),
            Round((monitor.Y - minY) * scale),
            Round((monitor.X + monitor.Width - minX) * scale),
            Round((monitor.Y + monitor.Height - minY) * scale))).ToList();

        return new SpanCanvas(Round((maxX - minX) * scale), Round((maxY - minY) * scale), scale, rectangles);
    }
}

public static class MonitorArrangementLayout
{
    public static IReadOnlyList<ArrangedMonitor> Arrange(
        IReadOnlyList<MonitorDevice> monitors,
        double panelWidth,
        double panelHeight,
        double margin)
    {
        if (monitors.Count == 0 || panelWidth <= margin * 2 || panelHeight <= margin * 2)
            return [];

        var minX = monitors.Min(monitor => monitor.X);
        var minY = monitors.Min(monitor => monitor.Y);
        var maxX = monitors.Max(monitor => monitor.X + monitor.Width);
        var maxY = monitors.Max(monitor => monitor.Y + monitor.Height);
        var boundsWidth = Math.Max(1, maxX - minX);
        var boundsHeight = Math.Max(1, maxY - minY);
        var availableWidth = panelWidth - margin * 2;
        var availableHeight = panelHeight - margin * 2;
        var scale = Math.Min(availableWidth / boundsWidth, availableHeight / boundsHeight);
        var usedWidth = boundsWidth * scale;
        var usedHeight = boundsHeight * scale;
        var offsetX = margin + (availableWidth - usedWidth) / 2;
        var offsetY = margin + (availableHeight - usedHeight) / 2;

        return monitors.Select(monitor => new ArrangedMonitor(
            monitor.Connector,
            offsetX + (monitor.X - minX) * scale,
            offsetY + (monitor.Y - minY) * scale,
            Math.Max(1, monitor.Width * scale),
            Math.Max(1, monitor.Height * scale))).ToList();
    }
}
