using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Wallpaperer.ViewModels;

namespace Wallpaperer.Controls;

public partial class MonitorArrangement : UserControl
{
    private MainWindowViewModel? _viewModel;

    public MonitorArrangement()
    {
        InitializeComponent();
        Loaded += (_, _) => Sync();
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
}
