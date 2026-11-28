using GitWolf.Core;
using Xunit;

namespace GitWolfCore.Tests;

public sealed class RepositoryScannerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("gitwolf-scan-").FullName;

    public RepositoryScannerTests()
    {
        foreach (var path in new[]
        {
            "a/.git", "b/inner/.git", "c/.git", "c/nested/.git", "plain/notes", "node_modules/x/.git",
            ".hidden/.git", "d/e/f/.git",
        })
            Directory.CreateDirectory(Path.Combine(_root, path));
        // A worktree or submodule has a `.git` file instead of a folder.
        Directory.CreateDirectory(Path.Combine(_root, "wt"));
        File.WriteAllText(Path.Combine(_root, "wt", ".git"), "gitdir: ../x");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void FindsRepositoriesTwoLevelsDown()
    {
        var names = RepositoryScanner.Find(_root)
            .Select(p => Path.GetRelativePath(_root, p).Replace('\\', '/'));

        Assert.Equal(["a", "b/inner", "c", "wt"], names);
    }

    [Fact]
    public void ARepositoryRootIsItselfTheResultAndTheLimitHolds()
    {
        Assert.Single(RepositoryScanner.Find(Path.Combine(_root, "a")));
        Assert.Equal(2, RepositoryScanner.Find(_root, limit: 2).Count);
    }
}
