using Avalonia.Input;
using Avalonia.Platform.Storage;
using Wallpaperer.Services;

namespace Wallpaperer.Controls;

public readonly record struct PictureDrop(string Path, bool Supported);

public static class PictureDrag
{
    public static readonly DataFormat<string> Format = DataFormat.CreateStringApplicationFormat("wallpaperer-path");

    public static PictureDrop? Read(IDataTransfer transfer)
    {
        var direct = transfer.TryGetValue(Format);
        if (!string.IsNullOrWhiteSpace(direct))
            return new PictureDrop(direct, PictureLibrary.IsSupportedImage(direct));

        var files = transfer.TryGetFiles()?.ToList();
        if (files is null || files.Count == 0)
            return null;

        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (path is not null && PictureLibrary.IsSupportedImage(path))
                return new PictureDrop(path, true);
        }

        var fallback = files[0].TryGetLocalPath() ?? files[0].Name;
        return new PictureDrop(fallback, false);
    }
}
