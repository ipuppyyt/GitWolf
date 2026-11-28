namespace GitWolf.Core;

/// <summary>A count with its noun, as in "1 commit" or "1,200 commits".</summary>
public static class CountLabel
{
    /// <param name="noun">The singular, of a word that takes a plain "s".</param>
    public static string Text(long count, string noun) => $"{count:N0} {noun}{(count == 1 ? "" : "s")}";
}
