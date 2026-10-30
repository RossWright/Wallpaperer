namespace Wallpaperer.Models;

public sealed record PictureRecord(string Path, DateTime LastWriteTimeUtc, int? PixelWidth = null, int? PixelHeight = null)
{
    public static int CompareNewest(PictureRecord left, PictureRecord right)
    {
        var byTime = right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc);
        return byTime != 0 ? byTime : string.Compare(left.Path, right.Path, StringComparison.Ordinal);
    }
}
