using System.Net;
using GitWolf.Core;
using Xunit;

namespace GitWolfCore.Tests;

public sealed class ContributorsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("gitwolf-people-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class Reply(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            Assert.False(request.Headers.Contains("Authorization"));
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public void ParsesSortsAndDropsBots()
    {
        var people = Contributors.Parse("""
            [{"login":"rage","type":"User","contributions":9,"avatar_url":"https://avatars.githubusercontent.com/u/1"},
             {"login":"dependabot[bot]","type":"Bot","contributions":50},
             {"login":"ipuppyyt","type":"User","contributions":300,"avatar_url":"https://evil.example/a.png"}]
            """);

        Assert.Equal(["ipuppyyt", "rage"], people.Select(p => p.Login));
        Assert.Equal(9, people[1].Contributions);
        Assert.Equal("https://github.com/rage", people[1].Profile.AbsoluteUri);
        Assert.NotNull(people[1].Avatar);
        Assert.Null(people[0].Avatar);
    }

    [Fact]
    public void TheMaintainerIsAlwaysListedAndOddLoginsAreRefused()
    {
        var people = Contributors.Parse(
            """[{"login":"rage","contributions":4},{"login":"../x"},{"login":7},3]""");

        Assert.Equal(["rage", "ipuppyyt"], people.Select(p => p.Login));
        Assert.True(people[1].IsMaintainer);
        Assert.Equal(["ipuppyyt"], Contributors.Parse("not json").Select(p => p.Login));
        Assert.Equal(["ipuppyyt"], Contributors.Parse("{}").Select(p => p.Login));
    }

    [Fact]
    public async Task KeepsTheReplyForADayAndIgnoresFailures()
    {
        var cache = Path.Combine(_dir, "contributors.json");
        var failing = new ContributorList(cache, new Reply(HttpStatusCode.Forbidden, "[]"));
        Assert.Null(await failing.RefreshAsync());
        Assert.False(File.Exists(cache));

        var reply = new Reply(HttpStatusCode.OK, """[{"login":"rage","contributions":4}]""");
        var list = new ContributorList(cache, reply);
        Assert.Equal(2, (await list.RefreshAsync())!.Count);
        Assert.Null(await list.RefreshAsync());
        Assert.Equal(1, reply.Calls);
        Assert.Equal("rage", list.Cached()[0].Login);
    }
}
