using System.Net;
using System.Text;
using GitWolf.Core;
using GitWolf.Store;
using Xunit;
using static GitWolfCore.Tests.RepositoryStoreTests;

namespace GitWolfCore.Tests;

public sealed class PullRequestOverviewTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("gitwolf-overview-").FullName;

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_dir, recursive: true);
    }

    private sealed class Settings : IStoreSettings
    {
        public int CommitPageSize => 100;
        public int AutoFetchMinutes => 0;
        public GitRepository Configure(GitRepository repo) => repo;
        public ProviderAccount? AccountForHost(string host) => new(ProviderKind.GitHub, host, "Me");
        public Task<string?> SecretAsync(ProviderAccount account) => Task.FromResult<string?>("test-token");

        public HttpTransport? Transport => request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            var body = url.Contains("/pulls")
                ? """
                  [{"number":1,"title":"Mine","state":"open","user":{"login":"me"},"head":{"sha":"abc","ref":"a"},"base":{"ref":"main"}},
                   {"number":2,"title":"Theirs","state":"open","draft":true,"user":{"login":"other"},"head":{"sha":"def","ref":"b"},"base":{"ref":"main"}}]
                  """
                : url.Contains("check-runs")
                    ? """{"check_runs":[{"name":"build","status":"completed","conclusion":"failure"}]}"""
                    : """{"statuses":[]}""";
            return Task.FromResult((Encoding.UTF8.GetBytes(body), HttpStatusCode.OK));
        };
    }

    [Fact]
    public void ListsPullRequestsOnceAndMarksTheUsersOwn() => SingleThread.Run(async () =>
    {
        var settings = new Settings();
        var stores = new List<RepositoryStore>();
        // Two checkouts of the same server repository.
        foreach (var name in new[] { "one", "two" })
        {
            var folder = Directory.CreateDirectory(Path.Combine(_dir, name)).FullName;
            var git = new GitRunner(folder);
            await git.RunAsync(["init", "-b", "main"]);
            await git.RunAsync(["remote", "add", "origin", "https://github.com/o/r.git"]);
            var store = new RepositoryStore(new GitRepository(folder), settings, _ => { });
            await store.RequestRefresh();
            for (var i = 0; i < 200 && store.RemoteLocation is null; i++) await Task.Delay(50);
            stores.Add(store);
        }

        var overview = new PullRequestOverview();
        await overview.LoadAsync(stores);

        Assert.Equal([1, 2], overview.Entries.Select(e => e.Item.Number).Order());
        var mine = Assert.Single(overview.Entries, e => e.Mine);
        Assert.Equal(CheckState.Failure, mine.Checks);
        // A draft's checks aren't looked up.
        Assert.Null(overview.Entries.Single(e => !e.Mine).Checks);
        Assert.False(overview.IsLoading);
        Assert.False(overview.SignedOut);
        Assert.Empty(overview.Skipped);
        foreach (var store in stores) store.Dispose();
    });
}
