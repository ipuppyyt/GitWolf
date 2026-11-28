using GitWolf.App.Services;
using GitWolf.Core;
using GitWolf.Store;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace GitWolf.App.Views;

/// <summary>
/// CI results of a commit: a summary that expands into the checks, with re-run and cancel. Hidden when
/// there are none. While a run is going it looks again every 15 seconds. Ported from ChecksSection.swift.
/// </summary>
public sealed class ChecksView : UserControl
{
    private readonly Expander _expander = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
    };
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(15) };
    private RepositoryStore? _store;
    private string _sha = "";
    private DateTimeOffset? _committed;
    private (CiStatus Status, string Buttons)? _shown;

    public ChecksView()
    {
        Content = _expander;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Visibility = Visibility.Collapsed;
        _poll.Tick += (_, _) => _ = _store?.PollChecksAsync(_sha, _committed);
        Loaded += (_, _) => { if (_store is not null) _poll.Start(); };
        // Moving the view within the page raises Unloaded too, while it stays loaded.
        Unloaded += (_, _) => { if (!IsLoaded) _poll.Stop(); };
    }

    /// <summary>Shows what the store knows about a commit; a null store or an empty hash clears it.</summary>
    internal void Show(RepositoryStore? store, string sha, DateTimeOffset? committed)
    {
        if (store is null || sha.Length == 0)
        {
            _store = null;
            _poll.Stop();
            Visibility = Visibility.Collapsed;
            return;
        }
        if (!ReferenceEquals(store, _store) || sha != _sha)
        {
            (_store, _sha, _shown) = (store, sha, null);
            _expander.IsExpanded = false;
            _poll.Start();
        }
        _committed = committed;

        var status = store.Checks(sha);
        Visibility = status is { IsEmpty: false } ? Visibility.Visible : Visibility.Collapsed;
        if (status is null || status.IsEmpty) return;
        // Rebuilt only when something visible changed, so polling doesn't move the focus or the scroll.
        var buttons = string.Concat(status.Checks.Select(c =>
            $"{(store.CanPerformCi(CiAction.Rerun, c) ? 'r' : store.CanPerformCi(CiAction.Cancel, c) ? 'c' : '-')}"
            + (store.IsCiBusy(sha, c) ? 'b' : '-')));
        if (_shown is { } shown && shown.Status.Equals(status) && shown.Buttons == buttons) return;
        _shown = (status, buttons);

        var overall = status.Overall ?? CheckState.Neutral;
        _expander.Header = Ui.Row(8, Icon(overall), Ui.Text(status.Summary, 12.5, bold: true));
        AutomationProperties.SetName(_expander, status.Summary);

        var offset = (_expander.Content as ScrollViewer)?.VerticalOffset ?? 0;
        var list = new StackPanel { Spacing = 4 };
        foreach (var check in status.Checks) list.Children.Add(Row(store, sha, check));
        var scroller = new ScrollViewer { Content = list, MaxHeight = 140 };
        _expander.Content = scroller;
        if (offset > 0) scroller.Loaded += (_, _) => scroller.ChangeView(null, offset, null, true);
    }

    private Grid Row(RepositoryStore store, string sha, CiCheck check)
    {
        var row = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition(),
                new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        var name = Ui.Text(check.Name, 12.5, wrap: false);
        name.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(name, 1);
        row.Children.Add(Icon(check.State));
        row.Children.Add(name);

        foreach (var action in new[] { CiAction.Cancel, CiAction.Rerun })
        {
            if (!store.CanPerformCi(action, check)) continue;
            var rerun = action == CiAction.Rerun;
            var button = new Button
            {
                Content = new FontIcon { Glyph = rerun ? "\uE72C" : "\uE71A", FontSize = 12 },
                Padding = new Thickness(5, 3, 5, 3), IsEnabled = !store.IsCiBusy(sha, check),
                Background = null, BorderThickness = new Thickness(0),
            };
            ToolTipService.SetToolTip(button, rerun ? "Re-run this check" : "Cancel this run");
            AutomationProperties.SetName(button, $"{(rerun ? "Re-run" : "Cancel")} {check.Name}");
            button.Click += (_, _) => _ = PerformAsync(store, action, sha, check);
            Grid.SetColumn(button, 2);
            row.Children.Add(button);
        }

        if (check.Url is { } url)
        {
            var details = new HyperlinkButton { Content = "Details", FontSize = 12, Padding = new Thickness(4, 0, 4, 0) };
            ToolTipService.SetToolTip(details, url.AbsoluteUri);
            details.Click += (_, _) => _ = Launcher.LaunchUriAsync(url);
            Grid.SetColumn(details, 3);
            row.Children.Add(details);
        }
        return row;
    }

    /// <summary>A running check shows a ring spinner instead of a glyph.</summary>
    private static FrameworkElement Icon(CheckState state) => state == CheckState.Pending
        ? new ProgressRing
        {
            IsActive = true, Width = 12, Height = 12, MinWidth = 12, MinHeight = 12, Foreground = Ui.CheckBrush(state),
            VerticalAlignment = VerticalAlignment.Center,
        }
        : new FontIcon
        {
            Glyph = Ui.CheckGlyph(state), FontSize = 12, Foreground = Ui.CheckBrush(state),
            VerticalAlignment = VerticalAlignment.Center,
        };

    /// <summary>Cancelling stops every job in the run, so it is asked about first.</summary>
    private async Task PerformAsync(RepositoryStore store, CiAction action, string sha, CiCheck check)
    {
        if (action == CiAction.Cancel && !await Dialogs.ConfirmAsync(XamlRoot, $"Cancel the run for “{check.Name}”?",
                "Other checks in the same run are stopped too.", "Cancel run"))
            return;
        await store.PerformCiAsync(action, sha, check);
    }
}
