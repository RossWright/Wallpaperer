namespace Wallpaperer.Models;

public sealed record MonitorDevice(
    string Connector,
    string DisplayName,
    int X,
    int Y,
    int Width,
    int Height,
    double Scale,
    bool IsPrimary,
    int ModeWidth,
    int ModeHeight);
