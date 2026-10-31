using System.Security.Cryptography;
using System.Text;
using SkiaSharp;

namespace Wallpaperer.Services;

public sealed class ThumbnailCache
{
    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(4, 4);

    public ThumbnailCache(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    public static string DefaultDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".cache", "wallpaperer", "thumbnails");
    }

    public async Task<string?> GetJpegAsync(string sourcePath, int maxEdge, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return CreateJpeg(sourcePath, maxEdge);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string? CreateJpeg(string sourcePath, int maxEdge)
    {
        FileInfo info;
        try
        {
            info = new FileInfo(sourcePath);
            if (!info.Exists)
                return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var key = $"{sourcePath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{maxEdge}";
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant() + ".jpg";
        var target = Path.Combine(_directory, name);
        if (File.Exists(target) && new FileInfo(target).Length > 0)
            return target;

        using var bitmap = OrientedBitmap.Decode(sourcePath, maxEdge);
        if (bitmap is null)
            return null;

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 80);
        if (data is null)
            return null;

        var temporary = target + ".tmp";
        using (var stream = File.Create(temporary))
            data.SaveTo(stream);

        File.Move(temporary, target, overwrite: true);
        return target;
    }
}
