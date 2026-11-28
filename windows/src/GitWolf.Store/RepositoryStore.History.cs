using System.Diagnostics;
using GitWolf.Core;

namespace GitWolf.Store;

// Searching history, and moving the current branch.
public sealed partial class RepositoryStore
{
    private CancellationTokenSource? _search;

    /// <summary>What git found for the current search; null while nothing is being searched.</summary>
    public IReadOnlyList<Commit>? SearchResults { get; private set; }

    public async Task SearchAsync(HistoryQuery? query)
    {
        // A newer search replaces an older one, which is stopped.
        _search?.Cancel();
        if (query is null || query.IsEmpty)
        {
            _search = null;
            SetSearchResults(null);
            return;
        }
        var search = _search = new CancellationTokenSource();
        var (repo, remotes) = (Repo, new HashSet<string>(Remotes));
        try
        {
            var found = await Task.Run(() => repo.SearchAsync(query, remotes, cancellationToken: search.Token));
            if (!search.IsCancellationRequested) SetSearchResults(found);
        }
        catch (OperationCanceledException)
        {
            // Replaced by a newer search.
        }
        catch (Exception e)
        {
            if (search.IsCancellationRequested) return;
            SetSearchResults([]);
            ErrorMessage = e.Message;
        }
    }

    private void SetSearchResults(IReadOnlyList<Commit>? results)
    {
        if (ReferenceEquals(SearchResults, results)) return;
        SearchResults = results;
        Notify(nameof(SearchResults));
    }

    /// <summary>Runs a read for a dialog; null (with the reason shown) when it fails.</summary>
    public async Task<T?> ReadAsync<T>(Func<GitRepository, Task<T>> read) where T : class
    {
        var repo = Repo;
        try
        {
            return await Task.Run(() => read(repo));
        }
        catch (Exception e) when (e is GitException or IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Reading history failed: {e.GetType().Name}");
            ErrorMessage = e.Message;
            return null;
        }
    }

    /// <summary>Destructive when hard: the UI must confirm first.</summary>
    public Task<bool> ResetAsync(string target, ResetMode mode) =>
        PerformAsync("Resetting…", r => r.ResetAsync(target, mode));

    public Task<bool> UndoAsync(UndoPlan plan) => PerformAsync("Undoing…", r => r.UndoAsync(plan));
}
