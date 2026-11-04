namespace Wallpaperer.Models;

public sealed record ImagePlacement(
    string? Path,
    IReadOnlyList<string> Connectors,
    IReadOnlyList<string>? Visible = null,
    ImageFit Fit = ImageFit.Zoom,
    uint? Color = null,
    long Token = 0);
