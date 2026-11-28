using System.Collections.ObjectModel;
using System.Diagnostics;
using GitWolf.App.Services;
using GitWolf.Core;
using GitWolf.Store;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace GitWolf.App.Views;

/// <summary>Branches, remote branches, tags and stashes. Ported from SidebarView.swift.</summary>
public sealed partial class SidebarView : UserControl
{
    private readonly ObservableCollection<SidebarItem> _rows = [];
    private readonly HashSet<SidebarKind> _open = [SidebarKind.LocalBranch, SidebarKind.RemoteBranch, SidebarKind.Stash];
    private readonly HashSet<string> _openRemotes = [];
    /// <summary>Rows just revealed by opening a group, which animate in when first drawn.</summary>
    private HashSet<SidebarItem> _entering = [];
    private RepositoryStore? _store;

    public SidebarView()
    {
        InitializeComponent();
        Rows.ItemsSource = _rows;
    }

    internal void Attach(RepositoryStore store)
    {
        _store = store;
        _entering.Clear();
        _rows.Clear();
        Show();
    }

    internal void Show()
    {
        if (_store is not { } store) return;
        var status = store.Status;
        RepoName.Text = store.Name;
        Dot.Fill = status.IsClean ? Palette.Green : Palette.Orange;
        var summary = status.IsClean ? "Clean" : status.ChangeCount == 1 ? "1 change" : $"{status.ChangeCount} changes";
        if (status.Ahead > 0) summary += $"  ↑{status.Ahead}";
        if (status.Behind > 0) summary += $"  ↓{status.Behind}";
        Summary.Text = summary;

        Web.Visibility = store.RemoteLocation is null ? Visibility.Collapsed : Visibility.Visible;
        WebText.Text = store.RemoteLocation is { } location
            ? (store.ProviderTitle is { } provider ? $"{provider} · " : "") + location.Path
            : "";

        var rows = new List<SidebarRow>();
        bool Section(SidebarKind kind, string title, int count, string empty)
        {
            var open = _open.Contains(kind);
            rows.Add(new SidebarRow(SidebarKind.Header, kind, title, count.ToString(), IsOpen: open));
            if (open && count == 0) rows.Add(new SidebarRow(SidebarKind.Note, kind, empty));
            return open;
        }

        if (Section(SidebarKind.LocalBranch, "BRANCHES", store.LocalBranches.Count, "No branches"))
            rows.AddRange(store.LocalBranches.Select(b =>
            {
                var sync = string.Join(" ", new[]
                {
                    b.Ahead > 0 ? $"\u2191{b.Ahead}" : null, b.Behind > 0 ? $"\u2193{b.Behind}" : null,
                }.OfType<string>());
                return new SidebarRow(SidebarKind.LocalBranch, SidebarKind.LocalBranch, b.Name, sync, b.Hash, b);
            }));

        if (Section(SidebarKind.RemoteBranch, "REMOTES", store.Remotes.Count, "No remotes"))
            foreach (var remote in store.Remotes)
            {
                var open = _openRemotes.Contains(remote);
                rows.Add(new SidebarRow(SidebarKind.RemoteGroup, SidebarKind.RemoteBranch, remote, IsOpen: open));
                if (!open) continue;
                var prefix = remote + "/";
                rows.AddRange(store.RemoteBranches.Where(b => b.Name.StartsWith(prefix, StringComparison.Ordinal))
                    .Select(b => new SidebarRow(SidebarKind.RemoteBranch, SidebarKind.RemoteBranch,
                        b.Name[prefix.Length..], "", b.Hash, b)));
            }

        if (Section(SidebarKind.Tag, "TAGS", store.Tags.Count, "No tags"))
            rows.AddRange(store.Tags.Select(t => new SidebarRow(SidebarKind.Tag, SidebarKind.Tag, t.Name, "", t.Hash,
                Tip: $"{t.Name}\n" + (!t.IsAnnotated ? "Lightweight tag"
                    : t.Message.Length > 0 ? $"Annotated: {t.Message}" : "Annotated tag"),
                Annotated: t.IsAnnotated)));

        if (Section(SidebarKind.Stash, "STASHES", store.Stashes.Count, "No stashes"))
            rows.AddRange(store.Stashes.Select(x =>
                new SidebarRow(SidebarKind.Stash, SidebarKind.Stash, x.Message, Stash: x)));

        // Stash messages can repeat, so stashes are keyed by their ref.
        ListSync.Apply(_rows, rows.Select(r => new SidebarItem(r)).ToList(),
            i => (i.Row.Kind, i.Row.Section, i.Row.Stash?.Ref ?? i.Row.Branch?.Name ?? i.Row.Title), Update);
    }

    /// <summary>Redraws a row where it is. Rows scrolled out of view are drawn when they return.</summary>
    private void Update(SidebarItem item, SidebarItem next)
    {
        if (item.Row == next.Row) return;
        item.Row = next.Row;
        if (Rows.ContainerFromItem(item) is ListViewItem { ContentTemplateRoot: SidebarRowView view } container)
        {
            view.Show(item.Row, animate: true);
            Spoken.Name(container, Spoken.Sidebar(item.Row));
        }
    }

    private void OnWeb(object sender, RoutedEventArgs e) => _store?.OpenOnWeb(new WebTarget.Repository());

    private void OnClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not SidebarItem { Row: var row }) return;
        if (row.Kind == SidebarKind.Header)
        {
            if (!_open.Remove(row.Section)) _open.Add(row.Section);
            ShowToggled();
        }
        else if (row.Kind == SidebarKind.RemoteGroup)
        {
            if (!_openRemotes.Remove(row.Title)) _openRemotes.Add(row.Title);
            ShowToggled();
        }
        else if (row.Stash is { } stash)
        {
            _ = _store?.SelectStashAsync(stash);
        }
        else if (row.Hash is { } hash)
        {
            _store?.Reveal(hash);
        }
    }

    private void ShowToggled()
    {
        var before = _rows.ToHashSet();
        Show();
        // The list draws the new rows after this returns.
        _entering = _rows.Where(item => !before.Contains(item)).ToHashSet();
    }

    private void OnDoubleTap(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (FindRow(e.OriginalSource as DependencyObject) is { Branch: { IsCurrent: false } branch })
            _ = _store?.CheckoutAsync(branch);
    }

    private void OnContext(UIElement sender, ContextRequestedEventArgs e)
    {
        if (_store is not { } store || FindRow(e.OriginalSource as DependencyObject) is not { } row) return;
        var menu = new MenuFlyout();
        void Add(string text, Action action, bool enabled = true)
        {
            var item = new MenuFlyoutItem { Text = text, IsEnabled = enabled };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        switch (row)
        {
            case { Kind: SidebarKind.LocalBranch, Branch: { } branch }:
                Add("Checkout", () => _ = store.CheckoutAsync(branch), !branch.IsCurrent);
                Add("Merge into current branch", () => _ = store.MergeAsync(branch.Name), !branch.IsCurrent);
                Add("Compare with current branch",
                    () => _ = store.OpenCompareAsync(store.Status.Branch ?? "HEAD", branch.Name), !branch.IsCurrent);
                Add("Rebase current branch onto this\u2026", () => _ = ConfirmAsync(
                    $"Rebase onto \u201c{branch.Name}\u201d?",
                    "The current branch\u2019s commits are rewritten on top of it. Uncommitted work is kept.",
                    "Rebase", () => store.RebaseOntoAsync(branch.Name)), !branch.IsCurrent && store.Operation is null);
                Add("New branch from here\u2026",
                    () => _ = Dialogs.NewBranchAsync(XamlRoot, store, branch.Name));
                Add("Rename\u2026", () => _ = BranchDialogs.RenameAsync(XamlRoot, store, branch.Name));
                menu.Items.Add(TrackingMenu(store, branch));
                Add("Clean up branches\u2026", () => _ = BranchDialogs.CleanupAsync(XamlRoot, store));
                menu.Items.Add(new MenuFlyoutSeparator());
                Add("Delete branch…", () => _ = ConfirmAsync(
                    $"Delete branch “{branch.Name}”?", "Git refuses if it has unmerged commits.", "Delete",
                    () => store.DeleteBranchAsync(branch.Name)), !branch.IsCurrent);
                break;
            case { Kind: SidebarKind.RemoteBranch, Branch: { } remote }:
                Add("Check out as local branch", () => _ = store.CheckoutAsync(remote));
                Add("Merge into current branch", () => _ = store.MergeAsync(remote.Name));
                Add("Compare with current branch",
                    () => _ = store.OpenCompareAsync(store.Status.Branch ?? "HEAD", remote.Name));
                Add("Rebase current branch onto this\u2026", () => _ = ConfirmAsync(
                    $"Rebase onto \u201c{remote.Name}\u201d?",
                    "The current branch\u2019s commits are rewritten on top of it. Uncommitted work is kept.",
                    "Rebase", () => store.RebaseOntoAsync(remote.Name)), store.Operation is null);
                if (GitWolf.Core.GitRepository.SplitRemoteBranch(remote.Name, store.Remotes) is var (host, name))
                    Add($"Delete on {host}…", () => _ = ConfirmAsync(
                        $"Delete branch “{name}” on {host}?",
                        "The branch is removed from the remote for everyone. Local branches are kept.",
                        "Delete on remote", () => store.DeleteRemoteBranchAsync(host, name)));
                break;
            case { Kind: SidebarKind.RemoteGroup }:
                Add($"Fetch {row.Title}", () => _ = store.FetchRemoteAsync(row.Title), !store.IsBusy);
                Add("Manage remotes\u2026", () => _ = RemoteDialogs.ManageAsync(XamlRoot, store));
                break;
            case { Kind: SidebarKind.Header, Section: SidebarKind.RemoteBranch }:
                Add("Manage remotes\u2026", () => _ = RemoteDialogs.ManageAsync(XamlRoot, store));
                break;
            case { Kind: SidebarKind.Tag }:
                foreach (var server in store.Remotes)
                {
                    Add($"Push to {server}", () => _ = store.PushTagAsync(row.Title, server));
                    Add($"Delete on {server}…", () => _ = ConfirmAsync(
                        $"Delete tag “{row.Title}” on {server}?",
                        "The tag is removed from the remote for everyone. The local tag is kept.",
                        "Delete on remote", () => store.DeleteRemoteTagAsync(row.Title, server)));
                }
                if (store.Tags.Count > 1)
                    foreach (var server in store.Remotes)
                        Add($"Push all tags to {server}", () => _ = store.PushAllTagsAsync(server));
                Add("Copy release notes since this tag", () => _ = CopyReleaseNotesAsync(store, row.Title));
                Add("Delete local tag…", () => _ = ConfirmAsync(
                    $"Delete tag “{row.Title}”?", "The local tag is removed.", "Delete",
                    () => store.DeleteTagAsync(row.Title)));
                break;
            case { Kind: SidebarKind.Stash, Stash: { } stash }:
                // stash@{n} shifts when stashes change, so only act if the ref still means the same stash.
                Task<bool> IfUnchanged(Func<string, Task<bool>> action) =>
                    store.Stashes.Contains(stash) ? action(stash.Ref) : Task.FromResult(false);
                Add("Preview changes", () => _ = store.SelectStashAsync(stash));
                Add("Apply", () => _ = IfUnchanged(store.StashApplyAsync));
                Add("Pop", () => _ = IfUnchanged(store.StashPopAsync));
                Add("Drop…", () => _ = ConfirmAsync(
                    "Drop this stash?", $"{stash.Message}\n\nThe stashed changes will be lost.", "Drop",
                    () => IfUnchanged(store.StashDropAsync)));
                break;
            default:
                return;
        }

        e.Handled = true;
        var target = (FrameworkElement)e.OriginalSource;
        if (e.TryGetPosition(target, out var point)) menu.ShowAt(target, point);
        else menu.ShowAt(target);
    }

    private static MenuFlyoutSubItem TrackingMenu(RepositoryStore store, Branch branch)
    {
        var menu = new MenuFlyoutSubItem
        {
            Text = "Track remote branch", IsEnabled = store.RemoteBranches.Count > 0 || branch.Upstream is not null,
        };
        foreach (var remote in store.RemoteBranches)
        {
            var item = new ToggleMenuFlyoutItem { Text = remote.Name, IsChecked = remote.Name == branch.Upstream };
            item.Click += (_, _) => _ = store.SetUpstreamAsync(branch.Name, remote.Name);
            menu.Items.Add(item);
        }
        if (branch.Upstream is null) return menu;
        var stop = new MenuFlyoutItem { Text = "Stop tracking" };
        stop.Click += (_, _) => _ = store.SetUpstreamAsync(branch.Name, null);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(stop);
        return menu;
    }

    private static async Task CopyReleaseNotesAsync(RepositoryStore store, string tag)
    {
        if (await store.ReadAsync(r => r.CommitSubjectsAsync(tag)) is not { } subjects) return;
        if (subjects.Count == 0) store.ErrorMessage = $"There are no commits since {tag}.";
        else Dialogs.Copy(CommitMessage.Changelog(subjects));
    }

    private async Task ConfirmAsync(string title, string message, string verb, Func<Task<bool>> action)
    {
        var store = _store;
        try
        {
            var dialog = new ContentDialog
            {
                RequestedTheme = ActualTheme,
                XamlRoot = XamlRoot, Title = title, Content = message,
                PrimaryButtonText = verb, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && ReferenceEquals(store, _store))
                await action();
        }
        catch (Exception e)
        {
            // Fire-and-forget: e.g. a second dialog while one is already open.
            Trace.TraceWarning($"Confirmation failed: {e.GetType().Name}");
        }
    }

    private SidebarRow? FindRow(DependencyObject? element)
    {
        while (element is not null and not ListViewItem)
            element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element);
        return element is ListViewItem container ? (Rows.ItemFromContainer(container) as SidebarItem)?.Row : null;
    }

    private void OnRow(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.ItemContainer.ContentTemplateRoot is SidebarRowView view && args.Item is SidebarItem item)
        {
            view.Show(item.Row);
            Spoken.Name(args.ItemContainer, Spoken.Sidebar(item.Row));
            if (_entering.Remove(item)) view.Enter();
        }
    }
}
