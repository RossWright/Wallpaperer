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
    private readonly string _wallpaperDirectory;
    private CancellationTokenSource? _scanCancellation;
    private CancellationTokenSource? _applyCancellation;
    private int _layoutGeneration;
    private bool _shuttingDown;
    private bool _applyingFilterSelection;
    private bool _scanning;
    private bool _hasPublished;
    private string? _savedFilterId;
    private string? _savedAspectId;
    private IReadOnlyList<SizeFilterOption> _allResolutions = [];
    private string? _selectionAnchor;
    private bool _loadingSettings = true;
    private readonly List<ImagePlacement> _base = [];
    private readonly List<ImagePlacement> _edits = [];
    private bool _wallpaperDirty;
    private SKBitmap? _composed;
    private string? _composedPath;
    private SKBitmap? _replayUnder;
    private ImagePlacement? _replay;
    private long _replayToken;
    private bool _composeFromReplay;
    private long _lastPaintToken;
    private long _nextToken;
    private uint _backgroundColor = 0xFF000000;
    private bool _openingFilterChosen;
    private bool _refreshingCounts;
    private bool _countsStale = true;
    private int _scanGeneration;
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
        IUpdateCheck? updates = null,
        string? wallpaperDirectory = null)
    {
        _monitors = monitors;
        _applier = applier;
        _thumbnails = thumbnails;
        _settings = settings;
        _wallpapers = wallpapers;
        _updates = updates ?? NoUpdateCheck.Instance;
        _wallpaperDirectory = wallpaperDirectory ?? WallpaperComposer.DefaultDirectory();
        _savedFilterId = settings.SizeFilterId;
        _savedAspectId = settings.AspectFilterId;
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
    private IReadOnlyList<AspectFilterOption> aspectFilters = [];

    [ObservableProperty]
    private AspectFilterOption? selectedAspectFilter;

    [ObservableProperty]
    private IReadOnlyList<SizeFilterOption> sizeFilters = [];

    [ObservableProperty]
    private SizeFilterOption? selectedSizeFilter;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshPicturesCommand))]
    private bool isScanningPictures;

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
            var composed = _composed;
            var replay = _replayUnder;
            _composed = null;
            _replayUnder = null;
            composed?.Dispose();
            if (!ReferenceEquals(replay, composed))
                replay?.Dispose();
        }
    }

    public void ArrangeMonitors(double panelWidth, double panelHeight)
    {
        var arranged = MonitorArrangementLayout.Arrange(
            Monitors.Select(monitor => monitor.Device).ToList(),
            panelWidth,
            panelHeight,
            margin: 12);
        var byConnector = arranged.ToDictionary(monitor => monitor.Connector);
        foreach (var monitor in Monitors)
        {
            if (byConnector.TryGetValue(monitor.Device.Connector, out var rect))
            {
                const double gap = 2;
                monitor.SetViewRect(
                    rect.X + gap / 2,
                    rect.Y + gap / 2,
                    Math.Max(1, rect.Width - gap),
                    Math.Max(1, rect.Height - gap));
            }
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
            if (baked.Length == 0 && (_base.Count == 0 || (_composed is not null && SameComposedBase())))
            {
                _wallpaperDirty = false;
                return;
            }

            Status = "Composing wallpaper…";
            var devices = Monitors.Select(monitor => monitor.Device).ToList();
            var fromReplay = _composeFromReplay && _replayUnder is not null;
            var underlay = fromReplay ? _replayUnder : (_composed is not null && SameComposedBase() ? _composed : null);
            var lastToken = _lastPaintToken;
            var directory = _wallpaperDirectory;
            (SKBitmap Bitmap, string Path, SKBitmap? Under)? pending = null;

            try
            {
                pending = await Task.Run(() =>
                {
                    SKBitmap? bitmap = null;
                    SKBitmap? replayUnder = null;
                    try
                    {
                        if (underlay is not null)
                            bitmap = WallpaperComposer.PaintOn(underlay, devices, baked, cancellation);
                        bitmap ??= WallpaperComposer.Render(devices, BasePlus(baked), cancellation);
                        var last = baked.LastOrDefault(item => item.Token == lastToken);
                        if (last is not null)
                        {
                            var older = baked.Where(item => item.Token != last.Token).ToArray();
                            if (older.Length == 0 && underlay is not null && baked.Length == 1)
                                replayUnder = underlay.Copy();
                            else if (underlay is not null)
                                replayUnder = WallpaperComposer.PaintOn(underlay, devices, older, cancellation);
                            else
                                replayUnder = WallpaperComposer.Render(devices, BasePlus(older), cancellation);
                        }

                        cancellation.ThrowIfCancellationRequested();
                        return (bitmap, WallpaperComposer.SaveJpeg(bitmap, directory), replayUnder);
                    }
                    catch
                    {
                        bitmap?.Dispose();
                        replayUnder?.Dispose();
                        throw;
                    }
                }, cancellation);

                IsSettingWallpaper = true;
                Status = "Setting wallpaper…";
                await _applier.ApplyAsync(pending.Value.Path, CancellationToken.None);
                cancellation.ThrowIfCancellationRequested();
                var lastPaint = baked.LastOrDefault(item => item.Token == lastToken);
                if (pending.Value.Under is not null && lastPaint is not null)
                {
                    if (!ReferenceEquals(_replayUnder, pending.Value.Under))
                        _replayUnder?.Dispose();
                    _replayUnder = pending.Value.Under;
                    _replay = lastPaint;
                    _replayToken = lastPaint.Token;
                    pending = (pending.Value.Bitmap, pending.Value.Path, null);
                }

                _composeFromReplay = false;
                FoldIntoBase(pending.Value.Bitmap, pending.Value.Path, baked);
                pending = null;
                _wallpaperDirty = _edits.Count > 0;
                Status = $"Applied {_composedPath}";
            }
            catch
            {
                if (pending is not null && !ReferenceEquals(pending.Value.Bitmap, _composed))
                    pending.Value.Bitmap.Dispose();
                if (pending?.Under is not null && !ReferenceEquals(pending.Value.Under, _replayUnder))
                    pending.Value.Under.Dispose();
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

    [RelayCommand(CanExecute = nameof(CanRefreshPictures))]
    private async Task RefreshPicturesAsync()
    {
        _scanCancellation?.Cancel();
        _scanCancellation = new CancellationTokenSource();
        await ScanAsync(_scanCancellation.Token);
    }

    private bool CanRefreshPictures() => !IsScanningPictures;

    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        var generation = Interlocked.Increment(ref _scanGeneration);
        IsScanningPictures = true;
        _scanning = true;
        Status = "Scanning…";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var found = new List<PictureRecord>();
        var lastStatus = Stopwatch.StartNew();
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
                    found.Add(new PictureRecord(path, written, pixels?.Width, pixels?.Height));
                    if (lastStatus.ElapsedMilliseconds < 500)
                        continue;

                    lastStatus.Restart();
                    var count = found.Count;
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (generation == _scanGeneration && !_shuttingDown && IsScanningPictures)
                            Status = $"Scanning… {count} images";
                    });
                }
            }, cancellationToken);

            if (generation != _scanGeneration || cancellationToken.IsCancellationRequested)
                return;

            found.Sort(static (left, right) => PictureRecord.CompareNewest(left, right));
            var catalog = found.Select(record => new PictureItemViewModel(record)).ToArray();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation != _scanGeneration || _shuttingDown)
                    return;

                foreach (var picture in _allPictures)
                    picture.ReleaseThumbnail();

                _allPictures = catalog;
                UpdateThumbnailEmphasis();
                _countsStale = true;
                _scanning = false;
                _hasPublished = true;
                IsScanningPictures = false;
                ApplyFilter();
            });
        }
        catch (OperationCanceledException)
        {
            // A newer scan replaced this one, or the window closed.
        }
        catch (Exception exception)
        {
            if (generation == _scanGeneration && !_shuttingDown)
            {
                _scanning = false;
                IsScanningPictures = false;
                Status = exception.Message;
            }
        }
    }

    private void RefreshSizeFilters()
    {
        var devices = Monitors.Select(monitor => monitor.Device).ToList();
        var resolutions = SizeFilterCatalog.Build(devices);
        var aspects = SizeFilterCatalog.BuildAspects(devices);
        var opening = !_openingFilterChosen;
        if (!opening && SameFilterIds(resolutions, _allResolutions) && SameAspectIds(aspects, AspectFilters))
            return;

        var savedAspect = opening ? null : _savedAspectId;
        var savedSize = opening ? null : _savedFilterId;
        _openingFilterChosen = true;
        var aspect = aspects.FirstOrDefault(option => option.Id == savedAspect)
            ?? aspects.First(option => option.Id == SizeFilterCatalog.DefaultAspectId(devices, aspects));
        var sized = SizeFilterCatalog.ForAspect(resolutions, aspect);
        var selected = SizeFilterCatalog.Select(sized, savedSize, devices);
        if (opening)
        {
            _savedAspectId = aspect.Id;
            _savedFilterId = selected.Id;
        }

        _allResolutions = resolutions;
        _countsStale = true;
        _applyingFilterSelection = true;
        AspectFilters = aspects;
        SelectedAspectFilter = aspect;
        SizeFilters = sized;
        SelectedSizeFilter = selected;
        _applyingFilterSelection = false;
    }

    partial void OnSelectedAspectFilterChanged(AspectFilterOption? value)
    {
        if (_refreshingCounts || _applyingFilterSelection)
            return;

        if (value is null)
        {
            if (AspectFilters.Count == 0)
                return;

            _applyingFilterSelection = true;
            SelectedAspectFilter = AspectFilters.FirstOrDefault(option => option.Id == _savedAspectId) ?? AspectFilters[0];
            _applyingFilterSelection = false;
            return;
        }

        _savedAspectId = value.Id;
        _settings.AspectFilterId = value.Id;
        _settings.Save();
        ApplyAspect(value, SelectedSizeFilter?.Id);
    }

    private void ApplyAspect(AspectFilterOption aspect, string? resolutionId)
    {
        var resolutions = SizeFilterCatalog.ForAspect(_allResolutions, aspect);
        var selected = resolutions.FirstOrDefault(option => option.Id == resolutionId)
            ?? resolutions.FirstOrDefault(option => option.Id == SizeFilterCatalog.AnyId)
            ?? resolutions[0];
        _countsStale = true;
        _applyingFilterSelection = true;
        SizeFilters = resolutions;
        SelectedSizeFilter = selected;
        _applyingFilterSelection = false;
    }

    partial void OnSelectedSizeFilterChanged(SizeFilterOption? value)
    {
        if (_refreshingCounts)
            return;

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
        NoteLibraryChange(countsChanged: true);
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
        NoteLibraryChange(countsChanged: true);
    }

    public void HideImage(PictureItemViewModel picture)
    {
        PictureLibraryOrganization.HideImage(
            _settings.Pins,
            _settings.HiddenImages,
            _settings.ShownImages,
            picture.Path);
        _settings.Save();
        NoteLibraryChange(countsChanged: true);
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
        NoteLibraryChange(countsChanged: true);
        Status = $"Hid {PictureLibraryOrganization.DisplayFolder(folder)}.";
    }

    public void ShowImage(PictureItemViewModel picture)
    {
        PictureLibraryOrganization.ShowImage(
            _settings.HiddenImages,
            _settings.HiddenFolders,
            _settings.ShownImages,
            picture.Path);
        _settings.Save();
        NoteLibraryChange(countsChanged: true);
    }

    public void ShowFolder(string folder)
    {
        PictureLibraryOrganization.ShowFolder(_settings.HiddenFolders, folder);
        _settings.Save();
        NoteLibraryChange(countsChanged: true);
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

    private void NoteLibraryChange(bool countsChanged)
    {
        if (countsChanged)
            _countsStale = true;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var aspect = SelectedAspectFilter;
        var filter = SelectedSizeFilter;
        var filtered = _allPictures.Where(picture =>
            (aspect is null || aspect.Matches(picture.Record.PixelWidth, picture.Record.PixelHeight))
            && (filter is null || filter.Matches(picture.Record.PixelWidth, picture.Record.PixelHeight))).ToArray();
        var byPath = filtered.ToDictionary(picture => PictureLibraryOrganization.Normalize(picture.Path));
        var arranged = PictureLibraryOrganization.Arrange(
            filtered.Select(picture => picture.Path).ToArray(),
            _settings.Pins,
            _settings.HiddenImages,
            _settings.HiddenFolders,
            _settings.ShownImages,
            _showHidden);
        var next = arranged.Paths.Select(path => byPath[PictureLibraryOrganization.Normalize(path)]).ToArray();
        var showHiddenLink = !_showHidden && arranged.HiddenCount > 0;
        if (ShowHiddenLink != showHiddenLink)
            ShowHiddenLink = showHiddenLink;
        if (!SamePictures(Pictures, next))
            Pictures = next;

        if (_countsStale)
            PublishFilterCounts();
        if (!_hasPublished || _scanning)
            return;

        Status = LibraryStatus();
    }

    private string LibraryStatus()
    {
        if (_allPictures.Count == 0)
            return "No images found under the home folder.";

        var visibility = new PictureVisibility(_settings.HiddenImages, _settings.HiddenFolders, _settings.ShownImages);
        var hidden = 0;
        foreach (var picture in _allPictures)
        {
            if (visibility.IsHidden(picture.Path))
                hidden++;
        }

        var shown = _allPictures.Count - hidden;
        var images = shown == 1 ? "1 image" : $"{shown} images";
        return hidden > 0 ? $"{images} ({hidden} hidden)" : images;
    }

    private static bool SamePictures(IReadOnlyList<PictureItemViewModel> current, IReadOnlyList<PictureItemViewModel> next)
    {
        if (current.Count != next.Count)
            return false;

        for (var index = 0; index < current.Count; index++)
        {
            if (!ReferenceEquals(current[index], next[index]))
                return false;
        }

        return true;
    }

    private static bool SameFilterIds(IReadOnlyList<SizeFilterOption> next, IReadOnlyList<SizeFilterOption> current)
    {
        if (next.Count != current.Count)
            return false;

        for (var index = 0; index < next.Count; index++)
        {
            if (next[index].Id != current[index].Id)
                return false;
        }

        return true;
    }

    private static bool SameAspectIds(IReadOnlyList<AspectFilterOption> next, IReadOnlyList<AspectFilterOption> current)
    {
        if (next.Count != current.Count)
            return false;

        for (var index = 0; index < next.Count; index++)
        {
            if (next[index].Id != current[index].Id)
                return false;
        }

        return true;
    }

    private void PublishFilterCounts()
    {
        if (!_countsStale || !_hasPublished || SizeFilters.Count == 0)
            return;

        var visibility = new PictureVisibility(_settings.HiddenImages, _settings.HiddenFolders, _settings.ShownImages);
        var visible = new List<PictureItemViewModel>();
        foreach (var picture in _allPictures)
        {
            if (!visibility.IsHidden(picture.Path))
                visible.Add(picture);
        }

        var aspect = SelectedAspectFilter;
        var exactAspect = ExactAspectId();
        var exactSize = ExactResolution();
        var aspects = AspectFilters.Select(option => option with
        {
            Count = visible.Count(picture => option.Matches(picture.Record.PixelWidth, picture.Record.PixelHeight)),
            Exact = exactAspect is not null && option.Id == exactAspect,
        }).ToArray();
        var resolutions = SizeFilters.Select(option => MarkResolution(option with
        {
            Count = visible.Count(picture =>
                (aspect is null || aspect.Matches(picture.Record.PixelWidth, picture.Record.PixelHeight))
                && option.Matches(picture.Record.PixelWidth, picture.Record.PixelHeight)),
        }, exactSize)).ToArray();
        _countsStale = false;
        if (aspects.SequenceEqual(AspectFilters) && resolutions.SequenceEqual(SizeFilters))
            return;

        var aspectId = SelectedAspectFilter?.Id;
        var sizeId = SelectedSizeFilter?.Id;
        _refreshingCounts = true;
        _applyingFilterSelection = true;
        if (aspects.Length > 0)
        {
            AspectFilters = aspects;
            if (aspectId is not null)
                SelectedAspectFilter = aspects.FirstOrDefault(option => option.Id == aspectId) ?? SelectedAspectFilter;
        }

        SizeFilters = resolutions;
        if (sizeId is not null)
            SelectedSizeFilter = resolutions.FirstOrDefault(option => option.Id == sizeId) ?? SelectedSizeFilter;
        _applyingFilterSelection = false;
        _refreshingCounts = false;
    }

    private void OnMonitorChanged(MonitorViewModel monitor)
    {
        ApplyCommand.NotifyCanExecuteChanged();
        if (_showingCurrentWallpaper || monitor.AssignedPath is not null)
            return;

        CancelCompose();
        TakeOffTop(monitor.Device.Connector);
        if (_replay?.Token == _lastPaintToken && !_edits.Any(edit => edit.Token == _lastPaintToken))
            _lastPaintToken = 0;
        _wallpaperDirty = true;
        Status = $"Cleared {monitor.Device.Connector}.";
        _ = RefreshCompositePreviewAsync();
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

        if (!AutoApply || _shuttingDown || (!_wallpaperDirty && !HasAssignment()))
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
        UpdateThumbnailEmphasis();
        var selected = Monitors.Where(monitor => monitor.IsSelected).Select(monitor => monitor.Device).ToList();
        if (SizeFilterCatalog.SelectionBounds(selected) is not { Width: > 0, Height: > 0 } bounds)
        {
            ApplyExactMarks();
            return;
        }

        var source = _allResolutions.Count > 0 ? _allResolutions : SizeFilters;
        var aspect = AspectFilters.FirstOrDefault(option => option.Id == SizeFilterCatalog.AspectOf(bounds.Width, bounds.Height).Id);
        _applyingFilterSelection = true;
        if (aspect is not null)
        {
            if (SelectedAspectFilter?.Id != aspect.Id)
                SelectedAspectFilter = aspect;

            var resolutions = SizeFilterCatalog.ForAspect(source, aspect);
            if (!SameFilterIds(resolutions, SizeFilters))
            {
                _countsStale = true;
                SizeFilters = resolutions;
            }
        }

        var match = SizeFilterCatalog.MatchSelection(SizeFilters, selected);
        if (match is not null && SelectedSizeFilter?.Id != match.Id)
        {
            _savedFilterId = match.Id;
            _settings.SizeFilterId = match.Id;
            if (aspect is not null)
            {
                _savedAspectId = aspect.Id;
                _settings.AspectFilterId = aspect.Id;
            }

            _settings.Save();
            SelectedSizeFilter = match;
        }

        ApplyExactMarks();
        _applyingFilterSelection = false;
        if (_countsStale)
            ApplyFilter();
    }

    private void UpdateThumbnailEmphasis()
    {
        var selection = ExactResolution();
        foreach (var picture in _allPictures)
            picture.SetResolutionExact(selection);
    }

    private void ApplyExactMarks()
    {
        if (AspectFilters.Count == 0 && SizeFilters.Count == 0)
            return;

        var aspectId = SelectedAspectFilter?.Id;
        var sizeId = SelectedSizeFilter?.Id;
        var exactAspect = ExactAspectId();
        var exactSize = ExactResolution();
        var aspects = AspectFilters.Select(option => option with { Exact = exactAspect is not null && option.Id == exactAspect }).ToArray();
        var resolutions = SizeFilters.Select(option => MarkResolution(option, exactSize)).ToArray();
        _refreshingCounts = true;
        AspectFilters = aspects;
        if (aspectId is not null)
            SelectedAspectFilter = aspects.FirstOrDefault(option => option.Id == aspectId) ?? SelectedAspectFilter;
        SizeFilters = resolutions;
        if (sizeId is not null)
            SelectedSizeFilter = resolutions.FirstOrDefault(option => option.Id == sizeId) ?? SelectedSizeFilter;
        _refreshingCounts = false;
    }

    private string? ExactAspectId()
    {
        if (ExactResolution() is not { } size)
            return null;

        var aspect = SizeFilterCatalog.AspectOf(size.Width, size.Height);
        return aspect.Id == SizeFilterCatalog.AnyId ? null : aspect.Id;
    }

    private (int Width, int Height)? ExactResolution()
    {
        var selected = Monitors.Where(monitor => monitor.IsSelected).Select(monitor => monitor.Device).ToList();
        return SizeFilterCatalog.SelectionBounds(selected);
    }

    private static SizeFilterOption MarkResolution(SizeFilterOption option, (int Width, int Height)? bounds) =>
        option with
        {
            Exact = bounds is { } size
                && option.Id is not (SizeFilterCatalog.AnyId or SizeFilterCatalog.MonitorsId)
                && option.Bands.Count == 1
                && option.Bands[0].Width == size.Width
                && option.Bands[0].Height == size.Height,
        };

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
        _composeFromReplay = false;
        var connectors = targets.Select(monitor => monitor.Device.Connector).ToList();
        var layer = new ImagePlacement(path, connectors, Token: ++_nextToken);
        _edits.Add(layer);
        _lastPaintToken = layer.Token;
        _wallpaperDirty = true;
        await RefreshCompositePreviewAsync();
        RememberAssignment(targets, path);
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

    public async Task ApplyScreenColorAsync(MonitorViewModel monitor, uint argb)
    {
        _backgroundColor = argb;
        CancelCompose();
        _composeFromReplay = false;
        var layer = new ImagePlacement(null, TargetConnectors(monitor), Color: argb | 0xFF000000, Token: ++_nextToken);
        _edits.Add(layer);
        _lastPaintToken = layer.Token;
        await RefreshCompositePreviewAsync();
        _wallpaperDirty = true;
        Status = $"Cleared {Describe(MonitorsFor(layer.Connectors))}.";
        ScheduleAutoApply();
        ApplyCommand.NotifyCanExecuteChanged();
    }

    public async Task ApplyFitAsync(MonitorViewModel monitor, ImageFit fit)
    {
        var connectors = TargetConnectors(monitor);
        var live = _edits.LastOrDefault(edit => edit.Token == _lastPaintToken && edit.Path is not null)
            ?? _edits.LastOrDefault(edit =>
                edit.Path is not null && VisibleConnectors(edit).Contains(monitor.Device.Connector, StringComparer.Ordinal));
        var fromReplay = false;
        ImagePlacement? source = live;
        if (source is null && _replay is { Path: not null } replay && _replayUnder is not null)
        {
            source = replay;
            fromReplay = true;
        }

        if (source?.Path is null)
        {
            Status = "Set an image on this screen first.";
            return;
        }

        CancelCompose();
        var outside = VisibleConnectors(source)
            .Where(connector => !connectors.Contains(connector, StringComparer.Ordinal))
            .ToList();
        if (fromReplay)
        {
            _edits.Clear();
            _composeFromReplay = true;
            if (outside.Count > 0)
                _edits.Add(source with { Visible = outside, Token = 0 });
        }
        else
        {
            _edits.Remove(source);
            if (outside.Count > 0)
                _edits.Add(source with { Visible = outside, Token = 0 });
        }

        var updated = source with
        {
            Connectors = connectors,
            Visible = null,
            Fit = fit,
            Token = ++_nextToken,
        };
        _edits.Add(updated);
        _lastPaintToken = updated.Token;
        if (fromReplay)
        {
            _replay = updated;
            _replayToken = updated.Token;
        }

        await RefreshCompositePreviewAsync();
        _wallpaperDirty = true;
        Status = $"{fit} on {Describe(MonitorsFor(connectors))}.";
        ScheduleAutoApply();
        ApplyCommand.NotifyCanExecuteChanged();
    }

    public uint BackgroundColor => _backgroundColor;

    private List<string> TargetConnectors(MonitorViewModel monitor)
    {
        var selected = Monitors.Where(item => item.IsSelected).Select(item => item.Device.Connector).ToList();
        if (selected.Count > 1 && selected.Contains(monitor.Device.Connector, StringComparer.Ordinal))
            return selected;

        return [monitor.Device.Connector];
    }

    private List<MonitorViewModel> MonitorsFor(IReadOnlyList<string> connectors) =>
        Monitors.Where(monitor => connectors.Contains(monitor.Device.Connector, StringComparer.Ordinal)).ToList();

    private async Task RefreshCompositePreviewAsync()
    {
        if (_shuttingDown || Monitors.Count == 0)
            return;

        var devices = Devices();
        var fromReplay = _composeFromReplay && _replayUnder is not null;
        var placements = fromReplay ? _edits.ToArray() : BasePlus(_edits).ToArray();
        var under = fromReplay ? _replayUnder : null;
        if (placements.Length == 0 && under is null)
            return;

        IReadOnlyDictionary<string, byte[]> pngs;
        try
        {
            pngs = await Task.Run(() => WallpaperComposer.PreviewPngs(devices, placements, under, 1024, CancellationToken.None));
        }
        catch (Exception exception)
        {
            Status = exception.Message;
            return;
        }

        _showingCurrentWallpaper = true;
        try
        {
            foreach (var monitor in Monitors)
            {
                if (pngs.TryGetValue(monitor.Device.Connector, out var png))
                {
                    var bitmap = OpenPng(png);
                    if (bitmap is not null)
                        monitor.SetPreview(bitmap);
                }

                var top = LayerCovering(_edits, monitor.Device.Connector);
                var path = top?.Path ?? (top?.Color is uint color ? $"#{color:X8}" : null);
                if (monitor.AssignedPath != path)
                    monitor.AssignedPath = path;
            }
        }
        finally
        {
            _showingCurrentWallpaper = false;
        }
    }

    private async Task RefreshSpanPreviewsAsync() => await RefreshCompositePreviewAsync();

    private void RememberAssignment(IReadOnlyList<MonitorViewModel> targets, string path)
    {
        _showingCurrentWallpaper = true;
        try
        {
            foreach (var monitor in targets)
            {
                if (monitor.AssignedPath != path)
                    monitor.AssignedPath = path;
            }
        }
        finally
        {
            _showingCurrentWallpaper = false;
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
