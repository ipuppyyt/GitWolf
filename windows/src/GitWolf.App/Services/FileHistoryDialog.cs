using System.Diagnostics;
using System.Globalization;
using GitWolf.App.Views;
using GitWolf.Core;
using GitWolf.Store;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GitWolf.App.Services;

/// <summary>A file's history and blame. Ported from FileHistoryView.swift.</summary>
internal static class FileHistoryDialog
{
    private static readonly FontFamily Mono = new("Consolas");

    public static async Task ShowAsync(XamlRoot root, RepositoryStore store, string path)
    {
        var remotes = new HashSet<string>(store.Remotes);
        if (await store.ReadAsync(r => r.FileHistoryAsync(path, remotes)) is not { } history) return;

        var width = Math.Clamp(root.Size.Width - 160, 520, 1100);
        var height = Math.Clamp(root.Size.Height - 280, 240, 700);
        string? reveal = null;
        var dialog = new ContentDialog
        {
            RequestedTheme = (root.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
            XamlRoot = root, Title = path, CloseButtonText = "Close",
        };
        dialog.Resources["ContentDialogMaxWidth"] = 1200d;
        void Show(string hash)
        {
            reveal = hash;
            dialog.Hide();
        }

        // History: the commits on the left, what the selected one did to the file on the right.
        var commits = new ListView { Width = 280 };
        foreach (var commit in history)
        {
            var date = commit.Date.LocalDateTime;
            var item = new StackPanel
            {
                Padding = new Thickness(0, 4, 0, 4),
                Children =
                {
                    new TextBlock { Text = commit.Subject, TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock
                    {
                        Text = string.Create(CultureInfo.CurrentCulture, $"{commit.AuthorName} · {date:d} · {commit.ShortHash}"),
                        FontSize = 11.5, Opacity = 0.65, TextTrimming = TextTrimming.CharacterEllipsis,
                    },
                },
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item,
                string.Create(CultureInfo.CurrentCulture, $"{commit.Subject}, {commit.AuthorName}, {date:d}"));
            commits.Items.Add(item);
        }
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(commits, "Commits that changed this file");
        var diff = new ListView { SelectionMode = ListViewSelectionMode.None, ItemContainerStyle = Compact() };
        var open = new Button { Content = "Show in History", IsEnabled = false };
        open.Click += (_, _) => Show(history[commits.SelectedIndex].Hash);
        var historyPane = new Grid
        {
            ColumnSpacing = 10, RowSpacing = 8,
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition() },
            RowDefinitions = { new RowDefinition(), new RowDefinition { Height = GridLength.Auto } },
            Children = { commits, diff, open },
        };
        Grid.SetColumn(diff, 1);
        Grid.SetRow(open, 1);
        var shown = 0;
        commits.SelectionChanged += async (_, _) =>
        {
            var index = commits.SelectedIndex;
            open.IsEnabled = index >= 0;
            if (index < 0) return;
            var token = ++shown;
            try
            {
                var result = await store.ReadAsync(r => r.FileDiffAsync(path, history[index].Hash));
                if (token != shown) return;
                diff.ItemsSource = result is null ? [] : result.Lines.Select(Line).ToList();
            }
            catch (Exception e)
            {
                Trace.TraceWarning($"Showing a file's diff failed: {e.GetType().Name}");
            }
        };

        var blame = new ListView
        {
            SelectionMode = ListViewSelectionMode.None, Visibility = Visibility.Collapsed, ItemContainerStyle = Compact(),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(diff, "Changes in this commit");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(blame, "Blame");
        var history_ = new RadioButton { Content = "History", IsChecked = true, MinWidth = 90 };
        var blame_ = new RadioButton { Content = "Blame", MinWidth = 90 };
        var blameLoaded = false;
        history_.Checked += (_, _) =>
        {
            historyPane.Visibility = Visibility.Visible;
            blame.Visibility = Visibility.Collapsed;
        };
        blame_.Checked += async (_, _) =>
        {
            historyPane.Visibility = Visibility.Collapsed;
            blame.Visibility = Visibility.Visible;
            if (blameLoaded) return;
            blameLoaded = true;
            try
            {
                // Blame is slow on long files, so it runs only when asked for.
                var lines = await store.ReadAsync(r => r.BlameAsync(path));
                if (lines is null)
                {
                    blame.ItemsSource = new[] { new TextBlock { Text = "This file can’t be blamed." } };
                    return;
                }
                blame.ItemsSource = lines
                    .Select((l, i) => BlameRow(l, i == 0 || lines[i - 1].Hash != l.Hash, Show)).ToList();
            }
            catch (Exception e)
            {
                Trace.TraceWarning($"Blame failed: {e.GetType().Name}");
            }
        };

        var panes = new Grid { Height = height, Children = { historyPane, blame } };
        dialog.Content = new StackPanel
        {
            Width = width, Spacing = 10,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Children = { history_, blame_ } },
                panes,
            },
        };
        try
        {
            if (history.Count > 0) commits.SelectedIndex = 0;
            await dialog.ShowAsync();
        }
        catch (Exception e)
        {
            Trace.TraceWarning($"The file history failed: {e.GetType().Name}");
        }
        if (reveal is null) return;
        store.Reveal(reveal);
        if (store.SelectedCommit is null)
            store.ErrorMessage = "That commit is older than the loaded history. Scroll to the end of History to load more.";
    }

    /// <summary>Code lines sit close together.</summary>
    private static Style Compact() => new(typeof(ListViewItem))
    {
        Setters = { new Setter(FrameworkElement.MinHeightProperty, 18d) },
    };

    private static TextBlock Line(DiffLine line) => new()
    {
        Text = line.Kind switch
        {
            DiffLineKind.Addition => "+ " + line.Text,
            DiffLineKind.Deletion => "- " + line.Text,
            DiffLineKind.Context => "  " + line.Text,
            _ => line.Text,
        },
        FontFamily = Mono, FontSize = 12,
        Opacity = line.Kind is DiffLineKind.HunkHeader or DiffLineKind.Note ? 0.6 : 1,
        Foreground = line.Kind switch
        {
            DiffLineKind.Addition => Palette.Green,
            DiffLineKind.Deletion => Palette.Red,
            _ => (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
        },
    };

    private static Grid BlameRow(BlameLine line, bool first, Action<string> show)
    {
        var grid = new Grid
        {
            ColumnSpacing = 8, MinHeight = 18,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(190) },
                new ColumnDefinition { Width = new GridLength(36) },
                new ColumnDefinition(),
            },
        };
        if (first)
        {
            var who = new HyperlinkButton
            {
                Content = line.IsUncommitted
                    ? "Not committed"
                    : string.Create(CultureInfo.CurrentCulture, $"{line.Author} · {line.Date.LocalDateTime:d}"),
                FontSize = 11.5, Padding = new Thickness(0), MinHeight = 0, IsEnabled = !line.IsUncommitted,
            };
            ToolTipService.SetToolTip(who, $"{line.ShortHash} {line.Summary}");
            who.Click += (_, _) => show(line.Hash);
            grid.Children.Add(who);
        }
        var number = new TextBlock { Text = $"{line.Line}", FontFamily = Mono, FontSize = 11.5, Opacity = 0.5 };
        var text = new TextBlock { Text = line.Text, FontFamily = Mono, FontSize = 12 };
        Grid.SetColumn(number, 1);
        Grid.SetColumn(text, 2);
        grid.Children.Add(number);
        grid.Children.Add(text);
        return grid;
    }
}
