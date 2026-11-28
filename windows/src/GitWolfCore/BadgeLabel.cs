namespace GitWolf.Core;

/// <summary>Text for an app-icon badge showing how many files have uncommitted changes. Ported from Badge.swift.</summary>
public static class BadgeLabel
{
    /// <summary>Null (no badge) for zero, the number up to 99, then "99+".</summary>
    public static string? Text(int changes) => changes <= 0 ? null : changes > 99 ? "99+" : $"{changes}";
}
