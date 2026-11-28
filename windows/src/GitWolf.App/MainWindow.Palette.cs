using GitWolf.App.Services;
using GitWolf.App.Views;
using GitWolf.Core;
using GitWolf.Store;

namespace GitWolf.App;

/// <summary>The command palette's entries.</summary>
public sealed partial class MainWindow
{
    private void OpenPalette()
    {
        var items = _workspace.Repos
            .Select(r => new PaletteItem(Icons.File, r.Name, "Switch repository", () => _workspace.Select(r)))
            .ToList();
        if (Idle is { } s)
        {
            void Add(Icons.Shape glyph, string title, string subtitle, Action run) =>
                items.Add(new PaletteItem(glyph, title, subtitle, run));
            if (s.Operation is { } operation)
            {
                var verb = operation.Verb.ToLowerInvariant();
                if (s.Status.Conflicted.Count == 0)
                    Add(Icons.Changes, $"Continue {verb}", "Repository action", () => _ = s.ContinueOperationAsync());
                Add(Icons.Changes, $"Abort {verb}\u2026", "Repository action",
                    () => _ = Dialogs.AbortAsync(Root.XamlRoot, s, operation));
            }
            Add(Icons.Fetch, "Fetch", "Repository action", () => _ = s.FetchAsync());
            Add(Icons.Pull, "Pull", "Repository action", () => _ = s.PullAsync());
            Add(Icons.Push, "Push", "Repository action", () => _ = s.PushAsync());
            AddSyncItems(items, s);
            Add(Icons.Plus, "New branch…", "Repository action",
                () => _ = Dialogs.NewBranchAsync(Root.XamlRoot, s, null));
            Add(Icons.Branch, "Clean up branches\u2026", "Repository action",
                () => _ = BranchDialogs.CleanupAsync(Root.XamlRoot, s));
            Add(Icons.Branch, "Worktrees & submodules\u2026", "Repository action",
                () => _ = CheckoutDialog.ShowAsync(Root.XamlRoot, s, WindowHandle, path => _ = OpenAsync(path)));
            Add(Icons.File, "Apply patch\u2026", "Repository action",
                () => _ = ToolDialogs.ApplyPatchesAsync(Root.XamlRoot, s, WindowHandle));
            Add(Icons.File, "Create patch from changes\u2026", "Repository action",
                () => _ = ToolDialogs.SavePatchAsync(s, WindowHandle));
            if (s.Bisect.IsActive)
                Add(Icons.History, "Stop bisect", "Repository action", () => _ = s.StopBisectAsync());
            if (s.IsShallow)
                Add(Icons.Fetch, "Fetch full history", "Sync", () => _ = s.UnshallowAsync());
            Add(Icons.Branch, "Compare refs\u2026", "Repository action", () => _ = s.OpenCompareAsync());
            Add(Icons.History, "Reflog\u2026", "Repository action",
                () => _ = HistoryDialogs.ReflogAsync(Root.XamlRoot, s));
            Add(Icons.History, "Repository statistics…", "Repository action",
                () => _ = InsightsDialog.ShowAsync(Root.XamlRoot, s));
            Add(Icons.History, "Undo last change\u2026", "Repository action",
                () => _ = HistoryDialogs.UndoAsync(Root.XamlRoot, s));
            Add(Icons.History, "Show history", "View", () => s.Mode = StoreMode.History);
            Add(Icons.Changes, "Show changes", "View", () => s.Mode = StoreMode.Changes);
            if (s.WebLinks is not null)
            {
                var site = s.ProviderTitle ?? "the web";
                Add(Icons.OpenLink, $"Open on {site}", "Repository action", () => s.OpenOnWeb(new WebTarget.Repository()));
                Add(Icons.OpenLink, $"Open branch on {site}", "Repository action", () => s.OpenOnWeb(new WebTarget.Branch()));
                Add(Icons.OpenLink, $"Create pull request on {site}", "Repository action",
                    () => s.OpenOnWeb(new WebTarget.PullRequest()));
            }
            AddProviderItems(items, s);
            foreach (var branch in s.LocalBranches.Where(b => !b.IsCurrent))
                Add(Icons.Branch, branch.Name, "Check out branch", () => _ = s.CheckoutAsync(branch));
        }
        AddOpenInItems(items);
        if (_workspace.Repos.Count > 0)
            items.Add(new PaletteItem(Icons.Pull, "Pull requests across repositories…", "Hosting",
                () => _ = PullRequestOverviewDialog.ShowAsync(Root.XamlRoot, _workspace)));
        items.Add(new PaletteItem(Icons.Plus, "Open repository…", "Workspace", () => _ = PickAsync()));
        items.Add(new PaletteItem(Icons.Plus, "Clone repository\u2026", "Workspace", () => _ = CreateAsync(clone: true)));
        items.Add(new PaletteItem(Icons.Plus, "New repository\u2026", "Workspace", () => _ = CreateAsync(clone: false)));
        items.Add(new PaletteItem(Icons.Plus, "Settings\u2026", "App", Settings.Open));
        items.Add(new PaletteItem(Icons.Fetch, "Check for Updates\u2026", "App",
            () => _ = CheckForUpdatesAsync(manual: true)));
        items.Add(new PaletteItem(Icons.File, "About GitWolf…", "App",
            () => _ = AboutDialog.ShowAsync(Root.XamlRoot)));
        CommandPalette.Open(items);
    }
}
