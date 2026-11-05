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
                new UiSettingsStore(Path.Combine(directory, "ui-settings.json"), null),
                EmptyCurrentWallpaperSource.Instance,
                wallpaperDirectory: directory);
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

            viewModel.ClearSelection();
            Assert.Equal(0, viewModel.Monitors.Count(monitor => monitor.IsSelected));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void A_drop_on_the_selection_uses_every_selected_monitor()
    {
        var monitors = Desk().Select(device => new MonitorViewModel(device, _ => { })).ToList();
        foreach (var monitor in monitors.Where(monitor => monitor.Device.Connector != "HDMI-2"))
            monitor.IsSelected = true;

        var spanned = MainWindowViewModel.DropTargets(monitors, monitors.Single(monitor => monitor.Device.Connector == "DP-5"));
        Assert.Equal(["DP-4", "DP-3", "DP-5"], spanned.Select(monitor => monitor.Device.Connector));

        var outside = MainWindowViewModel.DropTargets(monitors, monitors[0]);
        Assert.Equal("HDMI-2", Assert.Single(outside).Device.Connector);
        Assert.True(monitors.Single(monitor => monitor.Device.Connector == "DP-4").IsSelected);
        Assert.False(monitors[0].IsSelected);
    }

    [Fact]
    public void Auto_apply_disables_the_apply_command()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wallpaperer-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        try
        {
            var viewModel = new MainWindowViewModel(
                new MutterMonitorLayoutSource(new IdleProcessRunner()),
                new GnomeWallpaperApplier(new IdleProcessRunner()),
                new ThumbnailCache(directory),
                new UiSettingsStore(Path.Combine(directory, "ui-settings.json"), null),
                EmptyCurrentWallpaperSource.Instance,
                wallpaperDirectory: directory);
            var monitor = new MonitorViewModel(new MonitorDevice("DP-3", "DP-3", 0, 0, 100, 100, 1, true, 100, 100), _ => { });
            viewModel.Monitors.Add(monitor);
            monitor.AssignedPath = Path.Combine(directory, "missing.jpg");

            Assert.True(viewModel.ApplyCommand.CanExecute(null));
            viewModel.AutoApply = true;
            Assert.False(viewModel.ApplyCommand.CanExecute(null));
            viewModel.Shutdown();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Ctrl_click_can_select_every_monitor_on_the_desk()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wallpaperer-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        try
        {
            var viewModel = new MainWindowViewModel(
                new MutterMonitorLayoutSource(new IdleProcessRunner()),
                new GnomeWallpaperApplier(new IdleProcessRunner()),
                new ThumbnailCache(directory),
                new UiSettingsStore(Path.Combine(directory, "ui-settings.json"), null),
                EmptyCurrentWallpaperSource.Instance,
                wallpaperDirectory: directory);
            var desk = Desk();
            foreach (var device in desk)
                viewModel.Monitors.Add(new MonitorViewModel(device, _ => { }));

            viewModel.SizeFilters = SizeFilterCatalog.Build(desk);
            viewModel.SelectOnly("DP-4");
            viewModel.ExtendSelection("HDMI-2");
            viewModel.ExtendSelection("DP-5");

            Assert.Equal(4, viewModel.Monitors.Count(monitor => monitor.IsSelected));
            Assert.Equal("11520x4320", viewModel.SelectedSizeFilter?.Id);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void A_two_monitor_span_selects_the_7680_filter()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wallpaperer-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        try
        {
            var viewModel = new MainWindowViewModel(
                new MutterMonitorLayoutSource(new IdleProcessRunner()),
                new GnomeWallpaperApplier(new IdleProcessRunner()),
                new ThumbnailCache(directory),
                new UiSettingsStore(Path.Combine(directory, "ui-settings.json"), null),
                EmptyCurrentWallpaperSource.Instance,
                wallpaperDirectory: directory);
            foreach (var device in Desk())
                viewModel.Monitors.Add(new MonitorViewModel(device, _ => { }));

            viewModel.SizeFilters = SizeFilterCatalog.Build(Desk());
            viewModel.SelectOnly("DP-4");
            Assert.Equal("3840x2160", viewModel.SelectedSizeFilter?.Id);

            viewModel.ExtendSelection("DP-3");
            Assert.Equal("2-wide-7680x2160", viewModel.SelectedSizeFilter?.Id);
            Assert.True(viewModel.Monitors.Single(monitor => monitor.Device.Connector == "DP-4").IsSelected);
            Assert.True(viewModel.Monitors.Single(monitor => monitor.Device.Connector == "DP-3").IsSelected);
            Assert.False(viewModel.Monitors.Single(monitor => monitor.Device.Connector == "HDMI-2").IsSelected);
            Assert.False(viewModel.Monitors.Single(monitor => monitor.Device.Connector == "DP-5").IsSelected);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task An_available_update_shows_the_status_button()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wallpaperer-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        try
        {
            var updates = new ReadyUpdate();
            var viewModel = new MainWindowViewModel(
                new MutterMonitorLayoutSource(new IdleProcessRunner()),
                new GnomeWallpaperApplier(new IdleProcessRunner()),
                new ThumbnailCache(directory),
                new UiSettingsStore(Path.Combine(directory, "ui-settings.json"), null),
                EmptyCurrentWallpaperSource.Instance,
                updates,
                directory);

            Assert.False(viewModel.UpdateAvailable);
            await viewModel.CheckForUpdateAsync();
            Assert.True(viewModel.UpdateAvailable);
            Assert.Equal("Update available", viewModel.UpdateButtonLabel);

            await viewModel.UpdateCommand.ExecuteAsync(null);
            Assert.True(updates.Applied);
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

    private sealed class ReadyUpdate : IUpdateCheck
    {
        public bool Applied { get; private set; }

        public Task<bool> CheckAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task ApplyAsync(CancellationToken cancellationToken)
        {
            Applied = true;
            return Task.CompletedTask;
        }
    }

    private sealed class IdleProcessRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessResult(0, "", ""));
    }
}
