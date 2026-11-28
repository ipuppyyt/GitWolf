using System.Net;
using System.Net.Sockets;
using System.Text;
using GitWolf.Core;
using Xunit;

namespace GitWolfCore.Tests;

/// <summary>
/// What a repository's own configuration may run or redirect. Real git throughout: the marker files
/// appear only when git actually ran the configured command.
/// </summary>
public sealed class TrustTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("gitwolf-trust").FullName;
    private readonly string _work;
    private readonly string _askpass;

    public TrustTests()
    {
        _work = Path.Combine(_dir, "work");
        _askpass = AccountStore.WriteAskpass(_dir)!;
    }

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best effort: the folder is temporary.
        }
    }

    /// <summary>A shell command that records it ran. Forward slashes: git runs it through `sh`.</summary>
    private string Touch(string marker) => $"echo ran > '{Path.Combine(_dir, marker).Replace('\\', '/')}'";

    private bool Ran(string marker) => File.Exists(Path.Combine(_dir, marker));

    private static async Task<GitRunner> InitAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var git = new GitRunner(directory);
        await git.RunAsync(["init", "-b", "main"]);
        await git.RunAsync(["config", "user.name", "Tester"]);
        await git.RunAsync(["config", "user.email", "t@example.com"]);
        File.WriteAllText(Path.Combine(directory, "a.txt"), "one\n");
        await git.RunAsync(["add", "a.txt"]);
        await git.RunAsync(["commit", "-m", "base"]);
        return git;
    }

    private GitRepository Guarded() => new(_work) { Trust = new RepositoryTrust(Path.Combine(_dir, "trusted.json")) };

    [Fact]
    public async Task AnOrdinaryRepositoryNeedsNoTrust()
    {
        var git = await InitAsync(_work);
        await git.RunAsync(["remote", "add", "origin", "https://example.invalid/o/r.git"]);
        await git.RunAsync(["config", "core.fsmonitor", "false"]);
        await git.RunAsync(["config", "http.postBuffer", "1048576"]);

        Assert.Single((await Guarded().SnapshotAsync(10)).Commits);
    }

    [Fact]
    public async Task HooksNeedTrustAndAChangedHookAsksAgain()
    {
        await InitAsync(_work);
        var repo = Guarded();
        await repo.SnapshotAsync(10);
        var hook = Path.Combine(_work, ".git", "hooks", "post-checkout");
        File.WriteAllText(hook, $"#!/bin/sh\n{Touch("ran")}\n");

        var error = await Assert.ThrowsAsync<UntrustedRepositoryException>(
            () => repo.AddWorktreeAsync(Path.Combine(_dir, "other"), "side", newBranch: true));
        Assert.Equal("hooks/post-checkout", Assert.Single(error.Risk.Settings).Key);
        Assert.False(Ran("ran"));

        var trust = new RepositoryTrust(Path.Combine(_dir, "trusted.json"));
        trust.Trust(_work, error.Risk.Fingerprint);
        var trusted = new GitRepository(_work) { Trust = trust };
        await trusted.SnapshotAsync(10);

        File.WriteAllText(hook, "#!/bin/sh\nexit 0\n# changed\n");
        await Assert.ThrowsAsync<UntrustedRepositoryException>(() => trusted.SnapshotAsync(10));
    }

    [Fact]
    public async Task AHooksFolderSetByTheRepositoryNeedsTrust()
    {
        var git = await InitAsync(_work);
        await git.RunAsync(["config", "core.hooksPath", "scripts"]);

        await Assert.ThrowsAsync<UntrustedRepositoryException>(() => Guarded().SnapshotAsync(10));
    }

    [Fact]
    public async Task AChangedScriptInTheHooksFolderAsksAgain()
    {
        var git = await InitAsync(_work);
        await git.RunAsync(["config", "core.hooksPath", "scripts"]);
        Directory.CreateDirectory(Path.Combine(_work, "scripts"));
        var hook = Path.Combine(_work, "scripts", "pre-commit");
        File.WriteAllText(hook, "#!/bin/sh\nexit 0\n");
        var trust = new RepositoryTrust(Path.Combine(_dir, "trusted.json"));
        var repo = new GitRepository(_work) { Trust = trust };
        var error = await Assert.ThrowsAsync<UntrustedRepositoryException>(() => repo.SnapshotAsync(10));
        Assert.Contains(error.Risk.Settings, s => s is { Source: "core.hooksPath", Key: "hooks/pre-commit" });
        trust.Trust(_work, error.Risk.Fingerprint);
        await repo.SnapshotAsync(10);

        File.WriteAllText(hook, "#!/bin/sh\nexit 1\n# changed\n");
        await Assert.ThrowsAsync<UntrustedRepositoryException>(() => repo.SnapshotAsync(10));
    }

    [Theory]
    [InlineData("core.fsmonitor")]
    [InlineData("filter.x.clean")]
    [InlineData("diff.external")]
    [InlineData("gpg.program")]
    [InlineData("remote.origin.uploadpack")]
    public async Task NothingRunsUntilTheRepositoryIsTrusted(string key)
    {
        var git = await InitAsync(_work);
        await git.RunAsync(["remote", "add", "origin", _dir]);
        File.WriteAllText(Path.Combine(_work, ".gitattributes"), "* filter=x\n");
        File.WriteAllText(Path.Combine(_work, "a.txt"), "two\n");
        await git.RunAsync(["config", key, Touch("ran")]);
        var repo = Guarded();

        var error = await Assert.ThrowsAsync<UntrustedRepositoryException>(() => repo.SnapshotAsync(10));
        await Assert.ThrowsAsync<UntrustedRepositoryException>(repo.FetchAsync);
        await Assert.ThrowsAsync<UntrustedRepositoryException>(
            () => repo.DiffAsync(new DiffTarget(DiffSource.Unstaged, "a.txt", null)));

        Assert.False(Ran("ran"));
        Assert.Contains(error.Risk.Settings, s => s.Key == key);
    }

    [Fact]
    public async Task TrustIsRememberedUntilTheSettingsChange()
    {
        var git = await InitAsync(_work);
        await git.RunAsync(["config", "core.fsmonitor", Touch("first") + "; false"]);
        var repo = Guarded();
        var risk = (await Assert.ThrowsAsync<UntrustedRepositoryException>(repo.StatusAsync)).Risk;

        repo.Trust!.Trust(_work, risk.Fingerprint);
        await repo.StatusAsync();
        Assert.True(Ran("first"));
        await Guarded().StatusAsync(); // Saved to disk.

        // Changed while open, through an included file.
        var included = Path.Combine(_work, ".git", "extra");
        await git.RunAsync(["config", "include.path", "extra"]);
        await repo.StatusAsync();
        File.WriteAllText(included, $"[core]\n\tsshCommand = {Touch("ssh").Replace("'", "")}\n");
        await Assert.ThrowsAsync<UntrustedRepositoryException>(repo.StatusAsync);
    }

    [Fact]
    public async Task SubmoduleSettingsNeedTrustToo()
    {
        var library = Path.Combine(_dir, "library");
        await InitAsync(library);
        var git = await InitAsync(_work);
        await git.RunAsync(["-c", "protocol.file.allow=always", "submodule", "add", library, "lib"]);
        await git.RunAsync(["commit", "-m", "lib"]);
        var repo = Guarded();
        await repo.StatusAsync();

        await new GitRunner(Path.Combine(_work, "lib")).RunAsync(["config", "core.fsmonitor", Touch("sub") + "; false"]);
        // The parent's index is unchanged, so only the submodule's own file gives it away.
        await Assert.ThrowsAsync<UntrustedRepositoryException>(repo.StatusAsync);
        Assert.False(Ran("sub"));
    }

    [Fact]
    public async Task UntrackedDiffRunsNoExternalTool()
    {
        var git = await InitAsync(_work);
        File.WriteAllText(Path.Combine(_work, "new.txt"), "new\n");
        await git.RunAsync(["config", "diff.external", Touch("external")]);

        await new GitRepository(_work).DiffAsync(new DiffTarget(DiffSource.Untracked, "new.txt", null));

        Assert.False(Ran("external"));
    }

    [Fact]
    public async Task ExtRemotesNeverRunEvenWhenTheRepositoryAllowsThem()
    {
        var git = await InitAsync(_work);
        await git.RunAsync(["config", "protocol.ext.allow", "always"]);
        await git.RunAsync(["remote", "add", "origin", $"ext::sh -c touch% '{Path.Combine(_dir, "ext").Replace('\\', '/')}'"]);

        await Assert.ThrowsAsync<GitException>(new GitRepository(_work).FetchAsync);

        Assert.False(Ran("ext"));
    }

    [Fact]
    public async Task OnlyTheAccountsRemoteProcessGetsTheToken()
    {
        var upstream = Path.Combine(_dir, "upstream");
        await InitAsync(upstream);
        var git = await InitAsync(_work);
        await git.RunAsync(["remote", "add", "origin", "https://127.0.0.1:1/o/r.git"]);
        await git.RunAsync(["remote", "add", "local", upstream]);
        // A command configured for another remote records what it can see.
        var seen = Path.Combine(_dir, "seen").Replace('\\', '/');
        await git.RunAsync(["config", "remote.local.uploadpack",
            $"echo \"token=$GITWOLF_ASKPASS_TOKEN\" > '{seen}'; git-upload-pack"]);
        var asked = new List<string>();
        var repo = new GitRepository(_work)
        {
            Credentials = authority =>
            {
                asked.Add(authority);
                return Task.FromResult<GitCredential?>(new GitCredential(authority, "me", "FAKE-TOKEN"));
            },
            AskpassPath = _askpass,
        };

        await Assert.ThrowsAsync<GitException>(repo.FetchAsync); // origin has nothing listening

        Assert.Equal("token=", File.ReadAllText(Path.Combine(_dir, "seen")).Trim());
        Assert.Equal(["127.0.0.1:1"], asked);
        // The failing remote didn't stop the other.
        Assert.Contains((await repo.BranchesAsync()).Remote, b => b.Name == "local/main");
    }

    [Theory]
    [InlineData("http.proxy")]
    [InlineData("http.sslVerify")]
    [InlineData("http.http://127.0.0.1/.sslCAInfo")]
    [InlineData("remote.origin.proxy")]
    public async Task HttpSettingsWithholdTheToken(string key)
    {
        // Answers every request with a sign-in challenge and records any Authorization header.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var requests = new StringBuilder();
        using var stop = new CancellationTokenSource();
        var server = ChallengeAsync(listener, requests, stop.Token);

        var git = await InitAsync(_work);
        await git.RunAsync(["remote", "add", "origin", $"http://127.0.0.1:{port}/o/r.git"]);
        await git.RunAsync(["config", "credential.helper", ""]);
        var repo = new GitRepository(_work)
        {
            Credentials = authority => Task.FromResult<GitCredential?>(
                new GitCredential(authority, "me", "FAKE-TOKEN") { Scheme = "http" }),
            AskpassPath = _askpass,
        };
        var token = Convert.ToBase64String("me:FAKE-TOKEN"u8.ToArray());

        // Without the setting the token is sent, which proves the test can see it.
        await Assert.ThrowsAsync<GitException>(repo.FetchAsync);
        Assert.Contains(token, requests.ToString());
        requests.Clear();

        var value = key.EndsWith("proxy", StringComparison.Ordinal) ? $"http://127.0.0.1:{port}"
            : key.EndsWith("sslVerify", StringComparison.Ordinal) ? "false" : "missing.pem";
        await git.RunAsync(["config", key, value]);
        var error = await Assert.ThrowsAsync<GitException>(repo.FetchAsync);

        Assert.DoesNotContain("Authorization", requests.ToString());
        Assert.Contains("didn’t use your saved account", error.Message);
        await stop.CancelAsync();
        await server;
    }

    [Fact]
    public async Task ARedirectNeverCarriesTheBearerHeaderElsewhere()
    {
        using var own = new TcpListener(IPAddress.Loopback, 0);
        using var other = new TcpListener(IPAddress.Loopback, 0);
        own.Start();
        other.Start();
        var ownPort = ((IPEndPoint)own.LocalEndpoint).Port;
        var ownRequests = new StringBuilder();
        var otherRequests = new StringBuilder();
        using var stop = new CancellationTokenSource();
        var servers = Task.WhenAll(
            ChallengeAsync(own, ownRequests, stop.Token, $"http://127.0.0.1:{((IPEndPoint)other.LocalEndpoint).Port}"),
            ChallengeAsync(other, otherRequests, stop.Token));

        var git = await InitAsync(_work);
        await git.RunAsync(["remote", "add", "origin", $"http://127.0.0.1:{ownPort}/o/r.git"]);
        var repo = new GitRepository(_work)
        {
            Credentials = authority => Task.FromResult<GitCredential?>(
                new GitCredential(authority, "me", "FAKE-ENTRA") { Scheme = "http", UseBearerHeader = true }),
            AskpassPath = _askpass,
        };

        await Assert.ThrowsAsync<GitException>(repo.FetchAsync);
        await stop.CancelAsync();
        await servers;

        Assert.Contains("Bearer FAKE-ENTRA", ownRequests.ToString());
        Assert.DoesNotContain("FAKE-ENTRA", otherRequests.ToString());
    }

    /// <param name="redirectTo">Answer with a redirect to this server instead of a sign-in challenge.</param>
    private static async Task ChallengeAsync(
        TcpListener listener, StringBuilder requests, CancellationToken token, string? redirectTo = null)
    {
        try
        {
            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync(token);
                var stream = client.GetStream();
                var buffer = new byte[8192];
                var length = 0;
                int read;
                while (length < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(length), token)) > 0)
                {
                    length += read;
                    if (Encoding.ASCII.GetString(buffer, 0, length).Contains("\r\n\r\n")) break;
                }
                var request = Encoding.ASCII.GetString(buffer, 0, length);
                lock (requests) requests.Append(request);
                var status = redirectTo is null
                    ? "401 Unauthorized\r\nWWW-Authenticate: Basic realm=\"t\""
                    : $"302 Found\r\nLocation: {redirectTo}{request.Split(' ').ElementAtOrDefault(1)}";
                var reply = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(reply, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The test is over.
        }
    }

    [Fact]
    public async Task ServerLinksOverPlainHttpNeedAPlainHttpAccount()
    {
        const string reply = """[{"number":1,"title":"t","state":"open","html_url":"http://elsewhere.example/1"}]""";
        var items = await new StubHttp(reply).Client(ProviderKind.GitHub)
            .CollabAsync(CollabKind.PullRequest, "o/r", CollabFilter.Open, 1);
        Assert.Null(Assert.Single(items).Url);
    }
}
