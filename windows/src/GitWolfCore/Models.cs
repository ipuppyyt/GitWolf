namespace GitWolf.Core;

// Ported from apple/Sources/GitWolfCore/Models.swift. Keep in sync with the Swift types —
// they are the spec this port implements.

// MARK: - Commits

public enum RefKind { Head, Local, Remote, Tag }

public readonly record struct RefLabel(string Name, RefKind Kind, bool IsCurrent);

public sealed record Commit(
    string Hash,
    IReadOnlyList<string> Parents,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset Date,
    string Subject,
    IReadOnlyList<RefLabel> Refs)
{
    public string Id => Hash;
    public string ShortHash => Hash.Length > 7 ? Hash[..7] : Hash;
    public bool IsMerge => Parents.Count > 1;

    public bool Equals(Commit? other) =>
        other is not null && Hash == other.Hash && AuthorName == other.AuthorName
        && AuthorEmail == other.AuthorEmail && Date == other.Date && Subject == other.Subject
        && Parents.SequenceEqual(other.Parents) && Refs.SequenceEqual(other.Refs);

    public override int GetHashCode() => Hash.GetHashCode();
}

// MARK: - Working tree

public enum ChangeKind
{
    Modified,
    Added,
    Deleted,
    Renamed,
    Copied,
    TypeChanged,
    Untracked,
    Unmerged,
}

public static class ChangeKindCode
{
    public static ChangeKind FromCode(char code) => code switch
    {
        'M' => ChangeKind.Modified,
        'A' => ChangeKind.Added,
        'D' => ChangeKind.Deleted,
        'R' => ChangeKind.Renamed,
        'C' => ChangeKind.Copied,
        'T' => ChangeKind.TypeChanged,
        '?' => ChangeKind.Untracked,
        'U' => ChangeKind.Unmerged,
        _ => ChangeKind.Modified,
    };
}

public static class ChangeKindNames
{
    /// <summary>The plain name a screen reader says for a change.</summary>
    public static string SpokenName(this ChangeKind kind) => kind switch
    {
        ChangeKind.Added => "Added",
        ChangeKind.Deleted => "Deleted",
        ChangeKind.Renamed => "Renamed",
        ChangeKind.Copied => "Copied",
        ChangeKind.TypeChanged => "Type changed",
        ChangeKind.Untracked => "New file",
        ChangeKind.Unmerged => "Conflicted",
        _ => "Modified",
    };
}

public sealed record FileChange(string Path, string? OriginalPath, ChangeKind Kind)
{
    public string Id => Path;
    public string FileName => System.IO.Path.GetFileName(Path);
    public string Directory => System.IO.Path.GetDirectoryName(Path)?.Replace('\\', '/') ?? "";
}

public sealed record WorkingStatus
{
    public bool Equals(WorkingStatus? other) =>
        other is not null && Branch == other.Branch && HeadOid == other.HeadOid && Upstream == other.Upstream
        && Ahead == other.Ahead && Behind == other.Behind && Staged.SequenceEqual(other.Staged)
        && Unstaged.SequenceEqual(other.Unstaged) && Untracked.SequenceEqual(other.Untracked)
        && Conflicted.SequenceEqual(other.Conflicted);

    public override int GetHashCode() => HashCode.Combine(Branch, HeadOid, Ahead, Behind, ChangeCount);

    public string? Branch { get; init; }
    public string? HeadOid { get; init; }
    public string? Upstream { get; init; }
    public int Ahead { get; init; }
    public int Behind { get; init; }
    public IReadOnlyList<FileChange> Staged { get; init; } = [];
    public IReadOnlyList<FileChange> Unstaged { get; init; } = [];
    public IReadOnlyList<FileChange> Untracked { get; init; } = [];
    public IReadOnlyList<FileChange> Conflicted { get; init; } = [];

    public static readonly WorkingStatus Empty = new();

    public bool IsDetached => Branch is null && HeadOid is not null;
    public bool IsUnborn => HeadOid is null;
    public int ChangeCount => Staged.Count + Unstaged.Count + Untracked.Count + Conflicted.Count;
    public bool IsClean => ChangeCount == 0;

    public string BranchDisplay =>
        Branch ?? (HeadOid is { } oid ? $"detached @ {(oid.Length > 7 ? oid[..7] : oid)}" : "no commits yet");
}

// MARK: - Refs

public sealed record Branch(
    string Name,
    string FullRef,
    bool IsRemote,
    bool IsCurrent,
    string? Upstream,
    int Ahead,
    int Behind,
    string Hash,
    // The upstream branch was deleted on the remote.
    bool UpstreamGone = false)
{
    public string Id => FullRef;

    /// <summary>`origin` for `refs/remotes/origin/main`.</summary>
    public string? Remote
    {
        get
        {
            if (!IsRemote) return null;
            const string prefix = "refs/remotes/";
            var rest = FullRef.StartsWith(prefix, StringComparison.Ordinal) ? FullRef[prefix.Length..] : FullRef;
            var slash = rest.IndexOf('/');
            return slash < 0 ? rest : rest[..slash];
        }
    }

    /// <summary>Branch name without the remote prefix.</summary>
    public string ShortName => Remote is { } remote ? Name[(remote.Length + 1)..] : Name;
}

/// <param name="Message">The first line of an annotated tag's message.</param>
public sealed record GitTag(string Name, string Hash, bool IsAnnotated = false, string Message = "")
{
    public string Id => Name;
}

public sealed record Stash(string Ref, string Message, DateTimeOffset Date)
{
    public string Id => Ref;
}

// MARK: - Diffs

public enum DiffLineKind { Context, Addition, Deletion, HunkHeader, Note }

public sealed record DiffLine(
    int Id,
    DiffLineKind Kind,
    int HunkIndex,
    int? OldNumber,
    int? NewNumber,
    string Text,
    string Raw)
{
    public bool IsChange => Kind is DiffLineKind.Addition or DiffLineKind.Deletion;
    /// <summary>Stands for folded unchanged lines; see <see cref="DiffFolding"/>.</summary>
    public bool IsFold => Id < 0;
}

public readonly record struct DiffHunk(int Index, int OldStart, int NewStart, string Section);

public sealed record FileDiff
{
    public bool Equals(FileDiff? other) =>
        other is not null && IsBinary == other.IsBinary && IsTruncated == other.IsTruncated
        && IgnoresWhitespace == other.IgnoresWhitespace
        && Additions == other.Additions && Deletions == other.Deletions && Header.SequenceEqual(other.Header)
        && Hunks.SequenceEqual(other.Hunks) && Lines.SequenceEqual(other.Lines);

    public override int GetHashCode() => HashCode.Combine(Additions, Deletions, Lines.Count);

    public required IReadOnlyList<string> Header { get; init; }
    public required IReadOnlyList<DiffHunk> Hunks { get; init; }
    public required IReadOnlyList<DiffLine> Lines { get; init; }
    public required bool IsBinary { get; init; }
    public required int Additions { get; init; }
    public required int Deletions { get; init; }
    public required bool IsTruncated { get; init; }

    /// <summary>Whitespace changes were left out, so the lines don't add up to a patch.</summary>
    public bool IgnoresWhitespace { get; init; }

    public static readonly FileDiff Empty = new()
    {
        Header = [], Hunks = [], Lines = [], IsBinary = false, Additions = 0, Deletions = 0, IsTruncated = false,
    };

    /// <summary>Whether hunks/lines of this diff can safely be turned back into a patch.</summary>
    public bool CanPatch =>
        !IsBinary && !IsTruncated && !IgnoresWhitespace && Hunks.Count > 0 && Header.Count > 0
        && !Lines.Any(l => l.Raw.Contains('�'));

    public HashSet<int> ChangedLineIds(int hunkIndex) =>
        [.. Lines.Where(l => l.HunkIndex == hunkIndex && l.IsChange).Select(l => l.Id)];
}

public enum DiffSourceKind { Staged, Unstaged, Untracked, Conflicted, Commit, Compare }

public readonly record struct DiffSource(DiffSourceKind Kind, string? CommitHash = null, string? BaseHash = null)
{
    public static DiffSource Staged { get; } = new(DiffSourceKind.Staged);
    public static DiffSource Unstaged { get; } = new(DiffSourceKind.Unstaged);
    public static DiffSource Untracked { get; } = new(DiffSourceKind.Untracked);
    public static DiffSource Conflicted { get; } = new(DiffSourceKind.Conflicted);
    public static DiffSource ForCommit(string hash) => new(DiffSourceKind.Commit, hash);
    /// <summary>The difference between two resolved commits.</summary>
    public static DiffSource Between(string baseHash, string head) => new(DiffSourceKind.Compare, head, baseHash);
}

public readonly record struct DiffTarget(DiffSource Source, string Path, string? OriginalPath = null);

// MARK: - Snapshot

/// <summary>Everything the UI needs, captured in one pass so views never see a half-updated repository.</summary>
public sealed class RepositorySnapshot
{
    public required WorkingStatus Status { get; init; }
    public required IReadOnlyList<Commit> Commits { get; init; }
    public required IReadOnlyList<GraphRow> Graph { get; init; }
    public required IReadOnlyList<Branch> LocalBranches { get; init; }
    public required IReadOnlyList<Branch> RemoteBranches { get; init; }
    public required IReadOnlyList<GitTag> Tags { get; init; }
    public required IReadOnlyList<Stash> Stashes { get; init; }
    public required IReadOnlyList<string> Remotes { get; init; }
    public InProgressOperation? Operation { get; init; }

    public static readonly RepositorySnapshot Empty = new()
    {
        Status = WorkingStatus.Empty, Commits = [], Graph = [], LocalBranches = [], RemoteBranches = [],
        Tags = [], Stashes = [], Remotes = [],
    };
}

