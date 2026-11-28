using System.Globalization;

namespace GitWolf.Core;

public sealed record AuthorStat(string Name, string Email, int Commits, long Added, long Deleted);

public sealed record FileStat(string Path, int Commits, long Added, long Deleted)
{
    public long Churn => Added + Deleted;
}

/// <summary>Commits in the week starting on <see cref="Start"/> (a Monday, UTC).</summary>
public sealed record WeekStat(DateOnly Start, int Commits);

/// <summary>Who changes a top-level folder the most, as a share (0 to 1) of its changed lines.</summary>
public sealed record FolderOwner(string Folder, string Owner, double Share, long Lines);

/// <summary>Statistics about a repository's history, from one `git log --numstat`. Ported from Insights.swift.</summary>
public sealed record RepositoryInsights(
    int Commits, long Added, long Deleted, IReadOnlyList<WeekStat> Weeks, IReadOnlyList<AuthorStat> Contributors,
    IReadOnlyList<FileStat> HotFiles, IReadOnlyList<FileStat> ChurnFiles, IReadOnlyList<FolderOwner> Ownership)
{
    /// <summary>Whether the history was longer than what was read.</summary>
    public bool Truncated { get; init; }

    private const char RecordSeparator = '\u001e';
    private const char FieldSeparator = '\u001f';

    /// <summary>Parses `log --format=%x1e%an%x1f%ae%x1f%ct --numstat`, reading at most `limit` commits.</summary>
    public static RepositoryInsights Parse(string output, int limit = int.MaxValue)
    {
        int commits = 0;
        long added = 0, deleted = 0;
        var truncated = false;
        var people = new Dictionary<string, AuthorStat>();
        var files = new Dictionary<string, FileStat>();
        var weeks = new Dictionary<DateOnly, int>();
        var folders = new Dictionary<string, Dictionary<string, long>>();

        foreach (var record in output.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var lines = record.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) continue;
            var f = lines[0].TrimEnd('\r').Split(FieldSeparator, 3);
            if (f.Length != 3 || !long.TryParse(f[2], CultureInfo.InvariantCulture, out var time)
                || time is < 0 or > 253_402_300_799) continue;
            if (commits == limit)
            {
                truncated = true;
                break;
            }
            commits++;
            var email = f[1].ToLowerInvariant();
            var day = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(time).UtcDateTime);
            // Sunday is 0, so shift to count from Monday.
            var monday = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
            weeks[monday] = weeks.GetValueOrDefault(monday) + 1;
            var person = people.GetValueOrDefault(email) ?? new AuthorStat(f[0], email, 0, 0, 0);
            long mine = 0, removed = 0;
            foreach (var line in lines.Skip(1))
            {
                var columns = line.TrimEnd('\r').Split('\t', 3);
                if (columns.Length != 3) continue;
                // Binary files report "-" for both counts: a change with no lines.
                long.TryParse(columns[0], CultureInfo.InvariantCulture, out var add);
                long.TryParse(columns[1], CultureInfo.InvariantCulture, out var del);
                var path = columns[2];
                mine += add;
                removed += del;
                var stat = files.GetValueOrDefault(path) ?? new FileStat(path, 0, 0, 0);
                files[path] = stat with { Commits = stat.Commits + 1, Added = stat.Added + add, Deleted = stat.Deleted + del };
                var slash = path.IndexOf('/');
                var folder = slash > 0 ? path[..slash] : "(root)";
                if (!folders.TryGetValue(folder, out var authors)) folders[folder] = authors = [];
                authors[email] = authors.GetValueOrDefault(email) + add + del;
            }
            added += mine;
            deleted += removed;
            people[email] = person with
            {
                Commits = person.Commits + 1, Added = person.Added + mine, Deleted = person.Deleted + removed,
            };
        }

        var ownership = new List<FolderOwner>();
        foreach (var (folder, authors) in folders)
        {
            var total = authors.Values.Sum();
            if (total == 0) continue;
            var top = authors.OrderByDescending(a => a.Value).ThenBy(a => a.Key, StringComparer.Ordinal).First();
            ownership.Add(new FolderOwner(folder, people[top.Key].Name, (double)top.Value / total, total));
        }
        return new RepositoryInsights(
            commits, added, deleted,
            weeks.OrderBy(w => w.Key).Select(w => new WeekStat(w.Key, w.Value)).ToList(),
            people.Values.OrderByDescending(p => p.Commits).ThenBy(p => p.Name, StringComparer.Ordinal).ToList(),
            files.Values.OrderByDescending(s => s.Commits).ThenBy(s => s.Path, StringComparer.Ordinal).ToList(),
            files.Values.Where(s => s.Churn > 0).OrderByDescending(s => s.Churn)
                .ThenBy(s => s.Path, StringComparer.Ordinal).ToList(),
            ownership.OrderByDescending(o => o.Lines).ThenBy(o => o.Folder, StringComparer.Ordinal).ToList())
        {
            Truncated = truncated,
        };
    }
}

public sealed partial class GitRepository
{
    /// <summary>Statistics for the current branch's history, merges excluded.</summary>
    public async Task<RepositoryInsights> InsightsAsync(int limit = 20_000)
    {
        // One extra commit is read only to learn that the history is longer.
        var output = await Git.RunStringAsync(
            ["-c", "core.quotepath=false", "log", "--no-show-signature", "--no-merges", "-n", $"{limit + 1}",
                "--no-renames", "--format=%x1e%an%x1f%ae%x1f%ct", "--numstat"], readOnly: true);
        return RepositoryInsights.Parse(output, limit);
    }
}
