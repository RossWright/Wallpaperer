using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using Wallpaperer.Models;
using Wallpaperer.Services;

namespace Wallpaperer.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly MutterMonitorLayoutSource _monitors;
    private readonly GnomeWallpaperApplier _applier;
    private readonly ThumbnailCache _thumbnails;
    private readonly UiSettingsStore _settings;
    private readonly ICurrentWallpaperSource _wallpapers;
    private readonly IUpdateCheck _updates;
    private CancellationTokenSource? _scanCancellation;
    private CancellationTokenSource? _applyCancellation;
    private int _layoutGeneration;
    private bool _shuttingDown;
    private bool _applyingFilterSelection;
    private bool _scanning;
    private bool _hasPublished;
    private string? _savedFilterId;
    private string? _selectionAnchor;
    private bool _loadingSettings = true;
    private readonly List<ImagePlacement> _base = [];
    private readonly List<ImagePlacement> _edits = [];
    private bool _wallpaperDirty;
    private SKBitmap? _composed;
    private string? _composedPath;
    private readonly DebouncedAction _autoApply = new(TimeSpan.FromMilliseconds(500));
    private bool _shownCurrentWallpaper;
    private bool _showingCurrentWallpaper;
    private bool _showHidden;
    private IReadOnlyList<PictureItemViewModel> _allPictures = [];

    public MainWindowViewModel(
        MutterMonitorLayoutSource monitors,
        GnomeWallpaperApplier applier,
        ThumbnailCache thumbnails,
        UiSettingsStore settings,
        ICurrentWallpaperSource wallpapers,
        IUpdateCheck? updates = null)
    {
        _monitors = monitors;
        _applier = applier;
        _thumbnails = thumbnails;
        _settings = settings;
        _wallpapers = wallpapers;
        _updates = updates ?? NoUpdateCheck.Instance;
        _savedFilterId = settings.SizeFilterId;
        AutoApply = settings.AutoApply;
        _loadingSettings = false;
        Monitors.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMonitors));
    }

    public ThumbnailCache Thumbnails => _thumbnails;

    public ObservableCollection<MonitorViewModel> Monitors { get; } = [];

    public bool HasMonitors => Monitors.Count > 0;

    [ObservableProperty]
    private IReadOnlyList<PictureItemViewModel> pictures = [];

    [ObservableProperty]
    private bool showHiddenLink;

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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool autoApply;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    private bool updateAvailable;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    private bool isUpdating;

    [ObservableProperty]
    private string updateButtonLabel = "Update available";

    public event EventHandler? ApplyFinished;

    public async Task StartAsync(Screens screens)
    {
        _scanCancellation = new CancellationTokenSource();
        _ = CheckForUpdateAsync();
        await ReloadMonitorsAsync(screens);
        await ScanAsync(_scanCancellation.Token);
    }

    public async Task CheckForUpdateAsync()
    {
        try
        {
            var available = await _updates.CheckAsync(CancellationToken.None);
            if (_shuttingDown || !available)
                return;

            UpdateAvailable = true;
        }
        catch (Exception exception)
        {
            Trace.WriteLine("Update check failed: " + exception.Message);
        }
    }

    public Task ReloadMonitorsAsync(Screens? screens)
    {
        var generation = Interlocked.Increment(ref _layoutGeneration);
        return LoadMonitorsAsync(screens, generation);
    }

    public void Shutdown()
    {
        _shuttingDown = true;
        _autoApply.Cancel();
        _scanCancellation?.Cancel();
        if (!IsSettingWallpaper)
            _applyCancellation?.Cancel();
        if (!IsApplying)
        {
            _composed?.Dispose();
            _composed = null;
        }
    }

    public void ArrangeMonitors(double panelWidth, double panelHeight)
    {
        const double instructionBand = 48;
        var arranged = MonitorArrangementLayout.Arrange(
            Monitors.Select(monitor => monitor.Device).ToList(),
            panelWidth,
            Math.Max(1, panelHeight - instructionBand),
            margin: 12);
        var byConnector = arranged.ToDictionary(monitor => monitor.Connector);
        foreach (var monitor in Monitors)
        {
            if (byConnector.TryGetValue(monitor.Device.Connector, out var rect))
                monitor.SetViewRect(rect.X, rect.Y + instructionBand, rect.Width, rect.Height);
        }
    }

    public void SelectOnly(string connector)
    {
        _selectionAnchor = connector;
        ApplySelection(MonitorSelection.Fill(Devices(), [connector]));
    }

    public void ExtendSelection(string connector)
    {
        var seeds = Monitors.Where(monitor => monitor.IsSelected).Select(monitor => monitor.Device.Connector).ToList();
        if (!seeds.Contains(connector))
            seeds.Add(connector);

        if (seeds.Count == 0)
        {
            SelectOnly(connector);
            return;
        }

        _selectionAnchor = seeds[0];
        ApplySelection(MonitorSelection.Fill(Devices(), seeds));
    }

    public void ClearSelection()
    {
        _selectionAnchor = null;
        ApplySelection([]);
    }

    public void SelectRange(string fromConnector, string toConnector)
    {
        _selectionAnchor = fromConnector;
        ApplySelection(MonitorSelection.Fill(Devices(), [fromConnector, toConnector]));
    }

    public Task AssignSelectionAsync(string path)
    {
        var selected = Monitors.Where(item => item.IsSelected).ToList();
        if (selected.Count == 0)
        {
            Status = "Select a monitor first.";
            return Task.CompletedTask;
        }

        return AssignImageAsync(path, selected);
    }

    public Task AssignAsync(MonitorViewModel monitor, string path) =>
        AssignImageAsync(path, DropTargets(Monitors, monitor));

    internal static IReadOnlyList<MonitorViewModel> DropTargets(
        IReadOnlyList<MonitorViewModel> monitors,
        MonitorViewModel drop)
    {
        var selected = monitors.Where(monitor => monitor.IsSelected).ToList();
        return selected.Contains(drop) ? selected : [drop];
    }

    private async Task AssignImageAsync(string path, IReadOnlyList<MonitorViewModel> targets)
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

        await ApplyImageAsync(path, targets);
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        _autoApply.Cancel();
        _applyCancellation?.Cancel();
        _applyCancellation = new CancellationTokenSource();
        var cancellation = _applyCancellation.Token;
        IsApplying = true;
        try
        {
            var baked = _edits.ToArray();
            if (baked.Length == 0 && _composed is not null && SameComposedBase())
            {
                _wallpaperDirty = false;
                return;
            }

            Status = "Composing wallpaper…";
            var devices = Monitors.Select(monitor => monitor.Device).ToList();
            var underlay = _composed;
            var reuse = underlay is not null && SameComposedBase();
            var directory = WallpaperComposer.DefaultDirectory();
            (SKBitmap Bitmap, string Path)? pending = null;

            try
            {
                pending = await Task.Run(() =>
                {
                    SKBitmap? bitmap = null;
                    try
                    {
                        if (reuse && underlay is not null)
                            bitmap = WallpaperComposer.PaintOn(underlay, devices, baked, cancellation);
                        bitmap ??= WallpaperComposer.Render(devices, BasePlus(baked), cancellation);
                        cancellation.ThrowIfCancellationRequested();
                        return (bitmap, WallpaperComposer.SaveJpeg(bitmap, directory));
                    }
                    catch
                    {
                        bitmap?.Dispose();
                        throw;
                    }
                }, cancellation);

                IsSettingWallpaper = true;
                Status = "Setting wallpaper…";
                await _applier.ApplyAsync(pending.Value.Path, CancellationToken.None);
                cancellation.ThrowIfCancellationRequested();
                FoldIntoBase(pending.Value.Bitmap, pending.Value.Path, baked);
                pending = null;
                _wallpaperDirty = _edits.Count > 0;
                Status = $"Applied {_composedPath}";
            }
            catch
            {
                if (pending is not null && !ReferenceEquals(pending.Value.Bitmap, _composed))
                    pending.Value.Bitmap.Dispose();
                throw;
            }
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
            ApplyCommand.NotifyCanExecuteChanged();
            ApplyFinished?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool HasAssignment() => Monitors.Any(monitor => monitor.HasImage);

    private bool CanApply() =>
        !AutoApply && !IsApplying && (HasAssignment() || (_wallpaperDirty && _base.Count > 0));

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
        SyncFilterToSelection();
        _showingCurrentWallpaper = true;
        try
        {
            await ShowCurrentWallpapersAsync();
            await RefreshSpanPreviewsAsync();
        }
        finally
        {
            _showingCurrentWallpaper = false;
        }

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

    public void MoveImageToTop(PictureItemViewModel picture)
    {
        PictureLibraryOrganization.MoveImageToTop(
            _settings.Pins,
            _settings.HiddenImages,
            _settings.HiddenFolders,
            _settings.ShownImages,
            picture.Path);
        _settings.Save();
        ApplyFilter();
    }

    public void MoveFolderToTop(PictureItemViewModel picture)
    {
        PictureLibraryOrganization.MoveFolderToTop(
            _settings.Pins,
            _settings.HiddenImages,
            _settings.HiddenFolders,
            _settings.ShownImages,
            picture.Path,
            _allPictures.Select(item => item.Path));
        _settings.Save();
        ApplyFilter();
    }

    public void HideImage(PictureItemViewModel picture)
    {
        PictureLibraryOrganization.HideImage(
            _settings.Pins,
            _settings.HiddenImages,
            _settings.ShownImages,
            picture.Path);
        _settings.Save();
        ApplyFilter();
    }

    public void HideFolder(string folder)
    {
        PictureLibraryOrganization.HideFolder(
            _settings.Pins,
            _settings.HiddenImages,
            _settings.HiddenFolders,
            _settings.ShownImages,
            folder);
        _settings.Save();
        ApplyFilter();
    }

    public void ShowImage(PictureItemViewModel picture)
    {
        PictureLibraryOrganization.ShowImage(
            _settings.HiddenImages,
            _settings.HiddenFolders,
            _settings.ShownImages,
            picture.Path);
        _settings.Save();
        ApplyFilter();
    }

    public void ShowFolder(string folder)
    {
        PictureLibraryOrganization.ShowFolder(_settings.HiddenFolders, folder);
        _settings.Save();
        ApplyFilter();
    }

    public bool IsPictureHidden(string path) =>
        PictureLibraryOrganization.IsHidden(path, _settings.HiddenImages, _settings.HiddenFolders, _settings.ShownImages);

    public IReadOnlyList<string> HiddenFoldersCovering(string path) =>
        PictureLibraryOrganization.HiddenAncestors(path, _settings.HiddenFolders);

    public void ShowHiddenImages()
    {
        _showHidden = true;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filter = SelectedSizeFilter;
        PictureItemViewModel[] filtered = filter is null
            ? _allPictures.ToArray()
            : _allPictures.Where(picture => filter.Matches(picture.Record.PixelWidth, picture.Record.PixelHeight)).ToArray();
        var byPath = filtered.ToDictionary(picture => PictureLibraryOrganization.Normalize(picture.Path));
        var arranged = PictureLibraryOrganization.Arrange(
            filtered.Select(picture => picture.Path).ToArray(),
            _settings.Pins,
            _settings.HiddenImages,
            _settings.HiddenFolders,
            _settings.ShownImages,
            _showHidden);
        Pictures = arranged.Paths.Select(path => byPath[PictureLibraryOrganization.Normalize(path)]).ToArray();
        ShowHiddenLink = !_showHidden && arranged.HiddenCount > 0;
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
        ApplyCommand.NotifyCanExecuteChanged();
        if (_showingCurrentWallpaper || monitor.AssignedPath is not null)
            return;

        CancelCompose();
        var revealed = TakeOffTop(monitor.Device.Connector);
        _wallpaperDirty = true;
        Status = $"Cleared {monitor.Device.Connector}.";
        _ = RevealAsync(monitor, revealed);
        ScheduleAutoApply();
    }

    partial void OnAutoApplyChanged(bool value)
    {
        if (_loadingSettings)
            return;

        _settings.AutoApply = value;
        _settings.Save();
        if (!value)
            _autoApply.Cancel();
        else
            _ = ApplyIfReadyAsync();
    }

    private void ScheduleAutoApply()
    {
        if (!AutoApply || _shuttingDown || _showingCurrentWallpaper)
            return;

        _autoApply.Schedule(ApplyIfReadyAsync);
    }

    private async Task ApplyIfReadyAsync()
    {
        while (IsApplying && !_shuttingDown)
        {
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnFinished(object? sender, EventArgs e)
            {
                ApplyFinished -= OnFinished;
                finished.TrySetResult();
            }

            ApplyFinished += OnFinished;
            if (!IsApplying)
            {
                ApplyFinished -= OnFinished;
                break;
            }

            await finished.Task;
        }

        if (!AutoApply || _shuttingDown || (!HasAssignment() && !(_wallpaperDirty && _base.Count > 0)))
            return;

        await ApplyAsync();
    }

    private List<MonitorDevice> Devices() => Monitors.Select(monitor => monitor.Device).ToList();

    private void ApplySelection(IReadOnlyList<string> connectors)
    {
        var selected = connectors.ToHashSet(StringComparer.Ordinal);
        foreach (var monitor in Monitors)
            monitor.IsSelected = selected.Contains(monitor.Device.Connector);

        SyncFilterToSelection();
    }

    private void SyncFilterToSelection()
    {
        var selected = Monitors.Where(monitor => monitor.IsSelected).Select(monitor => monitor.Device).ToList();
        var match = SizeFilterCatalog.MatchSelection(SizeFilters, selected);
        if (match is null || match.Id == SelectedSizeFilter?.Id)
            return;

        SelectedSizeFilter = match;
    }

    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task UpdateAsync()
    {
        IsUpdating = true;
        UpdateButtonLabel = "Updating…";
        try
        {
            Status = "Downloading update…";
            await _updates.ApplyAsync(CancellationToken.None);
            Status = "Restarting to finish the update…";
        }
        catch (Exception exception)
        {
            Status = exception.Message;
            UpdateButtonLabel = "Update available";
            IsUpdating = false;
        }
    }

    private bool CanUpdate() => UpdateAvailable && !IsUpdating;

    private List<ImagePlacement> BasePlus(IReadOnlyList<ImagePlacement> edits)
    {
        var placements = new List<ImagePlacement>(_base.Count + edits.Count);
        placements.AddRange(_base);
        placements.AddRange(edits);
        return placements;
    }

    private bool SameComposedBase() =>
        _composed is not null
        && _base.Count == 1
        && string.Equals(_base[0].Path, _composedPath, StringComparison.Ordinal);

    private void CancelCompose() => _applyCancellation?.Cancel();

    private void FoldIntoBase(SKBitmap bitmap, string path, IReadOnlyList<ImagePlacement> baked)
    {
        foreach (var edit in baked)
            _edits.Remove(edit);

        var connectors = Monitors.Select(monitor => monitor.Device.Connector).ToList();
        _base.Clear();
        _base.Add(new ImagePlacement(path, connectors));
        var previous = _composed;
        _composed = bitmap;
        _composedPath = path;
        if (!ReferenceEquals(previous, bitmap))
            previous?.Dispose();

        _showingCurrentWallpaper = true;
        try
        {
            foreach (var monitor in Monitors)
            {
                if (_edits.Any(edit => VisibleConnectors(edit).Contains(monitor.Device.Connector, StringComparer.Ordinal)))
                    continue;
                monitor.AssignedPath = null;
            }
        }
        finally
        {
            _showingCurrentWallpaper = false;
        }
    }

    private async Task ApplyImageAsync(string path, IReadOnlyList<MonitorViewModel> targets, bool announce = true)
    {
        CancelCompose();
        var connectors = targets.Select(monitor => monitor.Device.Connector).ToList();
        var layer = new ImagePlacement(path, connectors);
        _edits.Add(layer);
        if (!await PaintLayerAsync(layer, connectors))
        {
            _edits.RemoveAt(_edits.Count - 1);
            if (announce)
                Status = $"Could not read {path}.";
            return;
        }

        foreach (var monitor in targets)
            monitor.AssignedPath = path;

        _wallpaperDirty = true;
        if (announce)
        {
            var file = Path.GetFileName(path);
            Status = targets.Count == 1
                ? $"Assigned {file} to {targets[0].Device.Connector}."
                : $"Assigned {file} across {Describe(targets)}.";
            ScheduleAutoApply();
        }

        ApplyCommand.NotifyCanExecuteChanged();
    }

    private async Task ShowCurrentWallpapersAsync()
    {
        if (_shownCurrentWallpaper || _shuttingDown || Monitors.Count == 0)
            return;

        _shownCurrentWallpaper = true;
        CurrentWallpapers current;
        try
        {
            current = await _wallpapers.ReadAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            Trace.WriteLine("Could not read the current wallpaper: " + exception.Message);
            return;
        }

        if (current.Desktop.Path is not string desktop || !File.Exists(desktop))
            return;

        var connectors = Monitors.Select(monitor => monitor.Device.Connector).ToList();
        _base.Clear();
        if (current.Desktop.ShowsAsOneSpan(connectors.Count))
            _base.Add(new ImagePlacement(desktop, connectors));
        else
        {
            foreach (var connector in connectors)
                _base.Add(new ImagePlacement(desktop, [connector]));
        }
    }

    private ImagePlacement? TakeOffTop(string connector)
    {
        for (var index = _edits.Count - 1; index >= 0; index--)
        {
            var visible = VisibleConnectors(_edits[index]);
            if (!visible.Contains(connector, StringComparer.Ordinal))
                continue;

            var remaining = visible.Where(item => !string.Equals(item, connector, StringComparison.Ordinal)).ToList();
            if (remaining.Count == 0)
                _edits.RemoveAt(index);
            else
                _edits[index] = _edits[index] with { Visible = remaining };

            return LayerCovering(_edits, connector) ?? LayerCovering(_base, connector);
        }

        return LayerCovering(_base, connector);
    }

    private static ImagePlacement? LayerCovering(List<ImagePlacement> layers, string connector)
    {
        for (var index = layers.Count - 1; index >= 0; index--)
        {
            if (VisibleConnectors(layers[index]).Contains(connector, StringComparer.Ordinal))
                return layers[index];
        }

        return null;
    }

    private static IReadOnlyList<string> VisibleConnectors(ImagePlacement placement) =>
        placement.Visible ?? placement.Connectors;

    private async Task RevealAsync(MonitorViewModel monitor, ImagePlacement? layer)
    {
        if (layer is null)
        {
            monitor.SetPreview(null);
            return;
        }

        if (!await PaintLayerAsync(layer, [monitor.Device.Connector]))
            return;

        if (_edits.Any(item => VisibleConnectors(item).Contains(monitor.Device.Connector, StringComparer.Ordinal)))
            monitor.AssignedPath = layer.Path;
    }

    private async Task<bool> PaintLayerAsync(ImagePlacement layer, IReadOnlyList<string> connectors)
    {
        var previewPath = await _thumbnails.GetJpegAsync(layer.Path, 1024, CancellationToken.None);
        if (previewPath is null)
            return false;

        var fitted = layer.Connectors.ToList();
        if (fitted.Count <= 1)
        {
            var bitmap = OpenBitmap(previewPath);
            if (bitmap is null)
                return false;

            var monitor = Monitors.FirstOrDefault(item => item.Device.Connector == fitted[0]);
            if (monitor is null || !connectors.Contains(fitted[0], StringComparer.Ordinal))
            {
                bitmap.Dispose();
                return false;
            }

            monitor.SetPreview(bitmap);
            return true;
        }

        var pngs = await Task.Run(() => SpanPreview.PngSlices(previewPath, Devices(), fitted));
        if (pngs is null)
            return false;

        var painted = false;
        foreach (var connector in connectors)
        {
            var index = fitted.FindIndex(item => string.Equals(item, connector, StringComparison.Ordinal));
            if (index < 0 || index >= pngs.Length)
                continue;

            var bitmap = OpenPng(pngs[index]);
            if (bitmap is null)
                continue;

            var monitor = Monitors.FirstOrDefault(item => item.Device.Connector == connector);
            if (monitor is null)
            {
                bitmap.Dispose();
                continue;
            }

            monitor.SetPreview(bitmap);
            painted = true;
        }

        return painted;
    }

    private async Task RefreshSpanPreviewsAsync()
    {
        var painted = new HashSet<string>(StringComparer.Ordinal);
        var layers = _edits.AsEnumerable().Reverse().Concat(_base.AsEnumerable().Reverse());
        foreach (var layer in layers)
        {
            var pending = VisibleConnectors(layer).Where(painted.Add).ToList();
            if (pending.Count == 0)
                continue;

            await PaintLayerAsync(layer, pending);
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
