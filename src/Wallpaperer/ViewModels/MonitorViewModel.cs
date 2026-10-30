using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wallpaperer.Models;

namespace Wallpaperer.ViewModels;

public partial class MonitorViewModel : ObservableObject
{
    private readonly Action<MonitorViewModel> _changed;
    private bool _suppressChange;

    public MonitorViewModel(MonitorDevice device, Action<MonitorViewModel> changed)
    {
        Device = device;
        _changed = changed;
    }

    public MonitorDevice Device { get; private set; }

    public string Title => Device.DisplayName;

    public string Detail => Device.IsPrimary
        ? $"{Device.Connector}  ·  {Device.ModeWidth}×{Device.ModeHeight}  ·  Primary"
        : $"{Device.Connector}  ·  {Device.ModeWidth}×{Device.ModeHeight}";

    public bool HasImage => AssignedPath is not null;

    [ObservableProperty]
    private string? assignedPath;

    [ObservableProperty]
    private Bitmap? preview;

    [ObservableProperty]
    private bool isDropHot;

    [ObservableProperty]
    private bool isSelected;

    public int? SpanGroupId { get; set; }

    [ObservableProperty]
    private double viewX;

    [ObservableProperty]
    private double viewY;

    [ObservableProperty]
    private double viewWidth;

    [ObservableProperty]
    private double viewHeight;

    public void UpdateDevice(MonitorDevice device)
    {
        Device = device;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Detail));
    }

    public void SetViewRect(double x, double y, double width, double height)
    {
        ViewX = x;
        ViewY = y;
        ViewWidth = width;
        ViewHeight = height;
    }

    public void SetPreview(Bitmap? bitmap)
    {
        var previous = Preview;
        Preview = bitmap;
        previous?.Dispose();
    }

    public void DisposePreview()
    {
        _suppressChange = true;
        AssignedPath = null;
        SetPreview(null);
        _suppressChange = false;
    }

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        AssignedPath = null;
        SetPreview(null);
    }

    private bool CanClear() => HasImage;

    partial void OnAssignedPathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasImage));
        ClearCommand.NotifyCanExecuteChanged();
        if (!_suppressChange)
            _changed(this);
    }
}
