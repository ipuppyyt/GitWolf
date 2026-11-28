using GitWolf.App.Services;
using GitWolf.Core;
using GitWolf.Store;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GitWolf.App.Views;

/// <summary>Branch switcher, mode tabs, sync actions and the jump button.</summary>
public sealed partial class TopBarView : UserControl
{
    private RepositoryStore? _store;

    public TopBarView()
    {
        InitializeComponent();
        AddSyncMenus();
    }

    internal event Action? JumpRequested;
    internal event Action? NotificationsRequested;
    internal event Action<FrameworkElement>? OpenInRequested;

    private void OnOpenIn(object sender, RoutedEventArgs e) => OpenInRequested?.Invoke((FrameworkElement)sender);

    /// <summary>The unread count beside the bell.</summary>
    internal void ShowUnread(int count)
    {
        BellCount.Text = count > 99 ? "99+" : count.ToString();
        BellCount.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A tab for pull requests or issues, offered when the remote's provider has them.</summary>
    private static void ShowTab(Button tab, Border selected, bool supported, bool current)
    {
        tab.Visibility = supported ? Visibility.Visible : Visibility.Collapsed;
        selected.Visibility = current ? Visibility.Visible : Visibility.Collapsed;
        tab.Opacity = current ? 1 : 0.6;
    }

    /// <summary>The empty stretch that drags the window.</summary>
    internal UIElement DragRegion => DragArea;

    internal void Show(RepositoryStore store)
    {
        _store = store;
        var status = store.Status;
        BranchName.Text = status.BranchDisplay;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(BranchButton, $"Switch branch, on {status.BranchDisplay}");
        ShowBranchIcon(status.Branch);
        var history = store.Mode == StoreMode.History;
        HistorySelected.Visibility = history ? Visibility.Visible : Visibility.Collapsed;
        var changes = store.Mode == StoreMode.Changes;
        ChangesSelected.Visibility = changes ? Visibility.Visible : Visibility.Collapsed;
        HistoryTab.Opacity = history ? 1 : 0.6;
        ChangesTab.Opacity = changes ? 1 : 0.6;
        ShowTab(PullsTab, PullsSelected, store.Supports(CollabKind.PullRequest), store.Mode == StoreMode.PullRequests);
        ShowTab(IssuesTab, IssuesSelected, store.Supports(CollabKind.Issue), store.Mode == StoreMode.Issues);
        PullsLabel.Text = store.PullRequestsTitle.StartsWith("Merge", StringComparison.Ordinal) ? "Merges" : "Pulls";
        Badge.Visibility = status.ChangeCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        BadgeText.Text = status.ChangeCount.ToString();
        PullCount.Text = status.Behind > 0 ? status.Behind.ToString() : "";
        PushCount.Text = status.Ahead > 0 ? status.Ahead.ToString() : "";
        FetchButton.IsEnabled = PullButton.IsEnabled = PushButton.IsEnabled = BranchButton.IsEnabled = !store.IsBusy;
        StashButton.IsEnabled = !store.IsBusy && !status.IsClean;
    }

    /// <summary>The current branch's icon; the plain branch symbol in the accent colour when detached.</summary>
    private void ShowBranchIcon(string? branch)
    {
        var look = branch is null ? null : FileIcons.BranchNamed(branch);
        BranchIcon.Content = FileIconView.Symbol(look?.Value ?? "arrow.triangle.branch",
            look is null ? Palette.Ref(RefKind.Local) : FileIconView.Brush(look.Color), 14);
    }

    /// <summary>Built when opened, so it never has to be kept in step with the branch list.</summary>
    private void OnBranchMenu(object sender, RoutedEventArgs e)
    {
        if (_store is not { } store) return;
        // Hangs under the pill with their left edges level, as on macOS.
        var menu = new MenuFlyout
        {
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedLeft,
        };
        foreach (var branch in store.LocalBranches)
        {
            // As on macOS: a check marks the current branch, the others show their icon.
            var look = FileIcons.BranchNamed(branch.Name);
            var item = new MenuFlyoutItem
            {
                Text = branch.Name,
                Icon = branch.IsCurrent
                    ? new FontIcon { Glyph = "\uE73E" }
                    : Icons.Create(SymbolShapes.Get(look.Value!), FileIconView.Brush(look.Color)),
            };
            item.Click += (_, _) =>
            {
                if (!branch.IsCurrent) _ = store.CheckoutAsync(branch);
            };
            menu.Items.Add(item);
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        var create = new MenuFlyoutItem { Text = "New branch…" };
        create.Click += (_, _) => _ = Dialogs.NewBranchAsync(XamlRoot, store, null);
        menu.Items.Add(create);
        menu.ShowAt(BranchButton);
    }

    private void OnTab(object sender, RoutedEventArgs e)
    {
        if (_store is { } store)
            store.Mode = ReferenceEquals(sender, ChangesTab) ? StoreMode.Changes
                : ReferenceEquals(sender, PullsTab) ? StoreMode.PullRequests
                : ReferenceEquals(sender, IssuesTab) ? StoreMode.Issues
                : StoreMode.History;
    }

    private void OnNotifications(object sender, RoutedEventArgs e) => NotificationsRequested?.Invoke();

    private void OnFetch(object sender, RoutedEventArgs e) => _ = _store?.FetchAsync();
    private void OnPull(object sender, RoutedEventArgs e) => _ = _store?.PullAsync();
    private void OnPush(object sender, RoutedEventArgs e) => _ = _store?.PushAsync();
    private void OnJump(object sender, RoutedEventArgs e) => JumpRequested?.Invoke();

    private void OnStash(object sender, RoutedEventArgs e)
    {
        if (_store is { } store) _ = Dialogs.StashAsync(XamlRoot, store);
    }
}
