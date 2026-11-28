using System.Collections.ObjectModel;
using GitWolf.App.Services;
using GitWolf.Core;
using GitWolf.Store;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace GitWolf.App.Views;

public sealed partial class ChangesView : UserControl
{
    private readonly ObservableCollection<FileRow> _rows = [];
    private RepositoryStore? _store;
    /// <summary>Set while the view mirrors the store, so selection events aren't read as user input.</summary>
    private bool _syncing;

    public ChangesView()
    {
        InitializeComponent();
        Files.ItemsSource = _rows;
    }

    internal void Attach(RepositoryStore store)
    {
        _store = store;
        _syncing = true;
        _rows.Clear();
        // A filter belongs to the repository it was typed in.
        Filter.Text = "";
        _syncing = false;
        ShowFiles();
        ShowDraft();
        _ = LoadSigningAsync(store);
    }

    internal void ShowFiles()
    {
        if (_store is not { } store) return;
        var rows = new List<FileRow>();
        var status = store.Status;
        var count = status.Conflicted.Count + status.Staged.Count + status.Unstaged.Count + status.Untracked.Count;
        var query = Filter.Text.Trim();
        // Only worth the space once the list is long; stays while something is typed.
        Filter.Visibility = count > FilterThreshold || query.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Filter.PlaceholderText = $"Filter {count} files";
        void Section(string title, DiffSource source, IReadOnlyList<FileChange> all)
        {
            var files = FileFilter.Apply(all, query);
            if (files.Count == 0) return;
            rows.Add(new FileRow(null, source, $"{title} ({files.Count})", Filtered: query.Length > 0));
            rows.AddRange(files.Select(f => new FileRow(f, source, IsLfs: store.LfsPaths.Contains(f.Path))));
        }
        Section("CONFLICTS", DiffSource.Conflicted, status.Conflicted);
        Section("STAGED", DiffSource.Staged, status.Staged);
        Section("CHANGES", DiffSource.Unstaged, status.Unstaged);
        Section("UNTRACKED", DiffSource.Untracked, status.Untracked);

        _syncing = true;
        // Headers are keyed by section only, so a changed count updates the row in place.
        ListSync.Apply(_rows, rows, r => (r.Source.Kind, r.File?.Path ?? ""));
        _syncing = false;
        Clean.Text = status.IsClean ? "Working tree is clean" : $"No files match “{query}”";
        Clean.Visibility = store.IsLoaded && (status.IsClean || rows.Count == 0)
            ? Visibility.Visible : Visibility.Collapsed;
        ShowSelection();
        ShowCommitState();
    }

    private const int FilterThreshold = 20;

    private void OnFilter(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (!_syncing) ShowFiles();
    }

    internal void ShowSelection()
    {
        if (_store is not { } store) return;
        _syncing = true;
        Files.SelectedItem = store.SelectedTarget is { } target
            ? _rows.FirstOrDefault(r => r.File?.Path == target.Path && r.Source == target.Source)
            : null;
        _syncing = false;
    }

    internal void ShowDraft()
    {
        if (_store is not { } store) return;
        if (Normalize(Message.Text) != store.DraftMessage) Message.Text = store.DraftMessage;
        Amend.IsChecked = store.Amend;
        ShowCommitState();
    }

    internal void ShowCommitState()
    {
        if (_store is not { } store) return;
        ShowSubjectWarning(store.DraftMessage);
        var hasMessage = !string.IsNullOrWhiteSpace(store.DraftMessage);
        CommitButton.IsEnabled = !store.IsBusy && store.Status.Conflicted.Count == 0
            && (store.Amend || (hasMessage && store.Status.Staged.Count > 0));
    }

    /// <summary>TextBox uses '\r' for line breaks; git must receive '\n'.</summary>
    private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    private void OnSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;
        if (Files.SelectedItem is FileRow { File: { } file } row) _store?.SelectChange(file, row.Source);
        else ShowSelection(); // Headers aren't selectable.
    }

    private void OnRow(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.ItemContainer.ContentTemplateRoot is not FileRowView view
            || args.Item is not FileRow row || _store is not { } store) return;
        Spoken.Name(args.ItemContainer, row.File is null ? Spoken.File(row)
            : $"{Spoken.File(row)}, {(row.Source == DiffSource.Staged ? "staged" : "not staged")}");

        var staged = row.Source == DiffSource.Staged;
        if (row.File is { } file)
        {
            // A staged rename must unstage both of its paths.
            string[] paths = file.OriginalPath is { } from && staged ? [from, file.Path] : [file.Path];
            view.Show(row, staged ? "Unstage" : "Stage",
                () => _ = staged ? store.UnstageAsync(paths) : store.StageAsync(paths),
                $"{(staged ? "Unstage" : "Stage")} {file.Path}");
        }
        else if (row.Source == DiffSource.Conflicted)
        {
            view.Show(row, null, null);
        }
        else
        {
            var source = row.Source;
            var label = (staged ? "Unstage " : "Stage ") + (row.Filtered ? "shown" : "all");
            view.Show(row, label, () =>
            {
                var query = Filter.Text.Trim();
                // While filtering, only the files on screen are acted on.
                if (staged && query.Length == 0) _ = store.UnstageAllAsync();
                else if (staged) _ = store.UnstageAsync(FileFilter.Apply(store.Status.Staged, query)
                    .SelectMany(f => f.OriginalPath is { } from ? [from, f.Path] : new[] { f.Path }).ToList());
                else _ = store.StageAsync(FileFilter.Apply(
                        source == DiffSource.Untracked ? store.Status.Untracked : store.Status.Unstaged, query)
                    .Select(f => f.Path).ToList());
            });
        }
    }

    private void OnContext(UIElement sender, ContextRequestedEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        while (element is not null and not ListViewItem) element = VisualTreeHelper.GetParent(element);
        if (_store is not { } store || element is not ListViewItem item
            || Files.ItemFromContainer(item) is not FileRow { File: { } file } row) return;

        var menu = new MenuFlyout();
        void Add(string text, Action action)
        {
            var entry = new MenuFlyoutItem { Text = text, IsEnabled = !store.IsBusy };
            entry.Click += (_, _) => action();
            menu.Items.Add(entry);
        }
        var staged = row.Source == DiffSource.Staged;
        // A staged rename must unstage both of its paths.
        string[] paths = file.OriginalPath is { } from && staged ? [from, file.Path] : [file.Path];
        if (row.Source == DiffSource.Conflicted)
        {
            Add("Resolve using ours", () => _ = store.ResolveConflictAsync(file.Path, ConflictSide.Ours));
            Add("Resolve using theirs", () => _ = store.ResolveConflictAsync(file.Path, ConflictSide.Theirs));
        }
        else if (staged) Add("Unstage", () => _ = store.UnstageAsync(paths));
        else Add("Stage", () => _ = store.StageAsync(paths));
        var untracked = row.Source == DiffSource.Untracked;
        if (untracked || row.Source == DiffSource.Unstaged)
            Add("Discard changes\u2026", () => _ = DiscardAsync(store, file, untracked));
        if (row.Source != DiffSource.Conflicted)
            menu.Items.Add(IgnoreMenu(store, file, tracked: !untracked));
        if (!untracked)
            Add("History & blame\u2026", () => _ = FileHistoryDialog.ShowAsync(XamlRoot, store, file.Path));
        Add("Copy path", () => Dialogs.Copy(file.Path));

        e.Handled = true;
        if (e.TryGetPosition(item, out var point)) menu.ShowAt(item, point);
        else menu.ShowAt(item);
    }

    /// <summary>Asks first, saying how much is lost.</summary>
    private async Task DiscardAsync(RepositoryStore store, FileChange file, bool untracked)
    {
        var summary = await store.DiscardSummaryAsync([file], untracked);
        var confirmed = await Dialogs.ConfirmAsync(XamlRoot, $"Discard changes to \u201C{file.FileName}\u201D?",
            summary?.Sentence ?? "This can\u2019t be undone.", "Discard");
        // Still the same repository, and the file still has the kind of change that was described.
        var current = untracked ? store.Status.Untracked : store.Status.Unstaged;
        if (confirmed && ReferenceEquals(store, _store) && current.Any(f => f.Path == file.Path))
            await store.DiscardAsync([file], untracked);
    }


    private void OnAmend(object sender, RoutedEventArgs e)
    {
        if (_store is { } store) store.Amend = Amend.IsChecked == true;
    }

}
