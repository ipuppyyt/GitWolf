using GitWolf.Core;
using Xunit;

namespace GitWolfCore.Tests;

public sealed class HistoryToolTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"gitwolf-history-{Guid.NewGuid()}");
    private GitRunner Git => new(_dir);
    private GitRepository Repo => new(_dir);
    private static readonly HashSet<string> NoRemotes = [];

    public HistoryToolTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left in the temporary folder.
        }
    }

    private async Task CommitAsync(string message, string file, string text)
    {
        File.WriteAllText(Path.Combine(_dir, file), text);
        await Git.RunAsync(["add", "-A"]);
        await Git.RunAsync(["commit", "-m", message]);
    }

    /// <summary>"first" creates a.txt, "second" changes it, "third" renames it to b.txt.</summary>
    private async Task InitAsync()
    {
        await Git.RunAsync(["init", "-b", "main"]);
        await Git.RunAsync(["config", "user.name", "Tester"]);
        await Git.RunAsync(["config", "user.email", "t@example.com"]);
        await Git.RunAsync(["config", "commit.gpgsign", "false"]);
        await Git.RunAsync(["config", "core.autocrlf", "false"]);
        await CommitAsync("first", "a.txt", "one\ntwo\nthree\n");
        await CommitAsync("second", "a.txt", "one\nNEEDLE\nthree\n");
        await Git.RunAsync(["mv", "a.txt", "b.txt"]);
        await Git.RunAsync(["commit", "-m", "third"]);
    }

    private async Task<List<string>> SubjectsAsync(HistoryQuery query) =>
        (await Repo.SearchAsync(query, NoRemotes)).Select(c => c.Subject).ToList();

    [Fact]
    public async Task HistoryIsSearchedByMessageAuthorContentAndFile()
    {
        await InitAsync();

        Assert.Equal(["second"], await SubjectsAsync(new(Text: "SECOND")));
        Assert.Equal(3, (await SubjectsAsync(new(HistorySearchKind.Author, "tester"))).Count);
        Assert.Equal(["second"], await SubjectsAsync(new(HistorySearchKind.ContentChanged, "NEEDLE")));
        Assert.Equal(["second"], await SubjectsAsync(new(HistorySearchKind.ContentRegex, "NEED.E")));
        Assert.Equal(["third"], await SubjectsAsync(new(Path: "b.txt")));
        Assert.Empty(await SubjectsAsync(new(Text: "--all-match")));
        Assert.Empty(await SubjectsAsync(new(Until: new DateOnly(2001, 1, 1))));
    }

    [Fact]
    public async Task FileHistoryAndDiffsFollowARename()
    {
        await InitAsync();

        var history = await Repo.FileHistoryAsync("b.txt", NoRemotes);
        Assert.Equal(["third", "second", "first"], history.Select(c => c.Subject));
        var diff = await Repo.FileDiffAsync("b.txt", history[1].Hash);
        Assert.Contains(diff.Lines, l => l.Kind == DiffLineKind.Addition && l.Text.Contains("NEEDLE"));
    }

    [Fact]
    public async Task BlameNamesTheCommitOfEachLine()
    {
        await InitAsync();
        File.AppendAllText(Path.Combine(_dir, "b.txt"), "four\n");

        var lines = await Repo.BlameAsync("b.txt");

        Assert.Equal(["first", "second", "first"], lines.Take(3).Select(l => l.Summary));
        Assert.Equal("NEEDLE", lines[1].Text);
        Assert.True(lines[3].IsUncommitted);
    }

    [Fact]
    public async Task UndoReturnsToThePreviousHeadAndKeepsLocalWork()
    {
        await InitAsync();
        File.WriteAllText(Path.Combine(_dir, "new.txt"), "local");
        var log = await Repo.ReflogAsync();

        await Assert.ThrowsAsync<GitException>(() => Repo.UndoAsync(new UndoPlan.Reset(log[2].Hash, "x") { ExpectedHead = log[1].Hash }));
        await Repo.UndoAsync(new UndoPlan.Reset(log[1].Hash, "x") { ExpectedHead = log[0].Hash });

        Assert.True(File.Exists(Path.Combine(_dir, "a.txt")));
        Assert.True(File.Exists(Path.Combine(_dir, "new.txt")));
        Assert.Equal(log[1].Hash, (await Repo.ReflogAsync())[0].Hash);
    }

    [Fact]
    public async Task ResetSaysWhatItWouldDropAndHardResetDropsIt()
    {
        await InitAsync();
        var first = (await Repo.FileHistoryAsync("b.txt", NoRemotes))[^1].Hash;
        File.AppendAllText(Path.Combine(_dir, "b.txt"), "local\n");

        Assert.Equal(new ResetImpact(2, 1), await Repo.ResetImpactAsync(first));
        await Repo.ResetAsync(first, ResetMode.Hard);
        Assert.True((await Repo.StatusAsync()).IsClean);
    }

    [Fact]
    public async Task UnsignedCommitsHaveNoSignature()
    {
        await InitAsync();

        Assert.Null(await Repo.SignatureAsync("HEAD"));
    }

    [Fact]
    public async Task TheRepositoryCannotChooseTheProgramThatChecksASignature()
    {
        await InitAsync();
        var marker = Path.Combine(_dir, "ran").Replace('\\', '/');
        var script = Path.Combine(_dir, "verify.sh").Replace('\\', '/');
        File.WriteAllText(script, $"#!/bin/sh\ntouch '{marker}'\n");
        await Git.RunAsync(["config", "gpg.program", script]);
        // A commit object that carries a (made-up) signature.
        var raw = await Git.RunStringAsync(["cat-file", "commit", "HEAD"]);
        var split = raw.IndexOf("\n\n", StringComparison.Ordinal);
        var signed = raw[..split]
            + "\ngpgsig -----BEGIN PGP SIGNATURE-----\n \n abc\n -----END PGP SIGNATURE-----" + raw[split..];
        var file = Path.Combine(_dir, "commit.raw");
        File.WriteAllText(file, signed);
        var hash = (await Git.RunStringAsync(["hash-object", "-t", "commit", "-w", file])).Trim();

        var signature = await Repo.SignatureAsync(hash);

        Assert.Equal(SignatureState.Unverifiable, signature?.State);
        Assert.False(File.Exists(marker));
    }
}
