using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Wallpaperer.Views;

public partial class ColorDialog : Window
{
    private static readonly string[] SwatchColors =
    [
        "#000000", "#FFFFFF", "#424242", "#9E9E9E",
        "#E53935", "#FB8C00", "#FDD835", "#43A047",
        "#00897B", "#1E88E5", "#3949AB", "#8E24AA", "#D81B60",
    ];

    private HsvColor _hsv = new(1, 0, 0, 0);
    private bool _editingHtml;
    private bool _accepted;

    public ColorDialog()
    {
        InitializeComponent();
    }

    public ColorDialog(uint argb) : this()
    {
        var color = Color.FromUInt32(argb);
        _hsv = new HsvColor(color);
        BuildSwatches();
        Matrix.PointerPressed += OnMatrixPressed;
        Matrix.PointerMoved += OnMatrixMoved;
        ValueSlider.ValueChanged += (_, _) =>
        {
            _hsv = new HsvColor(_hsv.A, _hsv.H, _hsv.S, ValueSlider.Value);
            PaintMatrix();
            SyncFromHsv();
        };
        HtmlColor.TextChanged += (_, _) =>
        {
            if (_editingHtml || HtmlColor.Text is null || !TryParseHtml(HtmlColor.Text, out var parsed))
                return;

            _hsv = new HsvColor(parsed);
            ValueSlider.Value = _hsv.V;
            PaintMatrix();
            MoveMarker();
            Preview.Background = new SolidColorBrush(parsed);
        };
        Opened += (_, _) =>
        {
            ValueSlider.Value = _hsv.V;
            PaintMatrix();
            SyncFromHsv();
        };
    }

    public uint Selected { get; private set; }

    public static async Task<uint?> Pick(Window owner, uint current)
    {
        var dialog = new ColorDialog(current);
        await dialog.ShowDialog(owner);
        return dialog._accepted ? dialog.Selected : null;
    }

    private void BuildSwatches()
    {
        foreach (var html in SwatchColors)
        {
            if (!TryParseHtml(html, out var color))
                continue;

            var swatch = new Button
            {
                Width = 28,
                Height = 28,
                Margin = new Avalonia.Thickness(0, 0, 8, 8),
                Padding = new Avalonia.Thickness(0),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(Color.Parse("#6A7382")),
                BorderThickness = new Avalonia.Thickness(1),
                Tag = color,
            };
            swatch.Click += (_, _) =>
            {
                if (swatch.Tag is Color picked)
                    ApplyColor(picked);
            };
            Swatches.Children.Add(swatch);
        }
    }

    private void ApplyColor(Color color)
    {
        _hsv = new HsvColor(color);
        ValueSlider.Value = _hsv.V;
        PaintMatrix();
        SyncFromHsv();
    }

    private void OnMatrixPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Matrix).Properties.IsLeftButtonPressed)
            return;

        Pick(e.GetPosition(Matrix));
        e.Pointer.Capture(Matrix);
        e.Handled = true;
    }

    private void OnMatrixMoved(object? sender, PointerEventArgs e)
    {
        if (!e.GetCurrentPoint(Matrix).Properties.IsLeftButtonPressed)
            return;

        Pick(e.GetPosition(Matrix));
    }

    private void Pick(Point point)
    {
        var width = Math.Max(1, Matrix.Bounds.Width);
        var height = Math.Max(1, Matrix.Bounds.Height);
        var hue = Math.Clamp(point.X / width, 0, 1) * 360;
        var saturation = 1 - Math.Clamp(point.Y / height, 0, 1);
        _hsv = new HsvColor(1, hue, saturation, _hsv.V);
        MoveMarker();
        SyncFromHsv();
    }

    private void PaintMatrix()
    {
        const int size = 256;
        using var bitmap = new SKBitmap(size, size);
        for (var y = 0; y < size; y++)
        {
            var saturation = 1 - y / (float)(size - 1);
            for (var x = 0; x < size; x++)
            {
                var hue = x / (float)(size - 1) * 360;
                var color = new HsvColor(1, hue, saturation, _hsv.V).ToRgb();
                bitmap.SetPixel(x, y, new SKColor(color.R, color.G, color.B));
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        Matrix.Source = new Bitmap(new MemoryStream(data!.ToArray()));
        MoveMarker();
    }

    private void MoveMarker()
    {
        var width = Matrix.Bounds.Width > 1 ? Matrix.Bounds.Width : 256;
        var height = Matrix.Bounds.Height > 1 ? Matrix.Bounds.Height : 220;
        Canvas.SetLeft(Marker, _hsv.H / 360 * width - Marker.Width / 2);
        Canvas.SetTop(Marker, (1 - _hsv.S) * height - Marker.Height / 2);
    }

    private void SyncFromHsv()
    {
        var color = _hsv.ToRgb();
        Selected = color.ToUInt32();
        Preview.Background = new SolidColorBrush(color);
        _editingHtml = true;
        HtmlColor.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        _editingHtml = false;
    }

    private void Accept(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (HtmlColor.Text is not null && TryParseHtml(HtmlColor.Text, out var parsed))
            Selected = parsed.ToUInt32();

        _accepted = true;
        Close();
    }

    private void Cancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();

    private static bool TryParseHtml(string text, out Color color)
    {
        color = default;
        var value = text.Trim();
        if (value.StartsWith('#'))
            value = value[1..];

        if (value.Length is 3 or 4)
            value = string.Concat(value.Select(item => $"{item}{item}"));

        if (value.Length == 6)
            value += "FF";

        if (value.Length != 8 || !uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
            return false;

        var red = (byte)((packed >> 24) & 0xFF);
        var green = (byte)((packed >> 16) & 0xFF);
        var blue = (byte)((packed >> 8) & 0xFF);
        var alpha = (byte)(packed & 0xFF);
        color = Color.FromArgb(alpha, red, green, blue);
        return true;
    }
}
