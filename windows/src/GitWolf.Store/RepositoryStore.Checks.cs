using System.Diagnostics;
using GitWolf.Core;

namespace GitWolf.Store;

// CI results by commit, and re-running or cancelling a check. Ported from RepositoryStore.swift.
public sealed partial class RepositoryStore
{
    private static readonly TimeSpan ChecksFreshFor = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan WatchPeriod = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RecentCommit = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan HeadPollInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan RefusedPause = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(3);
    private const int MaxCachedChecks = 200;

    private readonly Dictionary<string, CiStatus> _checks = [];
    private readonly Dictionary<string, DateTimeOffset> _checksRequested = [];
    private readonly Dictionary<string, DateTimeOffset> _checksWatched = [];
    private readonly HashSet<string> _checksLoading = [];
    private readonly HashSet<(string Sha, string Name)> _ciBusy = [];
    private DateTimeOffset _checksPausedUntil;
    /// <summary>Changes with the server or the account, so a late answer from the old one is dropped.</summary>
    private int _checksGeneration;

    /// <summary>CI results for a commit, if the server has any.</summary>
    public CiStatus? Checks(string sha) => _checks.GetValueOrDefault(sha);

    /// <summary>The result of the checked-out commit, for the repository's tile. Needs a connected account.</summary>
    public CheckState? HeadChecks =>
        HasProviderAccount && Status.HeadOid is { } head ? Checks(head)?.Overall : null;

    /// <summary>The checked-out commit's run went from running to passed or failed.</summary>
    public event Action<CheckState>? HeadChecksFinished;

    public bool CanPerformCi(CiAction action, CiCheck check) => Account?.Kind.CanPerform(action, check) == true;

    public bool IsCiBusy(string sha, CiCheck check) => _ciBusy.Contains((sha, check.Name));

    /// <summary>
    /// Whether to keep looking this commit up: a check is running, it was just pushed, or it is new and
    /// has no result yet. Only with an account, as anonymous requests are limited to a few dozen an hour.
    /// </summary>
    public bool ShouldPollChecks(string sha, DateTimeOffset? committed = null)
    {
        var now = DateTimeOffset.UtcNow;
        if (_lifetime.IsCancellationRequested || !HasProviderAccount || now < _checksPausedUntil) return false;
        if (Checks(sha) is { } status && status.Checks.Any(c => c.State == CheckState.Pending)) return true;
        if (_checksWatched.TryGetValue(sha, out var until) && until > now) return true;
        // It may have been pushed from a terminal. A commit that is still ahead can't have a run.
        return !_checks.ContainsKey(sha) && committed is { } time && now - time < RecentCommit
            && !(sha == Status.HeadOid && Status.Ahead > 0);
    }

    public Task PollChecksAsync(string sha, DateTimeOffset? committed = null) =>
        ShouldPollChecks(sha, committed) ? LoadChecksAsync(sha, force: true) : Task.CompletedTask;

    /// <summary>
    /// Raises "Checks" when the results change. A result is reused for a minute unless
    /// <paramref name="force"/> is set.
    /// </summary>
    public async Task LoadChecksAsync(string sha, bool force = false)
    {
        if (sha.Length == 0 || ProviderKindOfRemote is not { } kind || !kind.SupportsChecks()) return;
        if (!force && _checksRequested.TryGetValue(sha, out var at)
            && DateTimeOffset.UtcNow - at < ChecksFreshFor) return;
        var generation = _checksGeneration;
        if (!_checksLoading.Add(sha)) return;
        try
        {
            TrimChecks();
            _checksRequested[sha] = DateTimeOffset.UtcNow;
            if (await ConnectAsync() is not { } connection) return;
            var (client, path) = (connection.Client, connection.Path);
            var status = await Task.Run(() => client.CiStatusAsync(path, sha));
            if (_lifetime.IsCancellationRequested || generation != _checksGeneration || status.IsEmpty
                || status.Equals(Checks(sha))) return;
            var before = Checks(sha)?.Overall;
            _checks[sha] = status;
            Notify(nameof(Checks));
            if (sha == Status.HeadOid && before == CheckState.Pending
                && status.Overall is { } after && after is CheckState.Success or CheckState.Failure)
                HeadChecksFinished?.Invoke(after);
        }
        catch (ProviderException e)
        {
            // Checks are extra information: a commit the server doesn't know simply shows none.
            Trace.TraceInformation($"No CI status (HTTP {e.Status}).");
            // Refused or rate limited: asking again soon would only make it worse.
            if (e.Status is 401 or 403 or 429) _checksPausedUntil = DateTimeOffset.UtcNow + RefusedPause;
        }
        finally
        {
            if (generation == _checksGeneration) _checksLoading.Remove(sha);
        }
    }

    /// <summary>Looks a commit up now, and keeps looking for a few minutes.</summary>
    public void WatchChecks(string? sha)
    {
        if (sha is not { Length: > 0 }) return;
        _checksWatched[sha] = DateTimeOffset.UtcNow + WatchPeriod;
        _ = LoadChecksAsync(sha, force: true);
    }

    /// <summary>Re-runs a finished check or cancels a running one, then watches the commit.</summary>
    public async Task PerformCiAsync(CiAction action, string sha, CiCheck check)
    {
        var key = (sha, check.Name);
        if (!_ciBusy.Add(key)) return;
        Notify(nameof(Checks));
        ProviderAccount? account = null;
        try
        {
            if (await ConnectAsync() is not { SignedIn: true } connection)
                throw ProviderException.Unsupported("Add an account for this repository’s host in Settings → Accounts.");
            var (client, path) = (connection.Client, connection.Path);
            account = client.Account;
            await Task.Run(() => client.PerformAsync(action, check, path));
            if (_lifetime.IsCancellationRequested) return;
            // Shown as running right away; the lookup a moment later confirms it.
            if (action == CiAction.Rerun && Checks(sha) is { } status)
                _checks[sha] = new CiStatus(status.Checks
                    .Select(c => c.Name == check.Name ? c with { State = CheckState.Pending } : c).ToList());
            _checksWatched[sha] = DateTimeOffset.UtcNow + WatchPeriod;
            Notify(nameof(Checks));
            await Task.Delay(SettleDelay, _lifetime.Token);
            await LoadChecksAsync(sha, force: true);
        }
        catch (ProviderException e)
        {
            ErrorMessage = e.Message;
            if (e.IsForbidden && account is { UsesOAuth: true }) ReauthAccount = account;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The repository was closed.
        }
        finally
        {
            _ciBusy.Remove(key);
            Notify(nameof(Checks));
        }
    }

    /// <summary>A new commit is looked up for the tile, and a branch just fully pushed is watched.</summary>
    private void FollowChecks(WorkingStatus previous)
    {
        if (previous.Ahead > 0 && Status.Ahead == 0 && previous.Branch == Status.Branch) WatchChecks(Status.HeadOid);
        else if (previous.HeadOid != Status.HeadOid) LoadHeadChecks();
    }

    private void LoadHeadChecks()
    {
        if (HasProviderAccount && Status.HeadOid is { } head) _ = LoadChecksAsync(head);
    }

    /// <summary>Keeps the tile current while the checked-out commit's run is going.</summary>
    private async Task PollHeadChecksAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(HeadPollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(token))
                if (Status.HeadOid is { } head) await PollChecksAsync(head);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private void TrimChecks()
    {
        if (_checksRequested.Count < MaxCachedChecks) return;
        // The checked-out commit's result stays, as its tile shows it.
        var head = Status.HeadOid;
        foreach (var sha in _checksRequested.Keys.Where(k => k != head).ToList())
        {
            _checksRequested.Remove(sha);
            _checks.Remove(sha);
        }
    }

    private void ResetChecks()
    {
        _checksGeneration++;
        _checks.Clear();
        _checksRequested.Clear();
        _checksWatched.Clear();
        _checksLoading.Clear();
        _checksPausedUntil = default;
        Notify(nameof(Checks));
    }
}
