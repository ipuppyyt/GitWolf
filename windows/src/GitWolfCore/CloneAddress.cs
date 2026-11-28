namespace GitWolf.Core;

/// <summary>Decides whether copied text is a repository address worth offering to clone.</summary>
public static class CloneAddress
{
    private const int MaxLength = 2048;

    /// <summary>
    /// The trimmed address, or null. Local paths are never offered, nor web addresses that carry a
    /// user name or token, which git would save in the clone's configuration.
    /// </summary>
    public static string? FromClipboard(string? text)
    {
        var value = text?.Trim();
        if (value is null || value.Length is 0 or > MaxLength || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            return null;
        if (value.StartsWith("git@", StringComparison.Ordinal)) return value.Contains(':') ? value : null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Host.Length == 0
            || !value.Contains("://", StringComparison.Ordinal)) return null;
        var web = uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp;
        if (web && uri.UserInfo.Length > 0) return null;
        // An SSH user name is fine; a password is not.
        if (uri.Scheme == "ssh") return uri.UserInfo.Contains(':') ? null : value;
        if (!web) return null;
        return value.EndsWith(".git", StringComparison.OrdinalIgnoreCase) || uri.Host is "github.com" or "gitlab.com"
            ? value
            : null;
    }
}
