using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wallpaperer.Models;
using Wallpaperer.Services;

namespace Wallpaperer.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly MutterMonitorLayoutSource _monitors;
    private readonly GnomeWallpaperApplier _applier;
    private readonly ThumbnailCache _thumbnails;
    private readonly UiSettingsStore _settings;
    private CancellationTokenSource? _scanCancellation;
    private CancellationTokenSource? _applyCancellation;
    private int _layoutGeneration;
    private bool _shuttingDown;
    private bool _applyingFilterSelection;
    private bool _scanning;
    private bool _hasPublished;
    private string? _savedFilterId;
    private IReadOnlyList<PictureItemViewModel> _allPictures = [];

    public MainWindowViewModel(
        MutterMonitorLayoutSource monitors,
        GnomeWallpaperApplier applier,
        ThumbnailCache thumbnails,
        UiSettingsStore settings)
    {
        _monitors = monitors;
        _applier = applier;
        _thumbnails = thumbnails;
        _settings = settings;
        _savedFilterId = settings.SizeFilterId;
        Monitors.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMonitors));
    }

    public ThumbnailCache Thumbnails => _thumbnails;

    public ObservableCollection<MonitorViewModel> Monitors { get; } = [];

    public bool HasMonitors => Monitors.Count > 0;

    [ObservableProperty]
    private IReadOnlyList<PictureItemViewModel> pictures = [];

    [ObservableProperty]
    private IReadOnlyList<SizeFilterOption> sizeFilters = [];

    [ObservableProperty]
    private SizeFilterOption? selectedSizeFilter;

    [ObservableProperty]
    private string status = "Scanning…";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLayoutWarning))]
    private string? layoutWarning;

    public bool HasLayoutWarning => !string.IsNullOrWhiteSpace(LayoutWarning);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool isApplying;

    [ObservableProperty]
    private bool isSettingWallpaper;

    public event EventHandler? ApplyFinished;

    public async Task StartAsync(Screens screens)
    {
        _scanCancellation = new CancellationTokenSource();
        await ReloadMonitorsAsync(screens);
        await ScanAsync(_scanCancellation.Token);
    }

    public Task ReloadMonitorsAsync(Screens? screens)
    {
        var generation = Interlocked.Increment(ref _layoutGeneration);
        return LoadMonitorsAsync(screens, generation);
    }

    public void Shutdown()
    {
        _shuttingDown = true;
        _scanCancellation?.Cancel();
        if (!IsSettingWallpaper)
            _applyCancellation?.Cancel();
    }

    public void ArrangeMonitors(double panelWidth, double panelHeight)
    {
        var arranged = MonitorArrangementLayout.Arrange(
            Monitors.Select(monitor => monitor.Device).ToList(),
            panelWidth,
            panelHeight,
            margin: 16);
        var byConnector = arranged.ToDictionary(monitor => monitor.Connector);
        foreach (var monitor in Monitors)
        {
            if (byConnector.TryGetValue(monitor.Device.Connector, out var rect))
                monitor.SetViewRect(rect.X, rect.Y, rect.Width, rect.Height);
        }
    }

    public async Task AssignAsync(MonitorViewModel monitor, string path)
    {
        if (!PictureLibrary.IsSupportedImage(path))
        {
            Status = $"Ignored {Path.GetFileName(path)}.";
            return;
        }

        if (!File.Exists(path))
        {
            Status = $"Could not read {path}.";
            return;
        }

        var previewPath = await _thumbnails.GetJpegAsync(path, 1024, CancellationToken.None);
        if (previewPath is null)
        {
            Status = $"Could not read {path}.";
            return;
        }

        Bitmap bitmap;
        try
        {
            await using var stream = File.OpenRead(previewPath);
            bitmap = new Bitmap(stream);
        }
        catch (Exception)
        {
            Status = $"Could not read {path}.";
            return;
        }

        monitor.AssignedPath = path;
        monitor.SetPreview(bitmap);
        Status = $"Assigned {Path.GetFileName(path)} to {monitor.Device.Connector}.";
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        _applyCancellation = new CancellationTokenSource();
        var cancellation = _applyCancellation.Token;
        IsApplying = true;
        try
        {
            Status = "Composing wallpaper…";
            var devices = Monitors.Select(monitor => monitor.Device).ToList();
            var images = Monitors
                .Where(monitor => monitor.AssignedPath is not null)
                .ToDictionary(monitor => monitor.Device.Connector, monitor => monitor.AssignedPath!);
            var directory = WallpaperComposer.DefaultDirectory();

            var path = await Task.Run(() =>
            {
                using var bitmap = WallpaperComposer.Render(devices, images, cancellation);
                cancellation.ThrowIfCancellationRequested();
                return WallpaperComposer.SaveJpeg(bitmap, directory);
            }, cancellation);

            IsSettingWallpaper = true;
            Status = "Setting wallpaper…";
            await _applier.ApplyAsync(path, CancellationToken.None);
            Status = $"Applied {path}";
        }
        catch (OperationCanceledException)
        {
            Status = "Apply cancelled.";
        }
        catch (Exception exception)
        {
            Status = exception.Message;
        }
        finally
        {
            IsApplying = false;
            IsSettingWallpaper = false;
            ApplyFinished?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool CanApply() => !IsApplying && Monitors.Any(monitor => monitor.HasImage);

    private async Task LoadMonitorsAsync(Screens? screens, int generation)
    {
        IReadOnlyList<MonitorDevice> devices;
        string? warning = null;
        try
        {
            devices = await _monitors.LoadAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            Trace.WriteLine("Mutter layout failed: " + exception.Message);
            devices = screens is null ? [] : AvaloniaMonitorLayoutSource.FromScreens(screens);
            if (devices.Count > 0)
                warning = "Monitor layout came from Avalonia and may not match GNOME.";
        }

        if (generation != _layoutGeneration || _shuttingDown)
            return;

        var previous = Monitors.ToDictionary(monitor => monitor.Device.Connector);
        var next = new List<MonitorViewModel>();
        foreach (var device in devices)
        {
            if (previous.TryGetValue(device.Connector, out var existing))
            {
                existing.UpdateDevice(device);
                next.Add(existing);
                previous.Remove(device.Connector);
            }
            else
            {
                next.Add(new MonitorViewModel(device, OnMonitorChanged));
            }
        }

        foreach (var removed in previous.Values)
            removed.DisposePreview();

        Monitors.Clear();
        foreach (var monitor in next)
            Monitors.Add(monitor);

        LayoutWarning = warning;
        if (devices.Count == 0)
            Status = "No monitors detected.";

        RefreshSizeFilters();
        ApplyCommand.NotifyCanExecuteChanged();
    }

    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var found = new List<PictureItemViewModel>();
        var lastPublish = Stopwatch.StartNew();
        try
        {
            await Task.Run(() =>
            {
                foreach (var path in PictureLibrary.Enumerate(home, cancellationToken))
                {
                    DateTime written;
                    try
                    {
                        written = File.GetLastWriteTimeUtc(path);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        continue;
                    }

                    var pixels = ImageDimensions.TryRead(path);
                    found.Add(new PictureItemViewModel(new PictureRecord(path, written, pixels?.Width, pixels?.Height)));
                    if (found.Count == 1 || lastPublish.ElapsedMilliseconds >= 200)
                    {
                        Publish(found, scanning: true);
                        lastPublish.Restart();
                    }
                }
            }, cancellationToken);

            Publish(found, scanning: false);
        }
        catch (OperationCanceledException)
        {
            // The window closed.
        }
        catch (Exception exception)
        {
            if (!_shuttingDown)
                Status = exception.Message;
        }
    }

    private void Publish(List<PictureItemViewModel> found, bool scanning)
    {
        var snapshot = found.ToArray();
        Array.Sort(snapshot, static (left, right) => PictureRecord.CompareNewest(left.Record, right.Record));
        IReadOnlyList<PictureItemViewModel> published = snapshot;
        Dispatcher.UIThread.Post(() =>
        {
            if (_shuttingDown)
                return;

            _allPictures = published;
            _scanning = scanning;
            _hasPublished = true;
            ApplyFilter();
        });
    }

    private void RefreshSizeFilters()
    {
        var devices = Monitors.Select(monitor => monitor.Device).ToList();
        var options = SizeFilterCatalog.Build(devices);
        var selected = SizeFilterCatalog.Select(options, _savedFilterId, devices);
        _applyingFilterSelection = true;
        SizeFilters = options;
        SelectedSizeFilter = selected;
        _applyingFilterSelection = false;
    }

    partial void OnSelectedSizeFilterChanged(SizeFilterOption? value)
    {
        if (_applyingFilterSelection)
        {
            ApplyFilter();
            return;
        }

        if (value is null)
        {
            if (SizeFilters.Count == 0)
                return;

            var devices = Monitors.Select(monitor => monitor.Device).ToList();
            _applyingFilterSelection = true;
            SelectedSizeFilter = SizeFilterCatalog.Select(SizeFilters, _savedFilterId, devices);
            _applyingFilterSelection = false;
            return;
        }

        _savedFilterId = value.Id;
        _settings.SizeFilterId = value.Id;
        _settings.Save();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filter = SelectedSizeFilter;
        PictureItemViewModel[] filtered = filter is null
            ? _allPictures.ToArray()
            : _allPictures.Where(picture => filter.Matches(picture.Record.PixelWidth, picture.Record.PixelHeight)).ToArray();
        Pictures = filtered;
        if (!_hasPublished)
            return;

        var total = _allPictures.Count;
        var shown = filtered.Length;
        var narrowed = filter is not null && filter.Id != SizeFilterCatalog.AnyId;
        if (_scanning)
        {
            Status = narrowed ? $"Scanning… {shown} of {total} images" : $"Scanning… {total} images";
            return;
        }

        if (total == 0)
            Status = "No images found under the home folder.";
        else if (!narrowed)
            Status = $"{total} images";
        else if (shown == 0)
            Status = $"No images match {filter!.Label}.";
        else
            Status = $"{shown} of {total} images";
    }

    private void OnMonitorChanged(MonitorViewModel monitor)
    {
        if (monitor.AssignedPath is null)
            Status = $"Cleared {monitor.Device.Connector}.";

        ApplyCommand.NotifyCanExecuteChanged();
    }
}
