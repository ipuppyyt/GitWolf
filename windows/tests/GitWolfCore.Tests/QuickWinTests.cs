using System.Net;
using GitWolf.Core;
using Xunit;

namespace GitWolfCore.Tests;

public class QuickWinTests
{
    [Fact]
    public void BreakingChangeIsAddedAndRemoved()
    {
        var marked = CommitMessage.SetBreaking(true, "feat(api): drop v1\n\nBody\n");
        Assert.Equal("feat(api)!: drop v1\n\nBody\n\nBREAKING CHANGE: ", marked);
        Assert.True(CommitMessage.IsBreaking(marked));
        Assert.Equal(marked, CommitMessage.SetBreaking(true, marked));
        Assert.Equal("feat(api): drop v1\n\nBody", CommitMessage.SetBreaking(false, marked));
        Assert.False(CommitMessage.IsBreaking("Fix it! now"));
        Assert.Equal("Plain\n\nBREAKING CHANGE: ", CommitMessage.SetBreaking(true, "Plain"));
        Assert.Equal("Wow! done", CommitMessage.SetBreaking(false, "Wow! done"));
    }

    [Fact]
    public void ChangelogGroupsSubjectsByType()
    {
        var notes = CommitMessage.Changelog(
            ["fix: crash", "feat(ui)!: new rail", "tidy up", "wip: thing", "feat: search"]);
        Assert.Equal(
            "### Breaking changes\n- **ui:** new rail\n\n### Features\n- **ui:** new rail\n- search\n\n"
                + "### Fixes\n- crash\n\n### Other\n- tidy up\n- wip: thing", notes);
    }

    private static ReflogEntry Entry(int index, char hash, string message) =>
        new(index, new string(hash, 40), message, DateTimeOffset.UnixEpoch);

    [Fact]
    public void UndoPlanCoversCommitCheckoutAndRebase()
    {
        Assert.Null(UndoPlan.For([Entry(0, 'a', "commit (initial): one")]));

        var commit = Assert.IsType<UndoPlan.Reset>(UndoPlan.For(
            [Entry(0, 'b', "commit: two"), Entry(1, 'a', "commit (initial): one")]));
        Assert.Equal((new string('a', 40), "commit: two"), (commit.Target, commit.Label));

        var branch = Assert.IsType<UndoPlan.SwitchBack>(UndoPlan.For(
            [Entry(0, 'b', "checkout: moving from main to topic"), Entry(1, 'a', "commit: one")]));
        Assert.Equal(("main", false, "topic"), (branch.To, branch.Detached, branch.From));

        var detached = Assert.IsType<UndoPlan.SwitchBack>(UndoPlan.For(
            [Entry(0, 'b', $"checkout: moving from {new string('c', 40)} to main"), Entry(1, 'c', "commit: one")]));
        Assert.True(detached.Detached);

        var rebase = Assert.IsType<UndoPlan.Reset>(UndoPlan.For(
        [
            Entry(0, 'd', "rebase (finish): returning to refs/heads/topic"), Entry(1, 'd', "rebase (pick): two"),
            Entry(2, 'c', "rebase (start): checkout main"), Entry(3, 'b', "commit: two"),
        ]));
        Assert.Equal((new string('b', 40), "rebase", new string('d', 40)),
            (rebase.Target, rebase.Label, rebase.ExpectedHead));
    }

    [Theory]
    [InlineData(" https://example.com/a/b.git\n", "https://example.com/a/b.git")]
    [InlineData("https://github.com/a/b", "https://github.com/a/b")]
    [InlineData("git@host:a/b.git", "git@host:a/b.git")]
    [InlineData("ssh://git@host/a/b.git", "ssh://git@host/a/b.git")]
    [InlineData("https://user:token@github.com/a/b.git", null)]
    [InlineData("https://token@github.com/a/b.git", null)]
    [InlineData("ssh://git:pw@host/a/b.git", null)]
    [InlineData("https://example.com/page", null)]
    [InlineData("https://evil.test/github.com/a", null)]
    [InlineData("C:\\repos\\a.git", null)]
    [InlineData("\\\\server\\share\\a.git", null)]
    [InlineData("file:///c:/a.git", null)]
    [InlineData("ext::sh -c x.git", null)]
    [InlineData("two\nhttps://github.com/a/b", null)]
    [InlineData("", null)]
    public void OnlyRemoteAddressesWithoutSecretsAreOffered(string copied, string? offered) =>
        Assert.Equal(offered, CloneAddress.FromClipboard(copied));

    [Fact]
    public void TheFileFilterIgnoresCase()
    {
        FileChange[] files = [new("src/App.cs", null, ChangeKind.Modified), new("README.md", null, ChangeKind.Modified)];
        Assert.Equal("src/App.cs", Assert.Single(FileFilter.Apply(files, "app")).Path);
        Assert.Equal(2, FileFilter.Apply(files, "").Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task ARefusedWriteIsForbiddenAndSaysWhy(HttpStatusCode status)
    {
        var client = new ProviderClient(new ProviderAccount(ProviderKind.GitHub, "github.com", "me"), "t",
            _ => Task.FromResult(("{\"message\":\"Resource not accessible.\"}"u8.ToArray(), status)));
        var error = await Assert.ThrowsAsync<ProviderException>(
            () => client.AddCommentAsync(CollabKind.Issue, "a/b", 1, "hi"));
        Assert.True(error.IsForbidden);
        Assert.Equal("Resource not accessible. This account may not be allowed to do that.", error.Message);
        // A refused read stays an ordinary sign-in failure.
        var read = await Assert.ThrowsAsync<ProviderException>(client.CurrentUserAsync);
        Assert.False(read.IsForbidden);
    }
}

public class QuickWinRepositoryTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gitwolf-quick-" + Guid.NewGuid().ToString("N"));
    private GitRepository _repo = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _repo = new GitRepository(await GitRepository.CreateAsync(_root, "repo", "main"));
        await Git("config", "user.name", "Test");
        await Git("config", "user.email", "test@example.com");
        await Git("config", "commit.gpgsign", "false");
        await Commit("feat: one");
    }

    public Task DisposeAsync()
    {
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
        return Task.CompletedTask;
    }

    private Task Git(params string[] arguments) => new GitRunner(_repo.Root).RunAsync(arguments);

    private Task Commit(string subject) => Git("commit", "--allow-empty", "-m", subject);

    [Fact]
    public async Task UndoSwitchesBackToTheBranch()
    {
        await Git("switch", "-c", "topic");
        var plan = Assert.IsType<UndoPlan.SwitchBack>(UndoPlan.For(await _repo.ReflogAsync()));
        await _repo.UndoAsync(plan);
        Assert.Equal("main", (await _repo.StatusAsync()).Branch);
    }

    [Fact]
    public async Task SubjectsSinceATagAndTheCommitGraph()
    {
        await Git("tag", "v1");
        await Commit("fix: two");
        await Commit("docs: three");
        Assert.Equal(["docs: three", "fix: two"], await _repo.CommitSubjectsAsync("v1"));
        Assert.False(await _repo.HasCommitGraphAsync());
        await _repo.WriteCommitGraphAsync();
        Assert.True(await _repo.HasCommitGraphAsync());
    }
}
