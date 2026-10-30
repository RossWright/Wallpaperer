using Avalonia.Controls;
using Wallpaperer.Models;

namespace Wallpaperer.Services;

public sealed class MutterMonitorLayoutSource(IProcessRunner processes)
{
    public async Task<IReadOnlyList<MonitorDevice>> LoadAsync(CancellationToken cancellationToken)
    {
        var result = await processes.RunAsync(
            "busctl",
            [
                "--user",
                "--json=short",
                "call",
                "org.gnome.Mutter.DisplayConfig",
                "/org/gnome/Mutter/DisplayConfig",
                "org.gnome.Mutter.DisplayConfig",
                "GetCurrentState",
            ],
            cancellationToken);

        if (result.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? $"busctl exited with code {result.ExitCode}."
                : result.StandardError.Trim();
            throw new InvalidOperationException(detail);
        }

        return MutterDisplayConfigParser.Parse(result.StandardOutput);
    }
}

public static class AvaloniaMonitorLayoutSource
{
    public static IReadOnlyList<MonitorDevice> FromScreens(Screens screens)
    {
        var devices = new List<MonitorDevice>();
        foreach (var screen in screens.All)
        {
            var bounds = screen.Bounds;
            var displayName = string.IsNullOrWhiteSpace(screen.DisplayName) ? "Display" : screen.DisplayName;
            var scale = screen.Scaling > 0 ? screen.Scaling : 1;
            devices.Add(new MonitorDevice(
                $"{displayName}@{bounds.X},{bounds.Y}",
                displayName,
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                scale,
                screen.IsPrimary,
                bounds.Width,
                bounds.Height));
        }

        return devices;
    }
}
