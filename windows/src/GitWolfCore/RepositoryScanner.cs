namespace GitWolf.Core;

/// <summary>
/// Finds Git repositories under a folder, so a new user can open many at once. Ported from
/// RepositoryScanner.swift.
/// </summary>
public static class RepositoryScanner
{
    private static readonly HashSet<string> Skipped = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "Library", "Pods", "build", "DerivedData", "vendor", "target",
    };

    /// <summary>
    /// Folders under <paramref name="root"/>, to <paramref name="maxDepth"/> levels below it, that hold a
    /// `.git` folder, or a `.git` file as in a worktree or submodule. A repository's own subfolders are not
    /// searched, nor are hidden folders, links to other folders, and the usual build and dependency folders.
    /// </summary>
    public static IReadOnlyList<string> Find(string root, int maxDepth = 2, int limit = 100,
        CancellationToken token = default)
    {
        var found = new List<string>();
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
        };
        void Visit(string folder, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (found.Count >= limit) return;
            var marker = Path.Combine(folder, ".git");
            if (Directory.Exists(marker) || File.Exists(marker))
            {
                found.Add(folder);
                return;
            }
            if (depth >= maxDepth) return;
            string[] children;
            try
            {
                children = Directory.GetDirectories(folder, "*", options);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A folder that can't be listed has nothing to offer.
                System.Diagnostics.Trace.TraceInformation($"A folder was skipped: {e.GetType().Name}");
                return;
            }
            foreach (var child in children.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(child);
                if (!name.StartsWith('.') && !Skipped.Contains(name)) Visit(child, depth + 1);
            }
        }
        Visit(root, 0);
        return found;
    }
}
