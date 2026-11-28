using GitWolf.Core;
using Xunit;

namespace GitWolfCore.Tests;

public sealed class LaunchRequestTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("gitwolf launch & ").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static LaunchTarget? Parse(params string[] arguments) => LaunchRequest.Parse(arguments, _ => false);

    [Fact]
    public void ACommandLinePathOpensItsFolder()
    {
        var file = Path.Combine(_dir, "a.txt");
        File.WriteAllText(file, "x");

        Assert.Equal(new LaunchTarget(_dir, false), Parse(_dir));
        Assert.Equal(new LaunchTarget(_dir, false), Parse(file));
        Assert.Null(Parse(Path.Combine(_dir, "missing")));
        Assert.Null(Parse("--other"));
        Assert.Null(Parse(""));
        Assert.Null(Parse());
    }

    [Fact]
    public void ALinkRoundTrips() =>
        Assert.Equal(new LaunchTarget(_dir, true), Parse("--link", LaunchRequest.Link(_dir)));

    [Theory]
    [InlineData(@"\\server\share")]
    [InlineData("//server/share")]
    [InlineData(@"\\?\C:\Windows")]
    [InlineData(@"relative\folder")]
    [InlineData(@"\Windows")]
    [InlineData("C:")]
    public void ALinkToAnythingButALocalDriveFolderIsRefused(string path) =>
        Assert.Null(Parse("--link", LaunchRequest.Link(path)));

    [Fact]
    public void OtherLinksSplitLinksAndNetworkDrivesAreRefused()
    {
        var link = LaunchRequest.Link(_dir);

        Assert.Null(Parse("--link", link.Replace("gitwolf://", "https://")));
        Assert.Null(Parse("--link", link.Replace("//open", "//clone")));
        Assert.Null(Parse("--link", link, "--other"));
        Assert.Null(Parse("--link"));
        Assert.Null(LaunchRequest.Parse(["--link", link], _ => true));
    }
}
