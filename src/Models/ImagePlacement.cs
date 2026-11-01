namespace Wallpaperer.Models;

public sealed record ImagePlacement(
    string Path,
    IReadOnlyList<string> Connectors,
    IReadOnlyList<string>? Visible = null);
