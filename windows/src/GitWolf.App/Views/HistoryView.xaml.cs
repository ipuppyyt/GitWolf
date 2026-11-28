using System.Collections.ObjectModel;
using GitWolf.App.Services;
using GitWolf.Core;
using GitWolf.Store;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace GitWolf.App.Views;

public sealed partial class HistoryView : UserControl
{
    private readonly ObservableCollection<CommitRow> _commits = [];
    private RepositoryStore? _store;
    /// <summary>Set while the view mirrors the store, so selection events aren't read as user input.</summary>
    private bool _syncing;

    public HistoryView()
    {
        InitializeComponent();
        Commits.ItemsSource = _commits;
        BuildSearchOptions();
    }

    internal void Attach(RepositoryStore store)
    {
        _store = store;
        _syncing = true;
        _commits.Clear();
        _syncing = false;
        ResetSearch();
        ShowCommits();
    }

    internal void ShowCommits()
    {
        if (_store is not { } store) return;
        var count = Math.Min(store.Commits.Count, store.Graph.Count);
        var lanes = store.GraphLaneCount;
        var rows = new List<CommitRow>(count);
        var query = Search.Text.Trim();
        var found = GitSearch ? store.SearchResults : null;
        if (found is not null)
            rows.AddRange(found.Select(c => new CommitRow(c, NoGraph, 0)));
        // While git is searching, the previous rows stay until the answer arrives.
        else if (GitSearch) return;
        for (var i = 0; !GitSearch && i < count; i++)
        {
            var commit = store.Commits[i];
            if (query.Length == 0) rows.Add(new CommitRow(commit, store.Graph[i], lanes));
            // Lanes mean nothing once rows are skipped, so the graph is hidden while filtering.
            else if (commit.Subject.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || commit.AuthorName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || commit.Hash.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                rows.Add(new CommitRow(commit, store.Graph[i], 0));
        }

        _syncing = true;
        ListSync.Apply(_commits, rows, r => r.Commit.Hash);
        _syncing = false;
        Empty.Text = query.Length == 0 && !GitSearch ? "No commits yet" : "No matching commits";
        Empty.Visibility = store.IsLoaded && rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowSelection();
    }

    /// <summary>Redraws the pictures of the rows on screen; others draw theirs when they appear.</summary>
    internal void ShowPictures()
    {
        for (var i = 0; i < _commits.Count; i++)
            if (Commits.ContainerFromIndex(i) is ListViewItem { ContentTemplateRoot: CommitRowView view })
                view.ShowPicture();
    }

    internal void ShowSelection()
    {
        if (_store is not { } store) return;
        _syncing = true;
        Commits.SelectedItem = _commits.FirstOrDefault(r => r.Commit.Hash == store.SelectedCommitId);
        _syncing = false;
    }


    private void OnContext(UIElement sender, ContextRequestedEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        while (element is not null and not ListViewItem) element = VisualTreeHelper.GetParent(element);
        if (_store is not { } store || element is not ListViewItem item
            || Commits.ItemFromContainer(item) is not CommitRow { Commit: var commit }) return;

        var menu = new MenuFlyout();
        void Add(string text, Action action)
        {
            var entry = new MenuFlyoutItem { Text = text };
            entry.Click += (_, _) => action();
            menu.Items.Add(entry);
        }
        Add("Check out commit", () => _ = store.CheckoutCommitAsync(commit.Hash));
        Add("New branch here\u2026", () => _ = Dialogs.NewBranchAsync(XamlRoot, store, commit.Hash));
        Add("New tag\u2026", () => _ = Dialogs.NewTagAsync(XamlRoot, store, commit.Hash));
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("Cherry-pick", () => _ = store.CherryPickAsync(commit.Hash));
        Add("Revert", () => _ = store.RevertAsync(commit.Hash));
        var reset = new MenuFlyoutSubItem { Text = "Reset current branch to here", IsEnabled = !store.IsBusy };
        foreach (var (mode, label) in HistoryDialogs.ResetModes)
        {
            var entry = new MenuFlyoutItem { Text = label };
            entry.Click += (_, _) => _ = HistoryDialogs.ResetAsync(XamlRoot, store, commit.Hash, mode);
            reset.Items.Add(entry);
        }
        menu.Items.Add(reset);
        Add("Interactive rebase from here\u2026", () => _ = RebaseDialog.ShowAsync(XamlRoot, store, commit.Hash));
        Add("Compare with current branch",
            () => _ = store.OpenCompareAsync(store.Status.Branch ?? "HEAD", commit.Hash));
        menu.Items.Add(BisectMenu(store, commit.Hash));
        Add("Export as patch\u2026", () => _ = ToolDialogs.ExportPatchAsync(XamlRoot, store, commit.Hash));
        menu.Items.Add(new MenuFlyoutSeparator());
        if (store.WebLinks is not null)
            Add($"Open on {store.ProviderTitle ?? "the web"}", () => store.OpenOnWeb(new WebTarget.Commit(commit.Hash)));
        Add("Copy hash", () => Dialogs.Copy(commit.Hash));

        e.Handled = true;
        if (e.TryGetPosition(item, out var point)) menu.ShowAt(item, point);
        else menu.ShowAt(item);
    }

    private static MenuFlyoutSubItem BisectMenu(RepositoryStore store, string hash)
    {
        var menu = new MenuFlyoutSubItem { Text = "Bisect", IsEnabled = !store.IsBusy };
        void Add(string text, BisectMark mark)
        {
            var entry = new MenuFlyoutItem { Text = text };
            entry.Click += (_, _) => _ = store.BisectMarkCommitAsync(mark, hash);
            menu.Items.Add(entry);
        }
        var active = store.Bisect.IsActive;
        Add(active ? "This commit is good" : "Bisect: this commit is good", BisectMark.Good);
        Add(active ? "This commit is bad" : "Bisect: this commit is bad", BisectMark.Bad);
        if (active) Add("Skip this commit", BisectMark.Skip);
        return menu;
    }

    private void OnCommitSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && Commits.SelectedItem is CommitRow row) _store?.SelectCommit(row.Commit.Hash);
    }

    private void OnCommitRow(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.ItemContainer.ContentTemplateRoot is not CommitRowView view
            || args.Item is not CommitRow row) return;
        view.Show(row);
        Spoken.Name(args.ItemContainer, Spoken.Commit(row.Commit));
        // Near the end: fetch the next page of history.
        if (!GitSearch && Search.Text.Length == 0 && args.ItemIndex >= _commits.Count - 20) DispatcherQueue.TryEnqueue(() => _store?.LoadMoreCommits());
    }
}
