using GitWolf.App.Views;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GitWolf.App.Services;

/// <summary>Applies the chosen appearance and accent colour to the running window.</summary>
internal static class Theming
{
    /// <summary>Same values as AccentChoice in AppSettings.swift.</summary>
    public static Color Accent(AccentChoice choice, bool dark) => choice switch
    {
        // The logo's blue, lightened on dark backgrounds so it stays readable.
        AccentChoice.Wolf => Rgb(dark ? 0x3366FFu : 0x012AA5u),
        AccentChoice.Indigo => Rgb(0x6B61FA),
        AccentChoice.Purple => Rgb(0xA85CF5),
        AccentChoice.Pink => Rgb(0xF54D94),
        AccentChoice.Red => Rgb(0xF04D52),
        AccentChoice.Orange => Rgb(0xFA8524),
        AccentChoice.Green => Rgb(0x33B366),
        AccentChoice.Teal => Rgb(0x1AADB3),
        _ => Rgb(0x1F80FF),
    };

    private static Color Rgb(uint v) => Color.FromArgb(255, (byte)(v >> 16), (byte)(v >> 8), (byte)v);

    /// <summary>Blends towards <paramref name="target"/> by the given amount (0 to 1).</summary>
    private static Color Mix(Color from, Color target, double amount)
    {
        byte Channel(byte a, byte b) => (byte)Math.Round(a + (b - a) * amount);
        return Color.FromArgb(255, Channel(from.R, target.R), Channel(from.G, target.G), Channel(from.B, target.B));
    }

    public static void Apply(Window window, SettingsState settings)
    {
        if (window.Content is not FrameworkElement root) return;
        var wanted = settings.Appearance switch
        {
            AppearanceChoice.Light => ElementTheme.Light,
            AppearanceChoice.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        _applying = true;
        root.RequestedTheme = wanted;
        // Read after the request, so "System" resolves to what Windows is using.
        var dark = root.ActualTheme == ElementTheme.Dark;
        _appliedDark = dark;
        var accent = Accent(settings.Accent, dark);
        var resources = Application.Current.Resources;
        resources["SystemAccentColor"] = accent;
        double[] shades = [0.14, 0.30, 0.50];
        for (var i = 0; i < shades.Length; i++)
        {
            resources[$"SystemAccentColorLight{i + 1}"] = Mix(accent, Colors.White, shades[i]);
            resources[$"SystemAccentColorDark{i + 1}"] = Mix(accent, Colors.Black, shades[i]);
        }
        foreach (var dictionary in resources.MergedDictionaries.SelectMany(d => d.MergedDictionaries.Prepend(d)))
        {
            foreach (var pair in dictionary.ThemeDictionaries)
                if (pair.Value is ResourceDictionary colours) Recolour(colours, accent, pair.Key as string != "Light");
        }
        Palette.SetAccent(accent);

        // Accent resources are only re-read on a theme change, so pass through the other theme first.
        root.RequestedTheme = root.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        root.RequestedTheme = wanted;
        _applying = false;
        UpdateCaptionButtons(window, root.ActualTheme);
    }

    private static bool _applying;
    private static bool _appliedDark;

    /// <summary>GitWolf's blue differs by theme, so it is applied again when Windows switches.</summary>
    public static void ThemeChanged(Window window, SettingsState settings)
    {
        if (window.Content is not FrameworkElement root) return;
        UpdateCaptionButtons(window, root.ActualTheme);
        if (!_applying && settings.Accent == AccentChoice.Wolf && settings.Appearance == AppearanceChoice.System
            && (root.ActualTheme == ElementTheme.Dark) != _appliedDark)
            Apply(window, settings);
    }

    /// <summary>The window buttons are drawn by the system, which doesn't follow the app's theme.</summary>
    public static void UpdateCaptionButtons(Window window, ElementTheme theme)
    {
        var foreground = theme == ElementTheme.Light ? Colors.Black : Colors.White;
        var bar = window.AppWindow.TitleBar;
        bar.ButtonForegroundColor = foreground;
        bar.ButtonHoverForegroundColor = foreground;
        bar.ButtonHoverBackgroundColor = Color.FromArgb(0x20, foreground.R, foreground.G, foreground.B);
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
    }

    private static void Recolour(ResourceDictionary colours, Color accent, bool dark)
    {
        // Only this app's own dictionaries: the framework shares brushes between unrelated controls.
        if (!colours.ContainsKey("AccentLabelBrush")) return;
        void Set(string key, Color colour)
        {
            // The alpha of each brush is kept.
            if (colours.ContainsKey(key) && colours[key] is SolidColorBrush brush)
                brush.Color = Color.FromArgb(brush.Color.A, colour.R, colour.G, colour.B);
        }
        Set("AccentLabelBrush", dark ? Mix(accent, Colors.White, 0.08) : accent);
        Set("ListViewItemBackgroundSelected", accent);
        Set("ListViewItemBackgroundSelectedPointerOver", accent);
        Set("ListViewItemBackgroundSelectedPressed", accent);
    }
}
