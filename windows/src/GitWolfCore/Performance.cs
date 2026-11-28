namespace GitWolf.Core;

// Ported from Performance.swift.
public sealed partial class GitRepository
{
    /// <summary>Whether git already has a commit-graph, as one file or as a chain.</summary>
    public async Task<bool> HasCommitGraphAsync()
    {
        foreach (var name in new[] { "objects/info/commit-graph", "objects/info/commit-graphs" })
        {
            var path = (await Git.RunStringAsync(["rev-parse", "--git-path", name], readOnly: true)).Trim();
            var full = Path.Combine(Root, path);
            if (File.Exists(full) || Directory.Exists(full)) return true;
        }
        return false;
    }

    /// <summary>An index of history that makes log and graph reads much faster in large repositories.</summary>
    public Task WriteCommitGraphAsync() =>
        Git.RunAsync(["commit-graph", "write", "--reachable", "--changed-paths"]);
}
