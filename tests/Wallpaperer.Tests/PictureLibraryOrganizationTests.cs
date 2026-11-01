using Wallpaperer.Services;

namespace Wallpaperer.Tests;

public class PictureLibraryOrganizationTests
{
    private static readonly string[] Pictures =
    [
        "/home/ross/Pictures/new.jpg",
        "/home/ross/Pictures/Trip/a.jpg",
        "/home/ross/Pictures/Trip/b.jpg",
        "/home/ross/Pictures/old.jpg",
    ];

    [Fact]
    public void Pins_and_hides_keep_folder_mates_together_and_survive_a_reload()
    {
        var pins = new List<LibraryPin>();
        var hiddenImages = new List<string>();
        var hiddenFolders = new List<string>();
        var shownImages = new List<string>();

        PictureLibraryOrganization.MoveImageToTop(pins, hiddenImages, hiddenFolders, shownImages, Pictures[3]);
        PictureLibraryOrganization.MoveFolderToTop(pins, hiddenImages, hiddenFolders, shownImages, Pictures[1], Pictures);
        var topped = PictureLibraryOrganization.Arrange(Pictures, pins, hiddenImages, hiddenFolders, shownImages, showHidden: false);
        Assert.Equal(
            ["/home/ross/Pictures/Trip/a.jpg", "/home/ross/Pictures/Trip/b.jpg", "/home/ross/Pictures/old.jpg", "/home/ross/Pictures/new.jpg"],
            topped.Paths);

        PictureLibraryOrganization.HideImage(pins, hiddenImages, shownImages, "/home/ross/Pictures/old.jpg");
        PictureLibraryOrganization.HideFolder(pins, hiddenImages, hiddenFolders, shownImages, "/home/ross/Pictures/Trip");
        var hidden = PictureLibraryOrganization.Arrange(Pictures, pins, hiddenImages, hiddenFolders, shownImages, showHidden: false);
        Assert.Equal(["/home/ross/Pictures/new.jpg"], hidden.Paths);
        Assert.Equal(3, hidden.HiddenCount);

        var revealed = PictureLibraryOrganization.Arrange(Pictures, pins, hiddenImages, hiddenFolders, shownImages, showHidden: true);
        Assert.Equal(
            ["/home/ross/Pictures/new.jpg", "/home/ross/Pictures/Trip/a.jpg", "/home/ross/Pictures/Trip/b.jpg", "/home/ross/Pictures/old.jpg"],
            revealed.Paths);

        var directory = Path.Combine(Path.GetTempPath(), "wallpaperer-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "ui-settings.json");
        try
        {
            var store = new UiSettingsStore(path, "3840x2160", autoApply: true, pins, hiddenImages, hiddenFolders, shownImages);
            store.Save();
            var loaded = UiSettingsStore.Load(path);
            Assert.Equal("3840x2160", loaded.SizeFilterId);
            Assert.True(loaded.AutoApply);
            Assert.Equal(hiddenFolders, loaded.HiddenFolders);
            Assert.Equal(hiddenImages, loaded.HiddenImages);
            Assert.Equal(pins, loaded.Pins);
            Assert.Equal(shownImages, loaded.ShownImages);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Moving_one_image_out_of_a_hidden_folder_leaves_the_rest_hidden()
    {
        var pins = new List<LibraryPin>();
        var hiddenImages = new List<string>();
        var hiddenFolders = new List<string>();
        var shownImages = new List<string>();
        PictureLibraryOrganization.HideFolder(pins, hiddenImages, hiddenFolders, shownImages, "/home/ross/Pictures/Trip");
        PictureLibraryOrganization.MoveImageToTop(pins, hiddenImages, hiddenFolders, shownImages, "/home/ross/Pictures/Trip/b.jpg");

        var arranged = PictureLibraryOrganization.Arrange(Pictures, pins, hiddenImages, hiddenFolders, shownImages, showHidden: false);
        Assert.Equal(["/home/ross/Pictures/Trip/b.jpg", "/home/ross/Pictures/new.jpg", "/home/ross/Pictures/old.jpg"], arranged.Paths);
        Assert.Equal(1, arranged.HiddenCount);
    }

    [Fact]
    public void Hiding_a_parent_folder_hides_copies_in_its_subfolders()
    {
        var pictures = new[]
        {
            "/home/ross/Pictures/Sets/One/cat.jpg",
            "/home/ross/Pictures/Sets/Two/cat.jpg",
            "/home/ross/Pictures/keep.jpg",
        };
        var pins = new List<LibraryPin>();
        var hiddenImages = new List<string>();
        var hiddenFolders = new List<string>();
        var shownImages = new List<string>();
        PictureLibraryOrganization.HideFolder(pins, hiddenImages, hiddenFolders, shownImages, "/home/ross/Pictures/Sets");

        var hidden = PictureLibraryOrganization.Arrange(pictures, pins, hiddenImages, hiddenFolders, shownImages, showHidden: false);
        Assert.Equal(["/home/ross/Pictures/keep.jpg"], hidden.Paths);

        PictureLibraryOrganization.ShowImage(hiddenImages, hiddenFolders, shownImages, pictures[0]);
        var oneShown = PictureLibraryOrganization.Arrange(pictures, pins, hiddenImages, hiddenFolders, shownImages, showHidden: false);
        Assert.Equal(["/home/ross/Pictures/Sets/One/cat.jpg", "/home/ross/Pictures/keep.jpg"], oneShown.Paths);

        PictureLibraryOrganization.ShowFolder(hiddenFolders, "/home/ross/Pictures/Sets");
        var restored = PictureLibraryOrganization.Arrange(pictures, pins, hiddenImages, hiddenFolders, shownImages, showHidden: false);
        Assert.Equal(pictures, restored.Paths);
    }

    [Fact]
    public void The_hide_folder_menu_lists_ancestors_up_through_home()
    {
        var home = PictureLibraryOrganization.Normalize(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var image = Path.Combine(home, "Pictures", "Sets", "One", "cat.jpg");
        var ancestors = PictureLibraryOrganization.AncestorFolders(image);

        Assert.Equal(
            [
                Path.Combine(home, "Pictures", "Sets", "One"),
                Path.Combine(home, "Pictures", "Sets"),
                Path.Combine(home, "Pictures"),
                home,
            ],
            ancestors);
        Assert.Equal("Pictures/Sets/One", PictureLibraryOrganization.DisplayFolder(ancestors[0]));
    }
}