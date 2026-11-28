using GitWolf.App.Views;
using GitWolf.Core;
using GitWolf.Store;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace GitWolf.App.Services;

/// <summary>"Mine", "Others" and "CI failing" across all open repositories.</summary>
internal static class PullRequestOverviewDialog
{
    public static async Task ShowAsync(XamlRoot root, Workspace workspace)
    {
        var overview = new PullRequestOverview();
        using var closed = new CancellationTokenSource();
        var filter = new ComboBox { MinWidth = 180 };
        AutomationProperties.SetName(filter, "Show");
        string[] titles = ["Mine", "Others", "CI failing"];
        foreach (var title in titles) filter.Items.Add(title);
        filter.SelectedIndex = 0;
        var status = Ui.Text("", 12, secondary: true, wrap: true);
        var list = new ListView { Height = 360, SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true };
        AutomationProperties.SetName(list, "Pull requests");
        var content = new StackPanel { Spacing = 10, Width = 480, Children = { filter, list, status } };
        var dialog = new ContentDialog
        {
            RequestedTheme = (root.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
            XamlRoot = root, Title = "Pull Requests Across Repositories", Content = content,
            CloseButtonText = "Done", PrimaryButtonText = "Refresh",
        };

        void Show()
        {
            var shown = overview.Entries.Where(e => filter.SelectedIndex switch
            {
                0 => e.Mine,
                1 => !e.Mine,
                _ => e.Checks == CheckState.Failure,
            }).ToList();
            list.Items.Clear();
            foreach (var entry in shown) list.Items.Add(Row(entry, workspace, dialog));
            var lines = new List<string>();
            if (overview.IsLoading) lines.Add("Loading…");
            else if (shown.Count == 0)
                lines.Add(filter.SelectedIndex switch
                {
                    0 => "You have no open pull requests in these repositories.",
                    1 => "No other open pull requests.",
                    _ => "Nothing is failing.",
                });
            if (overview.SignedOut)
                lines.Add("Add an account in Settings → Accounts to see which pull requests are yours.");
            if (overview.Skipped.Count > 0) lines.Add($"Couldn’t load: {string.Join(", ", overview.Skipped)}.");
            status.Text = string.Join("\n", lines);
            dialog.IsPrimaryButtonEnabled = !overview.IsLoading;
        }

        overview.Changed += Show;
        filter.SelectionChanged += (_, _) => Show();
        list.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is FrameworkElement { Tag: Uri url }) _ = Launcher.LaunchUriAsync(url);
        };
        dialog.PrimaryButtonClick += (sender, e) =>
        {
            e.Cancel = true;
            _ = overview.LoadAsync(workspace.Repos.ToList(), closed.Token);
        };
        var loading = overview.LoadAsync(workspace.Repos.ToList(), closed.Token);
        await dialog.ShowAsync();
        overview.Changed -= Show;
        closed.Cancel();
        await loading;
    }

    private static FrameworkElement Row(OverviewEntry entry, Workspace workspace, ContentDialog dialog)
    {
        var item = entry.Item;
        var result = entry.Checks switch
        {
            CheckState.Success => "CI passed",
            CheckState.Failure => "CI failed",
            CheckState.Pending => "CI running",
            _ => null,
        };
        var detail = $"{entry.Repository.Name} #{item.Number} · {item.Author}"
            + (item.IsDraft ? " · Draft" : "") + (result is null ? "" : $" · {result}")
            + (item.Updated is null ? "" : $" · {Ui.Ago(item.Updated)}");
        var title = Ui.Text(item.Title, 13);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        var below = Ui.Text(detail, 11, secondary: true);
        below.TextTrimming = TextTrimming.CharacterEllipsis;
        if (entry.Checks is { } state && state != CheckState.Neutral) below.Foreground = Ui.CheckBrush(state);
        var row = new StackPanel
        {
            Padding = new Thickness(0, 4, 0, 4), Tag = item.Url, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Children = { title, below },
        };
        AutomationProperties.SetName(row, $"{item.Title}, {detail}");
        var show = new MenuFlyoutItem { Text = "Show repository" };
        show.Click += (_, _) =>
        {
            dialog.Hide();
            if (workspace.Repos.Contains(entry.Repository)) workspace.Select(entry.Repository);
        };
        row.ContextFlyout = new MenuFlyout { Items = { show } };
        return row;
    }
}
