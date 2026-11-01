using Wallpaperer.ViewModels;

namespace Wallpaperer.Controls;

public sealed class PictureRow(IReadOnlyList<PictureItemViewModel> items)
{
    public IReadOnlyList<PictureItemViewModel> Items { get; } = items;
}

public sealed class ShowHiddenRow
{
    public static readonly ShowHiddenRow Instance = new();

    private ShowHiddenRow()
    {
    }
}
