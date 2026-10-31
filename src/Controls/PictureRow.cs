using Wallpaperer.ViewModels;

namespace Wallpaperer.Controls;

public sealed class PictureRow(IReadOnlyList<PictureItemViewModel> items)
{
    public IReadOnlyList<PictureItemViewModel> Items { get; } = items;
}
