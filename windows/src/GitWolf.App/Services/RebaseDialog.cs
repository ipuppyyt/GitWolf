using System.Diagnostics;
using GitWolf.Core;
using GitWolf.Store;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GitWolf.App.Services;

/// <summary>
/// Edits an interactive rebase: reorder the commits and choose what happens to each. Ported from
/// RebaseSheet.swift.
/// </summary>
internal static class RebaseDialog
{
    /// <summary>The controls of one commit, kept on its list item so a reorder carries them along.</summary>
    private sealed record Row(RebaseItem Item, ComboBox Action, TextBox Message)
    {
        public RebaseItem Current => Item with
        {
            Action = (RebaseAction)Math.Max(Action.SelectedIndex, 0),
            // A text box separates lines with a carriage return only.
            Message = Message.Text.ReplaceLineEndings("\n"),
        };
    }

    public static async Task ShowAsync(XamlRoot root, RepositoryStore store, string from)
    {
        if (store.Operation is not null)
        {
            store.ErrorMessage = "Finish or abort the operation in progress first.";
            return;
        }
        if (await store.ReadAsync(r => r.RebaseCandidatesAsync(from)) is not { } candidates) return;
        if (candidates.HasMerges)
        {
            store.ErrorMessage = "This range contains merge commits, which an interactive rebase would flatten. "
                + "Start from a commit after the last merge.";
            return;
        }
        var plan = candidates.Plan;
        var width = Math.Clamp(root.Size.Width - 200, 460, 760);

        var list = new ListView
        {
            MaxHeight = Math.Clamp(root.Size.Height - 360, 180, 520),
            SelectionMode = ListViewSelectionMode.Single,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(list, "Commits to rebase");
        var problem = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
        var dialog = new ContentDialog
        {
            RequestedTheme = (root.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
            XamlRoot = root, Title = "Interactive rebase",
            PrimaryButtonText = "Start rebase", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 900d;

        RebasePlan Edited() => plan with { Items = list.Items.Cast<ListViewItem>().Select(i => ((Row)i.Tag).Current).ToList() };
        void Check()
        {
            var edited = Edited();
            var changed = !edited.Items.Select(i => (i.Hash, i.Action))
                .SequenceEqual(plan.Items.Select(i => (i.Hash, RebaseAction.Pick)));
            problem.Text = edited.Problem
                ?? (edited.Items.Any(i => i.Action == RebaseAction.Drop)
                    ? "Dropped commits leave the branch; they stay reachable from the reflog." : "");
            dialog.IsPrimaryButtonEnabled = edited.Problem is null && changed;
        }

        foreach (var item in plan.Items) list.Items.Add(MakeRow(item, width - 60, Check));

        void Move(int by)
        {
            var index = list.SelectedIndex;
            var target = index + by;
            if (index < 0 || target < 0 || target >= list.Items.Count) return;
            var moved = list.Items[index];
            list.Items.RemoveAt(index);
            list.Items.Insert(target, moved);
            list.SelectedIndex = target;
            Check();
        }
        var up = new Button { Content = "Move up" };
        var down = new Button { Content = "Move down" };
        up.Click += (_, _) => Move(-1);
        down.Click += (_, _) => Move(1);

        var count = plan.Items.Count == 1 ? "1 commit" : $"{plan.Items.Count} commits";
        dialog.Content = new StackPanel
        {
            Width = width, Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = $"Rewrites {count} on {store.Status.BranchDisplay}, oldest first. Select a commit and "
                        + "use the buttons to reorder. Squash and fixup fold into the commit above.",
                    TextWrapping = TextWrapping.Wrap, Opacity = 0.75,
                },
                list,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { up, down } },
                problem,
            },
        };
        Check();
        try
        {
            // The engine refuses to run if the branch moved while the dialog was open.
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) await store.StartRebaseAsync(Edited());
        }
        catch (Exception e)
        {
            // Fire-and-forget: e.g. a second dialog while one is already open.
            Trace.TraceWarning($"The rebase dialog failed: {e.GetType().Name}");
        }
    }

    private static ListViewItem MakeRow(RebaseItem item, double width, Action changed)
    {
        var action = new ComboBox { Width = 104 };
        foreach (var name in Enum.GetNames<RebaseAction>()) action.Items.Add(name);
        action.SelectedIndex = (int)item.Action;
        var hash = new TextBlock
        {
            Text = item.ShortHash, FontFamily = new("Consolas"), Opacity = 0.65,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var subject = new TextBlock
        {
            Text = item.Subject, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var message = new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 96,
            Text = item.Message, Visibility = Visibility.Collapsed,
        };
        var grid = new Grid { ColumnSpacing = 10, Width = width, Padding = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        FrameworkElement[] cells = [action, hash, subject, message];
        for (var i = 0; i < cells.Length; i++)
        {
            Grid.SetColumn(cells[i], Math.Min(i, 2));
            grid.Children.Add(cells[i]);
        }
        action.SelectionChanged += (_, _) =>
        {
            var chosen = (RebaseAction)action.SelectedIndex;
            var reword = chosen == RebaseAction.Reword;
            message.Visibility = reword ? Visibility.Visible : Visibility.Collapsed;
            subject.Visibility = reword ? Visibility.Collapsed : Visibility.Visible;
            subject.Opacity = chosen == RebaseAction.Drop ? 0.5 : 1;
            subject.TextDecorations = chosen == RebaseAction.Drop
                ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None;
            changed();
        };
        message.TextChanged += (_, _) => changed();
        var row = new ListViewItem { Content = grid, Tag = new Row(item, action, message) };
        // Working in a commit's controls selects it, so the move buttons act on that commit.
        row.GotFocus += (_, _) => row.IsSelected = true;
        return row;
    }
}
