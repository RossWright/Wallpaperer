using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Wallpaperer.Models;
using Wallpaperer.Services;

namespace Wallpaperer.ViewModels;

public partial class PictureItemViewModel : ObservableObject
{
    private int _loadId;

    public PictureItemViewModel(PictureRecord record)
    {
        Record = record;
        ResolutionLabel = record.PixelWidth is int width && record.PixelHeight is int height && width > 0 && height > 0
            ? $"{width}×{height}"
            : "";
    }

    public PictureRecord Record { get; }

    public string Path => Record.Path;

    public string FileName => System.IO.Path.GetFileName(Record.Path);

    public string FolderName => System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(Record.Path) ?? "");

    public string Tooltip => Path;

    public string ResolutionLabel { get; }

    public bool HasResolution => ResolutionLabel.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResolutionWeight))]
    private bool resolutionIsExact;

    public FontWeight ResolutionWeight => ResolutionIsExact ? FontWeight.Bold : FontWeight.Normal;

    public void SetResolutionExact((int Width, int Height)? selection)
    {
        var exact = selection is { } size
            && Record.PixelWidth == size.Width
            && Record.PixelHeight == size.Height;
        if (ResolutionIsExact != exact)
            ResolutionIsExact = exact;
    }

    [ObservableProperty]
    private Bitmap? thumbnail;

    public async Task LoadThumbnailAsync(ThumbnailCache cache)
    {
        var loadId = Interlocked.Increment(ref _loadId);
        try
        {
            var file = await cache.GetJpegAsync(Path, 256, CancellationToken.None).ConfigureAwait(false);
            if (file is null || loadId != Volatile.Read(ref _loadId))
                return;

            var bytes = await File.ReadAllBytesAsync(file).ConfigureAwait(false);
            if (loadId != Volatile.Read(ref _loadId))
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (loadId != _loadId)
                    return;

                Thumbnail?.Dispose();
                using var stream = new MemoryStream(bytes);
                Thumbnail = new Bitmap(stream);
            });
        }
        catch (Exception)
        {
            // A bad file leaves an empty tile.
        }
    }

    public void ReleaseThumbnail()
    {
        Interlocked.Increment(ref _loadId);
        void Clear()
        {
            var bitmap = Thumbnail;
            Thumbnail = null;
            bitmap?.Dispose();
        }

        if (Dispatcher.UIThread.CheckAccess())
            Clear();
        else
            Dispatcher.UIThread.Post(Clear);
    }
}
