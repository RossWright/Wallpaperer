using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Wallpaperer.ViewModels;

namespace Wallpaperer.Controls;

public partial class PictureGallery : UserControl
{
    private const double Cell = 120;
    private const double Gap = 8;

    private MainWindowViewModel? _viewModel;
    private int _columns = -1;
    private IReadOnlyList<PictureItemViewModel>? _source;
    private bool _showHiddenLink;

    public PictureGallery()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        _columns = -1;
        Rebuild();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Rebuild();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.Pictures) or nameof(MainWindowViewModel.ShowHiddenLink))
        {
            _columns = -1;
            Rebuild();
        }
    }

    private void Rebuild()
    {
        if (_viewModel is null || Bounds.Width <= 1)
            return;

        var columns = Math.Max(1, (int)Math.Floor((Bounds.Width + Gap) / (Cell + Gap)));
        var source = _viewModel.Pictures;
        var showHiddenLink = _viewModel.ShowHiddenLink;
        if (columns == _columns && ReferenceEquals(source, _source) && showHiddenLink == _showHiddenLink)
            return;

        _columns = columns;
        _source = source;
        _showHiddenLink = showHiddenLink;
        var rows = new List<object>();
        for (var index = 0; index < source.Count; index += columns)
        {
            var count = Math.Min(columns, source.Count - index);
            var slice = new PictureItemViewModel[count];
            for (var column = 0; column < count; column++)
                slice[column] = source[index + column];
            rows.Add(new PictureRow(slice));
        }

        if (showHiddenLink)
            rows.Add(ShowHiddenRow.Instance);

        List.ItemsSource = rows;
    }

    private void OnShowHiddenClick(object? sender, RoutedEventArgs e)
    {
        _viewModel?.ShowHiddenImages();
        e.Handled = true;
    }
}
