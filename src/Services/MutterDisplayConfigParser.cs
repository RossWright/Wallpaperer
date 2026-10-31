using System.Globalization;
using System.Text.Json;
using Wallpaperer.Models;

namespace Wallpaperer.Services;

public static class MutterDisplayConfigParser
{
    public static IReadOnlyList<MonitorDevice> Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var data = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var dataProperty)
                ? dataProperty
                : root;

            if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() < 3)
                throw new InvalidOperationException("GetCurrentState did not return monitor lists.");

            var physical = ReadPhysicalMonitors(data[1]);
            var devices = new List<MonitorDevice>();
            foreach (var logical in data[2].EnumerateArray())
            {
                if (logical.GetArrayLength() < 6)
                    continue;

                var connectors = logical[5].EnumerateArray()
                    .Select(entry => entry[0].GetString())
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name!)
                    .ToList();
                if (connectors.Count == 0 || !physical.TryGetValue(connectors[0], out var monitor))
                    continue;

                var scale = ReadDouble(logical[2]);
                if (scale <= 0)
                    scale = 1;

                var transform = ReadInt(logical[3]);
                var (width, height) = SpanLayout.LogicalSize(monitor.ModeWidth, monitor.ModeHeight, scale, transform);
                var displayName = monitor.DisplayName;
                if (connectors.Count > 1)
                    displayName += " (" + string.Join(", ", connectors.Skip(1)) + ")";

                devices.Add(new MonitorDevice(
                    connectors[0],
                    displayName,
                    ReadInt(logical[0]),
                    ReadInt(logical[1]),
                    width,
                    height,
                    scale,
                    ReadBool(logical[4]),
                    monitor.ModeWidth,
                    monitor.ModeHeight));
            }

            return devices;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or IndexOutOfRangeException)
        {
            throw new InvalidOperationException("Could not read the GNOME monitor layout.", exception);
        }
    }

    private static Dictionary<string, PhysicalMonitor> ReadPhysicalMonitors(JsonElement monitors)
    {
        var physical = new Dictionary<string, PhysicalMonitor>(StringComparer.Ordinal);
        foreach (var monitor in monitors.EnumerateArray())
        {
            if (monitor.GetArrayLength() < 3)
                continue;

            var identity = monitor[0];
            var connector = identity[0].GetString();
            if (string.IsNullOrWhiteSpace(connector))
                continue;

            var modeWidth = 0;
            var modeHeight = 0;
            var foundCurrent = false;
            foreach (var mode in monitor[1].EnumerateArray())
            {
                if (mode.GetArrayLength() < 3)
                    continue;

                var current = mode.GetArrayLength() > 6 && IsTrue(UnwrapObject(mode[6]), "is-current");
                if (!current && foundCurrent)
                    continue;

                modeWidth = ReadInt(mode[1]);
                modeHeight = ReadInt(mode[2]);
                if (current)
                {
                    foundCurrent = true;
                    break;
                }
            }

            var properties = UnwrapObject(monitor[2]);
            var displayName = ReadString(properties, "display-name") ?? connector;
            physical[connector] = new PhysicalMonitor(displayName, modeWidth, modeHeight);
        }

        return physical;
    }

    private static JsonElement Unwrap(JsonElement element)
    {
        while (element.ValueKind == JsonValueKind.Object
               && element.TryGetProperty("type", out _)
               && element.TryGetProperty("data", out var data))
        {
            element = data;
        }

        return element;
    }

    private static JsonElement UnwrapObject(JsonElement element)
    {
        var unwrapped = Unwrap(element);
        return unwrapped.ValueKind == JsonValueKind.Object ? unwrapped : default;
    }

    private static bool IsTrue(JsonElement properties, string name)
    {
        if (properties.ValueKind != JsonValueKind.Object || !properties.TryGetProperty(name, out var value))
            return false;

        value = Unwrap(value);
        return value.ValueKind == JsonValueKind.True;
    }

    private static string? ReadString(JsonElement properties, string name)
    {
        if (properties.ValueKind != JsonValueKind.Object || !properties.TryGetProperty(name, out var value))
            return null;

        value = Unwrap(value);
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static int ReadInt(JsonElement element)
    {
        element = Unwrap(element);
        if (element.ValueKind == JsonValueKind.Number)
            return element.TryGetInt32(out var integer) ? integer : RoundDouble(element.GetDouble());

        if (element.ValueKind == JsonValueKind.String
            && int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        throw new FormatException("Expected an integer in the monitor layout.");
    }

    private static double ReadDouble(JsonElement element)
    {
        element = Unwrap(element);
        if (element.ValueKind == JsonValueKind.Number)
            return element.GetDouble();

        if (element.ValueKind == JsonValueKind.String
            && double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        throw new FormatException("Expected a number in the monitor layout.");
    }

    private static bool ReadBool(JsonElement element)
    {
        element = Unwrap(element);
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new FormatException("Expected a boolean in the monitor layout."),
        };
    }

    private static int RoundDouble(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    private sealed record PhysicalMonitor(string DisplayName, int ModeWidth, int ModeHeight);
}
