using System.Globalization;

namespace GitWolf.Core;

public enum ResetMode
{
    /// <summary>Move the branch only; everything stays staged.</summary>
    Soft,
    /// <summary>Move the branch and unstage; working files are kept.</summary>
    Mixed,
    /// <summary>Move the branch and overwrite the working tree. Uncommitted changes are lost.</summary>
    Hard,
    /// <summary>Like hard, but refuses to overwrite uncommitted changes. Used by Undo.</summary>
    Keep,
}

/// <summary>Where HEAD was, and what moved it there. `HEAD@{Index}` names the entry.</summary>
public sealed record ReflogEntry(int Index, string Hash, string Message, DateTimeOffset Date)
{
    public string ShortHash => Hash.Length > 7 ? Hash[..7] : Hash;
}

/// <summary>How to take back the last thing that moved HEAD.</summary>
public abstract record UndoPlan
{
    /// <summary>The commit HEAD must still be at when the plan is carried out.</summary>
    public required string ExpectedHead { get; init; }

    /// <summary>Move the current branch back to <see cref="Target"/>.</summary>
    public sealed record Reset(string Target, string Label) : UndoPlan;

    /// <summary>Check out where HEAD was: a branch, or a commit when it was detached.</summary>
    public sealed record SwitchBack(string To, bool Detached, string From) : UndoPlan;

    private const string CheckoutPrefix = "checkout: moving from ";

    /// <summary>Null when there is nothing to undo. The log is newest first.</summary>
    public static UndoPlan? For(IReadOnlyList<ReflogEntry> log)
    {
        if (log.Count < 2) return null;
        var newest = log[0];
        if (newest.Message.StartsWith(CheckoutPrefix, StringComparison.Ordinal))
        {
            var move = newest.Message[CheckoutPrefix.Length..];
            var split = move.LastIndexOf(" to ", StringComparison.Ordinal);
            if (split <= 0) return null;
            var from = move[..split];
            // A detached HEAD is recorded as the full commit hash.
            var detached = from.Length >= 40 && from.All(char.IsAsciiHexDigit);
            return new SwitchBack(from, detached, move[(split + 4)..]) { ExpectedHead = newest.Hash };
        }
        if (newest.Message.StartsWith("rebase", StringComparison.Ordinal))
        {
            // A rebase writes one entry per step; undo all of them at once.
            var before = log.FirstOrDefault(e => !e.Message.StartsWith("rebase", StringComparison.Ordinal));
            return before is null ? null : new Reset(before.Hash, "rebase") { ExpectedHead = newest.Hash };
        }
        return new Reset(log[1].Hash, newest.Message) { ExpectedHead = newest.Hash };
    }
}

/// <summary>What a reset would throw away, so the UI can say so before doing it.</summary>
public sealed record ResetImpact(int DroppedCommits, int ModifiedFiles);

// Ported from Reset.swift.
public sealed partial class GitRepository
{
    public Task ResetAsync(string target, ResetMode mode) =>
        Git.RunAsync(["reset", $"--{mode.ToString().ToLowerInvariant()}", Operand(target), "--"]);

    public async Task<List<ReflogEntry>> ReflogAsync(int limit = 200)
    {
        var output = await Git.RunStringAsync(
            ["reflog", "show", "--no-show-signature", "-n", $"{limit}", "--format=%H%x1f%ct%x1f%gs", "HEAD", "--"], readOnly: true);
        var entries = new List<ReflogEntry>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.TrimEnd('\r').Split('\u001f', 3);
            if (f.Length != 3 || !long.TryParse(f[1], CultureInfo.InvariantCulture, out var time)) continue;
            entries.Add(new ReflogEntry(entries.Count, f[0], f[2], DateTimeOffset.FromUnixTimeSeconds(time)));
        }
        return entries;
    }

    /// <summary>
    /// Moves back to where HEAD was before its last change, keeping uncommitted work. Refused when
    /// HEAD is no longer at <paramref name="expectedHead"/>, which the user confirmed undoing.
    /// </summary>
    public async Task UndoAsync(UndoPlan plan)
    {
        var head = (await Git.RunStringAsync(["rev-parse", "HEAD"], readOnly: true)).Trim();
        if (head != plan.ExpectedHead)
            throw new GitException("reset", 1, "The repository changed, so nothing was undone. Try again.");
        switch (plan)
        {
            case UndoPlan.Reset reset:
                await ResetAsync(reset.Target, ResetMode.Keep);
                break;
            case UndoPlan.SwitchBack { Detached: true } back:
                await Git.RunAsync(["checkout", "--detach", Operand(back.To), "--"]);
                break;
            case UndoPlan.SwitchBack back:
                await Git.RunAsync(["switch", Operand(back.To)]);
                break;
        }
    }

    public async Task<ResetImpact> ResetImpactAsync(string target)
    {
        var count = await Git.RunStringAsync(["rev-list", "--count", $"{Operand(target)}..HEAD"], readOnly: true);
        var status = await StatusAsync();
        return new ResetImpact(int.Parse(count.Trim(), CultureInfo.InvariantCulture),
            status.Staged.Concat(status.Unstaged).Select(f => f.Path).Distinct().Count());
    }
}
