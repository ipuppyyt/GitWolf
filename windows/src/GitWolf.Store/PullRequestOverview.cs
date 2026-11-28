using System.Diagnostics;
using GitWolf.Core;

namespace GitWolf.Store;

/// <summary>An open pull request in one of the open repositories.</summary>
public sealed record OverviewEntry(RepositoryStore Repository, CollabItem Item, bool Mine, CheckState? Checks = null);

/// <summary>
/// Open pull requests across every open repository. Ported from PullRequestOverview.swift. Start it on the
/// UI thread; <see cref="Changed"/> is raised there.
/// </summary>
public sealed class PullRequestOverview
{
    /// <summary>CI results are looked up for this many, one request each.</summary>
    private const int MaxChecked = 30;
    private const int Parallel = 4;

    private readonly List<OverviewEntry> _entries = [];
    private readonly List<string> _skipped = [];

    public IReadOnlyList<OverviewEntry> Entries => _entries;
    /// <summary>Repositories whose list couldn't be loaded.</summary>
    public IReadOnlyList<string> Skipped => _skipped;
    /// <summary>Some repository has no account, so which pull requests are the user's isn't known.</summary>
    public bool SignedOut { get; private set; }
    public bool IsLoading { get; private set; }
    public event Action? Changed;

    public async Task LoadAsync(IReadOnlyList<RepositoryStore> repositories, CancellationToken token = default)
    {
        if (IsLoading) return;
        IsLoading = true;
        _entries.Clear();
        _skipped.Clear();
        SignedOut = false;
        Changed?.Invoke();
        try
        {
            using var gate = new SemaphoreSlim(Parallel);
            // Two checkouts of one server repository are listed once.
            var unique = repositories.Where(r => r.Supports(CollabKind.PullRequest))
                .DistinctBy(r => $"{r.RemoteLocation?.Host}/{r.RemoteLocation?.Path}".ToLowerInvariant())
                .ToList();
            var clients = new Dictionary<RepositoryStore, ProviderConnection>();
            await Task.WhenAll(unique.Select(async repository =>
            {
                await gate.WaitAsync(token);
                try
                {
                    if (await repository.ConnectAsync() is not { } connection)
                    {
                        _skipped.Add(repository.Name);
                        return;
                    }
                    SignedOut |= !connection.SignedIn;
                    var items = await Task.Run(() => connection.Client.CollabAsync(
                        CollabKind.PullRequest, connection.Path, CollabFilter.Open, 1), token);
                    var me = connection.SignedIn ? connection.Client.Account.Username : "";
                    clients[repository] = connection;
                    _entries.AddRange(items.Select(item => new OverviewEntry(repository, item,
                        me.Length > 0 && item.Author.Equals(me, StringComparison.OrdinalIgnoreCase))));
                }
                catch (ProviderException e)
                {
                    Trace.TraceInformation($"A pull request list wasn't loaded (HTTP {e.Status}).");
                    _skipped.Add(repository.Name);
                }
                finally
                {
                    gate.Release();
                }
                Changed?.Invoke();
            }));
            _entries.Sort((a, b) => Nullable.Compare(b.Item.Updated, a.Item.Updated));
            Changed?.Invoke();
            // Only with a sign-in: anonymous requests are limited to a few dozen an hour.
            await Task.WhenAll(_entries
                .Where(e => e.Item.HeadSha.Length > 0 && !e.Item.IsDraft && clients[e.Repository].SignedIn)
                .Take(MaxChecked).ToList().Select(async entry =>
                {
                    await gate.WaitAsync(token);
                    try
                    {
                        var connection = clients[entry.Repository];
                        if (!connection.Client.Account.Kind.SupportsChecks()) return;
                        var status = await Task.Run(
                            () => connection.Client.CiStatusAsync(connection.Path, entry.Item.HeadSha), token);
                        var index = _entries.IndexOf(entry);
                        if (index < 0 || status.Overall is not { } overall) return;
                        _entries[index] = entry with { Checks = overall };
                        Changed?.Invoke();
                    }
                    catch (ProviderException e)
                    {
                        Trace.TraceInformation($"A CI result wasn't loaded (HTTP {e.Status}).");
                    }
                    finally
                    {
                        gate.Release();
                    }
                }));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The window showing the list was closed.
        }
        finally
        {
            IsLoading = false;
            Changed?.Invoke();
        }
    }
}
