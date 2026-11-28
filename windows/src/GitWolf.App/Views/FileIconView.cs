using GitWolf.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace GitWolf.App.Views;

/// <summary>A flat, colourful icon for a file, folder or branch. Ported from FileIconView.swift.</summary>
public sealed partial class FileIconView : UserControl
{
    private const double Grid16 = 16;
    private static readonly Dictionary<uint, SolidColorBrush> Brushes = [];
    private FileIcon? _icon;

    public FileIconView()
    {
        Width = Height = 17;
        VerticalAlignment = VerticalAlignment.Center;
        ActualThemeChanged += (_, _) => Redraw();
        Loaded += (_, _) => Redraw();
    }

    private bool _light;

    private void Redraw()
    {
        if (_icon is { } icon && _light != (ActualTheme == ElementTheme.Light)) Render(icon);
    }

    private void Render(FileIcon icon)
    {
        _light = ActualTheme == ElementTheme.Light;
        // Tiles carry their own text colour; bare shapes sit directly on the panel.
        var bare = icon.Glyph != IconGlyph.Letters;
        Content = Draw(_light && bare ? icon with { Color = OnLight(icon.Color) } : icon, Width);
    }

    private static uint OnLight(uint rgb)
    {
        uint r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
        if (0.299 * r + 0.587 * g + 0.114 * b < 150) return rgb;
        return (r * 7 / 10 << 16) | (g * 7 / 10 << 8) | (b * 7 / 10);
    }

    /// <summary>Redraws only when the icon changes, so recycled rows cost little.</summary>
    internal void Show(FileIcon? icon)
    {
        Visibility = icon is null ? Visibility.Collapsed : Visibility.Visible;
        if (icon is null || icon == _icon) return;
        _icon = icon;
        Render(icon);
        ToolTipService.SetToolTip(this, icon.Name);
        AutomationProperties.SetName(this, icon.Name);
    }

    internal static SolidColorBrush Brush(uint rgb)
    {
        if (!Brushes.TryGetValue(rgb, out var brush))
            Brushes[rgb] = brush = new SolidColorBrush(
                Color.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        return brush;
    }

    /// <summary>A symbol at the given size, in its own colour.</summary>
    internal static FrameworkElement Symbol(string symbol, Brush brush, double size) => new Viewbox
    {
        Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center,
        Child = Icons.Create(SymbolShapes.Get(symbol), brush),
    };

    private static UIElement Draw(FileIcon icon, double size)
    {
        var colour = Brush(icon.Color);
        return icon switch
        {
            { Glyph: IconGlyph.Letters, Value: { } text } => Tile(text, colour, icon.DarkText, size),
            { Glyph: IconGlyph.Symbol, Value: { } symbol } => Symbol(symbol, colour, size * 0.94),
            { Glyph: IconGlyph.Folder } => Folder(icon.Value, colour, size),
            { Glyph: IconGlyph.Logo, Value: { } slug } => Logo(slug, icon.Color, size),
            _ => React(colour, size),
        };
    }

    private static FrameworkElement Logo(string slug, uint rgb, double size)
    {
        if (!LanguageLogos.Paths.TryGetValue(slug, out var path)) return Symbol("doc", Brush(rgb), size * 0.94);
        return new Viewbox
        {
            Width = size * 0.92, Height = size * 0.92,
            Child = Icons.Create(new Icons.Shape(24, path), Brush(OnDark(rgb))),
        };
    }

    /// <summary>Very dark brands are lightened so they show on a dark window.</summary>
    private static uint OnDark(uint rgb)
    {
        uint r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
        if (0.2126 * r + 0.7152 * g + 0.0722 * b >= 0.28 * 255) return rgb;
        static uint Lift(uint c) => (uint)(c + (255 - c) * 0.55);
        return (Lift(r) << 16) | (Lift(g) << 8) | Lift(b);
    }

    private static Border Tile(string text, Brush colour, bool dark, double size) => new()
    {
        Background = colour,
        CornerRadius = new CornerRadius(size * 0.24),
        Child = new TextBlock
        {
            Text = text, FontSize = size * (text.Length > 3 ? 0.32 : text.Length > 2 ? 0.40 : 0.52),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(dark ? Color.FromArgb(0xC7, 0, 0, 0) : Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            TextLineBounds = TextLineBounds.Tight,
        },
    };

    private static Grid Folder(string? inner, Brush colour, double size)
    {
        var grid = new Grid();
        grid.Children.Add(new Viewbox
        {
            Width = size, Height = size,
            Child = Icons.Create(new Icons.Shape(Grid16,
                "F1 M1,3.5 Q1,2.5 2,2.5 H6 L7.8,4.3 H14 Q15,4.3 15,5.3 V13 Q15,14 14,14 H2 Q1,14 1,13 Z"), colour),
        });
        if (inner is null) return grid;
        var mark = Symbol(inner, new SolidColorBrush(Colors.White), size * 0.48);
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        mark.Margin = new Thickness(0, size * 0.14, 0, 0);
        grid.Children.Add(mark);
        return grid;
    }

    /// <summary>Three ellipses crossing at 0°, 60° and 120°, and a dot.</summary>
    private static Grid React(Brush colour, double size)
    {
        var grid = new Grid();
        foreach (var angle in new[] { 0, 60, 120 })
            grid.Children.Add(new Ellipse
            {
                Width = size, Height = size * 0.40, Stroke = colour, StrokeThickness = size * 0.065,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
                RenderTransform = new RotateTransform { Angle = angle },
            });
        grid.Children.Add(new Ellipse
        {
            Width = size * 0.18, Height = size * 0.18, Fill = colour,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        return grid;
    }
}
