using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Wallpaperer.Models;
using Wallpaperer.ViewModels;
using Wallpaperer.Views;

namespace Wallpaperer.Controls;

public partial class MonitorTile : UserControl
{
    private MonitorViewModel? _monitor;

    public MonitorTile()
    {
        InitializeComponent();
        var menu = new ContextMenu { MaxHeight = 720 };
        var clear = new MenuItem { Header = "Clear screen" };
        clear.Click += async (_, _) => await ClearScreenAsync();
        var fit = new MenuItem { Header = "Fit" };
        foreach (var mode in new[] { ImageFit.Zoom, ImageFit.Fit, ImageFit.Stretch, ImageFit.Center, ImageFit.Tile })
        {
            var item = new MenuItem { Header = mode.ToString() };
            var chosen = mode;
            item.Click += async (_, _) => await FitAsync(chosen);
            fit.Items.Add(item);
        }

        menu.Items.Add(clear);
        menu.Items.Add(fit);
        ContextMenu = menu;
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
        if (e.PropertyName is nameof(MonitorViewModel.IsDropHot) or nameof(MonitorViewModel.IsSelected) or null)
            UpdateFrame();
    }

    private void UpdateFrame()
    {
        if (_monitor is { IsDropHot: true } or { IsSelected: true })
        {
            Frame.BorderBrush = new SolidColorBrush(Color.Parse("#4EA1FF"));
            Frame.BorderThickness = new Thickness(_monitor.IsDropHot ? 3 : 2);
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

    private async Task ClearScreenAsync()
    {
        if (_monitor is null || TopLevel.GetTopLevel(this) is not Window window)
            return;
        if (window.DataContext is not MainWindowViewModel viewModel)
            return;

        var color = await ColorDialog.Pick(window, viewModel.BackgroundColor);
        if (color is uint argb)
            await viewModel.ApplyScreenColorAsync(_monitor, argb);
    }

    private async Task FitAsync(ImageFit fit)
    {
        if (_monitor is null || TopLevel.GetTopLevel(this)?.DataContext is not MainWindowViewModel viewModel)
            return;

        await viewModel.ApplyFitAsync(_monitor, fit);
    }
}
