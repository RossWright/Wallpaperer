using Wallpaperer.Models;

namespace Wallpaperer.Services;

public static class MonitorSelection
{
    public static IReadOnlyList<string> Fill(IReadOnlyList<MonitorDevice> monitors, IReadOnlyList<string> seeds)
    {
        var chosen = monitors.Where(monitor => seeds.Contains(monitor.Connector)).ToList();
        if (chosen.Count == 0)
            return [];

        var left = chosen.Min(monitor => monitor.X);
        var top = chosen.Min(monitor => monitor.Y);
        var right = chosen.Max(monitor => monitor.X + monitor.Width);
        var bottom = chosen.Max(monitor => monitor.Y + monitor.Height);
        return monitors
            .Where(monitor => Overlaps(monitor, left, top, right, bottom))
            .Select(monitor => monitor.Connector)
            .ToList();
    }

    private static bool Overlaps(MonitorDevice monitor, int left, int top, int right, int bottom)
    {
        var width = Math.Min(monitor.X + monitor.Width, right) - Math.Max(monitor.X, left);
        var height = Math.Min(monitor.Y + monitor.Height, bottom) - Math.Max(monitor.Y, top);
        return width > 0 && height > 0;
    }
}
