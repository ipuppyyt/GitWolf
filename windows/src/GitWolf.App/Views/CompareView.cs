using GitWolf.Core;
using GitWolf.Store;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Windows.System;

namespace GitWolf.App.Views;

/// <summary>
/// Compare any two branches, tags or commits: what the second has that the first doesn't. Ported
/// from CompareView.swift.
/// </summary>
public sealed partial class CompareView : UserControl
{
    /// <summary>A heading, a commit or a changed file.</summary>
    private sealed record Row(string? Heading = null, Commit? Commit = null, FileChange? File = null);

    private readonly TextBox _from = new() { PlaceholderText = "Base" };
    private readonly TextBox _to = new() { PlaceholderText = "Compare" };
    private readonly CheckBox _since = new() { Content = "Only changes since the common ancestor", FontSize = 12 };
    private readonly Button _run = new() { Content = "Compare" };
    private readonly TextBlock _summary = new() { FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _empty = new()
    {
        TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(24),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly ProgressRing _progress = new() { Width = 28, Height = 28, IsActive = false };
    private readonly ListView _list = new()
    {
        SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true, Padding = new Thickness(6, 4, 6, 8),
        ItemTemplate = (DataTemplate)XamlReader.Load(
            "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">"
            + "<ContentControl HorizontalContentAlignment=\"Stretch\" HorizontalAlignment=\"Stretch\" IsTabStop=\"False\"/>"
            + "</DataTemplate>"),
    };
    private RepositoryStore? _store;
    private RefComparison? _shown;
    private bool _syncing;

    public CompareView()
    {
        var swap = new Button { Content = "⇄" };
        ToolTipService.SetToolTip(swap, "Swap");
        Spoken.Name(swap, "Swap");
        swap.Click += (_, _) => _ = _store?.SwapCompareAsync();
        _run.Click += (_, _) => _ = _store?.RunCompareAsync();
        _from.TextChanged += (_, _) => Edited();
        _to.TextChanged += (_, _) => Edited();
        foreach (var box in new[] { _from, _to })
            box.KeyDown += (_, e) =>
            {
                if (e.Key != VirtualKey.Enter) return;
                e.Handled = true;
                _ = _store?.RunCompareAsync();
            };
        _since.Click += (_, _) =>
        {
            if (_store is not { } store) return;
            store.CompareSinceAncestor = _since.IsChecked == true;
            if (store.Comparison is not null) _ = store.RunCompareAsync();
        };
        Spoken.Name(_list, "Comparison");
        _list.ContainerContentChanging += OnRow;
        _list.ItemClick += OnClick;

        var refs = new Grid { ColumnSpacing = 6 };
        foreach (var width in new[] { 1.0, 0, 0, 1.0, 0 })
            refs.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = width > 0 ? new GridLength(width, GridUnitType.Star) : GridLength.Auto,
            });
        FrameworkElement[] parts = [_from, Picker(_from), swap, _to, Picker(_to)];
        for (var i = 0; i < parts.Length; i++)
        {
            Grid.SetColumn(parts[i], i);
            refs.Children.Add(parts[i]);
        }
        var options = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } } };
        options.Children.Add(_since);
        Grid.SetColumn(_run, 1);
        options.Children.Add(_run);
        var controls = new StackPanel
        {
            Spacing = 10, Padding = new Thickness(14),
            Children =
            {
                new TextBlock { Text = "Compare", FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                refs, options, _summary,
            },
        };

        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
            },
        };
        root.Children.Add(controls);
        foreach (FrameworkElement element in new FrameworkElement[] { _list, _empty, _progress })
        {
            Grid.SetRow(element, 1);
            root.Children.Add(element);
        }
        Content = root;
    }

    /// <summary>A menu of branches, remote branches and tags that fills <paramref name="box"/>.</summary>
    private Button Picker(TextBox box)
    {
        var button = new Button { Content = new FontIcon { Glyph = "", FontSize = 10 }, MinWidth = 0 };
        ToolTipService.SetToolTip(button, "Pick a branch or tag");
        Spoken.Name(button, "Pick a branch or tag");
        button.Click += (_, _) =>
        {
            if (_store is not { } store) return;
            var menu = new MenuFlyout();
            void Group(string title, IEnumerable<string> names)
            {
                var group = new MenuFlyoutSubItem { Text = title };
                foreach (var name in names)
                {
                    var item = new MenuFlyoutItem { Text = name };
                    item.Click += (_, _) => box.Text = name;
                    group.Items.Add(item);
                }
                if (group.Items.Count > 0) menu.Items.Add(group);
            }
            Group("Branches", store.LocalBranches.Select(b => b.Name));
            Group("Remote", store.RemoteBranches.Select(b => b.Name));
            Group("Tags", store.Tags.Select(t => t.Name));
            var head = new MenuFlyoutItem { Text = "HEAD" };
            head.Click += (_, _) => box.Text = "HEAD";
            menu.Items.Add(head);
            menu.ShowAt(button);
        };
        return button;
    }

    private void Edited()
    {
        if (_syncing || _store is not { } store) return;
        store.CompareFrom = _from.Text;
        store.CompareTo = _to.Text;
        ShowRunState(store);
    }

    private void ShowRunState(RepositoryStore store) =>
        _run.IsEnabled = !store.IsComparing && _from.Text.Trim().Length > 0 && _to.Text.Trim().Length > 0;

    internal void Show(RepositoryStore store)
    {
        var changedStore = !ReferenceEquals(store, _store);
        _store = store;
        _syncing = true;
        if (_from.Text != store.CompareFrom) _from.Text = store.CompareFrom;
        if (_to.Text != store.CompareTo) _to.Text = store.CompareTo;
        _since.IsChecked = store.CompareSinceAncestor;
        _syncing = false;
        ShowRunState(store);
        _progress.IsActive = store.IsComparing;

        var result = store.Comparison;
        _empty.Text = store.CompareError
            ?? (result is null && !store.IsComparing
                ? "Pick a base and a branch, tag or commit to compare it with." : "");
        _summary.Text = result is null ? ""
            : $"{result.Ahead} ahead · {result.Behind} behind · {store.CompareTo} vs {store.CompareFrom}";
        _summary.Visibility = result is null ? Visibility.Collapsed : Visibility.Visible;

        if (!changedStore && ReferenceEquals(result, _shown)) return;
        _shown = result;
        _list.ItemsSource = result is null ? null : Rows(result);
    }

    private static List<Row> Rows(RefComparison result)
    {
        var shown = result.Commits.Count < result.Ahead ? $"{result.Commits.Count} of {result.Ahead}" : $"{result.Ahead}";
        return
        [
            new Row(Heading: $"COMMITS  {shown}"),
            .. result.Commits.Select(c => new Row(Commit: c)),
            new Row(Heading: $"FILES  {result.Files.Count}"),
            .. result.Files.Select(f => new Row(File: f)),
        ];
    }

    private void OnRow(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.ItemContainer.ContentTemplateRoot is not ContentControl host
            || args.Item is not Row row) return;
        args.ItemContainer.MinHeight = 30;
        if (row.File is { } file)
        {
            var view = new FileRowView();
            var shown = new FileRow(file, Source());
            view.Show(shown, null, null);
            host.Content = view;
            Spoken.Name(args.ItemContainer, Spoken.File(shown));
        }
        else if (row.Commit is { } commit)
        {
            var grid = new Grid { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock[] cells =
            [
                new() { Text = commit.Hash[..Math.Min(7, commit.Hash.Length)], FontFamily = new("Consolas"), FontSize = 12, Opacity = 0.65 },
                new() { Text = commit.Subject, TextTrimming = TextTrimming.CharacterEllipsis },
                new() { Text = commit.AuthorName, FontSize = 12, Opacity = 0.65, MaxWidth = 110, TextTrimming = TextTrimming.CharacterEllipsis },
            ];
            for (var i = 0; i < cells.Length; i++)
            {
                cells[i].VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(cells[i], i);
                grid.Children.Add(cells[i]);
            }
            ToolTipService.SetToolTip(grid, "Show in History");
            host.Content = grid;
            Spoken.Name(args.ItemContainer, Spoken.Commit(commit));
        }
        else
        {
            Spoken.Name(args.ItemContainer, row.Heading ?? "");
            host.Content = new TextBlock
            {
                Text = row.Heading ?? "", FontSize = 11.5, Opacity = 0.65, Margin = new Thickness(0, 10, 0, 2),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            };
        }
    }

    private DiffSource Source() =>
        _shown is { } result ? DiffSource.Between(result.Base, result.Head) : DiffSource.Unstaged;

    private void OnClick(object sender, ItemClickEventArgs e)
    {
        if (_store is not { } store || e.ClickedItem is not Row row || _shown is null) return;
        if (row.File is { } file) store.SelectChange(file, Source());
        else if (row.Commit is { } commit) store.Reveal(commit.Hash);
    }
}
