using GitWolf.App.Services;
using GitWolf.Core;
using GitWolf.Store;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace GitWolf.App.Views;

/// <summary>
/// What a stash holds, with Apply, Pop and Drop, without touching the working tree. Ported from
/// StashView.swift.
/// </summary>
public sealed partial class StashView : UserControl
{
    /// <summary>A heading or a file.</summary>
    private sealed record Row(string? Heading = null, FileRow? File = null);

    private readonly TextBlock _title = new()
    {
        FontSize = 17, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
        MaxLines = 3,
    };
    private readonly TextBlock _when = new() { FontSize = 12, Opacity = 0.7 };
    private readonly Button _apply = new() { Content = "Apply" };
    private readonly Button _pop = new() { Content = "Pop" };
    private readonly Button _drop = new() { Content = "Drop…" };
    private readonly TextBlock _empty = new()
    {
        TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(24),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly ListView _list = new()
    {
        SelectionMode = ListViewSelectionMode.Single, Padding = new Thickness(6, 4, 6, 8),
        ItemTemplate = (DataTemplate)XamlReader.Load(
            "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">"
            + "<ContentControl HorizontalContentAlignment=\"Stretch\" HorizontalAlignment=\"Stretch\" IsTabStop=\"False\"/>"
            + "</DataTemplate>"),
    };
    private readonly StackPanel _header;
    private RepositoryStore? _store;
    private StashContents? _shown;
    private List<Row> _rows = [];
    private bool _syncing;

    public StashView()
    {
        _apply.Click += (_, _) => _ = ActAsync((s, reference) => s.StashApplyAsync(reference));
        _pop.Click += (_, _) => _ = ActAsync((s, reference) => s.StashPopAsync(reference));
        _drop.Click += (_, _) => _ = DropAsync();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_list, "Files in this stash");
        _list.ContainerContentChanging += OnRow;
        _list.SelectionChanged += OnSelected;

        _header = new StackPanel
        {
            Spacing = 10, Padding = new Thickness(16),
            Children =
            {
                _title, _when,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _apply, _pop, _drop } },
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
        root.Children.Add(_header);
        foreach (FrameworkElement element in new FrameworkElement[] { _list, _empty })
        {
            Grid.SetRow(element, 1);
            root.Children.Add(element);
        }
        Content = root;
    }

    internal void Show(RepositoryStore store)
    {
        _store = store;
        var stash = store.SelectedStash;
        _header.Visibility = stash is null ? Visibility.Collapsed : Visibility.Visible;
        _title.Text = stash?.Message ?? "";
        _when.Text = stash is null ? "" : $"{stash.Ref} · {stash.Date.LocalDateTime:d MMM yyyy 'at' h:mm tt}";
        _apply.IsEnabled = _pop.IsEnabled = _drop.IsEnabled = !store.IsBusy;

        var contents = store.StashContents;
        if (!ReferenceEquals(contents, _shown))
        {
            _shown = contents;
            _rows = contents is null ? [] : Rows(contents);
            _syncing = true;
            _list.ItemsSource = _rows;
            _syncing = false;
        }
        _empty.Text = stash is null ? "Select a stash in the sidebar to preview it."
            : store.StashError ?? (contents is { Files.Count: 0, UntrackedFiles.Count: 0 }
                ? "This stash has no file changes." : "");

        _syncing = true;
        _list.SelectedIndex = _rows.FindIndex(r => r.File is { File: { } change } row && store.SelectedTarget is { } target
            && change.Path == target.Path && row.Source == target.Source);
        _syncing = false;
    }

    private static List<Row> Rows(StashContents contents)
    {
        var rows = new List<Row>();
        void Group(string title, IReadOnlyList<FileChange> files, DiffSource source)
        {
            if (files.Count == 0) return;
            rows.Add(new Row(Heading: $"{title}  {files.Count}"));
            rows.AddRange(files.Select(f => new Row(File: new FileRow(f, source))));
        }
        Group("FILES", contents.Files, contents.Source);
        if (contents.UntrackedSource is DiffSource untracked) Group("UNTRACKED", contents.UntrackedFiles, untracked);
        return rows;
    }

    private void OnRow(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.ItemContainer.ContentTemplateRoot is not ContentControl host
            || args.Item is not Row row) return;
        args.ItemContainer.MinHeight = 30;
        // Containers are reused, so both settings are made for either kind of row.
        args.ItemContainer.IsHitTestVisible = row.File is not null;
        args.ItemContainer.IsTabStop = row.File is not null;
        if (row.File is { } file)
        {
            var view = new FileRowView();
            view.Show(file, null, null);
            host.Content = view;
            Spoken.Name(args.ItemContainer, Spoken.File(file));
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

    private void OnSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && _list.SelectedItem is Row { File: { File: { } change } row })
            _store?.SelectChange(change, row.Source);
    }

    /// <summary>Stash numbers shift, so an action runs only while the entry on screen still exists.</summary>
    private async Task ActAsync(Func<RepositoryStore, string, Task<bool>> action)
    {
        if (_store is { SelectedStash: { } stash } store && store.Stashes.Contains(stash))
            await action(store, stash.Ref);
    }

    private async Task DropAsync()
    {
        if (_store is not { SelectedStash: { } stash } store) return;
        if (await Dialogs.ConfirmAsync(XamlRoot, "Drop this stash?",
                $"{stash.Message}\n\nThe stashed changes will be lost.", "Drop")
            && ReferenceEquals(store, _store) && store.Stashes.Contains(stash))
            await store.StashDropAsync(stash.Ref);
    }
}
