using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using GitWolf.App.Services;
using GitWolf.Core;
using GitWolf.Store;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace GitWolf.App.Views;

/// <summary>Header and file list of the selected commit. Ported from CommitDetailView.swift.</summary>
public sealed partial class CommitDetailView : UserControl
{
    private readonly ObservableCollection<FileRow> _files = [];
    private RepositoryStore? _store;
    private string? _shownHash;
    private bool _syncing;

    public CommitDetailView()
    {
        InitializeComponent();
        Files.ItemsSource = _files;
    }

    internal void Show(RepositoryStore store)
    {
        if (!ReferenceEquals(_store, store))
        {
            _store = store;
            _shownHash = null;
        }
        if (store.SelectedCommit is not { } commit) return;

        var parts = store.CommitMessage.Split('\n', 2);
        Subject.Text = parts[0].Trim().Length > 0 ? parts[0].Trim() : commit.Subject;
        Body.Text = parts.Length > 1 ? parts[1].Trim() : "";
        BodyScroll.Visibility = Body.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (_shownHash != commit.Hash)
        {
            _shownHash = commit.Hash;
            // The tick belongs to the commit that was copied.
            CopiedAnimation.Stop();
            CopiedPlayer.Stop();
            Author.Text = commit.AuthorName;
            var date = commit.Date.LocalDateTime;
            Date.Text = string.Create(CultureInfo.CurrentCulture, $"{date:d MMM yyyy} at {date:t}");
            Initials.Text = CommitRowView.InitialsOf(commit.AuthorName);
            Avatar.Background = Palette.Avatar(commit.AuthorEmail);
            Avatars.Show(Picture, commit.AuthorEmail);
            Hash.Text = commit.ShortHash;
            ShowParents(commit);
            _ = ShowSignatureAsync(store, commit.Hash);
        }

        Checks.Show(store, commit.Hash, commit.Date);

        var source = DiffSource.ForCommit(commit.Hash);
        _syncing = true;
        ListSync.Apply(_files, store.CommitFiles.Select(f => new FileRow(f, source)).ToList(), r => r.File!.Path);
        Files.SelectedItem = _files.FirstOrDefault(r => r.File?.Path == store.SelectedTarget?.Path);
        _syncing = false;
    }

    internal PanelSplitter HeightSplitter => FilesSplitter;

    internal double FilesListHeight => Files.ActualHeight;

    /// <summary>A height chosen by dragging, or null to fit the files, up to a limit.</summary>
    internal void SetFilesHeight(double? height)
    {
        Files.Height = height ?? double.NaN;
        Files.MaxHeight = height ?? 190;
    }

    internal void ShowPicture()
    {
        if (_store?.SelectedCommit is { } commit) Avatars.Show(Picture, commit.AuthorEmail);
    }

    private void ShowParents(Commit commit)
    {
        Parents.Children.Clear();
        Parents.Visibility = commit.Parents.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (commit.Parents.Count == 0) return;
        Parents.Children.Add(new TextBlock
        {
            Text = commit.Parents.Count > 1 ? "Parents" : "Parent", FontSize = 11.5, Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center,
        });
        foreach (var parent in commit.Parents)
        {
            var link = new HyperlinkButton
            {
                Content = parent[..Math.Min(7, parent.Length)], FontSize = 11.5, Padding = new Thickness(0),
                FontFamily = new FontFamily("Consolas"),
            };
            link.Click += (_, _) => _store?.Reveal(parent);
            Parents.Children.Add(link);
        }
    }

    private (string Hash, Task<CommitSignature?> Check)? _signature;

    /// <summary>Starts checking the signature of a commit that is about to be shown.</summary>
    internal Task PrepareAsync(RepositoryStore store)
    {
        if (store.SelectedCommit is not { } commit || commit.Hash == _shownHash) return Task.CompletedTask;
        var repo = store.Repo;
        var check = Task.Run(() => repo.SignatureAsync(commit.Hash));
        _signature = (commit.Hash, check);
        return check;
    }

    private async Task ShowSignatureAsync(RepositoryStore store, string hash)
    {
        Signature.Visibility = Visibility.Collapsed;
        try
        {
            var repo = store.Repo;
            // Already finished when prepared in time, so the badge appears with the rest.
            var signature = await (_signature is { } ready && ready.Hash == hash
                ? ready.Check : Task.Run(() => repo.SignatureAsync(hash)));
            if (signature is null || _shownHash != hash || !ReferenceEquals(store, _store)) return;
            // The end of a long key is the part people compare.
            var key = signature.Key.Length > 16 ? signature.Key[^16..] : signature.Key;
            var detail = string.Join(" · ", new[] { signature.Signer, key }.Where(part => part.Length > 0));
            var (tint, glyph) = signature.State switch
            {
                SignatureState.Good => (Palette.Green, "\uE73E"),
                SignatureState.Bad or SignatureState.Revoked => (Palette.Red, "\uE711"),
                _ => (Palette.Orange, "\uE7BA"),
            };
            SignatureTitle.Text = signature.Title;
            SignatureDetail.Text = detail;
            SignatureDetail.Visibility = detail.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            SignatureIcon.Glyph = glyph;
            SignatureIcon.Foreground = tint;
            Signature.Background = Palette.Tint(tint, 0x1F);
            Signature.Visibility = Visibility.Visible;
        }
        catch (Exception e)
        {
            Trace.TraceWarning($"Checking a signature failed: {e.GetType().Name}");
        }
    }

    private void OnContext(UIElement sender, ContextRequestedEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        while (element is not null and not ListViewItem) element = VisualTreeHelper.GetParent(element);
        if (_store is not { } store || element is not ListViewItem item
            || Files.ItemFromContainer(item) is not FileRow { File: { } file }) return;
        var menu = new MenuFlyout();
        void Add(string text, Action action)
        {
            var entry = new MenuFlyoutItem { Text = text };
            entry.Click += (_, _) => action();
            menu.Items.Add(entry);
        }
        Add("History & blame\u2026", () => _ = FileHistoryDialog.ShowAsync(XamlRoot, store, file.Path));
        Add("Copy path", () => Dialogs.Copy(file.Path));
        e.Handled = true;
        if (e.TryGetPosition(item, out var point)) menu.ShowAt(item, point);
        else menu.ShowAt(item);
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (_store?.SelectedCommit is not { } commit) return;
        Dialogs.Copy(commit.Hash);
        ShowCopied();
    }

    /// <summary>Draws a tick over the copy icon. Starts over when clicked again.</summary>
    private void ShowCopied()
    {
        // The success colour of the current theme.
        CopiedTick.SetColorProperty("Foreground", ActualTheme == ElementTheme.Light
            ? Windows.UI.Color.FromArgb(255, 0x0F, 0x7B, 0x0F)
            : Windows.UI.Color.FromArgb(255, 0x6C, 0xCB, 0x5F));
        CopiedAnimation.Begin();
        _ = CopiedPlayer.PlayAsync(
            CopiedTick.Markers["NormalOffToNormalOn_Start"], CopiedTick.Markers["NormalOffToNormalOn_End"], false);
    }

    private void OnFileSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && Files.SelectedItem is FileRow { File: { } file } row) _store?.SelectChange(file, row.Source);
    }

    private void OnFileRow(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.ItemContainer.ContentTemplateRoot is FileRowView view && args.Item is FileRow row)
        {
            view.Show(row, null, null);
            Spoken.Name(args.ItemContainer, Spoken.File(row));
        }
    }
}
