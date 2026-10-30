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
    private string? _selectionAnchor;
    private int _nextSpanGroup = 1;
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

    public void SelectOnly(string connector)
    {
        _selectionAnchor = connector;
        ApplySelection(MonitorSelection.Fill(Devices(), [connector]));
    }

    public void ExtendSelection(string connector)
    {
        if (_selectionAnchor is null)
        {
            SelectOnly(connector);
            return;
        }

        ApplySelection(MonitorSelection.Fill(Devices(), [_selectionAnchor, connector]));
    }

    public void SelectRange(string fromConnector, string toConnector)
    {
        _selectionAnchor = fromConnector;
        ApplySelection(MonitorSelection.Fill(Devices(), [fromConnector, toConnector]));
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

        var selected = Monitors.Where(item => item.IsSelected).ToList();
        var targets = selected.Count > 1 && selected.Contains(monitor)
            ? (IReadOnlyList<MonitorViewModel>)selected
            : [monitor];
        if (targets.Count == 1)
            SelectOnly(monitor.Device.Connector);

        await ApplyImageAsync(path, targets);
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
            var placements = Placements();
            var directory = WallpaperComposer.DefaultDirectory();

            var path = await Task.Run(() =>
            {
                using var bitmap = WallpaperComposer.Render(devices, placements, cancellation);
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

        var seeds = Monitors.Where(monitor => monitor.IsSelected).Select(monitor => monitor.Device.Connector).ToList();
        if (seeds.Count > 0)
            ApplySelection(MonitorSelection.Fill(devices, seeds));

        LayoutWarning = warning;
        if (devices.Count == 0)
            Status = "No monitors detected.";

        RefreshSizeFilters();
        await RefreshSpanPreviewsAsync();
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
        {
            List<MonitorViewModel> remaining = [];
            string? path = null;
            if (monitor.SpanGroupId is int group)
            {
                remaining = Monitors
                    .Where(item => item != monitor && item.SpanGroupId == group && item.AssignedPath is not null)
                    .ToList();
                path = remaining.FirstOrDefault()?.AssignedPath;
                monitor.SpanGroupId = null;
            }

            Status = $"Cleared {monitor.Device.Connector}.";
            if (path is not null)
                _ = ReflowSpanAsync(remaining, path);
        }

        ApplyCommand.NotifyCanExecuteChanged();
    }

    private List<MonitorDevice> Devices() => Monitors.Select(monitor => monitor.Device).ToList();

    private void ApplySelection(IReadOnlyList<string> connectors)
    {
        var selected = connectors.ToHashSet(StringComparer.Ordinal);
        foreach (var monitor in Monitors)
            monitor.IsSelected = selected.Contains(monitor.Device.Connector);
    }

    private List<ImagePlacement> Placements()
    {
        var placements = new List<ImagePlacement>();
        var seen = new HashSet<int>();
        foreach (var monitor in Monitors)
        {
            if (monitor.AssignedPath is null)
                continue;

            if (monitor.SpanGroupId is int group)
            {
                if (!seen.Add(group))
                    continue;

                var connectors = Monitors
                    .Where(item => item.SpanGroupId == group && item.AssignedPath is not null)
                    .Select(item => item.Device.Connector)
                    .ToList();
                placements.Add(new ImagePlacement(monitor.AssignedPath, connectors));
            }
            else
            {
                placements.Add(new ImagePlacement(monitor.AssignedPath, [monitor.Device.Connector]));
            }
        }

        return placements;
    }

    private async Task ApplyImageAsync(string path, IReadOnlyList<MonitorViewModel> targets)
    {
        var previewPath = await _thumbnails.GetJpegAsync(path, 1024, CancellationToken.None);
        if (previewPath is null)
        {
            Status = $"Could not read {path}.";
            return;
        }

        var displaced = CollectDisplaced(targets);
        if (!await PaintAsync(path, previewPath, targets, targets.Count > 1))
            return;

        foreach (var (remaining, oldPath) in displaced)
            await ReflowSpanAsync(remaining, oldPath);

        var file = Path.GetFileName(path);
        Status = targets.Count == 1
            ? $"Assigned {file} to {targets[0].Device.Connector}."
            : $"Assigned {file} across {Describe(targets)}.";
        ApplyCommand.NotifyCanExecuteChanged();
    }

    private List<(List<MonitorViewModel> Remaining, string Path)> CollectDisplaced(IReadOnlyList<MonitorViewModel> targets)
    {
        var targetSet = targets.ToHashSet();
        var displaced = new List<(List<MonitorViewModel> Remaining, string Path)>();
        var groups = targets
            .Select(monitor => monitor.SpanGroupId)
            .OfType<int>()
            .Distinct();
        foreach (var group in groups)
        {
            var members = Monitors.Where(monitor => monitor.SpanGroupId == group && monitor.AssignedPath is not null).ToList();
            if (members.Count == 0 || members[0].AssignedPath is not string path)
                continue;

            var remaining = members.Where(monitor => !targetSet.Contains(monitor)).ToList();
            if (remaining.Count > 0)
                displaced.Add((remaining, path));
        }

        return displaced;
    }

    private async Task<bool> PaintAsync(
        string path,
        string previewPath,
        IReadOnlyList<MonitorViewModel> targets,
        bool span)
    {
        if (!span)
        {
            var bitmap = OpenBitmap(previewPath);
            if (bitmap is null)
            {
                Status = $"Could not read {path}.";
                return false;
            }

            targets[0].SpanGroupId = null;
            targets[0].AssignedPath = path;
            targets[0].SetPreview(bitmap);
            return true;
        }

        var connectors = targets.Select(monitor => monitor.Device.Connector).ToList();
        var devices = Devices();
        var pngs = await Task.Run(() => SpanPreview.PngSlices(previewPath, devices, connectors));
        if (pngs is null)
        {
            Status = $"Could not read {path}.";
            return false;
        }

        var bitmaps = pngs.Select(OpenPng).ToList();
        if (bitmaps.Any(bitmap => bitmap is null))
        {
            foreach (var bitmap in bitmaps)
                bitmap?.Dispose();
            Status = $"Could not read {path}.";
            return false;
        }

        var group = _nextSpanGroup++;
        for (var index = 0; index < targets.Count; index++)
        {
            targets[index].SpanGroupId = group;
            targets[index].AssignedPath = path;
            targets[index].SetPreview(bitmaps[index]);
        }

        return true;
    }

    private async Task ReflowSpanAsync(List<MonitorViewModel> members, string path)
    {
        if (members.Count == 0)
            return;

        var previewPath = await _thumbnails.GetJpegAsync(path, 1024, CancellationToken.None);
        if (previewPath is null)
            return;

        if (members.Count == 1)
        {
            var bitmap = OpenBitmap(previewPath);
            if (bitmap is null)
                return;

            members[0].SpanGroupId = null;
            members[0].SetPreview(bitmap);
            return;
        }

        var connectors = members.Select(monitor => monitor.Device.Connector).ToList();
        var devices = Devices();
        var pngs = await Task.Run(() => SpanPreview.PngSlices(previewPath, devices, connectors));
        if (pngs is null)
            return;

        var bitmaps = pngs.Select(OpenPng).ToList();
        if (bitmaps.Any(bitmap => bitmap is null))
        {
            foreach (var bitmap in bitmaps)
                bitmap?.Dispose();
            return;
        }

        for (var index = 0; index < members.Count; index++)
            members[index].SetPreview(bitmaps[index]);
    }

    private async Task RefreshSpanPreviewsAsync()
    {
        var groups = Monitors
            .Where(monitor => monitor.SpanGroupId is not null && monitor.AssignedPath is not null)
            .GroupBy(monitor => monitor.SpanGroupId!.Value)
            .ToList();
        foreach (var group in groups)
        {
            var members = group.ToList();
            var path = members[0].AssignedPath;
            if (path is not null)
                await ReflowSpanAsync(members, path);
        }
    }

    private static string Describe(IReadOnlyList<MonitorViewModel> targets)
    {
        var names = targets
            .OrderBy(monitor => monitor.Device.Y)
            .ThenBy(monitor => monitor.Device.X)
            .Select(monitor => monitor.Device.Connector)
            .ToList();
        if (names.Count < 2)
            return names.FirstOrDefault() ?? "";
        if (names.Count == 2)
            return $"{names[0]} and {names[1]}";

        return string.Join(", ", names.Take(names.Count - 1)) + ", and " + names[^1];
    }

    private static Bitmap? OpenBitmap(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Bitmap? OpenPng(byte[]? png)
    {
        if (png is null)
            return null;

        try
        {
            return new Bitmap(new MemoryStream(png));
        }
        catch (Exception)
        {
            return null;
        }
    }
}
