using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Wallpaperer.ViewModels;

namespace Wallpaperer.Controls;

public partial class MonitorTile : UserControl
{
    private MonitorViewModel? _monitor;

    public MonitorTile()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_monitor is not null)
            _monitor.PropertyChanged -= OnMonitorPropertyChanged;

        _monitor = DataContext as MonitorViewModel;
        if (_monitor is not null)
            _monitor.PropertyChanged += OnMonitorPropertyChanged;

        UpdateFrame();
    }

    private void OnMonitorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MonitorViewModel.IsDropHot) or null)
            UpdateFrame();
    }

    private void UpdateFrame()
    {
        if (_monitor?.IsDropHot == true)
        {
            Frame.BorderBrush = new SolidColorBrush(Color.Parse("#4EA1FF"));
            Frame.BorderThickness = new Thickness(2);
        }
        else
        {
            Frame.BorderBrush = new SolidColorBrush(Color.Parse("#6A7382"));
            Frame.BorderThickness = new Thickness(1);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var drop = PictureDrag.Read(e.DataTransfer);
        if (drop is { Supported: true })
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
            if (_monitor is not null)
                _monitor.IsDropHot = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        var point = e.GetPosition(this);
        if (point.X < 0 || point.Y < 0 || point.X > Bounds.Width || point.Y > Bounds.Height)
        {
            if (_monitor is not null)
                _monitor.IsDropHot = false;
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (_monitor is null)
            return;

        var drop = PictureDrag.Read(e.DataTransfer);
        if (drop is null)
            return;

        e.Handled = true;
        _monitor.IsDropHot = false;
        if (TopLevel.GetTopLevel(this)?.DataContext is not MainWindowViewModel viewModel)
            return;

        await viewModel.AssignAsync(_monitor, drop.Value.Path);
    }
}
