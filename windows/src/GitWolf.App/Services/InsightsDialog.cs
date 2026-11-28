using System.Globalization;
using GitWolf.App.Views;
using GitWolf.Core;
using GitWolf.Store;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace GitWolf.App.Services;

/// <summary>Repository statistics. Ported from InsightsSheet.swift.</summary>
internal static class InsightsDialog
{
    private const int Rows = 10;
    private const int Weeks = 52;
    private const double ChartHeight = 64;

    public static async Task ShowAsync(XamlRoot root, RepositoryStore store)
    {
        if (await store.ReadAsync(r => r.InsightsAsync()) is not { } data) return;
        var content = new StackPanel { Spacing = 8, Width = 460, Padding = new Thickness(0, 0, 14, 0) };
        content.Children.Add(Ui.Text(
            $"{CountLabel.Text(data.Commits, "commit")} · {CountLabel.Text(data.Contributors.Count, "contributor")} · "
                + $"+{data.Added:N0} −{data.Deleted:N0} lines", 13, wrap: true));
        if (data.Truncated)
            content.Children.Add(Ui.Text("Only the newest commits are counted; older history is left out.", 12,
                secondary: true, wrap: true));
        if (data.Weeks.Count > 0)
        {
            Heading(content, "COMMITS PER WEEK");
            content.Children.Add(Chart(data.Weeks));
        }
        Table(content, "CONTRIBUTORS", data.Contributors.Select(c =>
            (c.Name, $"{CountLabel.Text(c.Commits, "commit")} · +{c.Added:N0} −{c.Deleted:N0}")));
        Table(content, "MOST CHANGED FILES", data.HotFiles.Select(f => (f.Path, CountLabel.Text(f.Commits, "commit"))));
        Table(content, "MOST LINES CHANGED", data.ChurnFiles.Select(f => (f.Path, CountLabel.Text(f.Churn, "line"))));
        Table(content, "OWNERSHIP BY FOLDER", data.Ownership.Select(o => (o.Folder,
            $"{o.Owner} · {o.Share.ToString("P0", CultureInfo.CurrentCulture)}")));

        await new ContentDialog
        {
            RequestedTheme = (root.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
            XamlRoot = root, Title = $"Statistics for {store.Name}", CloseButtonText = "Done",
            DefaultButton = ContentDialogButton.Close,
            Content = new ScrollViewer { Content = content, MaxHeight = 520 },
        }.ShowAsync();
    }

    private static void Heading(Panel panel, string title)
    {
        var text = Ui.Text(title, 11, bold: true, secondary: true);
        text.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(text);
    }

    private static void Table(Panel panel, string title, IEnumerable<(string Name, string Detail)> rows)
    {
        var shown = rows.Take(Rows).ToList();
        if (shown.Count == 0) return;
        Heading(panel, title);
        foreach (var (name, detail) in shown)
        {
            var row = new Grid
            {
                ColumnSpacing = 12,
                ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } },
            };
            var left = Ui.Text(name, 12.5, wrap: false);
            left.TextTrimming = TextTrimming.CharacterEllipsis;
            ToolTipService.SetToolTip(left, name);
            var right = Ui.Text(detail, 12, secondary: true, wrap: false);
            Grid.SetColumn(right, 1);
            row.Children.Add(left);
            row.Children.Add(right);
            panel.Children.Add(row);
        }
    }

    /// <summary>One flat bar per week for the last year with commits, quiet weeks included.</summary>
    private static FrameworkElement Chart(IReadOnlyList<WeekStat> weeks)
    {
        var counts = weeks.ToDictionary(w => w.Start, w => w.Commits);
        var last = weeks[^1].Start;
        var first = weeks[0].Start;
        var shown = Math.Min(Weeks, (last.DayNumber - first.DayNumber) / 7 + 1);
        var starts = Enumerable.Range(0, shown).Select(i => last.AddDays(-7 * (shown - 1 - i))).ToList();
        var peak = Math.Max(1, starts.Max(s => counts.GetValueOrDefault(s)));
        var bars = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Height = ChartHeight };
        foreach (var start in starts)
        {
            var count = counts.GetValueOrDefault(start);
            var bar = new Rectangle
            {
                Width = 6, Height = count == 0 ? 1 : Math.Max(3, ChartHeight * count / peak),
                Fill = Palette.Ref(RefKind.Local), VerticalAlignment = VerticalAlignment.Bottom,
            };
            ToolTipService.SetToolTip(bar, $"Week of {start.ToString("d", CultureInfo.CurrentCulture)}: {count:N0}");
            bars.Children.Add(bar);
        }
        AutomationProperties.SetName(bars, $"Commits per week over {CountLabel.Text(shown, "week")}, at most {peak} in one week");
        return bars;
    }
}
