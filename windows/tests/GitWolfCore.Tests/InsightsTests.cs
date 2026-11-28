using GitWolf.Core;
using Xunit;

namespace GitWolfCore.Tests;

public sealed class InsightsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("gitwolf-insights-").FullName;

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_dir, recursive: true);
    }

    private static string Record(string name, string email, long time, params string[] stats) =>
        $"\u001e{name}\u001f{email}\u001f{time}\n\n{string.Join('\n', stats)}\n";

    [Fact]
    public void ParsesContributorsFilesWeeksAndOwnership()
    {
        const long day = 86_400;
        // 2024-01-01 is a Monday (UTC).
        const long monday = 1_704_067_200;
        var result = RepositoryInsights.Parse(
            Record("Ann", "ann@x.io", monday, "10\t2\tsrc/a.cs", "5\t0\tREADME.md")
            + Record("Bob", "BOB@x.io", monday + 2 * day, "3\t1\tsrc/a.cs", "-\t-\tlogo.png")
            + Record("Ann", "ann@x.io", monday + 8 * day, "1\t1\tsrc/b.cs"));

        Assert.Equal((3, 19L, 4L), (result.Commits, result.Added, result.Deleted));
        Assert.Equal(new AuthorStat("Ann", "ann@x.io", 2, 16, 3), result.Contributors[0]);
        Assert.Equal("bob@x.io", result.Contributors[1].Email);
        Assert.Equal(("src/a.cs", 2), (result.HotFiles[0].Path, result.HotFiles[0].Commits));
        Assert.Equal(("src/a.cs", 16L), (result.ChurnFiles[0].Path, result.ChurnFiles[0].Churn));
        Assert.Equal([2, 1], result.Weeks.Select(w => w.Commits));
        Assert.Equal(new DateOnly(2024, 1, 1), result.Weeks[0].Start);
        var src = result.Ownership.Single(o => o.Folder == "src");
        Assert.Equal(("Ann", 18L), (src.Owner, src.Lines));
        Assert.Contains(result.Ownership, o => o.Folder == "(root)");
    }

    [Fact]
    public async Task ReadsARealRepositoryAndSaysWhenHistoryIsCut()
    {
        var git = new GitRunner(_dir);
        await git.RunAsync(["init", "-b", "main"]);
        await git.RunAsync(["config", "user.name", "T"]);
        await git.RunAsync(["config", "user.email", "t@example.invalid"]);
        await git.RunAsync(["config", "core.autocrlf", "false"]);
        foreach (var (file, text) in new[] { ("a.txt", "1\n2\n"), ("b.txt", "x\n"), ("a.txt", "1\n2\n3\n") })
        {
            await File.WriteAllTextAsync(Path.Combine(_dir, file), text);
            await git.RunAsync(["add", "-A"]);
            await git.RunAsync(["commit", "-m", "change"]);
        }
        var repo = new GitRepository(_dir);

        var all = await repo.InsightsAsync();
        Assert.Equal((3, 4L, false), (all.Commits, all.Added, all.Truncated));
        Assert.Equal(3, Assert.Single(all.Contributors).Commits);
        Assert.Equal("a.txt", all.HotFiles[0].Path);

        var limited = await repo.InsightsAsync(limit: 2);
        Assert.Equal((2, true), (limited.Commits, limited.Truncated));
    }
}
