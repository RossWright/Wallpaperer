using Wallpaperer.Models;
using Wallpaperer.Services;
using Wallpaperer.ViewModels;

namespace Wallpaperer.Tests;

public class MonitorSelectionTests
{
    [Fact]
    public void Ends_of_a_row_include_the_monitors_between_them()
    {
        var filled = MonitorSelection.Fill(Desk(), ["DP-4", "DP-5"]);

        Assert.Equal(["DP-4", "DP-3", "DP-5"], filled);
    }

    [Fact]
    public void A_vertical_pair_stays_a_pair()
    {
        var filled = MonitorSelection.Fill(Desk(), ["HDMI-2", "DP-3"]);

        Assert.Equal(["HDMI-2", "DP-3"], filled);
    }

    [Fact]
    public void A_single_click_selects_only_that_monitor()
    {
        Assert.Equal(["HDMI-2"], MonitorSelection.Fill(Desk(), ["HDMI-2"]));
    }

    [Fact]
    public void A_diagonal_pair_includes_the_monitor_inside_the_box()
    {
        var filled = MonitorSelection.Fill(Desk(), ["HDMI-2", "DP-4"]);

        Assert.Equal(["HDMI-2", "DP-4", "DP-3"], filled);
    }

    [Fact]
    public void Dragging_from_end_to_end_highlights_the_filled_row()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wallpaperer-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        try
        {
            var viewModel = new MainWindowViewModel(
                new MutterMonitorLayoutSource(new IdleProcessRunner()),
                new GnomeWallpaperApplier(new IdleProcessRunner()),
                new ThumbnailCache(directory),
                new UiSettingsStore(Path.Combine(directory, "ui-settings.json"), null));
            foreach (var device in Desk())
                viewModel.Monitors.Add(new MonitorViewModel(device, _ => { }));

            viewModel.SelectRange("DP-4", "DP-5");

            Assert.False(viewModel.Monitors.Single(monitor => monitor.Device.Connector == "HDMI-2").IsSelected);
            Assert.True(viewModel.Monitors.Single(monitor => monitor.Device.Connector == "DP-4").IsSelected);
            Assert.True(viewModel.Monitors.Single(monitor => monitor.Device.Connector == "DP-3").IsSelected);
            Assert.True(viewModel.Monitors.Single(monitor => monitor.Device.Connector == "DP-5").IsSelected);

            viewModel.SelectOnly("HDMI-2");
            Assert.Equal(1, viewModel.Monitors.Count(monitor => monitor.IsSelected));
            Assert.True(viewModel.Monitors.Single(monitor => monitor.Device.Connector == "HDMI-2").IsSelected);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void A_monitor_outside_the_box_stays_out()
    {
        var monitors = Desk().ToList();
        monitors.Add(Device("FAR", 11620, 2160, 3840, 2160));

        var filled = MonitorSelection.Fill(monitors, ["DP-4", "DP-5"]);

        Assert.Equal(["DP-4", "DP-3", "DP-5"], filled);
        Assert.DoesNotContain("FAR", filled);
    }

    private static List<MonitorDevice> Desk() =>
    [
        Device("HDMI-2", 3840, 0, 3840, 2160),
        Device("DP-4", 0, 2160, 3840, 2160),
        Device("DP-3", 3840, 2160, 3840, 2160),
        Device("DP-5", 7680, 2160, 3840, 2160),
    ];

    private static MonitorDevice Device(string connector, int x, int y, int width, int height) =>
        new(connector, connector, x, y, width, height, 1, connector == "DP-3", width, height);

    private sealed class IdleProcessRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessResult(0, "", ""));
    }
}
