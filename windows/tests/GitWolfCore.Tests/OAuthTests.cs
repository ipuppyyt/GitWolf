using System.Net.Sockets;
using System.Text;
using GitWolf.Core;
using Xunit;

namespace GitWolfCore.Tests;

// Ported from OAuthTests and OAuthBitbucketAzureTests in apple/Tests/GitWolfCoreTests/GitWolfCoreTests.swift.
public sealed class OAuthTests
{
    private static OAuthClient MakeClient(
        ProviderKind kind, StubHttp http, string origin = "https://github.com", string? secret = null,
        List<int>? sleeps = null) =>
        new(kind, origin, "cid", secret, http.Send, (seconds, _) =>
        {
            sleeps?.Add(seconds);
            return Task.CompletedTask;
        });

    [Fact]
    public void PkceMatchesRfc7636Vector()
    {
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            Pkce.ChallengeFor("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
        var p = Pkce.Generate();
        Assert.True(p.Verifier.Length >= 43);
        Assert.Equal(p.Challenge, Pkce.ChallengeFor(p.Verifier));
        Assert.DoesNotContain(p.Challenge, c => c is '=' or '+' or '/');
    }

    [Fact]
    public void BuildsAuthorizationUrls()
    {
        var gitlab = MakeClient(ProviderKind.GitLab, new StubHttp(), "https://gitlab.acme.com");
        var url = gitlab.AuthorizationUrl(OAuthLoopback.RedirectUri, "s1", "ch")!.AbsoluteUri;
        Assert.StartsWith("https://gitlab.acme.com/oauth/authorize?", url);
        foreach (var part in new[]
        {
            "client_id=cid", "code_challenge=ch", "code_challenge_method=S256", "state=s1", "response_type=code",
            "scope=read_api",
        })
            Assert.Contains(part, url);
        var gitea = MakeClient(ProviderKind.Gitea, new StubHttp(), "https://codeberg.org");
        Assert.StartsWith("https://codeberg.org/login/oauth/authorize?",
            gitea.AuthorizationUrl("r", "s", "c")!.AbsoluteUri);
    }

    [Fact]
    public async Task ExchangesCodeAndRefreshes()
    {
        var http = StubHttp.Sequence(
            """{"access_token":"a1","refresh_token":"r1","expires_in":7200}""",
            """{"access_token":"a2","expires_in":7200}""");
        var client = MakeClient(ProviderKind.GitLab, http, "https://gitlab.com");
        var tokens = await client.ExchangeAsync("the code", "ver", OAuthLoopback.RedirectUri);
        Assert.Equal(("a1", "r1"), (tokens.AccessToken, tokens.RefreshToken));
        Assert.NotNull(tokens.ExpiresAt);
        Assert.False(tokens.NeedsRefresh());
        var body = http.Calls[0].Body;
        Assert.Contains("grant_type=authorization_code", body);
        Assert.Contains("code=the%20code", body);
        Assert.Contains("code_verifier=ver", body);
        Assert.Equal("https://gitlab.com/oauth/token", http.Calls[0].Url);

        var past = DateTimeOffset.UtcNow.AddSeconds(-5);
        var fresh = await client.RefreshAsync(new OAuthTokens("a1", "r1", past));
        // The refresh token is kept when the server doesn't rotate it.
        Assert.Equal(("a2", "r1"), (fresh.AccessToken, fresh.RefreshToken));
        Assert.True(new OAuthTokens("x", ExpiresAt: past).NeedsRefresh());
        Assert.False(new OAuthTokens("x").NeedsRefresh());
    }

    [Fact]
    public async Task DeviceFlowPollsUntilApproved()
    {
        var http = StubHttp.Sequence(
            """{"device_code":"dc","user_code":"ABCD-1234","verification_uri":"https://github.com/login/device","interval":5,"expires_in":900}""",
            """{"error":"authorization_pending"}""",
            """{"error":"slow_down"}""",
            """{"access_token":"gho_x","token_type":"bearer"}""");
        var sleeps = new List<int>();
        var client = MakeClient(ProviderKind.GitHub, http, sleeps: sleeps);
        var device = await client.StartDeviceFlowAsync();
        Assert.Equal(("ABCD-1234", 5), (device.UserCode, device.Interval));
        var tokens = await client.PollDeviceFlowAsync(device);
        Assert.Equal("gho_x", tokens.AccessToken);
        Assert.Null(tokens.ExpiresAt);
        // slow_down adds 5 seconds.
        Assert.Equal([5, 5, 10], sleeps);
        Assert.Equal("https://github.com/login/oauth/access_token", http.Calls[1].Url);
    }

    [Fact]
    public async Task DeviceFlowReportsDenialAndExpiry()
    {
        var device = new DeviceCode("d", "u", new Uri("https://x"), 1, 30);
        var denied = MakeClient(ProviderKind.GitHub, new StubHttp("""{"error":"access_denied"}"""));
        var error = await Assert.ThrowsAsync<OAuthException>(() => denied.PollDeviceFlowAsync(device));
        Assert.Equal(OAuthException.Denied.Message, error.Message);
        var expired = MakeClient(ProviderKind.GitHub, new StubHttp("""{"error":"expired_token"}"""));
        error = await Assert.ThrowsAsync<OAuthException>(() => expired.PollDeviceFlowAsync(device));
        Assert.Equal(OAuthException.Expired.Message, error.Message);
    }

    [Fact]
    public async Task DeviceFlowRefusesNonWebVerificationAddress()
    {
        var client = MakeClient(ProviderKind.GitHub, new StubHttp(
            """{"device_code":"d","user_code":"u","verification_uri":"file:///c:/windows/system32/calc.exe"}"""));
        await Assert.ThrowsAsync<OAuthException>(() => client.StartDeviceFlowAsync());
    }

    [Fact]
    public async Task OAuthAccountsUseBearerAndProviderFlows()
    {
        Assert.Equal(OAuthFlow.Device, ProviderKind.GitHub.Flow());
        foreach (var kind in new[] { ProviderKind.GitLab, ProviderKind.Bitbucket, ProviderKind.Azure })
            Assert.Equal(OAuthFlow.Pkce, kind.Flow());
        Assert.Equal("oauth2", GitCredential.OAuthUsername(ProviderKind.GitLab, "me"));
        var account = new ProviderAccount(ProviderKind.GitLab, "gitlab.com", "me", oauthClientId: "cid");
        Assert.True(account.UsesOAuth);
        var http = new StubHttp("[]");
        await new ProviderClient(account, "tok", http.Send).RepositoriesAsync();
        Assert.Equal("Bearer tok", http.Calls[0].Header("Authorization"));
        Assert.Null(http.Calls[0].Header("PRIVATE-TOKEN"));
    }

    [Fact]
    public async Task BitbucketUsesClientSecretWithoutPkce()
    {
        var http = new StubHttp("""{"access_token":"bb","refresh_token":"br","expires_in":7200}""");
        var client = MakeClient(ProviderKind.Bitbucket, http, "https://bitbucket.org", secret: "shh");
        var url = client.AuthorizationUrl(OAuthLoopback.RedirectUri, "s", "ch")!.AbsoluteUri;
        Assert.StartsWith("https://bitbucket.org/site/oauth2/authorize?", url);
        Assert.DoesNotContain("code_challenge", url);
        var tokens = await client.ExchangeAsync("c", "v", OAuthLoopback.RedirectUri);
        Assert.Equal(("bb", "shh"), (tokens.AccessToken, tokens.ClientSecret));
        var call = http.Calls[0];
        Assert.Equal("https://bitbucket.org/site/oauth2/access_token", call.Url);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("cid:shh")), call.Header("Authorization"));
        Assert.DoesNotContain("code_verifier", call.Body);
        Assert.Contains("grant_type=authorization_code", call.Body);
        Assert.Equal("shh", (await client.RefreshAsync(tokens)).ClientSecret);
        Assert.Equal("x-token-auth", GitCredential.OAuthUsername(ProviderKind.Bitbucket, "me"));
        Assert.True(ProviderKind.Bitbucket.SupportsOAuth("bitbucket.org"));
        Assert.False(ProviderKind.Bitbucket.SupportsOAuth("bb.acme.com"));
    }

    [Fact]
    public async Task AzureUsesEntraPkceWithScope()
    {
        var http = new StubHttp("""{"access_token":"az","refresh_token":"zr","expires_in":3600}""");
        var client = MakeClient(ProviderKind.Azure, http, "https://dev.azure.com");
        var url = client.AuthorizationUrl(OAuthLoopback.RedirectUri, "s", "ch")!.AbsoluteUri;
        Assert.StartsWith("https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize?", url);
        Assert.Contains("code_challenge=ch", url);
        Assert.Contains("user_impersonation", url);
        Assert.Contains("offline_access", url);
        await client.ExchangeAsync("c", "v", OAuthLoopback.RedirectUri);
        Assert.Contains("code_verifier=v", http.Calls[0].Body);
        Assert.Contains("scope=", http.Calls[0].Body);
        Assert.Null(http.Calls[0].Header("Authorization"));
    }

    [Fact]
    public void BearerHeaderIsScopedAndStaysOffTheCommandLine()
    {
        var credential = new GitCredential("dev.azure.com", "u", "tok") { UseBearerHeader = true };
        var env = credential.Environment("/x", "https://dev.azure.com/o/p/_git/r");
        Assert.Equal("3", env["GIT_CONFIG_COUNT"]);
        Assert.Equal("http.https://dev.azure.com/.followRedirects", env["GIT_CONFIG_KEY_2"]);
        Assert.Equal("false", env["GIT_CONFIG_VALUE_2"]);
        // The header is tied to the origin, so no other server receives it.
        Assert.Equal("http.https://dev.azure.com/.extraheader", env["GIT_CONFIG_KEY_1"]);
        Assert.Equal("Authorization: Bearer tok", env["GIT_CONFIG_VALUE_1"]);
        Assert.Equal("1", new GitCredential("h", "u", "t").Environment("/x", "https://h/r")["GIT_CONFIG_COUNT"]);
        Assert.DoesNotContain("tok", credential.ToString());
    }

    [Fact]
    public async Task AzureProfileVerifiesSignedInUser()
    {
        var http = new StubHttp("""{"displayName":"Dee","emailAddress":"dee@acme.com"}""");
        var account = new ProviderAccount(ProviderKind.Azure, "dev.azure.com", "", oauthClientId: "cid");
        Assert.Equal("dee@acme.com", await new ProviderClient(account, "az", http.Send).CurrentUserAsync());
        Assert.Equal("Bearer az", http.Calls[0].Header("Authorization"));
        Assert.StartsWith("https://app.vssps.visualstudio.com/", http.Calls[0].Url);
    }

    [Fact]
    public async Task LoopbackAcceptsOnlyTheMatchingState()
    {
        using var loopback = new OAuthLoopback(0);
        var waiting = loopback.WaitForCodeAsync("good", timeout: TimeSpan.FromSeconds(20));
        Assert.StartsWith("HTTP/1.1 404", await GetAsync(loopback.Port, "/other?state=good&code=x"));
        Assert.StartsWith("HTTP/1.1 400", await GetAsync(loopback.Port, "/callback?state=bad&code=stolen"));
        Assert.False(waiting.IsCompleted);
        var page = await GetAsync(loopback.Port, "/callback?state=good&code=a%2Fb");
        Assert.StartsWith("HTTP/1.1 200", page);
        Assert.DoesNotContain("a/b", page);
        Assert.Equal("a/b", await waiting);
    }

    [Fact]
    public async Task LoopbackReportsDenialAndTimeout()
    {
        using var loopback = new OAuthLoopback(0);
        var waiting = loopback.WaitForCodeAsync("s", timeout: TimeSpan.FromSeconds(20));
        await GetAsync(loopback.Port, "/callback?state=s&error=access_denied");
        var error = await Assert.ThrowsAsync<OAuthException>(() => waiting);
        Assert.Equal(OAuthException.Denied.Message, error.Message);

        using var silent = new OAuthLoopback(0);
        error = await Assert.ThrowsAsync<OAuthException>(
            () => silent.WaitForCodeAsync("s", timeout: TimeSpan.FromMilliseconds(100)));
        Assert.Equal(OAuthException.Expired.Message, error.Message);
    }

    private static async Task<string> GetAsync(int port, string target)
    {
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"GET {target} HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n"));
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }
}
