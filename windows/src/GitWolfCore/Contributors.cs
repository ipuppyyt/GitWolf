using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GitWolf.Core;

/// <summary>A person who has contributed to GitWolf, as listed by GitHub's public contributors endpoint.</summary>
public sealed record Contributor(string Login, int Contributions = 0, Uri? Avatar = null)
{
    /// <summary>Built from the login, so nothing in a reply decides which page is opened.</summary>
    public Uri Profile => new($"https://github.com/{Login}");

    public bool IsMaintainer => Login.Equals(Contributors.Maintainer, StringComparison.OrdinalIgnoreCase);
}

/// <summary>The contributor list. Ported from Contributors.swift.</summary>
public static partial class Contributors
{
    public const string Repository = "ipuppyyt/GitWolf";

    /// <summary>The project's owner, always listed even when the lookup fails.</summary>
    public const string Maintainer = "ipuppyyt";

    public static readonly Uri ApiAddress = new($"https://api.github.com/repos/{Repository}/contributors?per_page=100");

    /// <summary>What is shown with no network and nothing cached.</summary>
    public static IReadOnlyList<Contributor> Fallback { get; } = [new Contributor(Maintainer)];

    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})$", RegexOptions.None, 500)]
    private static partial Regex Login();

    /// <summary>Reads GitHub's reply without bots, sorted by contributions then name.</summary>
    public static IReadOnlyList<Contributor> Parse(string json)
    {
        var people = new List<Contributor>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return Fallback;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || Text(item, "login") is not { } login) continue;
                // A bot's login ends in "[bot]", which the pattern refuses too.
                if (Text(item, "type") == "Bot" || !Login().IsMatch(login)) continue;
                var count = item.TryGetProperty("contributions", out var n) && n.ValueKind == JsonValueKind.Number
                    && n.TryGetInt32(out var value) ? Math.Max(0, value) : 0;
                people.Add(new Contributor(login, count, Picture(Text(item, "avatar_url"))));
            }
        }
        catch (JsonException)
        {
            return Fallback;
        }
        if (!people.Any(p => p.IsMaintainer)) people.AddRange(Fallback);
        return people.OrderByDescending(p => p.Contributions)
            .ThenBy(p => p.Login, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>Only GitHub's picture server is ever contacted for a picture.</summary>
    private static Uri? Picture(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps
            && url.IsDefaultPort && url.UserInfo.Length == 0
            && url.Host.Equals("avatars.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ? url : null;
}

/// <summary>
/// The list from GitHub, asked without credentials or cookies at most once a day. The last reply is kept in
/// <paramref name="cache"/> for offline use.
/// </summary>
public sealed class ContributorList(string cache, HttpMessageHandler? handler = null)
{
    private const int MaxBytes = 1024 * 1024;
    private static readonly TimeSpan Fresh = TimeSpan.FromDays(1);

    private readonly HttpClient _client = new(
        handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = MaxBytes,
    };

    /// <summary>What can be shown at once.</summary>
    public IReadOnlyList<Contributor> Cached()
    {
        try
        {
            var file = new FileInfo(cache);
            return file.Exists && file.Length <= MaxBytes
                ? Contributors.Parse(File.ReadAllText(cache)) : Contributors.Fallback;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Reading the contributor list failed: {e.GetType().Name}");
            return Contributors.Fallback;
        }
    }

    /// <summary>A newer list, or null when the cached one is recent or GitHub couldn't be asked.</summary>
    public async Task<IReadOnlyList<Contributor>?> RefreshAsync(CancellationToken token = default)
    {
        try
        {
            if (File.Exists(cache) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cache) < Fresh) return null;
            using var request = new HttpRequestMessage(HttpMethod.Get, Contributors.ApiAddress);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.UserAgent.ParseAdd("GitWolf");
            using var response = await _client.SendAsync(request, token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK) return null;
            var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            var people = Contributors.Parse(body);
            // A reply that couldn't be read doesn't replace a saved list.
            if (ReferenceEquals(people, Contributors.Fallback)) return null;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(cache))!);
            await File.WriteAllTextAsync(cache, body, token).ConfigureAwait(false);
            return people;
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException
            or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Updating the contributor list failed: {e.GetType().Name}");
            return null;
        }
    }
}
