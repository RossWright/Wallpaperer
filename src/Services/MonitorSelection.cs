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
            .Where(monitor => CenterIsInside(monitor, left, top, right, bottom))
            .Select(monitor => monitor.Connector)
            .ToList();
    }

    private static bool CenterIsInside(MonitorDevice monitor, int left, int top, int right, int bottom)
    {
        var centerX = monitor.X + monitor.Width / 2.0;
        var centerY = monitor.Y + monitor.Height / 2.0;
        return centerX >= left && centerX <= right && centerY >= top && centerY <= bottom;
    }
}
