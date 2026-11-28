namespace GitWolf.Core;

/// <summary>A folder something outside the app asked to open.</summary>
/// <param name="FromLink">Whether a link asked, which the user hasn't necessarily meant.</param>
public sealed record LaunchTarget(string Folder, bool FromLink);

/// <summary>
/// Works out which folder an "open this" request means: a folder or file on the command line (also the
/// `gitwolf` command), or a `gitwolf://open?path=C:\folder` link. Ported from LaunchRequest.swift.
/// </summary>
public static class LaunchRequest
{
    public const string Scheme = "gitwolf";

    /// <summary>The argument the link handler is registered with, before the link itself.</summary>
    public const string LinkSwitch = "--link";

    /// <param name="arguments">The command line without the program.</param>
    /// <param name="isRemoteDrive">Whether a drive root is a network drive.</param>
    public static LaunchTarget? Parse(IReadOnlyList<string> arguments, Func<string, bool>? isRemoteDrive = null)
    {
        if (arguments.Count == 0) return null;
        if (arguments[0] == LinkSwitch)
            // A link containing quotes could split into more arguments: such a request is refused whole.
            return arguments.Count == 2 && FromLink(arguments[1], isRemoteDrive ?? IsRemoteDrive) is { } linked
                ? new LaunchTarget(linked, FromLink: true) : null;
        // `"C:\"` reaches a program as `C:"`, because the backslash escapes the quote.
        var path = arguments[0].Replace('"', '\\');
        if (path.Length == 0 || path.StartsWith('-')) return null;
        return Existing(path) is { } folder ? new LaunchTarget(folder, FromLink: false) : null;
    }

    /// <summary>A link that opens `folder` in GitWolf.</summary>
    public static string Link(string folder) => $"{Scheme}://open?path={Uri.EscapeDataString(folder)}";

    /// <summary>
    /// Only a folder on this computer's own drives. A network path would send the user's Windows sign-in to
    /// whoever serves it, and let that server choose the repository's configuration.
    /// </summary>
    private static string? FromLink(string link, Func<string, bool> isRemoteDrive)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var url)
            || !url.Scheme.Equals(Scheme, StringComparison.OrdinalIgnoreCase)
            || !url.Host.Equals("open", StringComparison.OrdinalIgnoreCase)) return null;
        var value = url.Query.TrimStart('?').Split('&')
            .Where(p => p.StartsWith("path=", StringComparison.Ordinal))
            .Select(p => Uri.UnescapeDataString(p["path=".Length..])).FirstOrDefault();
        if (value is null || !IsDrivePath(value)) return null;
        try
        {
            var full = Path.GetFullPath(value);
            return IsDrivePath(full) && !isRemoteDrive(full[..3]) ? Existing(full) : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    private static bool IsDrivePath(string path) =>
        path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/'
        && !path.Any(char.IsControl);

    private static bool IsRemoteDrive(string root) =>
        new DriveInfo(root).DriveType is DriveType.Network or DriveType.NoRootDirectory or DriveType.Unknown;

    /// <summary>The folder itself, or the one holding a file; null when neither exists.</summary>
    private static string? Existing(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            if (Directory.Exists(full)) return full;
            return File.Exists(full) ? Path.GetDirectoryName(full) : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException)
        {
            return null;
        }
    }
}
