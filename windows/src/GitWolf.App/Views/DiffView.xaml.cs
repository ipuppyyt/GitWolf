using System.Diagnostics;
using GitWolf.Core;
using GitWolf.Store;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GitWolf.App.Views;

public sealed partial class DiffView : UserControl
{
    private sealed record Highlights(
        Dictionary<int, List<SyntaxToken>> Tokens, Dictionary<int, IReadOnlyList<TextSpan>> Changes);

    /// <summary>Advance width of 13px Consolas.</summary>
    private const double CharWidth = 7.15;
    private const double GutterWidth = 112;

    private double _rowWidth;
    private FileDiff? _diff;
    private string _path = "";
    private Dictionary<int, List<SyntaxToken>> _tokens = [];
    private IReadOnlyDictionary<int, IReadOnlyList<TextSpan>> _changes = NoChanges;
    private static readonly Dictionary<int, IReadOnlyList<TextSpan>> NoChanges = [];

    public DiffView()
    {
        InitializeComponent();
        // Syntax colours differ between light and dark.
        ActualThemeChanged += (_, _) => Rebind();
        // Rows fill at least the visible width, which isn't known until the list is laid out.
        Lines.SizeChanged += (_, e) =>
        {
            if (e.NewSize.Width != e.PreviousSize.Width) Repaint();
        };
    }

    private bool _split;

    /// <summary>Raised when the user picks the other layout, so it can be remembered.</summary>
    internal event Action<bool>? LayoutChanged;

    private bool _syntax = true;

    /// <summary>The syntax highlighting setting.</summary>
    internal bool SyntaxEnabled
    {
        get => _syntax;
        set
        {
            if (_syntax == value) return;
            _syntax = value;
            _tokens = [];
            _prepared = null;
            Repaint();
            if (value && _diff is { } diff) _ = HighlightAsync(diff, _path);
        }
    }

    internal bool IsSplit
    {
        get => _split;
        set
        {
            _split = value;
            Rebind();
        }
    }

    private RepositoryStore? _store;
    private DiffStaging? _staging;

    internal void Show(RepositoryStore store)
    {
        _store = store;
        var diff = store.Diff;
        var path = store.SelectedTarget?.Path ?? "";
        var slash = path.LastIndexOf('/');
        Folder.Text = path[..(slash + 1)];
        FileName.Text = path[(slash + 1)..] + (diff?.IsTruncated == true ? "  (truncated)" : "");
        Header.Visibility = path.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        TypeIcon.Show(path.Length == 0 ? null : FileIcons.ForPath(path));
        var message = store.SelectedTarget is null ? "Select a file to see its changes"
            : diff is null ? ""
            : diff.IsBinary ? "Binary file"
            : diff.Lines.Count == 0 ? (diff.IgnoresWhitespace ? "Only whitespace changed" : "No textual changes")
            : "";
        Empty.Text = ShowImages(store, diff) ? "" : message;

        var shown = message.Length == 0 ? diff : null;
        // The options stay reachable when ignoring whitespace leaves nothing to show.
        Tools.Visibility = diff is null || (diff.IsBinary) || store.SelectedTarget is null
            ? Visibility.Collapsed : Visibility.Visible;
        var staging = shown is not null && store.CanPatch ? Staging(store) : null;
        // Rows carry buttons and selection marks, so they are redrawn when either changes.
        var redraw = staging is null != _staging is null || staging?.Staged != _staging?.Staged
            || !ReferenceEquals(staging?.Selection, _staging?.Selection)
            || staging?.Stash is null != _staging?.Stash is null;
        _staging = staging;
        ShowSelectionBar();
        if (ReferenceEquals(_diff, shown))
        {
            ShowLayout();
            if (redraw) Repaint();
            return;
        }
        _diff = shown;
        _path = path;
        _unfolded.Clear();
        // Colours worked out in advance are drawn with the text.
        var done = Prepared(shown, path) is { IsCompletedSuccessfully: true } ready ? ready.Result : null;
        _tokens = done?.Tokens ?? [];
        _changes = done?.Changes ?? NoChanges;
        Added.Text = $"+{shown?.Lines.Count(l => l.Kind == DiffLineKind.Addition) ?? 0}";
        Removed.Text = $"\u2212{shown?.Lines.Count(l => l.Kind == DiffLineKind.Deletion) ?? 0}";
        // Every row gets the width of the longest line, so horizontal scrolling stays stable.
        var longest = shown is { Lines.Count: > 0 } ? shown.Lines.Max(DiffRowView.DisplayLength) : 0;
        _rowWidth = GutterWidth + longest * CharWidth;
        Rebind();
        if (shown is not null && Prepared(shown, path) is not { IsCompletedSuccessfully: true })
            _ = HighlightAsync(shown, path);
    }

    private void OnLayout(object sender, RoutedEventArgs e)
    {
        var split = ReferenceEquals(sender, SplitButton);
        if (split == _split) return;
        IsSplit = split;
        LayoutChanged?.Invoke(split);
    }

    private void ShowLayout()
    {
        Lines.Visibility = _diff is not null && !_split ? Visibility.Visible : Visibility.Collapsed;
        Split.Visibility = _diff is not null && _split ? Visibility.Visible : Visibility.Collapsed;
        UnifiedSelected.Visibility = _split ? Visibility.Collapsed : Visibility.Visible;
        SplitSelected.Visibility = _split ? Visibility.Visible : Visibility.Collapsed;
    }

    private (FileDiff Diff, string Path, Task<Highlights> Work)? _prepared;

    /// <summary>Starts colouring a diff that is about to be shown.</summary>
    internal Task PrepareAsync(RepositoryStore store)
    {
        if (store.Diff is not { } diff || ReferenceEquals(diff, _diff)) return Task.CompletedTask;
        return Start(diff, store.SelectedTarget?.Path ?? "");
    }

    private Task<Highlights> Start(FileDiff diff, string path)
    {
        var syntax = _syntax;
        var work = Task.Run(() => new Highlights(
            syntax ? SyntaxHighlighter.Tokens(diff, path) : [], diff.InlineRanges()));
        _prepared = (diff, path, work);
        return work;
    }

    private Task<Highlights>? Prepared(FileDiff? diff, string path) =>
        _prepared is { } ready && ReferenceEquals(ready.Diff, diff) && ready.Path == path ? ready.Work : null;

    /// <summary>Tokenises off the UI thread; a result for a diff no longer shown is dropped.</summary>
    private async Task HighlightAsync(FileDiff diff, string path)
    {
        try
        {
            var (tokens, changes) = await (Prepared(diff, path) ?? Start(diff, path));
            if (!ReferenceEquals(_diff, diff) || path != _path || (tokens.Count == 0 && changes.Count == 0)) return;
            (_tokens, _changes) = (_syntax ? tokens : [], changes);
            Repaint();
        }
        catch (Exception e)
        {
            // Colouring is optional; the plain diff stays visible.
            Trace.TraceWarning($"Syntax highlighting failed: {e.GetType().Name}");
        }
    }

    /// <summary>Redraws only the rows that exist; later rows are drawn as they appear.</summary>
    private void Repaint()
    {
        if (Lines.ItemsPanelRoot is ItemsStackPanel unified)
            foreach (var child in unified.Children)
                if (child is ListViewItem { ContentTemplateRoot: DiffRowView view } item
                    && Lines.ItemFromContainer(item) is DiffLine line)
                    Render(view, line);
        if (Split.ItemsPanelRoot is ItemsStackPanel split)
            foreach (var child in split.Children)
                if (child is ListViewItem { ContentTemplateRoot: SplitRowView view } item
                    && Split.ItemFromContainer(item) is SplitRow row)
                    view.Show(row, _tokens, _changes, _staging, Unfold);
    }

    /// <summary>Only the visible layout holds items, so the hidden one costs nothing.</summary>
    private void Rebind()
    {
        ShowLayout();
        Lines.ItemsSource = null;
        Split.ItemsSource = null;
        (_lineRows, _splitRows) = (null, null);
        if (_diff is null) return;
        if (_split) Split.ItemsSource = _splitRows = new(SideBySide.Rows(_diff, Rows(_diff)));
        else Lines.ItemsSource = _lineRows = new(Rows(_diff));
    }

    private void OnSplitRow(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.ItemContainer.ContentTemplateRoot is SplitRowView view && args.Item is SplitRow row)
        {
            view.Show(row, _tokens, _changes, _staging, Unfold);
            Spoken.Name(args.ItemContainer, Spoken.Split(row));
        }
    }

    private void Render(DiffRowView view, DiffLine line) =>
        view.Show(line, _tokens.GetValueOrDefault(line.Id),
            Math.Max(_rowWidth + (_staging is null ? 0 : LineSelector.ColumnWidth), Lines.ActualWidth - 16),
            _staging, Lines.ActualWidth, _changes.GetValueOrDefault(line.Id), Unfold);

    private void OnRow(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.ItemContainer.ContentTemplateRoot is DiffRowView view && args.Item is DiffLine line)
        {
            Render(view, line);
            Spoken.Name(args.ItemContainer, Spoken.Line(line));
        }
    }
}
