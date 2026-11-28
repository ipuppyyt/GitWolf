using System.Diagnostics;
using System.Text;
using GitWolf.Core;
using Microsoft.Win32;

namespace GitWolf.App.Services;

/// <summary>
/// The `gitwolf` command and `gitwolf://` links, both for this user only and only once asked for in
/// Settings.
/// </summary>
internal static class OutsideAccess
{
    private const string LinkKey = @"Software\Classes\" + LaunchRequest.Scheme;

    /// <summary>A folder that is on every user's PATH, so nothing has to be added to it.</summary>
    private static readonly string CommandFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "WindowsApps", "gitwolf.cmd");

    private const string Marker = "rem Installed by GitWolf";

    private static string Program => Environment.ProcessPath
        ?? throw new IOException("The program’s own location is unknown.");

    public static bool CommandInstalled => IsOurCommand();

    public static bool LinksRegistered
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(LinkKey + @"\shell\open\command");
            return key?.GetValue(null) as string == LinkCommand();
        }
    }

    private static string LinkCommand() => $"\"{Program}\" {LaunchRequest.LinkSwitch} \"%1\"";

    /// <returns>Null on success, otherwise a message for the user.</returns>
    public static string? SetCommand(bool installed) => Attempt(() =>
    {
        if (File.Exists(CommandFile) && !IsOurCommand())
            return "Another gitwolf.cmd is already there, so it was left alone.";
        if (!installed)
        {
            File.Delete(CommandFile);
            return null;
        }
        // The console reads scripts in its own code page, which only agrees with others on ASCII.
        if (!Program.All(char.IsAscii))
            return "The command can’t be installed because GitWolf’s folder has non-English characters.";
        var program = Program.Replace("%", "%%");
        var script = string.Join("\r\n",
            "@echo off", Marker,
            "if \"%~1\"==\"\" (",
            $"  start \"\" \"{program}\" \"%CD%\"",
            ") else (",
            $"  start \"\" \"{program}\" \"%~f1\"",
            ")", "");
        Directory.CreateDirectory(Path.GetDirectoryName(CommandFile)!);
        File.WriteAllText(CommandFile, script, Encoding.ASCII);
        return null;
    });

    /// <returns>Null on success, otherwise a message for the user.</returns>
    public static string? SetLinks(bool registered) => Attempt(() =>
    {
        if (!registered)
        {
            if (LinksRegistered) Registry.CurrentUser.DeleteSubKeyTree(LinkKey, throwOnMissingSubKey: false);
            return null;
        }
        using var key = Registry.CurrentUser.CreateSubKey(LinkKey);
        key.SetValue(null, "URL:GitWolf");
        key.SetValue("URL Protocol", "");
        using var command = key.CreateSubKey(@"shell\open\command");
        command.SetValue(null, LinkCommand());
        return null;
    });

    private static bool IsOurCommand()
    {
        try
        {
            var file = new FileInfo(CommandFile);
            return file.Exists && file.Length < 4096 && File.ReadAllText(CommandFile).Contains(Marker);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? Attempt(Func<string?> change)
    {
        try
        {
            return change();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Trace.TraceWarning($"Changing outside access failed: {e.GetType().Name}");
            return "Windows didn’t allow the change.";
        }
    }
}
