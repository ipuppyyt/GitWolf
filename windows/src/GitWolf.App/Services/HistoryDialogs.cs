using System.Diagnostics;
using System.Globalization;
using GitWolf.Core;
using GitWolf.Store;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GitWolf.App.Services;

/// <summary>Reset, undo and the reflog.</summary>
internal static class HistoryDialogs
{
    public static readonly IReadOnlyList<(ResetMode Mode, string Label)> ResetModes =
    [
        (ResetMode.Soft, "Soft (keep everything staged)…"),
        (ResetMode.Mixed, "Mixed (keep files, unstage)…"),
        (ResetMode.Hard, "Hard (discard local changes)…"),
    ];

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    public static async Task ResetAsync(XamlRoot root, RepositoryStore store, string target, ResetMode mode)
    {
        if (await store.ReadAsync(r => r.ResetImpactAsync(target)) is not { } impact) return;
        var lines = new List<string>
        {
            impact.DroppedCommits == 0
                ? "No commits leave the current branch."
                : $"{Count(impact.DroppedCommits, "commit")} will no longer be on the current branch.",
        };
        if (mode == ResetMode.Hard && impact.ModifiedFiles > 0)
            lines.Add($"Uncommitted changes in {Count(impact.ModifiedFiles, "file")} will be lost for good.");
        var branch = store.Status.BranchDisplay;
        if (await Dialogs.ConfirmAsync(root, $"{mode} reset “{branch}”?", string.Join("\n\n", lines), "Reset")
            && store.Status.BranchDisplay == branch)
            await store.ResetAsync(target, mode);
    }

    public static async Task UndoAsync(XamlRoot root, RepositoryStore store)
    {
        // Enough entries to find where a long rebase started.
        if (await store.ReadAsync(r => r.ReflogAsync()) is not { } log) return;
        var plan = UndoPlan.For(log);
        var confirmed = plan switch
        {
            UndoPlan.Reset reset => await Dialogs.ConfirmAsync(root, $"Undo “{reset.Label}”?",
                $"HEAD moves back to {Short(reset.Target)}. Uncommitted changes are kept; if they would be "
                    + "overwritten, nothing happens.", "Undo"),
            UndoPlan.SwitchBack back => await Dialogs.ConfirmAsync(root, $"Undo switching to “{back.From}”?",
                $"Go back to {(back.Detached ? Short(back.To) : $"“{back.To}”")}. Uncommitted changes come along "
                    + "if Git allows it.", "Switch back"),
            _ => false,
        };
        if (plan is null) store.ErrorMessage = "There is nothing to undo.";
        else if (confirmed) await store.UndoAsync(plan);

        static string Short(string hash) => hash.Length > 7 ? hash[..7] : hash;
    }

    public static async Task ReflogAsync(XamlRoot root, RepositoryStore store)
    {
        if (await store.ReadAsync(r => r.ReflogAsync()) is not { } log) return;
        var list = new ListView { Height = 360, SelectionMode = ListViewSelectionMode.Single };
        foreach (var entry in log)
        {
            var date = entry.Date.LocalDateTime;
            var item = new StackPanel
            {
                Padding = new Thickness(0, 4, 0, 4),
                Children =
                {
                    new TextBlock { Text = entry.Message, TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock
                    {
                        Text = string.Create(CultureInfo.CurrentCulture,
                            $"HEAD@{{{entry.Index}}} · {entry.ShortHash} · {date:d} {date:t}"),
                        FontSize = 11.5, Opacity = 0.65,
                    },
                },
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item,
                string.Create(CultureInfo.CurrentCulture, $"{entry.Message}, {date:d} {date:t}"));
            list.Items.Add(item);
        }
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(list, "Reflog entries");
        var hint = new TextBlock { Text = "Select an entry.", FontSize = 12, Opacity = 0.75 };
        // One dialog can't open another, so the choice is made after this one closes.
        Func<Task>? next = null;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var dialog = new ContentDialog
        {
            RequestedTheme = (root.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
            XamlRoot = root, Title = "Reflog", CloseButtonText = "Close",
            Content = new StackPanel { Width = 480, Spacing = 10, Children = { list, hint, actions } },
        };
        ReflogEntry? Selected() => list.SelectedIndex >= 0 ? log[list.SelectedIndex] : null;
        void Button(string text, Func<ReflogEntry, Task> run)
        {
            var button = new Button { Content = text, IsEnabled = false };
            button.Click += (_, _) =>
            {
                if (Selected() is not { } entry) return;
                next = () => run(entry);
                dialog.Hide();
            };
            actions.Children.Add(button);
        }
        Button("Check out", e => store.CheckoutCommitAsync(e.Hash));
        Button("New branch here…", e => Dialogs.NewBranchAsync(root, store, e.Hash));
        foreach (var (mode, _) in ResetModes) Button($"{mode} reset…", e => ResetAsync(root, store, e.Hash, mode));
        // Two rows fit the dialog's width.
        actions.Orientation = Orientation.Vertical;
        var first = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var second = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var buttons = actions.Children.OfType<Button>().ToList();
        actions.Children.Clear();
        for (var i = 0; i < buttons.Count; i++) (i < 2 ? first : second).Children.Add(buttons[i]);
        actions.Children.Add(first);
        actions.Children.Add(second);
        list.SelectionChanged += (_, _) =>
        {
            foreach (var button in buttons) button.IsEnabled = Selected() is not null;
        };
        try
        {
            await dialog.ShowAsync();
            if (next is not null) await next();
        }
        catch (Exception e)
        {
            Trace.TraceWarning($"The reflog failed: {e.GetType().Name}");
        }
    }
}
