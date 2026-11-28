namespace GitWolf.Core;

// Ported from apple/Sources/GitWolfCore/Providers.swift.

// MARK: - Provider kinds

/// <summary>
/// Hosting services GitWolf knows how to talk to. Listing repositories needs an API per kind; web
/// links work for all of them.
/// </summary>
public enum ProviderKind { GitHub, GitLab, Bitbucket, Gitea, Azure }

public static class ProviderKindExtensions
{
    public static string Id(this ProviderKind kind) => kind switch
    {
        ProviderKind.GitHub => "github",
        ProviderKind.GitLab => "gitlab",
        ProviderKind.Bitbucket => "bitbucket",
        ProviderKind.Gitea => "gitea",
        ProviderKind.Azure => "azure",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string Title(this ProviderKind kind) => kind switch
    {
        ProviderKind.GitHub => "GitHub",
        ProviderKind.GitLab => "GitLab",
        ProviderKind.Bitbucket => "Bitbucket",
        ProviderKind.Gitea => "Gitea / Forgejo",
        ProviderKind.Azure => "Azure DevOps",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string DefaultHost(this ProviderKind kind) => kind switch
    {
        ProviderKind.GitHub => "github.com",
        ProviderKind.GitLab => "gitlab.com",
        ProviderKind.Bitbucket => "bitbucket.org",
        ProviderKind.Gitea => "codeberg.org",
        ProviderKind.Azure => "dev.azure.com",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Whether repositories can be listed (and tokens verified) through an API.</summary>
    public static bool SupportsRepositoryListing(this ProviderKind kind) => kind != ProviderKind.Azure;

    /// <summary>Whether this kind needs a username next to the secret (Bitbucket app passwords).</summary>
    public static bool NeedsUsername(this ProviderKind kind) => kind == ProviderKind.Bitbucket;

    /// <summary>Where the user creates a token / app password for this provider.</summary>
    public static string? TokenPageUrl(this ProviderKind kind, string host) => kind switch
    {
        ProviderKind.GitHub => $"https://{host}/settings/tokens/new?scopes=repo&description=GitWolf",
        ProviderKind.GitLab => $"https://{host}/-/user_settings/personal_access_tokens?name=GitWolf&scopes=read_api,read_repository,write_repository",
        ProviderKind.Bitbucket => "https://bitbucket.org/account/settings/app-passwords/",
        ProviderKind.Gitea => $"https://{host}/user/settings/applications",
        ProviderKind.Azure => "https://dev.azure.com",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>What the secret is called in this provider's UI.</summary>
    public static string SecretName(this ProviderKind kind) => kind switch
    {
        ProviderKind.Bitbucket => "App password",
        ProviderKind.Gitea => "Access token",
        _ => "Personal access token",
    };

    /// <summary>Short hint on the scopes the secret needs.</summary>
    public static string ScopeHint(this ProviderKind kind) => kind switch
    {
        ProviderKind.GitHub => "Needs the “repo” scope.",
        ProviderKind.GitLab => "Needs read_api, read_repository and write_repository.",
        ProviderKind.Bitbucket => "Needs repository read and write permissions.",
        ProviderKind.Gitea => "Needs repository read and write permissions.",
        ProviderKind.Azure => "Needs Code (read & write). In Azure DevOps: User settings → Personal access tokens.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Best guess for a host that has no saved account.</summary>
    public static ProviderKind? Guess(string host)
    {
        var h = host.ToLowerInvariant();
        if (h == "github.com" || h.EndsWith(".github.com", StringComparison.Ordinal) || h.Contains("github")) return ProviderKind.GitHub;
        if (h == "gitlab.com" || h.Contains("gitlab")) return ProviderKind.GitLab;
        if (h == "bitbucket.org" || h.Contains("bitbucket")) return ProviderKind.Bitbucket;
        if (h == "dev.azure.com" || h.EndsWith(".visualstudio.com", StringComparison.Ordinal) || h == "ssh.dev.azure.com") return ProviderKind.Azure;
        if (h == "codeberg.org" || h.Contains("gitea") || h.Contains("forgejo")) return ProviderKind.Gitea;
        return null;
    }
}

public sealed class ProviderAccount
{
    public string Id { get; }
    public ProviderKind Kind { get; }
    public string Host { get; }
    public string Username { get; }
    /// <summary>True when the secret is an account password rather than an access token (sent as HTTP Basic).</summary>
    public bool? UsesPassword { get; }
    /// <summary>"http" for self-hosted servers without TLS; null means https.</summary>
    public string? Scheme { get; }

    /// <summary>Set when the account signed in through the browser; the secret is then a JSON <see cref="OAuthTokens"/>.</summary>
    public string? OAuthClientId { get; }

    public bool UsesOAuth => OAuthClientId is not null;

    public ProviderAccount(ProviderKind kind, string host, string username, bool usesPassword = false, string? scheme = null,
        string? id = null, string? oauthClientId = null)
    {
        OAuthClientId = string.IsNullOrEmpty(oauthClientId) ? null : oauthClientId;
        Id = id ?? Guid.NewGuid().ToString();
        Kind = kind;
        Host = host;
        Username = username;
        Scheme = scheme == "http" ? "http" : null;
        UsesPassword = usesPassword ? true : null;
    }

    /// <summary>Host without a port or path, which is what Git remotes are matched against.</summary>
    public string HostName
    {
        get
        {
            var h = Host;
            var slash = h.IndexOf('/');
            if (slash >= 0) h = h[..slash];
            var colon = h.IndexOf(':');
            if (colon >= 0) h = h[..colon];
            return h.ToLowerInvariant();
        }
    }

    public string Origin => $"{Scheme ?? "https"}://{Host}";

    public string Title => Host == Kind.DefaultHost() ? Kind.Title() : $"{Kind.Title()} · {Host}";

    public ProviderAccount With(string username) =>
        new(Kind, Host, username, UsesPassword == true, Scheme, Id, OAuthClientId);

    /// <summary>
    /// What the user typed as a server, reduced to `host[:port][/path]`, plus whether they asked for plain
    /// HTTP. Null when it isn't a server name, so a secret is never sent to an address built from junk.
    /// </summary>
    public static (string Host, string? Scheme)? CleanHost(string input)
    {
        var h = input.Trim();
        string? scheme = null;
        if (h.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) h = h[8..];
        else if (h.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) (h, scheme) = (h[7..], "http");
        h = h.TrimEnd('/');
        if (h.Length == 0 || h.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '?' or '#' or '\\' or '%' or '@'))
            return null;
        var slash = h.IndexOf('/');
        var authority = (slash < 0 ? h : h[..slash]).ToLowerInvariant();
        var path = slash < 0 ? "" : h[slash..];
        if (path.Split('/').Any(s => s is "." or "..")) return null;
        if (!Uri.TryCreate($"https://{authority}", UriKind.Absolute, out var uri) || uri.HostNameType == UriHostNameType.Unknown)
            return null;
        return (authority + path, scheme);
    }
}

/// <summary>A secret handed to git for one server. <paramref name="Host"/> includes a non-default port.</summary>
public sealed record GitCredential(string Host, string Username, string Token)
{
    public string Username { get; } = string.IsNullOrEmpty(Username) ? "git" : Username;

    public const string AskpassScriptFileName = "askpass.sh";

    /// <summary>
    /// `GIT_ASKPASS` helper: answers git's prompts from the environment, so the secret never
    /// appears in a URL, a command line or a config file. Git for Windows runs it with its bundled
    /// `sh`. Do not replace it with a `.cmd`: cmd.exe would execute metacharacters in the prompt,
    /// which contains the (untrusted) remote URL. Must be written with LF endings and no BOM.
    /// </summary>
    public const string AskpassScript =
        "#!/bin/sh\n" +
        // Prompts look like: Password for 'https://user@host:port/path':
        "url=${1#*\\'}\n" +
        "url=${url%\\'*}\n" +
        "authority=${url#*://}\n" +
        "authority=${authority%%/*}\n" +
        "actual=$(printf '%s://%s' \"${url%%://*}\" \"${authority##*@}\" | tr '[:upper:]' '[:lower:]')\n" +
        "expected=$(printf '%s' \"$GITWOLF_ASKPASS_ORIGIN\" | tr '[:upper:]' '[:lower:]')\n" +
        // Another remote, submodule, redirect or proxy gets nothing.
        "[ -n \"$expected\" ] && [ \"$actual\" = \"$expected\" ] || exit 1\n" +
        "case \"$1\" in\n" +
        "  \"Username for \"*) printf '%s\\n' \"$GITWOLF_ASKPASS_USER\" ;;\n" +
        "  \"Password for \"*) printf '%s\\n' \"$GITWOLF_ASKPASS_TOKEN\" ;;\n" +
        "  *) exit 1 ;;\n" +
        "esac\n";

    /// <summary>"http" only for accounts on self-hosted servers without TLS.</summary>
    public string Scheme { get; init; } = "https";

    /// <summary>
    /// Send the token as `Authorization: Bearer` (Microsoft Entra tokens for Azure DevOps) instead of
    /// answering git's password prompt.
    /// </summary>
    public bool UseBearerHeader { get; init; }

    /// <summary>The user name git should send with an OAuth access token.</summary>
    public static string OAuthUsername(ProviderKind kind, string fallback) => kind switch
    {
        ProviderKind.GitLab => "oauth2",
        ProviderKind.GitHub => "x-access-token",
        ProviderKind.Bitbucket => "x-token-auth",
        _ => fallback,
    };

    /// <summary>Never print the token, for example when a record is logged.</summary>
    public override string ToString() => $"GitCredential({Scheme}://{Host})";

    /// <summary>
    /// `host[:port]` of an http or https address, lowercase and without a default port. Null for
    /// anything else: ssh remotes never take a token.
    /// </summary>
    public static string? HttpAuthority(string url) =>
        Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) && u.Scheme is "http" or "https" && u.Host.Length > 0
            ? (u.IsDefaultPort ? u.Host : $"{u.Host}:{u.Port}").ToLowerInvariant()
            : null;

    /// <summary>Whether the address is the account's own server: same scheme, host and port.</summary>
    public bool Allows(string remoteUrl) =>
        Uri.TryCreate(remoteUrl.Trim(), UriKind.Absolute, out var url)
        && Uri.TryCreate($"{Scheme}://{Host}", UriKind.Absolute, out var own)
        && url.Scheme == own.Scheme && url.Port == own.Port
        && url.Host.Equals(own.Host, StringComparison.OrdinalIgnoreCase);

    /// <summary>The only `scheme://host[:port]` the secret may be sent to.</summary>
    private string AllowedOrigin(string remoteUrl)
    {
        if (!Allows(remoteUrl)) return $"{Scheme}://{Host}";
        // Raw text, not Uri.Authority: git keeps an explicit default port in its prompt.
        var authority = remoteUrl.Trim().Split("://", 2)[1].Split('/', 2)[0];
        return $"{Scheme}://{authority[(authority.LastIndexOf('@') + 1)..]}";
    }

    /// <summary>
    /// Environment that makes git use this credential for <paramref name="remoteUrl"/>'s server and
    /// nothing else (other helpers, such as a stale credential manager entry, are disabled for the
    /// call).
    /// </summary>
    public Dictionary<string, string> Environment(string askpassPath, string remoteUrl)
    {
        var origin = AllowedOrigin(remoteUrl);
        var environment = new Dictionary<string, string>
        {
            ["GIT_ASKPASS"] = askpassPath,
            ["GITWOLF_ASKPASS_ORIGIN"] = origin,
            ["GITWOLF_ASKPASS_USER"] = Username,
            ["GITWOLF_ASKPASS_TOKEN"] = Token,
        };
        AddConfig(environment, "credential.helper", "");
        // Passed through the environment, never the command line or a config file. Unlike macOS, the
        // header is scoped to this origin, so another remote, submodule or redirect never receives it.
        if (UseBearerHeader)
        {
            AddConfig(environment, $"http.{origin}/.extraheader", $"Authorization: Bearer {Token}");
            // Some git versions (2.43, for one) keep sending the header to wherever a redirect leads.
            AddConfig(environment, $"http.{origin}/.followRedirects", "false");
        }
        return environment;
    }

    /// <summary>Appends a git setting to the `GIT_CONFIG_*` variables.</summary>
    internal static void AddConfig(Dictionary<string, string> environment, string key, string value)
    {
        var count = environment.TryGetValue("GIT_CONFIG_COUNT", out var text) ? int.Parse(text) : 0;
        environment[$"GIT_CONFIG_KEY_{count}"] = key;
        environment[$"GIT_CONFIG_VALUE_{count}"] = value;
        environment["GIT_CONFIG_COUNT"] = (count + 1).ToString();
    }
}

// MARK: - Remote URLs

/// <summary>Where a remote lives: `github.com` + `owner/repo`.</summary>
public sealed record RemoteLocation(string Host, string Path)
{
    private static readonly HashSet<string> AllowedSchemes = ["http", "https", "ssh", "git", "git+ssh", "ssh+git"];

    /// <summary>Parses https, ssh:// and scp-like (`git@host:owner/repo.git`) remotes. Local paths return null.</summary>
    public static RemoteLocation? Parse(string remote)
    {
        var s = remote.Trim();
        if (s.Length == 0) return null;

        string host, path;
        var schemeIndex = s.IndexOf("://", StringComparison.Ordinal);
        if (schemeIndex >= 0)
        {
            var scheme = s[..schemeIndex].ToLowerInvariant();
            if (!AllowedSchemes.Contains(scheme)) return null;
            s = s[(schemeIndex + 3)..];
            var slash = s.IndexOf('/');
            if (slash < 0) return null;
            var authority = s[..slash];
            path = s[(slash + 1)..];
            var at = authority.LastIndexOf('@');
            if (at >= 0) authority = authority[(at + 1)..];
            var colon = authority.IndexOf(':');
            if (colon >= 0) authority = authority[..colon];
            host = authority;
        }
        else
        {
            var at = s.IndexOf('@');
            var colon = at >= 0 ? s.IndexOf(':', at) : -1;
            if (at < 0 || colon < 0) return null;
            host = s[(at + 1)..colon];
            path = s[(colon + 1)..];
        }

        while (path.EndsWith('/')) path = path[..^1];
        if (path.EndsWith(".git", StringComparison.Ordinal)) path = path[..^4];
        while (path.StartsWith('/')) path = path[1..];
        if (host.Length == 0 || path.Length == 0) return null;

        // Azure DevOps over ssh: ssh.dev.azure.com:v3/org/project/repo -> dev.azure.com/org/project/_git/repo
        if (host.Equals("ssh.dev.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 4 && parts[0] == "v3")
                return new RemoteLocation("dev.azure.com", $"{parts[1]}/{parts[2]}/_git/{parts[3]}");
        }
        return new RemoteLocation(host.ToLowerInvariant(), path);
    }
}

/// <summary>Deep links into a provider's web UI.</summary>
public sealed class WebLinks(ProviderKind? kind, RemoteLocation location)
{
    public ProviderKind? Kind { get; } = kind;
    public RemoteLocation Location { get; } = location;

    private string Base => $"https://{Location.Host}/{Location.Path}";

    private static string Enc(string s, bool allowSlash = true)
    {
        var encoded = Uri.EscapeDataString(s);
        return allowSlash ? encoded.Replace("%2F", "/") : encoded;
    }

    private static string Query(string s) => Uri.EscapeDataString(s);

    public Uri? Repository => Uri.TryCreate(Base, UriKind.Absolute, out var u) ? u : null;

    public Uri? Branch(string name)
    {
        var url = Kind switch
        {
            ProviderKind.GitHub => $"{Base}/tree/{Enc(name)}",
            ProviderKind.GitLab => $"{Base}/-/tree/{Enc(name)}",
            ProviderKind.Bitbucket => $"{Base}/src/{Enc(name)}",
            ProviderKind.Gitea => $"{Base}/src/branch/{Enc(name)}",
            ProviderKind.Azure => $"{Base}?version=GB{Query(name)}",
            null => null,
            _ => throw new ArgumentOutOfRangeException(),
        };
        return url is null ? Repository : Uri.TryCreate(url, UriKind.Absolute, out var u) ? u : null;
    }

    public Uri? Commit(string hash)
    {
        var url = Kind switch
        {
            ProviderKind.Bitbucket => $"{Base}/commits/{hash}",
            ProviderKind.GitLab => $"{Base}/-/commit/{hash}",
            _ => $"{Base}/commit/{hash}",
        };
        return Uri.TryCreate(url, UriKind.Absolute, out var u) ? u : null;
    }

    public Uri? NewPullRequest(string branch)
    {
        var url = Kind switch
        {
            ProviderKind.GitHub => $"{Base}/compare/{Enc(branch)}?expand=1",
            ProviderKind.GitLab => $"{Base}/-/merge_requests/new?merge_request%5Bsource_branch%5D={Query(branch)}",
            ProviderKind.Bitbucket => $"{Base}/pull-requests/new?source={Query(branch)}",
            ProviderKind.Gitea => $"{Base}/pulls",
            ProviderKind.Azure => $"{Base}/pullrequestcreate?sourceRef={Query(branch)}",
            null => null,
            _ => throw new ArgumentOutOfRangeException(),
        };
        return url is null ? Repository : Uri.TryCreate(url, UriKind.Absolute, out var u) ? u : null;
    }

    public string PullRequestsWord => Kind == ProviderKind.GitLab ? "Merge Request" : "Pull Request";
}
