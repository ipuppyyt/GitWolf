using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GitWolf.Core;

// Ported from apple/Sources/GitWolfCore/ProviderClient.swift.

public sealed record RemoteRepository(
    string FullName, string Description, bool IsPrivate, string HttpsUrl, string SshUrl, DateTimeOffset? Updated)
{
    public string Id => FullName;
    public string Name => FullName.Split('/').LastOrDefault() ?? FullName;
}

public sealed class ProviderException(string message, int? status = null) : Exception(message)
{
    /// <summary>The HTTP status behind the error, when there was one.</summary>
    public int? Status { get; } = status;

    public bool IsUnauthorized => Status is 401 or 403;

    /// <summary>A change refused for lack of permission. Signing in again can fix it for a browser sign-in.</summary>
    public bool IsForbidden { get; private init; }

    public static ProviderException Forbidden(int code, string reason) =>
        new($"{reason.TrimEnd('.')}. This account may not be allowed to do that.", code) { IsForbidden = true };

    public static ProviderException Unauthorized =>
        new("The server rejected this token. Check that it is valid and has repository access.", 401);

    public static ProviderException Http(int code) => new($"The server returned an error (HTTP {code}).", code);

    public static ProviderException InvalidResponse =>
        new("The server sent a response GitWolf couldn’t understand.");

    public static ProviderException Unsupported(string what) => new(what);
}

/// <summary>A single HTTP round trip, abstracted so tests can stub it without a real network call.</summary>
public delegate Task<(byte[] Body, HttpStatusCode Status)> HttpTransport(HttpRequestMessage request);

/// <summary>Talks to a hosting provider's REST API to verify a token and list the repositories it can see.</summary>
public sealed partial class ProviderClient
{
    public ProviderAccount Account { get; }
    private readonly string _secret;
    private readonly HttpTransport _transport;

    // No redirects: custom auth headers (PRIVATE-TOKEN) would be forwarded to the redirect target.
    private static readonly HttpClient SharedHttpClient =
        new(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
        {
            Timeout = TimeSpan.FromSeconds(30),
            // A server can't make the app buffer without limit.
            MaxResponseContentBufferSize = 20 * 1024 * 1024,
        };

    public static readonly HttpTransport HttpClientTransport = async request =>
    {
        using var response = await SharedHttpClient.SendAsync(request);
        var body = await response.Content.ReadAsByteArrayAsync();
        return (body, response.StatusCode);
    };

    public ProviderClient(ProviderAccount account, string token, HttpTransport? transport = null)
    {
        Account = account;
        _secret = token;
        _transport = transport ?? HttpClientTransport;
    }

    // MARK: Endpoints

    private string ApiBase
    {
        get
        {
            var h = Account.Host;
            var o = Account.Origin;
            return Account.Kind switch
            {
                ProviderKind.GitHub when h == "github.com" => "https://api.github.com",
                // GitHub Enterprise Cloud with data residency: api.<tenant>.ghe.com
                ProviderKind.GitHub when h.EndsWith(".ghe.com", StringComparison.Ordinal) => $"https://api.{h}",
                ProviderKind.GitHub => $"{o}/api/v3",
                ProviderKind.GitLab => $"{o}/api/v4",
                ProviderKind.Bitbucket => h == "bitbucket.org" ? "https://api.bitbucket.org/2.0" : $"{o}/rest/api/1.0",
                ProviderKind.Gitea => $"{o}/api/v1",
                ProviderKind.Azure => o,
                _ => throw new ArgumentOutOfRangeException(),
            };
        }
    }

    private HttpRequestMessage Request(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) throw ProviderException.InvalidResponse;
        var r = new HttpRequestMessage(HttpMethod.Get, uri);
        r.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        r.Headers.UserAgent.Add(new ProductInfoHeaderValue("GitWolf", "1.0"));
        if (_secret.Length == 0) return r; // Public data without an account.

        if (Account.UsesOAuth)
        {
            r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _secret);
            if (Account.Kind == ProviderKind.GitHub) r.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            return r;
        }

        if (Account.UsesPassword == true)
        {
            r.Headers.Authorization = BasicAuth(Account.Username, _secret);
            return r;
        }

        switch (Account.Kind)
        {
            case ProviderKind.GitHub:
                r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _secret);
                r.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
                break;
            case ProviderKind.GitLab:
                r.Headers.Add("PRIVATE-TOKEN", _secret);
                break;
            case ProviderKind.Gitea:
                r.Headers.Authorization = new AuthenticationHeaderValue("token", _secret);
                break;
            case ProviderKind.Bitbucket when Account.Host != "bitbucket.org":
                // Bitbucket Data Center / Server: HTTP access tokens are Bearer tokens.
                r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _secret);
                break;
            case ProviderKind.Bitbucket:
            case ProviderKind.Azure:
                r.Headers.Authorization = BasicAuth(Account.Username, _secret);
                break;
        }
        return r;
    }

    private static AuthenticationHeaderValue BasicAuth(string username, string secret) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{secret}")));

    private Task<byte[]> FetchAsync(string url) => SendAsync(Request(url));

    /// <summary>Sends a request that changes something (POST, PUT, PATCH), with an optional JSON body.</summary>
    private Task<byte[]> SendAsync(HttpMethod method, string url, object? json = null)
    {
        var request = Request(url);
        request.Method = method;
        if (json is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(json), Encoding.UTF8, "application/json");
        return SendAsync(request);
    }

    private async Task<byte[]> SendAsync(HttpRequestMessage request)
    {
        var isRead = request.Method == HttpMethod.Get;
        byte[] body;
        HttpStatusCode status;
        try
        {
            using (request) (body, status) = await _transport(request);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            System.Diagnostics.Trace.TraceWarning($"A provider request failed: {e.GetType().Name}");
            throw new ProviderException($"GitWolf couldn’t reach {Account.HostName}. Check your connection and try again.");
        }
        var code = (int)status;
        if (code is >= 200 and < 300) return body;
        // A refused write says why, which is what the user needs: reading can work while changing isn't allowed.
        if (!isRead && code is 401 or 403 && ErrorMessage(body) is { } reason)
            throw ProviderException.Forbidden(code, reason);
        if (code == 401) throw ProviderException.Unauthorized;
        // Writes report the server's own reason; reads keep plain codes so callers can react to 404.
        if (!isRead && ErrorMessage(body) is { } message) throw new ProviderException(message, code);
        if (code == 403) throw ProviderException.Unauthorized;
        throw ProviderException.Http(code);
    }

    /// <summary>The human-readable reason in a provider's JSON error body.</summary>
    internal static string? ErrorMessage(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var parts = new List<string>();
            if (GetString(root, "message") is { } m) parts.Add(m);
            else if (GetString(root, "error") is { } e) parts.Add(e);
            else if (Property(root, "error") is { } nested && GetString(nested, "message") is { } n) parts.Add(n);
            // GitHub adds details such as "A pull request already exists for owner:branch".
            if (Property(root, "errors") is { ValueKind: JsonValueKind.Array } errors)
                parts.AddRange(errors.EnumerateArray().Select(x => GetString(x, "message")).OfType<string>());
            var text = string.Join(": ", parts);
            return text.Length == 0 ? null : text.Length > 600 ? text[..600] + "…" : text;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // MARK: Verify

    /// <summary>Returns the username the token belongs to; throws Unauthorized for a bad token.</summary>
    public async Task<string> CurrentUserAsync()
    {
        if (Account.Kind == ProviderKind.Azure && Account.UsesOAuth)
        {
            // The Visual Studio profile service identifies the signed-in Entra user.
            using var profile = ParseJson(await FetchAsync(
                "https://app.vssps.visualstudio.com/_apis/profile/profiles/me?api-version=7.1"));
            var who = GetString(profile.RootElement, "emailAddress") ?? GetString(profile.RootElement, "displayName");
            return string.IsNullOrEmpty(who) ? throw ProviderException.InvalidResponse : who;
        }
        if (!Account.Kind.SupportsRepositoryListing())
            throw ProviderException.Unsupported("Azure DevOps tokens can’t be verified here; they are used when you clone, fetch and push.");

        if (Account.Kind == ProviderKind.Bitbucket && Account.Host != "bitbucket.org")
        {
            // Data Center has no "current user" endpoint; listing one repository proves the token works.
            await FetchAsync(ApiBase + "/repos?limit=1");
            return Account.Username;
        }

        var data = await FetchAsync(ApiBase + "/user");
        using var doc = ParseJson(data);
        var root = doc.RootElement;
        string? name = Account.Kind switch
        {
            ProviderKind.GitHub or ProviderKind.Gitea => GetString(root, "login") ?? GetString(root, "username"),
            ProviderKind.GitLab => GetString(root, "username"),
            ProviderKind.Bitbucket => GetString(root, "username") ?? GetString(root, "nickname") ?? GetString(root, "display_name"),
            ProviderKind.Azure => null,
            _ => null,
        };
        if (string.IsNullOrEmpty(name)) throw ProviderException.InvalidResponse;
        return name;
    }

    // MARK: Repositories

    /// <summary>Repositories the token can access, most recently active first. Capped to keep it quick.</summary>
    public async Task<List<RemoteRepository>> RepositoriesAsync(int maxPages = 5)
    {
        switch (Account.Kind)
        {
            case ProviderKind.GitHub:
                return await PagedAsync(maxPages, 100,
                    page => $"{ApiBase}/user/repos?per_page=100&sort=pushed&affiliation=owner,collaborator,organization_member&page={page}",
                    ParseGitHub);
            case ProviderKind.GitLab:
                return await PagedAsync(maxPages, 100,
                    page => $"{ApiBase}/projects?membership=true&simple=true&per_page=100&order_by=last_activity_at&page={page}",
                    ParseGitLab);
            case ProviderKind.Gitea:
                return await PagedAsync(maxPages, 50,
                    page => $"{ApiBase}/user/repos?limit=50&page={page}",
                    ParseGitea);
            case ProviderKind.Bitbucket when Account.Host != "bitbucket.org":
            {
                var all = new List<RemoteRepository>();
                var start = 0;
                for (var i = 0; i < Math.Max(maxPages, 1); i++)
                {
                    var data = await FetchAsync($"{ApiBase}/repos?limit=100&start={start}");
                    all.AddRange(ParseBitbucketServer(data));
                    using var doc = ParseJson(data);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("isLastPage", out var lastEl) && lastEl.ValueKind == JsonValueKind.True) break;
                    if (!root.TryGetProperty("nextPageStart", out var nextEl) || nextEl.ValueKind != JsonValueKind.Number) break;
                    start = nextEl.GetInt32();
                }
                return all;
            }
            case ProviderKind.Bitbucket:
            {
                var all = new List<RemoteRepository>();
                string? next = $"{ApiBase}/repositories?role=member&pagelen=100&sort=-updated_on";
                var pages = 0;
                while (next is not null && pages < maxPages)
                {
                    // `next` comes from the response: never send the credential anywhere else.
                    if (!IsSameOrigin(next, ApiBase)) throw ProviderException.InvalidResponse;
                    var data = await FetchAsync(next);
                    all.AddRange(ParseBitbucket(data));
                    using var doc = ParseJson(data);
                    next = doc.RootElement.TryGetProperty("next", out var n) && n.ValueKind == JsonValueKind.String
                        ? n.GetString() : null;
                    pages++;
                }
                return all;
            }
            case ProviderKind.Azure:
                throw ProviderException.Unsupported("Listing repositories isn’t available for Azure DevOps. Paste a clone URL instead.");
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static bool IsSameOrigin(string url, string expected) =>
        Uri.TryCreate(url, UriKind.Absolute, out var a) && Uri.TryCreate(expected, UriKind.Absolute, out var b)
        && a.Scheme == b.Scheme && a.Port == b.Port && a.UserInfo.Length == 0
        && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase);

    private async Task<List<RemoteRepository>> PagedAsync(int maxPages, int perPage, Func<int, string> url, Func<byte[], List<RemoteRepository>> parse)
    {
        var result = new List<RemoteRepository>();
        for (var page = 1; page <= Math.Max(maxPages, 1); page++)
        {
            var data = await FetchAsync(url(page));
            var items = parse(data);
            result.AddRange(items);
            if (items.Count < perPage) break;
        }
        return result;
    }

    // MARK: Parsing (internal so tests can feed sample payloads)

    private static JsonDocument ParseJson(byte[] data)
    {
        try { return JsonDocument.Parse(data); }
        catch (JsonException) { throw ProviderException.InvalidResponse; }
    }

    private static string? GetString(JsonElement element, string property) =>
        Property(element, property) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    private static JsonElement? Property(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var v) ? v : null;

    private static bool GetBool(JsonElement element, string property, bool fallback) =>
        Property(element, property) is { ValueKind: JsonValueKind.True or JsonValueKind.False } v ? v.GetBoolean() : fallback;

    private static DateTimeOffset? Iso(string? s) =>
        s is not null && DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var d) ? d : null;

    private static List<JsonElement> ArrayOf(byte[] data)
    {
        using var doc = ParseJson(data);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw ProviderException.InvalidResponse;
        // Clone each element: the source document is disposed when this returns.
        return [.. doc.RootElement.EnumerateArray().Select(e => e.Clone())];
    }

    internal static List<RemoteRepository> ParseGitHub(byte[] data)
    {
        var result = new List<RemoteRepository>();
        foreach (var r in ArrayOf(data))
        {
            var name = GetString(r, "full_name");
            var https = GetString(r, "clone_url");
            if (name is null || https is null) continue;
            result.Add(new RemoteRepository(
                name, GetString(r, "description") ?? "", GetBool(r, "private", false), https,
                GetString(r, "ssh_url") ?? "", Iso(GetString(r, "pushed_at") ?? GetString(r, "updated_at"))));
        }
        return result;
    }

    internal static List<RemoteRepository> ParseGitLab(byte[] data)
    {
        var result = new List<RemoteRepository>();
        foreach (var r in ArrayOf(data))
        {
            var name = GetString(r, "path_with_namespace");
            var https = GetString(r, "http_url_to_repo");
            if (name is null || https is null) continue;
            result.Add(new RemoteRepository(
                name, GetString(r, "description") ?? "", (GetString(r, "visibility") ?? "private") != "public", https,
                GetString(r, "ssh_url_to_repo") ?? "", Iso(GetString(r, "last_activity_at"))));
        }
        return result;
    }

    internal static List<RemoteRepository> ParseGitea(byte[] data)
    {
        var result = new List<RemoteRepository>();
        foreach (var r in ArrayOf(data))
        {
            var name = GetString(r, "full_name");
            var https = GetString(r, "clone_url");
            if (name is null || https is null) continue;
            result.Add(new RemoteRepository(
                name, GetString(r, "description") ?? "", GetBool(r, "private", false), https,
                GetString(r, "ssh_url") ?? "", Iso(GetString(r, "updated_at"))));
        }
        return result;
    }

    private static string CloneLink(JsonElement r, string name)
    {
        if (!r.TryGetProperty("links", out var links) || !links.TryGetProperty("clone", out var clones)
            || clones.ValueKind != JsonValueKind.Array) return "";
        foreach (var c in clones.EnumerateArray())
            if (GetString(c, "name") == name) return GetString(c, "href") ?? "";
        return "";
    }

    private static string StripUserinfo(string httpsUrl)
    {
        if (!Uri.TryCreate(httpsUrl, UriKind.Absolute, out var url) || string.IsNullOrEmpty(url.UserInfo)) return httpsUrl;
        var builder = new UriBuilder(url) { UserName = "", Password = "" };
        return builder.Uri.ToString();
    }

    internal static List<RemoteRepository> ParseBitbucketServer(byte[] data)
    {
        using var doc = ParseJson(data);
        var root = doc.RootElement;
        if (!root.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array) throw ProviderException.InvalidResponse;

        var result = new List<RemoteRepository>();
        foreach (var r in values.EnumerateArray())
        {
            var slug = GetString(r, "slug");
            var project = r.TryGetProperty("project", out var p) ? GetString(p, "key") : null;
            if (slug is null || project is null) continue;
            var https = StripUserinfo(CloneLink(r, "http"));
            result.Add(new RemoteRepository(
                $"{project}/{slug}", GetString(r, "description") ?? "", !GetBool(r, "public", false), https,
                CloneLink(r, "ssh"), null));
        }
        return result;
    }

    internal static List<RemoteRepository> ParseBitbucket(byte[] data)
    {
        using var doc = ParseJson(data);
        var root = doc.RootElement;
        if (!root.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array) throw ProviderException.InvalidResponse;

        var result = new List<RemoteRepository>();
        foreach (var r in values.EnumerateArray())
        {
            var name = GetString(r, "full_name");
            if (name is null) continue;
            // Bitbucket puts the username into https clone URLs; strip it so our credential is used.
            var https = StripUserinfo(CloneLink(r, "https"));
            result.Add(new RemoteRepository(
                name, GetString(r, "description") ?? "", GetBool(r, "is_private", true), https,
                CloneLink(r, "ssh"), Iso(GetString(r, "updated_on"))));
        }
        return result;
    }
}
