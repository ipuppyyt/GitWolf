using System.Diagnostics;
using GitWolf.Store;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace GitWolf.App.Views;

// Dragging a local branch onto the current one offers to merge it or rebase onto it.
public sealed partial class SidebarView
{
    /// <summary>The local branch being dragged from this list; text dragged from elsewhere is ignored.</summary>
    private (RepositoryStore Store, string Name)? _dragged;

    private void OnDragStarting(object sender, DragItemsStartingEventArgs e)
    {
        _dragged = null;
        if (_store is { } store && e.Items.FirstOrDefault() is SidebarItem
                { Row: { Kind: SidebarKind.LocalBranch, Branch: { IsCurrent: false } branch } })
        {
            _dragged = (store, branch.Name);
            e.Data.RequestedOperation = DataPackageOperation.Link;
        }
        else e.Cancel = true;
    }

    /// <summary>The dragged branch's name, when it is over the current branch and could be applied.</summary>
    private string? Droppable(DragEventArgs e) =>
        _dragged is { } dragged && ReferenceEquals(dragged.Store, _store)
        && dragged.Store is { IsBusy: false, Operation: null }
        && FindRow(e.OriginalSource as DependencyObject) is
            { Kind: SidebarKind.LocalBranch, Branch.IsCurrent: true }
            ? dragged.Name
            : null;

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var name = Droppable(e);
        e.AcceptedOperation = name is null ? DataPackageOperation.None : DataPackageOperation.Link;
        if (name is not null) e.DragUIOverride.Caption = $"Merge or rebase “{name}”";
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        var name = Droppable(e);
        _dragged = null;
        if (name is null || _store is not { } store) return;
        try
        {
            var current = store.Status.BranchDisplay;
            var dialog = new ContentDialog
            {
                RequestedTheme = ActualTheme, XamlRoot = XamlRoot, Title = $"Bring “{name}” into “{current}”?",
                Content = new TextBlock
                {
                    Text = "Merge keeps both histories and adds a merge commit. Rebase replays your commits "
                        + "on top of the other branch.",
                    TextWrapping = TextWrapping.Wrap,
                },
                PrimaryButtonText = "Merge", SecondaryButtonText = "Rebase", CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            var choice = await dialog.ShowAsync();
            // Still the same repository on the same branch as the question named.
            if (!ReferenceEquals(store, _store) || store.Status.BranchDisplay != current) return;
            if (choice == ContentDialogResult.Primary) await store.MergeAsync(name);
            else if (choice == ContentDialogResult.Secondary) await store.RebaseOntoAsync(name);
        }
        catch (Exception error)
        {
            // An event handler can't pass the failure on; both actions remain in the branch's menu.
            Trace.TraceWarning($"Dropping a branch failed: {error.GetType().Name}");
        }
    }
}
