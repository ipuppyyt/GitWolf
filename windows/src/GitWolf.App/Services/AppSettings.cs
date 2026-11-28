using System.Diagnostics;
using System.Text.Json;
using GitWolf.Core;
using GitWolf.Store;

namespace GitWolf.App.Services;

internal enum AppearanceChoice { System, Light, Dark }

/// <summary>Saved as a number, so new colours go at the end.</summary>
internal enum AccentChoice { Indigo, Blue, Purple, Pink, Red, Orange, Green, Teal, Wolf }

/// <summary>The saved preferences. Never holds credentials.</summary>
internal sealed record SettingsState(
    AppearanceChoice Appearance = AppearanceChoice.System,
    AccentChoice Accent = AccentChoice.Wolf,
    int AutoFetchMinutes = 5,
    int CommitPageSize = 1000,
    int DiffContext = 4,
    bool SyntaxHighlighting = true,
    string GitPath = "", bool PullRebase = false, bool ProfilePictures = true,
    bool IgnoreWhitespace = false, bool FullFileDiffs = false,
    bool SpellCheck = true, double SidebarWidth = 248, double ListShare = 0.45,
    double MessageHeight = 90, double FilesHeight = 0,
    bool CheckForUpdates = true, long LastUpdateCheck = 0, string SkippedUpdate = "",
    string EditorPath = "", IReadOnlyList<CustomAction>? CustomActions = null,
    string CloneFolder = "", bool ShowTrayIcon = true, bool NotifyChecks = true,
    bool WelcomeSeen = false);

/// <summary>
/// User settings, kept in %LOCALAPPDATA%\GitWolf\settings.json and read on every use so changes
/// apply without a restart. Ported from AppSettings.swift. With no saved credential, git uses the
/// user's own sign-in (Git Credential Manager or SSH).
/// </summary>
internal sealed class AppSettings : IStoreSettings
{
    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GitWolf", "settings.json");

    internal static readonly string Folder = Path.GetDirectoryName(StatePath)!;

    public AppSettings()
    {
        State = Load();
        Accounts = new AccountStore(Path.Combine(Folder, "accounts.json"), new WindowsCredentialStore());
    }

    /// <summary>Saved provider accounts; their secrets are in Windows Credential Manager.</summary>
    public AccountStore Accounts { get; }

    public SettingsState State { get; private set; }

    /// <summary>Raised with the old and new values after a change.</summary>
    public event Action<SettingsState, SettingsState>? Changed;

    public int CommitPageSize => State.CommitPageSize;
    public int AutoFetchMinutes => State.AutoFetchMinutes;
    public bool PullRebase => State.PullRebase;

    /// <summary>The custom git executable, or null to use the one found on PATH.</summary>
    public string? CustomGitPath => IsLocalProgram(State.GitPath) ? State.GitPath : null;

    public const int MaxActions = 50;

    /// <summary>The custom actions that have both a name and a command.</summary>
    public IEnumerable<CustomAction> Actions => (State.CustomActions ?? []).Where(a => a.IsValid);

    /// <summary>The editor to open repositories in, or null when the chosen or default one can't be used.</summary>
    public string? Editor => State.EditorPath.Length == 0 ? OpenIn.FindDefaultEditor()
        : IsLocalProgram(State.EditorPath) ? State.EditorPath : null;

    public string EditorName => State.EditorPath.Length == 0
        ? "Visual Studio Code" : Path.GetFileNameWithoutExtension(State.EditorPath);

    public GitRepository Configure(GitRepository repo) => new(repo.Root, CustomGitPath, State.DiffContext)
    {
        // git gets the account matching a remote's server; others use the user's own sign-in.
        Credentials = Accounts.CredentialForRemoteAsync,
        IgnoreWhitespace = State.IgnoreWhitespace,
        FullFileDiffs = State.FullFileDiffs,
        Trust = Trust,
        AskpassPath = AccountStore.WriteAskpass(Folder),
    };

    /// <summary>Repositories allowed to run what their own configuration asks for.</summary>
    private RepositoryTrust Trust { get; } = new(Path.Combine(Folder, "trusted.json"));

    public void TrustRepository(string root, string fingerprint) => Trust.Trust(root, fingerprint);

    public ProviderAccount? AccountForHost(string host) => Accounts.ForHost(host);

    public Task<string?> SecretAsync(ProviderAccount account) => Accounts.SecretAsync(account);

    /// <summary>
    /// A full path to a local .exe. Relative and network paths are refused: the app runs this file.
    /// </summary>
    public static bool IsLocalProgram(string path) =>
        Path.IsPathFullyQualified(path) && !path.StartsWith(@"\\", StringComparison.Ordinal)
        && !path.StartsWith("//", StringComparison.Ordinal)
        && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path);

    /// <summary>A full path to an existing folder on this computer, not on the network.</summary>
    public static bool IsLocalFolder(string path) =>
        Path.IsPathFullyQualified(path) && !path.StartsWith(@"\\", StringComparison.Ordinal)
        && !path.StartsWith("//", StringComparison.Ordinal) && Directory.Exists(path);

    public void Update(SettingsState next)
    {
        next = Sanitize(next);
        var previous = State;
        if (next == previous) return;
        State = next;
        Save();
        Changed?.Invoke(previous, next);
    }

    /// <summary>The file can be edited by hand, so every value is brought into range.</summary>
    private static SettingsState Sanitize(SettingsState s) => s with
    {
        Appearance = Enum.IsDefined(s.Appearance) ? s.Appearance : AppearanceChoice.System,
        Accent = Enum.IsDefined(s.Accent) ? s.Accent : AccentChoice.Wolf,
        AutoFetchMinutes = Math.Clamp(s.AutoFetchMinutes, 0, 1440),
        CommitPageSize = Math.Clamp(s.CommitPageSize, 100, 20000),
        DiffContext = Math.Clamp(s.DiffContext, 0, 50),
        GitPath = (s.GitPath ?? "").Trim().Trim('"'),
        SidebarWidth = double.IsFinite(s.SidebarWidth) ? Math.Clamp(s.SidebarWidth, 160, 600) : 248,
        ListShare = double.IsFinite(s.ListShare) ? Math.Clamp(s.ListShare, 0.15, 0.85) : 0.45,
        SkippedUpdate = s.SkippedUpdate ?? "",
        EditorPath = (s.EditorPath ?? "").Trim().Trim('"'),
        CustomActions = Complete(s.CustomActions),
        CloneFolder = (s.CloneFolder ?? "").Trim(),
        MessageHeight = double.IsFinite(s.MessageHeight) ? Math.Clamp(s.MessageHeight, 60, 600) : 90,
        // Zero means the list fits its files.
        FilesHeight = double.IsFinite(s.FilesHeight) && s.FilesHeight > 0 ? Math.Clamp(s.FilesHeight, 52, 800) : 0,
    };

    /// <summary>The same list when nothing is missing, so an unchanged state still compares as equal.</summary>
    private static IReadOnlyList<CustomAction>? Complete(IReadOnlyList<CustomAction>? actions) =>
        actions is null || (actions.Count <= MaxActions
            && actions.All(a => a is { Name: not null, Command: not null }))
            ? actions
            : actions.OfType<CustomAction>().Take(MaxActions)
                .Select(a => a with { Name = a.Name ?? "", Command = a.Command ?? "" }).ToList();

    private static SettingsState Load()
    {
        try
        {
            return File.Exists(StatePath)
                ? Sanitize(JsonSerializer.Deserialize<SettingsState>(File.ReadAllText(StatePath)) ?? new())
                : new();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Settings could not be read: {e.GetType().Name}");
            return new();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            // Write then rename, so a crash can't leave a half-written file.
            var temporary = StatePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(State));
            File.Move(temporary, StatePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Settings could not be saved: {e.GetType().Name}");
        }
    }
}
