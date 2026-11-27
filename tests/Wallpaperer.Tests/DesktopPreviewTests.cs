using Wallpaperer.Services;
using Wallpaperer.ViewModels;

namespace Wallpaperer.Tests;

public class DesktopPreviewTests
{
    [Fact]
    public async Task Preview_asks_the_desktop_to_hide_windows_until_dismissed()
    {
        var root = Path.Combine(Path.GetTempPath(), "wallpaperer-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        var helper = Path.Combine(root, "preview-desktop.py");
        var token = Path.Combine(root, "token");
        await File.WriteAllTextAsync(helper, "# helper\n");
        var runner = new ScriptedRunner();
        try
        {
            var preview = new GnomeDesktopPreview(runner, helper, token);
            await preview.ShowUntilDismissedAsync(CancellationToken.None);

            var call = Assert.Single(runner.Calls);
            Assert.Equal("python3", call.File);
            Assert.Contains(helper, call.Args);
            Assert.Contains(token, call.Args);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_denied_preview_explains_the_permission()
    {
        var root = Path.Combine(Path.GetTempPath(), "wallpaperer-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        var helper = Path.Combine(root, "preview-desktop.py");
        await File.WriteAllTextAsync(helper, "# helper\n");
        var runner = new ScriptedRunner { ExitCode = 2, Error = "Permission was not granted." };
        try
        {
            var preview = new GnomeDesktopPreview(runner, helper, Path.Combine(root, "token"));

            var error = await Assert.ThrowsAsync<DesktopPreviewException>(() => preview.ShowUntilDismissedAsync(CancellationToken.None));

            Assert.Equal("Desktop preview needs permission to send the show-desktop shortcut.", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Preview_brings_the_windows_back_and_keeps_the_status()
    {
        var directory = TempDirectory();
        var preview = new RecordingPreview();
        try
        {
            var viewModel = ViewModel(directory, preview);
            viewModel.Status = "12 images";

            await viewModel.PreviewCommand.ExecuteAsync(null);

            Assert.Equal(1, preview.Shows);
            Assert.Equal("12 images", viewModel.Status);
            Assert.False(viewModel.IsPreviewing);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task A_failed_preview_explains_what_happened()
    {
        var directory = TempDirectory();
        var preview = new RecordingPreview
        {
            Failure = new DesktopPreviewException("Could not enable the desktop preview."),
        };
        try
        {
            var viewModel = ViewModel(directory, preview);

            await viewModel.PreviewCommand.ExecuteAsync(null);

            Assert.Equal("Could not enable the desktop preview.", viewModel.Status);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string TempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "wallpaperer-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static MainWindowViewModel ViewModel(string directory, IDesktopPreview preview) =>
        new(
            new MutterMonitorLayoutSource(new IdleProcessRunner()),
            new GnomeWallpaperApplier(new IdleProcessRunner()),
            new ThumbnailCache(directory),
            new UiSettingsStore(Path.Combine(directory, "ui-settings.json"), null),
            EmptyCurrentWallpaperSource.Instance,
            wallpaperDirectory: directory,
            preview: preview);

    private sealed class RecordingPreview : IDesktopPreview
    {
        public int Shows { get; private set; }

        public Exception? Failure { get; init; }

        public Task ShowUntilDismissedAsync(CancellationToken cancellationToken)
        {
            Shows++;
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }

        public Task RestoreAsync() => Task.CompletedTask;
    }

    private sealed class ScriptedRunner : IProcessRunner
    {
        public List<(string File, string Args)> Calls { get; } = [];

        public int ExitCode { get; init; }

        public string Error { get; init; } = "";

        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Calls.Add((fileName, string.Join(' ', arguments)));
            return Task.FromResult(new ProcessResult(ExitCode, "", Error));
        }
    }

    private sealed class IdleProcessRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
            Task.FromResult(new ProcessResult(0, "", ""));
    }
}
