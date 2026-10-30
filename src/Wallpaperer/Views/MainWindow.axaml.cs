using Avalonia.Controls;
using Wallpaperer.Services;
using Wallpaperer.ViewModels;

namespace Wallpaperer.Views;

public partial class MainWindow : Window
{
    private bool _closeAfterApply;

    public MainWindow()
    {
        InitializeComponent();
        var processes = new SystemProcessRunner();
        DataContext = new MainWindowViewModel(
            new MutterMonitorLayoutSource(processes),
            new GnomeWallpaperApplier(processes),
            new ThumbnailCache(ThumbnailCache.DefaultDirectory()),
            UiSettingsStore.Load(UiSettingsStore.DefaultPath()));
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        Screens.Changed += OnScreensChanged;
        _ = viewModel.StartAsync(Screens);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Shutdown();
            if (viewModel.IsSettingWallpaper)
            {
                e.Cancel = true;
                if (!_closeAfterApply)
                {
                    _closeAfterApply = true;
                    viewModel.ApplyFinished += (_, _) => Close();
                }

                return;
            }
        }

        base.OnClosing(e);
    }

    private void OnScreensChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
            _ = viewModel.ReloadMonitorsAsync(Screens);
    }
}
