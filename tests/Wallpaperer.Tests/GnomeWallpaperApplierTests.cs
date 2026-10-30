using Wallpaperer.Services;

namespace Wallpaperer.Tests;

public class GnomeWallpaperApplierTests
{
    [Fact]
    public async Task Apply_sets_both_uris_then_spanned_and_deletes_older_span_files()
    {
        var root = TempDirectory();
        try
        {
            var current = Path.Combine(root, "span-20261029-120000.jpg");
            var older = Path.Combine(root, "span-20260101-000000.jpg");
            await File.WriteAllTextAsync(current, "new");
            await File.WriteAllTextAsync(older, "old");
            var runner = new FakeRunner();
            var applier = new GnomeWallpaperApplier(runner);

            await applier.ApplyAsync(current, CancellationToken.None);

            var quoted = GnomeWallpaperApplier.Quote(new Uri(current).AbsoluteUri);
            Assert.Equal(
            [
                "set org.gnome.desktop.background picture-uri " + quoted,
                "set org.gnome.desktop.background picture-uri-dark " + quoted,
                "set org.gnome.desktop.background picture-options " + GnomeWallpaperApplier.Quote("spanned"),
            ], runner.Calls.Select(call => string.Join(' ', call.Arguments)));
            Assert.True(File.Exists(current));
            Assert.False(File.Exists(older));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_failed_gsettings_call_keeps_the_previous_span_file()
    {
        var root = TempDirectory();
        try
        {
            var current = Path.Combine(root, "my file.jpg");
            var older = Path.Combine(root, "span-old.jpg");
            await File.WriteAllTextAsync(current, "new");
            await File.WriteAllTextAsync(older, "old");
            var runner = new FakeRunner { FailOnCall = 2 };
            var applier = new GnomeWallpaperApplier(runner);

            var error = await Assert.ThrowsAsync<WallpaperApplyException>(() => applier.ApplyAsync(current, CancellationToken.None));

            Assert.Equal("nope", error.Message);
            Assert.True(File.Exists(older));
            Assert.Contains("%20", runner.Calls[0].Arguments[3]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "wallpaperer-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeRunner : IProcessRunner
    {
        public List<Call> Calls { get; } = [];

        public int FailOnCall { get; set; } = -1;

        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Calls.Add(new Call(fileName, arguments.ToArray()));
            if (Calls.Count == FailOnCall)
                return Task.FromResult(new ProcessResult(1, "", "nope"));

            return Task.FromResult(new ProcessResult(0, "", ""));
        }
    }

    private sealed record Call(string FileName, string[] Arguments);
}
