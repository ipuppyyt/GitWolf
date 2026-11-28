using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using GitWolf.Core;

namespace GitWolf.Store;

/// <summary>
/// Single source of truth for one open repository. Refreshes are coalesced and driven by the file
/// watcher, app activation, a periodic fetch and every mutating operation. Ported from
/// RepositoryStore.swift.
/// <para>
/// Not thread-safe: create and use it on the UI thread. Continuations return to that thread;
/// git processes and parsing run on the thread pool. The app calls <see cref="RequestRefresh"/>
/// when a window is activated.
/// </para>
/// </summary>
public sealed partial class RepositoryStore : INotifyPropertyChanged, IDisposable
{
    private readonly GitRepository _baseRepo;
    private readonly IStoreSettings _settings;
    private readonly Action<Uri> _openUri;
    private readonly CancellationTokenSource _lifetime = new();

    private int _commitLimit;
    private Task? _refreshTask;
    private bool _needsRefresh;
    private int _diffToken;
    private int _commitToken;
    private int? _selectionAnchor;
    private FileWatcher? _watcher;
    private int _pageSize;
    private bool _started;

    public RepositoryStore(GitRepository repo, IStoreSettings settings, Action<Uri> openUri)
    {
        _baseRepo = repo;
        _settings = settings;
        _openUri = openUri;
        _commitLimit = _pageSize = settings.CommitPageSize;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Re-reads history and the open diff after the page size, diff context or git path changed.</summary>
    public void ApplySettings()
    {
        if (_pageSize != _settings.CommitPageSize)
        {
            _commitLimit = _pageSize = _settings.CommitPageSize;
            Notify(nameof(CanLoadMore));
        }
        ReloadDiff();
        _ = RequestRefresh();
    }

    /// <summary>The repository with the user's current settings applied.</summary>
    public GitRepository Repo => _settings.Configure(_baseRepo);
    public string Id => _baseRepo.Root;
    public string Name => _baseRepo.Name;

    // MARK: Repository state

    public WorkingStatus Status { get; private set; } = WorkingStatus.Empty;
    public IReadOnlyList<Commit> Commits { get; private set; } = [];
    public IReadOnlyList<GraphRow> Graph { get; private set; } = [];
    public IReadOnlyList<Branch> LocalBranches { get; private set; } = [];
    public IReadOnlyList<Branch> RemoteBranches { get; private set; } = [];
    public IReadOnlyList<GitTag> Tags { get; private set; } = [];
    public IReadOnlyList<Stash> Stashes { get; private set; } = [];
    public IReadOnlyList<string> Remotes { get; private set; } = [];
    /// <summary>Where `origin` (or the first remote) lives, for web links.</summary>
    public RemoteLocation? RemoteLocation { get; private set; }
    public bool IsLoaded { get; private set; }
    /// <summary>A merge, rebase, cherry-pick or revert that is waiting for the user.</summary>
    public InProgressOperation? Operation { get; private set; }

    public int GraphLaneCount => Math.Min(10, Math.Max(1, Graph.Count == 0 ? 1 : Graph.Max(r => r.LaneCount)));
    public bool CanLoadMore => Commits.Count >= _commitLimit;

    // MARK: Activity

    public bool IsBusy { get; private set; }
    public string? BusyLabel { get; private set; }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            // The offer belongs to the message it came with.
            ReauthAccount = null;
            Set(ref _errorMessage, value);
        }
    }

    private ProviderAccount? _reauthAccount;
    /// <summary>Set after <see cref="ErrorMessage"/> when signing in again may fix a refused change.</summary>
    public ProviderAccount? ReauthAccount { get => _reauthAccount; private set => Set(ref _reauthAccount, value); }

    private PullBlock? _pullBlock;
    /// <summary>Set when a pull was refused to protect local work; the UI asks what to do, then clears it.</summary>
    public PullBlock? PullBlock { get => _pullBlock; set => Set(ref _pullBlock, value); }

    private PushOptions? _rejectedPush;
    /// <summary>The push the remote just refused; the UI offers a way forward, then clears it.</summary>
    public PushOptions? RejectedPush { get => _rejectedPush; set => Set(ref _rejectedPush, value); }

    private HookFailure? _hookFailure;
    /// <summary>Set when a hook refused a commit, until the UI has shown it.</summary>
    public HookFailure? HookFailure { get => _hookFailure; set => Set(ref _hookFailure, value); }

    private StoreSheet? _sheet;
    public StoreSheet? Sheet { get => _sheet; set => Set(ref _sheet, value); }

    private string _draftMessage = "";
    public string DraftMessage { get => _draftMessage; set => Set(ref _draftMessage, value); }

    private bool _amend;
    public bool Amend
    {
        get => _amend;
        set
        {
            Set(ref _amend, value);
            if (value && DraftMessage.Length == 0) _ = LoadHeadMessageAsync();
        }
    }

    private RepositoryRisk? _untrusted;
    /// <summary>Set while git is refused because of what the repository's configuration runs.</summary>
    public RepositoryRisk? Untrusted { get => _untrusted; private set => Set(ref _untrusted, value); }

    /// <summary>Allows exactly the settings the user was shown, then reads the repository.</summary>
    public Task TrustAsync(RepositoryRisk risk)
    {
        _settings.TrustRepository(_baseRepo.Root, risk.Fingerprint);
        Untrusted = null;
        ErrorMessage = null;
        _ = RefreshRemoteLocationAsync();
        return RequestRefresh();
    }

    // MARK: Lifecycle

    public void Start()
    {
        if (_started || _lifetime.IsCancellationRequested) return;
        _started = true;
        _ = RequestRefresh();
        _ = RefreshRemoteLocationAsync();

        _watcher = new FileWatcher(_baseRepo.Root);
        _ = WatchAsync(_watcher, _lifetime.Token);
        _ = AutoFetchAsync(_lifetime.Token);
        _ = PollHeadChecksAsync(_lifetime.Token);
    }

    public void Dispose()
    {
        if (_lifetime.IsCancellationRequested) return;
        _lifetime.Cancel();
        _watcher?.Dispose();
        _watcher = null;
    }

    private async Task WatchAsync(FileWatcher watcher, CancellationToken token)
    {
        try
        {
            await foreach (var change in watcher.Events.ReadAllAsync(token)) _ = RequestRefresh();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    /// <summary>Background fetch keeps ahead/behind counts honest; it never reports errors in the UI.</summary>
    private async Task AutoFetchAsync(CancellationToken token)
    {
        // Ticks every 30s so changes to the interval in Settings apply promptly.
        var tick = TimeSpan.FromSeconds(30);
        var elapsed = TimeSpan.Zero;
        using var timer = new PeriodicTimer(tick);
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                elapsed += tick;
                var minutes = _settings.AutoFetchMinutes;
                if (minutes <= 0 || elapsed < TimeSpan.FromMinutes(minutes)) continue;
                elapsed = TimeSpan.Zero;
                if (IsBusy) continue;
                try
                {
                    var repo = Repo;
                    await Task.Run(repo.FetchAsync, token);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    Trace.TraceWarning($"Background fetch failed: {e.GetType().Name}");
                }
                _ = RequestRefresh();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    // MARK: Provider web links

    private async Task RefreshRemoteLocationAsync()
    {
        var repo = Repo;
        var location = await Task.Run(repo.RemoteLocationAsync);
        if (location == RemoteLocation) return;
        RemoteLocation = location;
        // Another repository on the server: what was loaded no longer applies.
        AccountsChanged();
    }

    public WebLinks? WebLinks => RemoteLocation is { } location ? new WebLinks(ProviderKindOfRemote, location) : null;

    /// <summary>"GitHub", "GitLab"... or null when the remote isn't a recognised provider.</summary>
    public string? ProviderTitle => ProviderKindOfRemote?.Title();

    public void OpenOnWeb(WebTarget target)
    {
        if (WebLinks is not { } links) return;
        var branch = Status.Branch;
        var url = target switch
        {
            WebTarget.Branch when branch is not null => links.Branch(branch),
            WebTarget.PullRequest when branch is not null => links.NewPullRequest(branch),
            WebTarget.Commit commit => links.Commit(commit.Hash),
            _ => links.Repository,
        };
        if (url is not null) _openUri(url);
    }

    // MARK: Refresh

    /// <summary>Coalesced: requests made during a refresh cause exactly one more pass.</summary>
    public Task RequestRefresh()
    {
        if (_lifetime.IsCancellationRequested) return Task.CompletedTask;
        if (_refreshTask is { IsCompleted: false })
        {
            _needsRefresh = true;
            return _refreshTask;
        }
        return _refreshTask = RefreshLoopAsync();
    }

    private async Task RefreshLoopAsync()
    {
        // Yield first so _refreshTask is assigned before any state change can re-enter.
        await Task.Yield();
        do
        {
            _needsRefresh = false;
            await PerformRefreshAsync();
        } while (_needsRefresh && !_lifetime.IsCancellationRequested);
    }

    private async Task PerformRefreshAsync()
    {
        try
        {
            var repo = Repo;
            var limit = _commitLimit;
            var started = Stopwatch.GetTimestamp();
            var (snapshot, extras) = await Task.Run(async () =>
            {
                var read = await repo.SnapshotAsync(limit);
                return (read, await ReadExtrasAsync(repo, read.Status));
            });
            if (_lifetime.IsCancellationRequested) return;
            // Before the snapshot, so rows drawn for it already know which files use LFS.
            Untrusted = null;
            ApplyExtras(extras.Bisect, extras.Lfs, extras.Shallow);
            Apply(snapshot);
            if (snapshot.Commits.Count >= LargeHistory && Stopwatch.GetElapsedTime(started) > SlowRefresh)
                SpeedUpHistory(repo);
        }
        catch (UntrustedRepositoryException e)
        {
            // Compared by fingerprint, so one question per set of settings, not one per refresh.
            if (Untrusted?.Fingerprint != e.Risk.Fingerprint) Untrusted = e.Risk;
        }
        catch (Exception e)
        {
            Trace.TraceWarning($"Refresh failed: {e.GetType().Name}");
            if (!IsLoaded) ErrorMessage = e.Message;
        }
    }

    private static readonly TimeSpan SlowRefresh = TimeSpan.FromSeconds(0.5);
    /// <summary>Below this many commits the index gains nothing, however slow the disk was.</summary>
    private const int LargeHistory = 500;
    private bool _commitGraphTried;

    /// <summary>Once per session, in the background, for a repository that is slow to read.</summary>
    private void SpeedUpHistory(GitRepository repo)
    {
        if (_commitGraphTried) return;
        _commitGraphTried = true;
        _ = Task.Run(async () =>
        {
            try
            {
                if (!await repo.HasCommitGraphAsync()) await repo.WriteCommitGraphAsync();
            }
            catch (Exception e)
            {
                // Only an optimisation: the repository works without it.
                Trace.TraceWarning($"Writing the commit-graph failed: {e.GetType().Name}");
            }
        });
    }

    /// <summary>Only assigns what changed so the UI doesn't re-render (and flicker) needlessly.</summary>
    private void Apply(RepositorySnapshot s)
    {
        if (!Status.Equals(s.Status))
        {
            var previous = Status;
            Status = s.Status;
            Notify(nameof(Status));
            FollowChecks(previous);
            if (previous.Branch != Status.Branch) DismissPushOffer();
        }
        if (!Commits.SequenceEqual(s.Commits))
        {
            Commits = s.Commits;
            Graph = s.Graph;
            Notify(nameof(Commits));
            Notify(nameof(Graph));
            Notify(nameof(GraphLaneCount));
            Notify(nameof(CanLoadMore));
            Notify(nameof(SelectedCommit));
        }
        if (!LocalBranches.SequenceEqual(s.LocalBranches)) { LocalBranches = s.LocalBranches; Notify(nameof(LocalBranches)); }
        if (!RemoteBranches.SequenceEqual(s.RemoteBranches)) { RemoteBranches = s.RemoteBranches; Notify(nameof(RemoteBranches)); }
        if (!Tags.SequenceEqual(s.Tags)) { Tags = s.Tags; Notify(nameof(Tags)); }
        if (!Stashes.SequenceEqual(s.Stashes)) { Stashes = s.Stashes; Notify(nameof(Stashes)); }
        if (!Remotes.SequenceEqual(s.Remotes))
        {
            Remotes = s.Remotes;
            Notify(nameof(Remotes));
            _ = RefreshRemoteLocationAsync();
        }
        if (Operation != s.Operation)
        {
            Operation = s.Operation;
            Notify(nameof(Operation));
        }
        if (!IsLoaded)
        {
            IsLoaded = true;
            Notify(nameof(IsLoaded));
            if (!s.Status.IsClean) Mode = StoreMode.Changes;
        }
        ReconcileSelection();
    }

    public void LoadMoreCommits()
    {
        if (!CanLoadMore) return;
        _commitLimit += _settings.CommitPageSize;
        Notify(nameof(CanLoadMore));
        _ = RequestRefresh();
    }

    // MARK: Change notification

    private void Notify(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    private void Set<T>(ref T field, T value, [CallerMemberName] string property = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Notify(property);
    }
}
