using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Wallpaperer.Services;
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
        var menu = new ContextMenu { MaxHeight = 720 };
        menu.Opening += (_, _) => PopulateMenu(menu);
        ContextMenu = menu;
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
        e.Pointer.Capture(this);
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
        e.Pointer.Capture(null);
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

    private async void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var press = _press;
        var item = _item;
        _press = null;
        e.Pointer.Capture(null);
        if (press is null || _dragging || item is null || e.InitialPressMouseButton != MouseButton.Left)
            return;

        if (TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel viewModel)
            await viewModel.AssignSelectionAsync(item.Path);
    }

    private void PopulateMenu(ContextMenu menu)
    {
        menu.Items.Clear();
        if (_item is null)
            return;

        var viewModel = TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;
        var picture = _item;
        menu.Items.Add(CommandItem("move image to top", () => viewModel?.MoveImageToTop(picture)));
        menu.Items.Add(CommandItem("move folder to top", () => viewModel?.MoveFolderToTop(picture)));
        menu.Items.Add(CommandItem("hide this image", () => viewModel?.HideImage(picture)));

        var hideFolder = new MenuItem { Header = "hide folder" };
        foreach (var folder in PictureLibraryOrganization.AncestorFolders(picture.Path))
        {
            var chosen = folder;
            hideFolder.Items.Add(CommandItem(
                PictureLibraryOrganization.DisplayFolder(chosen),
                () => viewModel?.HideFolder(chosen)));
        }

        menu.Items.Add(hideFolder);
        if (viewModel is null || !viewModel.IsPictureHidden(picture.Path))
            return;

        menu.Items.Add(new Separator());
        menu.Items.Add(CommandItem("show this image", () => viewModel.ShowImage(picture)));
        var covered = viewModel.HiddenFoldersCovering(picture.Path);
        if (covered.Count == 0)
            return;

        var showFolder = new MenuItem { Header = "show folder" };
        foreach (var folder in covered)
        {
            var chosen = folder;
            showFolder.Items.Add(CommandItem(
                PictureLibraryOrganization.DisplayFolder(chosen),
                () => viewModel.ShowFolder(chosen)));
        }

        menu.Items.Add(showFolder);
    }

    private static MenuItem CommandItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, args) =>
        {
            action();
            args.Handled = true;
        };
        return item;
    }
}
