namespace GitWolf.Core;

/// <summary>Narrows a list of changed files by part of their path, ignoring case.</summary>
public static class FileFilter
{
    public static IReadOnlyList<FileChange> Apply(IReadOnlyList<FileChange> files, string query) =>
        query.Length == 0
            ? files
            : files.Where(f => f.Path.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
}
