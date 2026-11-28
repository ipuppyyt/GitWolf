namespace GitWolf.Core;

/// <summary>A named set of repositories on the home screen.</summary>
public sealed record HomeGroup(Guid Id, string Name, List<string> Paths);

/// <summary>
/// How the home screen organises repositories: a pinned list plus named groups, by root path.
/// Ported from HomeLayout.swift. Paths are compared ignoring case, as Windows does.
/// </summary>
public sealed class HomeLayout
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public List<string> Pinned { get; init; } = [];
    public List<HomeGroup> Groups { get; init; } = [];

    public bool IsPinned(string path) => Pinned.Contains(path, Comparer);

    public void TogglePin(string path) => Toggle(Pinned, path);

    /// <summary>Names are trimmed; a blank one is ignored.</summary>
    public Guid? AddGroup(string name, string? path = null)
    {
        if (name.Trim() is not { Length: > 0 } trimmed) return null;
        var group = new HomeGroup(Guid.NewGuid(), trimmed, path is null ? [] : [path]);
        Groups.Add(group);
        return group.Id;
    }

    public void RenameGroup(Guid id, string name)
    {
        var index = Groups.FindIndex(g => g.Id == id);
        if (index >= 0 && name.Trim() is { Length: > 0 } trimmed) Groups[index] = Groups[index] with { Name = trimmed };
    }

    public void DeleteGroup(Guid id) => Groups.RemoveAll(g => g.Id == id);

    public void ToggleInGroup(string path, Guid id)
    {
        if (Groups.Find(g => g.Id == id) is { } group) Toggle(group.Paths, path);
    }

    /// <summary>Forgets a repository in the pins and every group.</summary>
    public void Remove(string path)
    {
        Pinned.RemoveAll(p => Comparer.Equals(p, path));
        foreach (var group in Groups) group.Paths.RemoveAll(p => Comparer.Equals(p, path));
    }

    /// <summary>A copy without the paths for which <paramref name="exists"/> is false.</summary>
    public HomeLayout Pruned(Func<string, bool> exists) => new()
    {
        Pinned = Pinned.Where(exists).ToList(),
        Groups = Groups.Select(g => g with { Paths = g.Paths.Where(exists).ToList() }).ToList(),
    };

    /// <summary>The saved file can be edited by hand, so missing and damaged entries are dropped.</summary>
    public static HomeLayout Sanitized(HomeLayout? layout)
    {
        static List<string> Clean(IEnumerable<string?>? paths) =>
            (paths ?? []).OfType<string>().Where(p => p.Trim().Length > 0).Distinct(Comparer).ToList();
        return new HomeLayout
        {
            Pinned = Clean(layout?.Pinned),
            Groups = (layout?.Groups ?? []).Where(g => g is { Name: not null } && g.Name.Trim().Length > 0)
                .Select(g => g with { Paths = Clean(g.Paths) }).ToList(),
        };
    }

    private static void Toggle(List<string> list, string path)
    {
        if (list.RemoveAll(p => Comparer.Equals(p, path)) == 0) list.Add(path);
    }
}
