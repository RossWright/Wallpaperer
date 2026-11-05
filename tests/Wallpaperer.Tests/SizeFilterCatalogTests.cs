using Wallpaperer.Models;
using Wallpaperer.Services;

namespace Wallpaperer.Tests;

public class SizeFilterCatalogTests
{
    [Fact]
    public void Band_matches_inside_tolerance_and_rejects_other_sizes_and_orientations()
    {
        var band = new SizeBand(3840, 2160);
        Assert.True(band.Contains(3840, 2160));
        Assert.True(band.Contains(4000, 2250));
        Assert.False(band.Contains(1920, 1080));
        Assert.False(band.Contains(2160, 3840));
        Assert.False(band.Contains(7680, 4320));

        var option = new SizeFilterOption("3840x2160", "3840×2160", [band]);
        Assert.False(option.Matches(null, null));
        Assert.True(new SizeFilterOption(SizeFilterCatalog.AnyId, "Any", []).Matches(null, null));
    }

    [Fact]
    public void Four_monitor_layout_offers_any_4k_and_the_three_span_sizes()
    {
        var monitors = Desk();
        var options = SizeFilterCatalog.Build(monitors);

        Assert.Equal(SizeFilterCatalog.AnyId, options[0].Id);
        Assert.Contains(options, option => option.Id == "3840x2160" && option.Label == "3840×2160");
        Assert.Contains(options, option => option.Id == "2-wide-7680x2160" && option.Label == "7680×2160");
        Assert.Contains(options, option => option.Id == "2-tall-3840x4320" && option.Label == "3840×4320");
        Assert.Contains(options, option => option.Id == "3-wide-11520x2160" && option.Label == "11520×2160");
        Assert.Equal(3, options.Count(option => option.Id.Contains("-wide-") || option.Id.Contains("-tall-")));
        Assert.DoesNotContain(options, option => option.Id.Contains("4-wide") || option.Id.Contains("3-tall"));
        Assert.Equal("3840x2160", SizeFilterCatalog.DefaultId(monitors, options));
        Assert.Equal("3840x2160", SizeFilterCatalog.Select(options, null, monitors).Id);
        Assert.Equal(SizeFilterCatalog.AnyId, SizeFilterCatalog.Select(options, SizeFilterCatalog.AnyId, monitors).Id);
        Assert.Equal("3840x2160", SizeFilterCatalog.Select(options, "2-wide-missing", monitors).Id);
    }

    [Fact]
    public void Mixed_monitor_sizes_default_to_the_union()
    {
        var monitors = new List<MonitorDevice>
        {
            Device("A", 0, 0, 3840, 2160),
            Device("B", 3840, 0, 1920, 1080),
        };
        var options = SizeFilterCatalog.Build(monitors);
        var monitorsOption = Assert.Single(options, option => option.Id == SizeFilterCatalog.MonitorsId);

        Assert.Equal("3840x2160", SizeFilterCatalog.DefaultId(monitors, options));
        Assert.Contains("3840×2160", monitorsOption.Label);
        Assert.Contains("1920×1080", monitorsOption.Label);
        Assert.True(monitorsOption.Matches(3840, 2160));
        Assert.True(monitorsOption.Matches(1920, 1080));
        Assert.False(monitorsOption.Matches(1280, 720));
    }

    [Fact]
    public void A_span_matches_its_resolution_and_the_full_desk_uses_the_closest_aspect()
    {
        var monitors = Desk();
        var options = SizeFilterCatalog.Build(monitors);

        Assert.Equal("3840x2160", SizeFilterCatalog.MatchSelection(options, [monitors[2]])?.Id);
        Assert.Equal("2-wide-7680x2160", SizeFilterCatalog.MatchSelection(options, [monitors[1], monitors[2]])?.Id);
        Assert.Equal("11520x4320", SizeFilterCatalog.MatchSelection(options, monitors)?.Id);
        Assert.Null(SizeFilterCatalog.MatchSelection(options, []));
    }

    [Fact]
    public void Aspect_ratio_groups_a_monitor_and_its_span_and_keeps_other_sizes_for_scaling()
    {
        var monitors = Desk();
        var aspects = SizeFilterCatalog.BuildAspects(monitors);
        var resolutions = SizeFilterCatalog.Build(monitors);

        Assert.Equal("16:9", SizeFilterCatalog.AspectOf(3840, 2160).Id);
        Assert.Equal("32:9", SizeFilterCatalog.AspectOf(7680, 2160).Id);
        Assert.Equal("21:9", SizeFilterCatalog.AspectOf(3440, 1440).Id);
        Assert.Equal("16:10", SizeFilterCatalog.AspectOf(1920, 1200).Id);
        Assert.Equal("16:3", SizeFilterCatalog.AspectOf(11520, 2160).Id);
        Assert.Contains(aspects, option => option.Id == "16:9");
        Assert.Contains(aspects, option => option.Id == "32:9");
        Assert.Equal("16:9", SizeFilterCatalog.DefaultAspectId(monitors, aspects));

        var widescreen = aspects.Single(option => option.Id == "16:9");
        Assert.True(widescreen.Matches(4000, 2250));
        Assert.False(widescreen.Matches(1920, 1200));
        var sized = SizeFilterCatalog.ForAspect(resolutions, widescreen);
        Assert.Contains(sized, option => option.Id == SizeFilterCatalog.AnyId);
        Assert.Contains(sized, option => option.Id == "1920x1080");
        Assert.Contains(sized, option => option.Id == "3840x2160");
        Assert.DoesNotContain(sized, option => option.Id == "2-wide-7680x2160");
        Assert.DoesNotContain(sized, option => option.Id == "1920x1200");
        Assert.DoesNotContain(sized, option => option.Id == "5760x1080");
    }

    [Fact]
    public void A_three_wide_row_also_offers_three_1080p_screens()
    {
        var options = SizeFilterCatalog.Build(Desk());
        var screens = Assert.Single(options, option => option.Id == "5760x1080");

        Assert.Equal("5760×1080", screens.Label);
        Assert.True(screens.Matches(5760, 1080));
        Assert.Contains(options, option => option.Id == "3840x1080");
        Assert.DoesNotContain(options, option => option.Id == "11520x2160");

        var spanned = SizeFilterCatalog.AspectOf(11520, 2160);
        var sized = SizeFilterCatalog.ForAspect(options, spanned);
        Assert.Contains(sized, option => option.Id == "5760x1080");
        Assert.Contains(sized, option => option.Id == "3-wide-11520x2160");
        Assert.DoesNotContain(sized, option => option.Id == "3840x1080");
    }

    [Fact]
    public void A_monitor_a_hundred_pixels_away_is_not_part_of_the_span()
    {
        var monitors = Desk();
        monitors.Add(Device("FAR", 11620, 2160, 3840, 2160));
        var options = SizeFilterCatalog.Build(monitors);

        Assert.Contains(options, option => option.Id == "3-wide-11520x2160");
        Assert.DoesNotContain(options, option => option.Label.Contains("15360") || option.Id.Contains("4-wide"));
    }

    private static List<MonitorDevice> Desk() =>
    [
        Device("HDMI-2", 3840, 0, 3840, 2160),
        Device("DP-4", 0, 2160, 3840, 2160),
        Device("DP-3", 3840, 2160, 3840, 2160),
        Device("DP-5", 7680, 2160, 3840, 2160),
    ];

    private static MonitorDevice Device(string connector, int x, int y, int width, int height) =>
        new(connector, connector, x, y, width, height, 1, connector == "DP-3", width, height);
}
