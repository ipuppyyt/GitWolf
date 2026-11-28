using System.ComponentModel;
using System.Diagnostics;
using GitWolf.App.Services;
using GitWolf.App.Views;
using GitWolf.Core;
using GitWolf.Store;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using Windows.System;

namespace GitWolf.App;

/// <summary>Shell: repository rail, top bar, sidebar, list and detail panels for the selected repository.</summary>
public sealed partial class MainWindow : Window
{
    private readonly Workspace _workspace;
    private readonly AppSettings _settings = new();
    /// <summary>The repository the panels currently mirror.</summary>
    private RepositoryStore? _store;

    public MainWindow()
    {
        InitializeComponent();
        Title = "GitWolf";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        _workspace = new Workspace(_settings, uri => _ = Launcher.LaunchUriAsync(uri));
        Avatars.Enabled = _settings.State.ProfilePictures;
        Avatars.Changed += () =>
        {
            History.ShowPictures();
            if (!_holding) Detail.ShowPicture();
        };
        _workspace.Changed += Bind;
        Rail.Attach(_workspace);
        Rail.AddRequested += ShowAddMenu;
        Dashboard.CloneRequested += () => _ = CreateAsync(clone: true);
        Dashboard.NewRequested += () => _ = CreateAsync(clone: false);
        TopBar.JumpRequested += OpenPalette;
        Dashboard.OpenRequested += () => _ = PickAsync();
        Dashboard.JumpRequested += OpenPalette;
        Dashboard.PathsRequested += paths => _ = OpenAllAsync(paths);
        Diff.LayoutChanged += split => _workspace.SplitDiff = split;
        Rail.SettingsRequested += Settings.Open;
        AttachRepoIcons();
        Settings.Attach(this, _settings, _workspace);
        Settings.SplitChanged += split => _workspace.SplitDiff = Diff.IsSplit = split;
        _settings.Changed += OnSettingsChanged;
        Diff.SyntaxEnabled = _settings.State.SyntaxHighlighting;
        Diff.ShowOptions(_settings.State.IgnoreWhitespace, _settings.State.FullFileDiffs);
        Diff.OptionsChanged += (ignore, whole) =>
            _settings.Update(_settings.State with { IgnoreWhitespace = ignore, FullFileDiffs = whole });
        Root.ActualThemeChanged += (_, _) => Theming.ThemeChanged(this, _settings.State);
        Root.Loaded += (_, _) => Theming.Apply(this, _settings.State);
        AddShortcuts();
        AttachProvider();
        SetUpUpdates();
        SetUpOpenIn();
        SetUpShell();

        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated) return;
            _ = _store?.RequestRefresh();
            // Pictures on screen are checked for changes, and authors looked up if due.
            Avatars.Refresh();
            if (_store is { } store) LoadAvatars(store);
        };
        Closed += (_, _) =>
        {
            Avatars.Save();
            _notifications.Dispose();
            _workspace.Dispose();
        };
        SetUpPanels();
        // Starts over when something else is copied before it has faded.
        Dialogs.Copied += () => ShowNotice("Copied");
        Bind();
    }

    private async Task OpenAsync(string path)
    {
        try
        {
            ShowError(null);
            await _workspace.OpenAsync(path);
        }
        catch (GitException e)
        {
            ShowError(e.Message);
        }
    }

    private async Task PickAsync()
    {
        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            // Unpackaged apps must tell the picker which window owns it.
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            if (await picker.PickSingleFolderAsync() is { } folder) await OpenAsync(folder.Path);
        }
        catch (Exception e)
        {
            Trace.TraceError($"Opening a repository failed: {e.GetType().Name}");
            ShowError("The folder couldn’t be opened.");
        }
    }

    private void ShowAddMenu(FrameworkElement anchor)
    {
        var menu = new MenuFlyout();
        void Add(string text, Action action)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Add("Open repository\u2026", () => _ = PickAsync());
        Add("Clone repository\u2026", () => _ = CreateAsync(clone: true));
        Add("New repository\u2026", () => _ = CreateAsync(clone: false));
        menu.ShowAt(anchor);
    }

    private async Task CreateAsync(bool clone)
    {
        var window = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var git = _settings.CustomGitPath;
        var folder = clone
            ? await CreateDialogs.CloneAsync(
                Root.XamlRoot, window, git, _settings.Accounts, _settings.State.CloneFolder)
            : await CreateDialogs.NewAsync(Root.XamlRoot, window, git);
        if (clone && folder is not null && Path.GetDirectoryName(folder) is { } parent)
            _settings.Update(_settings.State with { CloneFolder = parent });
        if (folder is not null) await OpenAllAsync([folder]);
    }

    /// <summary>One at a time, so each repository is added before the next is checked.</summary>
    private async Task OpenAllAsync(IReadOnlyList<string> paths)
    {
        try
        {
            foreach (var path in paths) await OpenAsync(path);
        }
        catch (Exception e)
        {
            Trace.TraceError($"Opening a repository failed: {e.GetType().Name}");
            ShowError("The folder couldn’t be opened.");
        }
    }

    /// <summary>Points the panels at the selected repository.</summary>
    private void Bind()
    {
        Rail.Show();
        var selected = _workspace.Selected;
        if (!ReferenceEquals(selected, _store))
        {
            if (_store is not null)
            {
                _store.PropertyChanged -= OnStoreChanged;
                _store.Succeeded -= ShowNotice;
            }
            _store = selected;
            ReleaseDetail();
            if (selected is { IsLoadingSelection: true }) HoldDetail();
            if (selected is not null)
            {
                selected.PropertyChanged += OnStoreChanged;
                selected.Succeeded += ShowNotice;
                History.Attach(selected);
                Changes.Attach(selected);
                Sidebar.Attach(selected);
            }
            ShowError(selected?.ErrorMessage);
            Title = selected is null ? "GitWolf" : $"{selected.Name} — GitWolf";
            if (selected?.PullBlock is not null) DispatcherQueue.TryEnqueue(ShowPullBlock);
            if (selected?.RejectedPush is not null) DispatcherQueue.TryEnqueue(ShowRejectedPush);
            if (selected?.HookFailure is not null) DispatcherQueue.TryEnqueue(ShowHookFailure);
            if (selected?.Untrusted is not null) DispatcherQueue.TryEnqueue(ShowTrustQuestion);
        }
        ShowAll();
    }

    /// <summary>Asked once, for the repository on screen; one in the background asks when selected.</summary>
    private void ShowHookFailure()
    {
        if (_store is not { HookFailure: { } failure } store) return;
        store.HookFailure = null;
        _ = CommitDialogs.HookFailedAsync(Root.XamlRoot, store, failure);
    }

    private async void ShowTrustQuestion()
    {
        if (_store is not { Untrusted: { } risk } store) return;
        try
        {
            if (await Dialogs.TrustAsync(Root.XamlRoot, store.Name, risk)) await store.TrustAsync(risk);
            // Declined: the repository stays unread, and the banner says why.
            else store.ErrorMessage = new UntrustedRepositoryException(risk).Message
                + " Select it again to be asked once more.";
        }
        catch (Exception e)
        {
            // An event handler can't pass the failure on.
            Trace.TraceWarning($"Trusting a repository failed: {e.GetType().Name}");
        }
    }

    private void ShowPullBlock()
    {
        if (_store is not { PullBlock: { } block } store) return;
        store.PullBlock = null;
        _ = Dialogs.PullBlockedAsync(Root.XamlRoot, store, block);
    }

    private void ShowAll()
    {
        ShowPushOffer();
        var store = _store;
        var loaded = store?.IsLoaded == true;
        Home.Visibility = When(store is null);
        if (store is null) Dashboard.Show(_workspace);
        Repository.Visibility = When(loaded);
        Loading.IsActive = store is { Untrusted: null } && !loaded;
        SetTitleBar(loaded ? (UIElement)TopBar.DragRegion : HomeDragArea);
        ShowBusy();
        if (store is null || !loaded) return;
        LoadAvatars(store);
        Banner.Show(store);
        ToolBanners.Show(store);

        var history = store.Mode == StoreMode.History;
        History.Visibility = When(history);
        Changes.Visibility = When(store.Mode == StoreMode.Changes);
        Compare.Visibility = When(store.Mode == StoreMode.Compare);
        if (store.Mode == StoreMode.Compare) Compare.Show(store);
        StashPanel.Visibility = When(store.Mode == StoreMode.Stash);
        ShowStash();
        ShowCollab();
        TopBar.Show(store);
        History.ShowCommits();
        Changes.ShowFiles();
        Sidebar.Show();
        ShowDetail();
    }

    /// <summary>Right panel: commit header, files and diff in history; only the diff in changes.</summary>
    private void ShowDetail()
    {
        // Redrawn in one step once the new selection has been read.
        if (_holding || _store is not { } store) return;
        var history = store.Mode == StoreMode.History;
        var comparing = store.Mode == StoreMode.Compare;
        // Nothing to show on the right until a commit, or a compared file, is picked.
        // Pull requests and issues have their own detail view in this panel.
        if (CollabKindOf(store.Mode) is not null)
        {
            Detail.Visibility = Diff.Visibility = Conflict.Visibility = NoCommit.Visibility = Visibility.Collapsed;
            return;
        }
        var previewing = comparing || store.Mode == StoreMode.Stash;
        var noCommit = (history && store.SelectedCommit is null) || (previewing && store.SelectedTarget is null);
        NoCommit.Text = previewing ? "Select a file to see its changes" : "Select a commit to see its changes";
        Detail.Visibility = When(history && !noCommit);
        // A conflicted file gets the resolver instead of its diff.
        var conflict = store.Mode == StoreMode.Changes && store.SelectedTarget is { } target && target.Source == DiffSource.Conflicted
            && store.Status.Conflicted.Any(f => f.Path == target.Path)
                ? target.Path : null;
        Diff.Visibility = When(!noCommit && conflict is null);
        Conflict.Visibility = When(conflict is not null);
        NoCommit.Visibility = When(noCommit);
        if (history) Detail.Show(store);
        Conflict.Show(store, conflict);
        if (conflict is null) Diff.Show(store);
    }

    private void ShowStash()
    {
        if (_store is { Mode: StoreMode.Stash } store) StashPanel.Show(store);
    }

    private static Visibility When(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;

    private void ShowBusy()
    {
        var busy = _store?.IsBusy == true;
        Busy.IsActive = busy;
        BusyPill.Visibility = When(busy);
        BusyText.Text = _store?.BusyLabel ?? "";
    }

    private void OnStoreChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_store is not { } store || !ReferenceEquals(sender, store)) return;
        switch (e.PropertyName)
        {
            case nameof(RepositoryStore.Commits):
            case nameof(RepositoryStore.Graph):
                History.ShowCommits();
                LoadAvatars(store);
                break;
            case nameof(RepositoryStore.CommitFiles):
            case nameof(RepositoryStore.CommitMessage):
            case nameof(RepositoryStore.Checks):
                if (!_holding) Detail.Show(store);
                break;
            case nameof(RepositoryStore.IsLoadingSelection):
                if (store.IsLoadingSelection) HoldDetail();
                else _ = SwapDetailAsync(store);
                break;
            case nameof(RepositoryStore.SelectedCommitId):
                History.ShowSelection();
                ShowDetail();
                break;
            case nameof(RepositoryStore.SelectedTarget):
                History.ShowSelection();
                Changes.ShowSelection();
                ShowStash();
                ShowDetail();
                break;
            case nameof(RepositoryStore.Diff):
            case nameof(RepositoryStore.CanPatch):
            case nameof(RepositoryStore.LineSelection):
                ShowDetail();
                break;
            case nameof(RepositoryStore.Untrusted):
                ShowAll(); // Nothing is loading while the question is open.
                if (store.Untrusted is not null) DispatcherQueue.TryEnqueue(ShowTrustQuestion);
                break;
            case nameof(RepositoryStore.Operation):
                Banner.Show(store);
                break;
            case nameof(RepositoryStore.Bisect):
            case nameof(RepositoryStore.IsShallow):
                ToolBanners.Show(store);
                break;
            case nameof(RepositoryStore.LfsPaths):
                Changes.ShowFiles();
                break;
            case nameof(RepositoryStore.Status):
                Banner.Show(store);
                ShowDetail();
                Changes.ShowFiles();
                Sidebar.Show();
                TopBar.Show(store);
                break;
            case nameof(RepositoryStore.RemoteLocation):
                // The avatars, and the tabs on offer, depend on the remote's host.
                ShowAll();
                break;
            case nameof(RepositoryStore.LocalBranches):
            case nameof(RepositoryStore.RemoteBranches):
            case nameof(RepositoryStore.Remotes):
            case nameof(RepositoryStore.Tags):
            case nameof(RepositoryStore.Stashes):
            case nameof(RepositoryStore.ProviderTitle):
                Sidebar.Show();
                break;
            case nameof(RepositoryStore.DraftMessage):
            case nameof(RepositoryStore.Amend):
                Changes.ShowDraft();
                break;
            case nameof(RepositoryStore.IsBusy):
            case nameof(RepositoryStore.BusyLabel):
                ShowBusy();
                ShowStash();
                TopBar.Show(store);
                Changes.ShowCommitState();
                Banner.Show(store);
                ToolBanners.Show(store);
                Conflict.ShowState();
                break;
            case nameof(RepositoryStore.ErrorMessage):
                ShowError(store.ErrorMessage);
                break;
            case nameof(RepositoryStore.ReauthAccount):
                ShowReauth();
                break;
            case nameof(RepositoryStore.Comparison):
                Compare.Show(store);
                break;
            case nameof(RepositoryStore.SelectedStash):
                ShowStash();
                ShowDetail();
                break;
            case nameof(RepositoryStore.SearchResults):
                History.ShowCommits();
                break;
            case nameof(RepositoryStore.HookFailure):
                DispatcherQueue.TryEnqueue(ShowHookFailure);
                break;
            case nameof(RepositoryStore.PullBlock):
                // The dialog can start another pull, so wait until this one has finished.
                DispatcherQueue.TryEnqueue(ShowPullBlock);
                break;
            case nameof(RepositoryStore.PushOffer):
                ShowPushOffer();
                break;
            case nameof(RepositoryStore.RejectedPush):
                DispatcherQueue.TryEnqueue(ShowRejectedPush);
                break;
            case nameof(RepositoryStore.Mode):
            case nameof(RepositoryStore.IsLoaded):
                ShowAll();
                break;
        }
    }

    private void AddShortcuts()
    {
        void Add(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
        {
            var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
            accelerator.Invoked += (_, e) =>
            {
                e.Handled = true;
                action();
            };
            Root.KeyboardAccelerators.Add(accelerator);
        }
        const VirtualKeyModifiers shifted = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift;
        Root.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        Add(VirtualKey.K, VirtualKeyModifiers.Control, OpenPalette);
        Add(VirtualKey.O, VirtualKeyModifiers.Control, () => _ = PickAsync());
        // The comma key (VK_OEM_COMMA) has no named VirtualKey.
        Add((VirtualKey)188, VirtualKeyModifiers.Control, Settings.Open);
        Add(VirtualKey.F, shifted, () => _ = Idle?.FetchAsync());
        Add(VirtualKey.L, shifted, () => _ = Idle?.PullAsync());
        Add(VirtualKey.P, shifted, () => _ = Idle?.PushAsync());
        Add(VirtualKey.C, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu,
            () => _ = Idle?.OpenCompareAsync());
        AddMoreShortcuts(Add);
    }

    /// <summary>The selected repository when it can take a new operation.</summary>
    private RepositoryStore? Idle => _store is { IsLoaded: true, IsBusy: false } store ? store : null;

    private void OnSettingsChanged(SettingsState old, SettingsState now)
    {
        if (old.Appearance != now.Appearance || old.Accent != now.Accent)
        {
            Theming.Apply(this, now);
            Rail.Show();
        }
        Diff.SyntaxEnabled = now.SyntaxHighlighting;
        Changes.SpellCheck = now.SpellCheck;
        if (old.ProfilePictures != now.ProfilePictures)
        {
            Avatars.Enabled = now.ProfilePictures;
            Avatars.Refresh();
            if (_store is { } current) LoadAvatars(current);
        }
        if (old.CommitPageSize == now.CommitPageSize && old.DiffContext == now.DiffContext && old.GitPath == now.GitPath
            && old.IgnoreWhitespace == now.IgnoreWhitespace && old.FullFileDiffs == now.FullFileDiffs)
            return;
        foreach (var repo in _workspace.Repos) repo.ApplySettings();
    }

    private void ShowError(string? message)
    {
        ErrorText.Text = message ?? "";
        // A new message starts from its first line.
        ErrorScroller.ChangeView(null, 0, null, true);
        ShowReauth();
        Error.IsOpen = message is not null;
    }

    private void OnErrorClosed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        if (_store is { } store) store.ErrorMessage = null;
    }
}
