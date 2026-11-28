using System.Diagnostics;
using GitWolf.App.Services;
using GitWolf.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace GitWolf.App.Views;

// Pinned repositories, the user's groups and the recent ones.
public sealed partial class DashboardView
{
    private const int Shown = 9;
    private const double MinCardWidth = 250;
    private const double Gap = 12;

    private readonly List<Grid> _grids = [];
    private Workspace? _workspace;
    private string _drawn = "";
    private int _columns;

    internal void Show(Workspace workspace)
    {
        _workspace = workspace;
        var home = workspace.Home;
        var recents = workspace.Recents.Where(p => !home.IsPinned(p)).Take(Shown).ToList();
        // Rebuilt only when something on screen would differ.
        var key = string.Join('\n', home.Pinned.Prepend("pinned")
            .Concat(home.Groups.SelectMany(g => g.Paths.Prepend($"{g.Id}{g.Name}")))
            .Concat(recents.Prepend("recent")));
        if (key == _drawn) return;
        _drawn = key;
        Sections.Children.Clear();
        _grids.Clear();
        if (home.Pinned.Count > 0) Section("PINNED", home.Pinned);
        foreach (var group in home.Groups) Section(group.Name.ToUpperInvariant(), group.Paths, group);
        if (recents.Count > 0) Section("RECENT", recents);
        if (Sections.Children.Count > 0)
        {
            var add = new Button { Content = "New group\u2026", HorizontalAlignment = HorizontalAlignment.Left };
            add.Click += (_, _) => _ = NameGroupAsync(null, null);
            Sections.Children.Add(add);
        }
        _columns = 0;
        Arrange(Sections.ActualWidth);
    }

    private void Section(string title, IReadOnlyList<string> paths, HomeGroup? group = null)
    {
        var heading = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } } };
        var label = Ui.Text(title, 11, bold: true, secondary: true);
        label.CharacterSpacing = 70;
        label.VerticalAlignment = VerticalAlignment.Center;
        heading.Children.Add(label);
        if (group is not null)
        {
            var menu = new MenuFlyout();
            menu.Items.Add(Entry("Rename\u2026", () => _ = NameGroupAsync(group, null)));
            menu.Items.Add(Entry("Delete group", () => _workspace?.EditHome(h => h.DeleteGroup(group.Id))));
            var options = new Button
            {
                Content = new FontIcon { Glyph = "\uE712", FontSize = 12 }, Padding = new Thickness(6, 2, 6, 2),
                Background = null, BorderThickness = new Thickness(0), Flyout = menu,
            };
            AutomationProperties.SetName(options, $"Options for group {group.Name}");
            Grid.SetColumn(options, 1);
            heading.Children.Add(options);
        }
        var panel = new StackPanel { Spacing = Gap, Children = { heading } };
        if (paths.Count == 0)
            panel.Children.Add(Ui.Text("Right-click a repository and choose Add to group.", 12, secondary: true));
        var grid = new Grid { ColumnSpacing = Gap, RowSpacing = Gap };
        foreach (var path in paths)
        {
            var card = Card(path);
            card.ContextFlyout = CardMenu(path);
            grid.Children.Add(card);
        }
        _grids.Add(grid);
        panel.Children.Add(grid);
        Sections.Children.Add(panel);
    }

    private MenuFlyout CardMenu(string path)
    {
        var menu = new MenuFlyout();
        var home = _workspace!.Home;
        menu.Items.Add(Entry(home.IsPinned(path) ? "Unpin" : "Pin", () => _workspace?.EditHome(h => h.TogglePin(path))));
        var groups = new MenuFlyoutSubItem { Text = "Add to group" };
        foreach (var group in home.Groups)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = group.Name, IsChecked = group.Paths.Contains(path, StringComparer.OrdinalIgnoreCase),
            };
            item.Click += (_, _) => _workspace?.EditHome(h => h.ToggleInGroup(path, group.Id));
            groups.Items.Add(item);
        }
        if (home.Groups.Count > 0) groups.Items.Add(new MenuFlyoutSeparator());
        groups.Items.Add(Entry("New group\u2026", () => _ = NameGroupAsync(null, path)));
        menu.Items.Add(groups);
        menu.Items.Add(Entry("Show in File Explorer", () => _ = RevealAsync(path)));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Entry("Remove from home", () => _workspace?.Forget(path)));
        return menu;
    }

    private static MenuFlyoutItem Entry(string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();
        return item;
    }

    private static async Task RevealAsync(string path)
    {
        try
        {
            await Launcher.LaunchFolderPathAsync(Path.GetFullPath(path));
        }
        catch (Exception e)
        {
            // Fire-and-forget from a menu: nothing is lost if Explorer doesn't open.
            Trace.TraceWarning($"Showing a folder failed: {e.GetType().Name}");
        }
    }

    /// <summary>Names a new group (holding <paramref name="path"/>, if given) or renames one.</summary>
    private async Task NameGroupAsync(HomeGroup? existing, string? path)
    {
        try
        {
            var name = new TextBox { Header = "Name", Text = existing?.Name ?? "", MaxLength = 60 };
            var dialog = new ContentDialog
            {
                RequestedTheme = ActualTheme, XamlRoot = XamlRoot,
                Title = existing is null ? "New group" : "Rename group", Content = name,
                PrimaryButtonText = existing is null ? "Create" : "Rename", CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            _workspace?.EditHome(home =>
            {
                if (existing is null) home.AddGroup(name.Text, path);
                else home.RenameGroup(existing.Id, name.Text);
            });
        }
        catch (Exception e)
        {
            // Fire-and-forget: e.g. another dialog is already open.
            Trace.TraceWarning($"Naming a group failed: {e.GetType().Name}");
        }
    }

    private void OnSectionsSized(object sender, SizeChangedEventArgs e) => Arrange(e.NewSize.Width);

    /// <summary>As many equal columns as fit, the same in every section.</summary>
    private void Arrange(double width)
    {
        var columns = Math.Max(1, (int)((width + Gap) / (MinCardWidth + Gap)));
        if (columns == _columns) return;
        _columns = columns;
        foreach (var grid in _grids)
        {
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Clear();
            for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (var i = 0; i < grid.Children.Count; i++)
            {
                if (i % columns == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var card = (FrameworkElement)grid.Children[i];
                Grid.SetColumn(card, i % columns);
                Grid.SetRow(card, i / columns);
            }
        }
    }
}
