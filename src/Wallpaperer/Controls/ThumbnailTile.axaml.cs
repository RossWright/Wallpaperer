using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Wallpaperer.ViewModels;

namespace Wallpaperer.Controls;

public partial class ThumbnailTile : UserControl
{
    private PictureItemViewModel? _item;
    private PointerPressedEventArgs? _press;
    private Point _origin;
    private bool _dragging;

    public ThumbnailTile()
    {
        InitializeComponent();
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        BindItem();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        BindItem();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _press = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void BindItem()
    {
        var next = DataContext as PictureItemViewModel;
        if (!ReferenceEquals(next, _item))
        {
            if (next is not null)
                _item?.ReleaseThumbnail();
            _item = next;
        }

        if (_item is null || _item.Thumbnail is not null)
            return;

        if (TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel viewModel)
            _ = _item.LoadThumbnailAsync(viewModel.Thumbnails);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_item is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        _press = e;
        _origin = e.GetPosition(this);
        _dragging = false;
        e.Handled = true;
    }

    private async void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_press is null || _dragging || _item is null)
            return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _press = null;
            return;
        }

        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _origin.X) < 6 && Math.Abs(point.Y - _origin.Y) < 6)
            return;

        _dragging = true;
        var press = _press;
        _press = null;
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(PictureDrag.Format, _item.Path));
        try
        {
            await DragDrop.DoDragDropAsync(press, transfer, DragDropEffects.Copy);
        }
        catch (Exception)
        {
            // The pointer was released before the drag platform accepted it.
        }
        finally
        {
            _dragging = false;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e) => _press = null;
}
