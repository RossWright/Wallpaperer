using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Wallpaperer.ViewModels;

namespace Wallpaperer.Controls;

public partial class MonitorArrangement : UserControl
{
    private MainWindowViewModel? _viewModel;
    private string? _pressConnector;
    private Point _pressPoint;
    private bool _draggingSelection;
    private bool _extendSelection;

    public MonitorArrangement()
    {
        InitializeComponent();
        Loaded += (_, _) => Sync();
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += (_, _) => EndSelectionGesture();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
            _viewModel.Monitors.CollectionChanged -= OnMonitorsChanged;

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
            _viewModel.Monitors.CollectionChanged += OnMonitorsChanged;

        Sync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.Monitors.CollectionChanged -= OnMonitorsChanged;

        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Sync();
    }

    private void OnMonitorsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Sync();

    private void Sync()
    {
        if (_viewModel is null)
            return;

        _viewModel.ArrangeMonitors(Bounds.Width, Bounds.Height);
        var wanted = _viewModel.Monitors.ToDictionary(monitor => monitor.Device.Connector);
        foreach (var tile in Surface.Children.OfType<MonitorTile>().ToList())
        {
            if (tile.DataContext is not MonitorViewModel monitor || !wanted.ContainsKey(monitor.Device.Connector))
                Surface.Children.Remove(tile);
        }

        var existing = Surface.Children.OfType<MonitorTile>()
            .Where(tile => tile.DataContext is MonitorViewModel)
            .ToDictionary(tile => ((MonitorViewModel)tile.DataContext!).Device.Connector);

        foreach (var monitor in _viewModel.Monitors)
        {
            if (!existing.TryGetValue(monitor.Device.Connector, out var tile))
            {
                tile = new MonitorTile { DataContext = monitor };
                Surface.Children.Add(tile);
            }

            Canvas.SetLeft(tile, monitor.ViewX);
            Canvas.SetTop(tile, monitor.ViewY);
            tile.Width = monitor.ViewWidth;
            tile.Height = monitor.ViewHeight;
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (e.Source is Button || (e.Source is Visual visual && visual.FindAncestorOfType<Button>() is not null))
            return;

        var monitor = HitMonitor(e.GetPosition(Surface));
        if (monitor is null)
        {
            _viewModel.ClearSelection();
            e.Handled = true;
            return;
        }

        _pressConnector = monitor.Device.Connector;
        _pressPoint = e.GetPosition(this);
        _draggingSelection = false;
        _extendSelection = e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_viewModel is null || _pressConnector is null || _draggingSelection && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var point = e.GetPosition(this);
        if (!_draggingSelection
            && Math.Abs(point.X - _pressPoint.X) < 6
            && Math.Abs(point.Y - _pressPoint.Y) < 6)
            return;

        _draggingSelection = true;
        var under = HitMonitor(e.GetPosition(Surface));
        if (under is not null)
            _viewModel.SelectRange(_pressConnector, under.Device.Connector);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_viewModel is null || _pressConnector is null)
            return;

        if (!_draggingSelection)
        {
            if (_extendSelection)
                _viewModel.ExtendSelection(_pressConnector);
            else
                _viewModel.SelectOnly(_pressConnector);
        }

        EndSelectionGesture();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void EndSelectionGesture()
    {
        _pressConnector = null;
        _draggingSelection = false;
    }

    private MonitorViewModel? HitMonitor(Point point) =>
        _viewModel?.Monitors.FirstOrDefault(monitor =>
            point.X >= monitor.ViewX
            && point.Y >= monitor.ViewY
            && point.X < monitor.ViewX + monitor.ViewWidth
            && point.Y < monitor.ViewY + monitor.ViewHeight);
}
