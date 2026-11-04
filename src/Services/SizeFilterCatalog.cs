using Wallpaperer.Models;

namespace Wallpaperer.Services;

public readonly record struct SizeBand(int Width, int Height)
{
    public bool Contains(int width, int height)
    {
        if (width <= 0 || height <= 0 || Width <= 0 || Height <= 0)
            return false;

        var tolerance = SizeFilterCatalog.Tolerance;
        var minWidth = Width * (1 - tolerance);
        var maxWidth = Width * (1 + tolerance);
        var minHeight = Height * (1 - tolerance);
        var maxHeight = Height * (1 + tolerance);
        return width >= minWidth && width <= maxWidth && height >= minHeight && height <= maxHeight;
    }
}

public sealed record SizeFilterOption(string Id, string Label, IReadOnlyList<SizeBand> Bands)
{
    public int Count { get; init; } = -1;

    public bool Matches(int? width, int? height)
    {
        if (Id == SizeFilterCatalog.AnyId)
            return true;
        if (width is not int pixelWidth || height is not int pixelHeight)
            return false;

        return Bands.Any(band => band.Contains(pixelWidth, pixelHeight));
    }

    public override string ToString() => Count < 0 ? Label : $"{Label} · {Count}";
}

public static class SizeFilterCatalog
{
    public const string AnyId = "any";
    public const string MonitorsId = "monitors";
    public const double Tolerance = 0.12;

    private static readonly (int Width, int Height)[] PopularResolutions =
    [
        (1280, 720),
        (1366, 768),
        (1600, 900),
        (1920, 1080),
        (1920, 1200),
        (2560, 1080),
        (2560, 1440),
        (2560, 1600),
        (3440, 1440),
        (3840, 2160),
        (5120, 1440),
        (5120, 2160),
        (7680, 4320),
    ];

    public static string ResolutionId(int width, int height) => $"{width}x{height}";

    public static IReadOnlyList<SizeFilterOption> Build(IReadOnlyList<MonitorDevice> monitors)
    {
        var options = new List<SizeFilterOption>
        {
            new(AnyId, "Any", []),
        };
        var seen = new HashSet<string>(StringComparer.Ordinal) { AnyId };
        var monitorSizes = DistinctMonitorSizes(monitors);

        if (monitorSizes.Count > 1)
        {
            var label = "Monitor sizes · " + string.Join(", ", monitorSizes.Select(size => $"{size.Width}×{size.Height}"));
            options.Add(new SizeFilterOption(MonitorsId, label, monitorSizes.Select(size => new SizeBand(size.Width, size.Height)).ToList()));
            seen.Add(MonitorsId);
        }

        foreach (var size in PopularResolutions.Concat(monitorSizes).Distinct().OrderBy(size => size.Width).ThenBy(size => size.Height))
        {
            var id = ResolutionId(size.Width, size.Height);
            if (!seen.Add(id))
                continue;

            options.Add(new SizeFilterOption(id, $"{size.Width}×{size.Height}", [new SizeBand(size.Width, size.Height)]));
        }

        foreach (var span in SpanOptions(monitors))
        {
            if (seen.Add(span.Id))
                options.Add(span);
        }

        return options;
    }

    public static string DefaultId(IReadOnlyList<MonitorDevice> monitors, IReadOnlyList<SizeFilterOption> options)
    {
        MonitorDevice? biggest = null;
        long area = -1;
        foreach (var monitor in monitors)
        {
            if (monitor.ModeWidth <= 0 || monitor.ModeHeight <= 0)
                continue;

            var pixels = (long)monitor.ModeWidth * monitor.ModeHeight;
            if (pixels > area || (pixels == area && biggest is not null && monitor.ModeWidth > biggest.ModeWidth))
            {
                area = pixels;
                biggest = monitor;
            }
        }

        if (biggest is not null)
        {
            var id = ResolutionId(biggest.ModeWidth, biggest.ModeHeight);
            if (options.Any(option => option.Id == id))
                return id;
        }

        return AnyId;
    }

    public static SizeFilterOption Select(
        IReadOnlyList<SizeFilterOption> options,
        string? savedId,
        IReadOnlyList<MonitorDevice> monitors)
    {
        if (!string.IsNullOrEmpty(savedId))
        {
            var saved = options.FirstOrDefault(option => option.Id == savedId);
            if (saved is not null)
                return saved;
        }

        var fallback = DefaultId(monitors, options);
        return options.First(option => option.Id == fallback);
    }

    public static SizeFilterOption? MatchSelection(
        IReadOnlyList<SizeFilterOption> options,
        IReadOnlyList<MonitorDevice> selected)
    {
        if (selected.Count == 0)
            return null;

        var left = selected.Min(monitor => monitor.X);
        var top = selected.Min(monitor => monitor.Y);
        var right = selected.Max(monitor => monitor.X + monitor.Width);
        var bottom = selected.Max(monitor => monitor.Y + monitor.Height);
        var width = right - left;
        var height = bottom - top;
        if (width <= 0 || height <= 0)
            return null;

        var exact = options.FirstOrDefault(option =>
            option.Bands.Any(band => band.Width == width && band.Height == height));
        if (exact is not null)
            return exact;

        var aspect = width / (double)height;
        SizeFilterOption? closest = null;
        var closestAspect = double.MaxValue;
        var closestPixels = int.MaxValue;
        foreach (var option in options)
        {
            if (option.Id is AnyId or MonitorsId || option.Bands.Count == 0)
                continue;

            var band = option.Bands[0];
            if (band.Width <= 0 || band.Height <= 0)
                continue;

            var aspectDistance = Math.Abs(band.Width / (double)band.Height - aspect);
            var pixelDistance = Math.Abs(band.Width - width) + Math.Abs(band.Height - height);
            if (aspectDistance < closestAspect - 0.0001
                || (Math.Abs(aspectDistance - closestAspect) <= 0.0001 && pixelDistance < closestPixels))
            {
                closest = option;
                closestAspect = aspectDistance;
                closestPixels = pixelDistance;
            }
        }

        return closest;
    }

    private static List<(int Width, int Height)> DistinctMonitorSizes(IReadOnlyList<MonitorDevice> monitors) =>
        monitors
            .Where(monitor => monitor.ModeWidth > 0 && monitor.ModeHeight > 0)
            .Select(monitor => (monitor.ModeWidth, monitor.ModeHeight))
            .Distinct()
            .ToList();

    private static IEnumerable<SizeFilterOption> SpanOptions(IReadOnlyList<MonitorDevice> monitors)
    {
        if (monitors.Count < 2)
            yield break;

        SpanCanvas canvas;
        try
        {
            canvas = SpanLayout.Measure(monitors);
        }
        catch (ArgumentException)
        {
            yield break;
        }

        var rects = canvas.Rectangles
            .Select(rect => new PixelRect(rect.Connector, rect.Left, rect.Top, rect.Right, rect.Bottom))
            .ToList();
        var yielded = new HashSet<string>(StringComparer.Ordinal);

        foreach (var chain in Chains(rects, horizontal: true))
        {
            foreach (var option in Subspan(chain, 2, wide: true, yielded))
                yield return option;
            foreach (var option in Subspan(chain, 3, wide: true, yielded))
                yield return option;
        }

        foreach (var chain in Chains(rects, horizontal: false))
        {
            foreach (var option in Subspan(chain, 2, wide: false, yielded))
                yield return option;
        }
    }

    private static IEnumerable<SizeFilterOption> Subspan(
        IReadOnlyList<PixelRect> chain,
        int length,
        bool wide,
        HashSet<string> yielded)
    {
        if (chain.Count < length)
            yield break;

        for (var start = 0; start <= chain.Count - length; start++)
        {
            var slice = chain.Skip(start).Take(length).ToList();
            var left = slice.Min(rect => rect.Left);
            var top = slice.Min(rect => rect.Top);
            var right = slice.Max(rect => rect.Right);
            var bottom = slice.Max(rect => rect.Bottom);
            var width = right - left;
            var height = bottom - top;
            if (width <= 0 || height <= 0)
                continue;

            var direction = wide ? "wide" : "tall";
            var id = $"{length}-{direction}-{width}x{height}";
            if (!yielded.Add(id))
                continue;

            var label = length == 2
                ? $"{width}×{height} · 2 monitors {(wide ? "wide" : "tall")}"
                : $"{width}×{height} · 3 monitors wide";
            yield return new SizeFilterOption(id, label, [new SizeBand(width, height)]);
        }
    }

    private static List<List<PixelRect>> Chains(IReadOnlyList<PixelRect> rects, bool horizontal)
    {
        var byConnector = rects.ToDictionary(rect => rect.Connector);
        var next = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var origin in rects)
        {
            PixelRect? nearest = null;
            var nearestGap = int.MaxValue;
            foreach (var candidate in rects)
            {
                if (candidate.Connector == origin.Connector)
                    continue;

                var adjacent = horizontal
                    ? HorizontallyAdjacent(origin, candidate)
                    : VerticallyAdjacent(origin, candidate);
                if (!adjacent)
                    continue;

                var gap = horizontal ? candidate.Left - origin.Right : candidate.Top - origin.Bottom;
                if (gap < nearestGap)
                {
                    nearestGap = gap;
                    nearest = candidate;
                }
            }

            if (nearest is not null)
                next[origin.Connector] = nearest.Value.Connector;
        }

        var hasPredecessor = next.Values.ToHashSet(StringComparer.Ordinal);
        var chains = new List<List<PixelRect>>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var start in rects.Where(rect => !hasPredecessor.Contains(rect.Connector)))
        {
            var chain = new List<PixelRect>();
            var cursor = start.Connector;
            while (visited.Add(cursor))
            {
                chain.Add(byConnector[cursor]);
                if (!next.TryGetValue(cursor, out var following))
                    break;
                cursor = following;
            }

            if (chain.Count > 1)
                chains.Add(chain);
        }

        return chains;
    }

    private static bool HorizontallyAdjacent(PixelRect left, PixelRect right)
    {
        if (right.Left < left.Left)
            return false;

        var gap = right.Left - left.Right;
        if (gap is < -8 or > 16)
            return false;

        return Overlap(left.Top, left.Bottom, right.Top, right.Bottom, left.Bottom - left.Top, right.Bottom - right.Top);
    }

    private static bool VerticallyAdjacent(PixelRect top, PixelRect bottom)
    {
        if (bottom.Top < top.Top)
            return false;

        var gap = bottom.Top - top.Bottom;
        if (gap is < -8 or > 16)
            return false;

        return Overlap(top.Left, top.Right, bottom.Left, bottom.Right, top.Right - top.Left, bottom.Right - bottom.Left);
    }

    private static bool Overlap(int startA, int endA, int startB, int endB, int lengthA, int lengthB)
    {
        var overlap = Math.Min(endA, endB) - Math.Max(startA, startB);
        var smaller = Math.Min(lengthA, lengthB);
        return smaller > 0 && overlap >= smaller * 0.5;
    }

    private readonly record struct PixelRect(string Connector, int Left, int Top, int Right, int Bottom);
}
