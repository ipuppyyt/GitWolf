using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace GitWolf.App.Views;

/// <summary>
/// Shown when no repository is selected: open something or jump back into recents. Ported from
/// DashboardView.swift.
/// </summary>
public sealed partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
        // The accent brush is recoloured in place, so these follow the accent setting.
        var accent = Palette.Ref(Core.RefKind.Local);
        Logo.Background = accent;
        // White on the solid accent as on macOS; the default style uses a paler shade in dark mode.
        var white = new SolidColorBrush(Microsoft.UI.Colors.White);
        // Each key is set once.
        foreach (var (state, fill) in new[]
                 {
                     ("", accent), ("PointerOver", Palette.Tint(accent, 0xE6)), ("Pressed", Palette.Tint(accent, 0xCC)),
                 })
        {
            OpenButton.Resources[$"AccentButtonBackground{state}"] = fill;
            OpenButton.Resources[$"AccentButtonForeground{state}"] = white;
        }
        DropOutline.Stroke = accent;
        DropOutline.Fill = Palette.Tint(accent, 0x0F);
        // The icon keeps its own 16px box and the Viewbox scales it.
        LogoIcon.Child = Icons.Create(Icons.Branch, new SolidColorBrush(Microsoft.UI.Colors.White));
    }

    internal event Action? OpenRequested;
    internal event Action? JumpRequested;
    internal event Action? CloneRequested;
    internal event Action? NewRequested;
    internal event Action<IReadOnlyList<string>>? PathsRequested;

    private Button Card(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(path.Replace('/', Path.DirectorySeparatorChar));
        var name = Path.GetFileName(full) is { Length: > 0 } file ? file : full;
        var avatar = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(12), Background = Palette.Avatar(name),
            Child = new TextBlock
            {
                Text = char.ToUpperInvariant(name[0]) + name[1..Math.Min(2, name.Length)],
                FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        var text = new StackPanel
        {
            Spacing = 2, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
                new TextBlock
                {
                    Text = Path.GetDirectoryName(full) ?? "", FontSize = 12, Opacity = 0.65,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            },
        };
        Grid.SetColumn(text, 1);
        var button = new Button
        {
            Style = (Style)Resources["CardStyle"],
            Content = new Grid
            {
                ColumnSpacing = 12,
                ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition() },
                Children = { avatar, text },
            },
        };
        button.Resources["ButtonBorderBrushPointerOver"] = Palette.Tint(Palette.Ref(Core.RefKind.Local), 0x80);
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, full);
        button.Click += (_, _) => PathsRequested?.Invoke([path]);
        return button;
    }

    private void OnOpen(object sender, RoutedEventArgs e) => OpenRequested?.Invoke();

    private void OnClone(object sender, RoutedEventArgs e) => CloneRequested?.Invoke();

    private void OnNew(object sender, RoutedEventArgs e) => NewRequested?.Invoke();

    private void OnJump(object sender, RoutedEventArgs e) => JumpRequested?.Invoke();

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        e.AcceptedOperation = DataPackageOperation.Link;
        e.DragUIOverride.IsCaptionVisible = false;
        DropHint.Visibility = Visibility.Visible;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => DropHint.Visibility = Visibility.Collapsed;

    private async void OnDrop(object sender, DragEventArgs e)
    {
        DropHint.Visibility = Visibility.Collapsed;
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var folders = items.OfType<Windows.Storage.StorageFolder>().Select(f => f.Path)
                .Where(p => p.Length > 0).ToList();
            if (folders.Count > 0) PathsRequested?.Invoke(folders);
        }
        catch (Exception error)
        {
            // An event handler can't pass the failure on; the folder can still be opened with the button.
            Trace.TraceWarning($"Dropped items could not be read: {error.GetType().Name}");
        }
    }
}
