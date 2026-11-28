using System.Text.RegularExpressions;

namespace GitWolf.Core;

/// <summary>Helpers for writing commit messages. Ported from CommitMessage.swift.</summary>
public static partial class CommitMessage
{
    public const int SubjectLimit = 72;

    public static readonly IReadOnlyList<(string Name, string Summary)> ConventionalTypes =
    [
        ("feat", "A new feature"), ("fix", "A bug fix"), ("docs", "Documentation only"),
        ("style", "Formatting, no code change"), ("refactor", "Neither a fix nor a feature"),
        ("perf", "A performance improvement"), ("test", "Adding or fixing tests"),
        ("build", "Build system or dependencies"), ("ci", "CI configuration"), ("chore", "Other changes"),
        ("revert", "Reverts an earlier commit"),
    ];

    /// <summary>Sets the `type(scope): ` prefix on the first line, replacing an existing one.</summary>
    public static string ApplyPrefix(string type, string? scope, string message)
    {
        var lines = message.Split('\n');
        var scopeText = string.IsNullOrEmpty(scope) ? "" : $"({scope})";
        lines[0] = $"{type}{scopeText}: {Prefix().Replace(lines[0], "")}";
        return string.Join('\n', lines);
    }

    /// <summary>Up to three top-level folders of the paths, to offer as scopes.</summary>
    public static IReadOnlyList<string> SuggestedScopes(IEnumerable<string> paths) =>
        paths.Where(p => p.IndexOf('/') > 0).Select(p => p[..p.IndexOf('/')]).Distinct().Take(3).ToList();

    /// <summary>The first line's length when it is over the limit, otherwise null.</summary>
    public static int? LongSubject(string message)
    {
        var end = message.IndexOf('\n');
        var length = (end < 0 ? message : message[..end]).TrimEnd().Length;
        return length > SubjectLimit ? length : null;
    }

    /// <summary>Adds a footer line such as `Closes #12` (after a blank line), unless it is already there.</summary>
    public static string AddReference(string keyword, int number, string message)
    {
        var line = $"{keyword} #{number}";
        var trimmed = message.ReplaceLineEndings("\n").Trim();
        var lines = trimmed.Split('\n');
        if (lines.Contains(line)) return message;
        if (trimmed.Length == 0) return line;
        // Join an existing footer block of references, otherwise start a new paragraph.
        var gap = lines.Length > 1 && Reference().IsMatch(lines[^1]) ? "\n" : "\n\n";
        return trimmed + gap + line;
    }

    private const string BreakingFooter = "BREAKING CHANGE:";

    /// <summary>Whether the message is marked as breaking, by a `!` or a footer.</summary>
    public static bool IsBreaking(string message)
    {
        var lines = message.Split('\n');
        return BreakingSubject().IsMatch(lines[0])
            || lines.Any(l => l.StartsWith(BreakingFooter, StringComparison.Ordinal));
    }

    /// <summary>Adds or removes the `!` before the colon and the `BREAKING CHANGE:` footer.</summary>
    public static string SetBreaking(bool breaking, string message)
    {
        var lines = message.Split('\n').ToList();
        var subject = Subject().Match(lines[0]);
        var marked = BreakingSubject().IsMatch(lines[0]);
        if (breaking)
        {
            if (subject.Success && !marked) lines[0] = lines[0].Insert(subject.Length - 1, "!");
            if (!lines.Any(l => l.StartsWith(BreakingFooter, StringComparison.Ordinal)))
            {
                TrimEnd(lines);
                lines.AddRange(["", BreakingFooter + " "]);
            }
        }
        else
        {
            if (marked) lines[0] = lines[0].Remove(lines[0].IndexOf('!'), 1);
            lines.RemoveAll(l => l.StartsWith(BreakingFooter, StringComparison.Ordinal));
            TrimEnd(lines);
        }
        return string.Join('\n', lines);

        static void TrimEnd(List<string> all)
        {
            while (all.Count > 0 && all[^1].Trim().Length == 0) all.RemoveAt(all.Count - 1);
        }
    }

    private static readonly (string Type, string Title)[] ChangelogSections =
    [
        ("feat", "Features"), ("fix", "Fixes"), ("perf", "Performance"), ("docs", "Documentation"),
        ("refactor", "Refactoring"), ("test", "Tests"), ("build", "Build"), ("ci", "CI"), ("style", "Style"),
        ("chore", "Chores"), ("revert", "Reverts"),
    ];

    /// <summary>Release notes in Markdown, grouped by conventional commit type.</summary>
    public static string Changelog(IEnumerable<string> subjects)
    {
        var groups = new Dictionary<string, List<string>>();
        void Add(string title, string line)
        {
            if (!groups.TryGetValue(title, out var list)) groups[title] = list = [];
            list.Add(line);
        }
        foreach (var subject in subjects)
        {
            var match = Conventional().Match(subject);
            var title = ChangelogSections.FirstOrDefault(s => s.Type == match.Groups[1].Value).Title;
            if (!match.Success || title is null)
            {
                Add("Other", "- " + subject);
                continue;
            }
            var scope = match.Groups[3].Value;
            var line = "- " + (scope.Length == 0 ? "" : $"**{scope}:** ") + subject[match.Length..];
            if (match.Groups[4].Success) Add("Breaking changes", line);
            Add(title, line);
        }
        var order = ChangelogSections.Select(s => s.Title).Prepend("Breaking changes").Append("Other");
        return string.Join("\n\n", order.Where(groups.ContainsKey)
            .Select(t => $"### {t}\n{string.Join('\n', groups[t])}"));
    }

    [GeneratedRegex(@"^[a-z]+(\([^)\n]*\))?!?:")]
    private static partial Regex Subject();

    [GeneratedRegex(@"^[a-z]+(\([^)\n]*\))?!:")]
    private static partial Regex BreakingSubject();

    [GeneratedRegex(@"^([a-z]+)(\(([^)\n]*)\))?(!)?:\s*")]
    private static partial Regex Conventional();

    [GeneratedRegex(@"^(Closes|Fixes|Resolves|Refs|Related to) #\d+$")]
    private static partial Regex Reference();

    [GeneratedRegex(@"^[a-z]+(\([^)\n]*\))?!?:\s*")]
    private static partial Regex Prefix();
}

public sealed partial class GitRepository
{
    private const long MaxTemplateBytes = 64 * 1024;

    /// <summary>The file named by `commit.template`, if one is configured and readable.</summary>
    public async Task<string?> CommitTemplateAsync()
    {
        try
        {
            // --path expands "~/".
            var path = (await Git.RunStringAsync(["config", "--path", "--get", "commit.template"], readOnly: true)).Trim();
            if (path.Length == 0 || IsNetworkPath(path)) return null;
            var file = new FileInfo(Path.Combine(Root, path));
            if (!file.Exists || file.Length > MaxTemplateBytes) return null;
            var text = (await File.ReadAllTextAsync(file.FullName)).Replace("\r\n", "\n");
            // Git ignores lines starting with # in a template.
            var body = string.Join('\n', text.Split('\n').Where(l => !l.StartsWith('#'))).Trim('\n');
            return body.Length == 0 ? null : body;
        }
        catch (Exception e) when (e is GitException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Subjects of the commits after <paramref name="since"/>, newest first, without merges.</summary>
    public async Task<IReadOnlyList<string>> CommitSubjectsAsync(string since)
    {
        var output = await Git.RunStringAsync(
            ["log", "--no-show-signature", "--no-merges", "--format=%s", $"{Operand(since)}..HEAD", "--"], readOnly: true);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
    }

    /// <summary>Full messages of recent commits, newest first, without duplicates, for reuse.</summary>
    public async Task<IReadOnlyList<string>> RecentMessagesAsync(int limit = 10)
    {
        try
        {
            var output = await Git.RunStringAsync(["log", "--no-show-signature", "-n", $"{limit * 3}", "--format=%B%x00"], readOnly: true);
            return output.Split('\0').Select(m => m.Trim()).Where(m => m.Length > 0).Distinct().Take(limit).ToList();
        }
        catch (GitException)
        {
            return []; // No commits yet.
        }
    }
}
