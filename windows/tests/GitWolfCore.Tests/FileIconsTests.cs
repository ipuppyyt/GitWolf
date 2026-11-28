using GitWolf.Core;
using Xunit;

namespace GitWolfCore.Tests;

public sealed class FileIconsTests
{
    [Theory]
    [InlineData("src/App.tsx", "React")]
    [InlineData("a/b/Button.JSX", "React")]
    [InlineData("x.ts", "TypeScript")]
    [InlineData("types/index.d.ts", "TypeScript declarations")]
    [InlineData("main.swift", "Swift")]
    [InlineData("package.json", "npm")]
    [InlineData("data.json", "JSON")]
    [InlineData("Dockerfile", "Docker")]
    [InlineData("Dockerfile.dev", "Docker")]
    [InlineData(".env.local", "Environment")]
    [InlineData("LICENSE", "License")]
    [InlineData("docs/README.md", "Readme")]
    [InlineData("vendor/app.min.js", "Minified")]
    [InlineData("logo.PNG", "Image")]
    [InlineData("weird.zzz", "File")]
    [InlineData("noextension", "File")]
    [InlineData(".gitignore", "Git")]
    [InlineData(".key", "File")]
    [InlineData("", "File")]
    public void ChoosesTheFileIcon(string path, string name) => Assert.Equal(name, FileIcons.ForPath(path).Name);

    [Theory]
    [InlineData("x.ts", "typescript")]
    [InlineData("main.swift", "swift")]
    [InlineData("data.json", "json")]
    public void LanguagesUseTheirLogo(string path, string slug)
    {
        var icon = FileIcons.ForPath(path);
        Assert.Equal((IconGlyph.Logo, slug), (icon.Glyph, icon.Value));
    }

    [Theory]
    [InlineData("main", "Main branch")]
    [InlineData("Master", "Main branch")]
    [InlineData("develop", "Development")]
    [InlineData("feature/login-form", "Feature")]
    [InlineData("feat-login", "Feature")]
    [InlineData("fix/crash-on-start", "Bug fix")]
    [InlineData("hotfix/1.2.1", "Hotfix")]
    [InlineData("release/2.0", "Release")]
    [InlineData("dependabot/npm_and_yarn/x", "Dependencies")]
    [InlineData("123-fix-login", "Issue")]
    [InlineData("dhanush/experiment", "Branch")]
    [InlineData("some-random", "Branch")]
    [InlineData("", "Branch")]
    public void ChoosesTheBranchIcon(string branch, string name) =>
        Assert.Equal(name, FileIcons.BranchNamed(branch).Name);

    [Theory]
    [InlineData("v2.0.0", "Major release")]
    [InlineData("1.0", "Major release")]
    [InlineData("v1.4.2", "Release")]
    [InlineData("v0.3.0", "Release")]
    [InlineData("v1.5.0-rc.1", "Release candidate")]
    [InlineData("v1.5.0-beta.2", "Pre-release")]
    [InlineData("v3.0.0-alpha", "Pre-release")]
    [InlineData("nightly-2026-11-27", "Nightly")]
    [InlineData("latest", "Latest")]
    [InlineData("release-lts", "Long-term support")]
    [InlineData("deploy-prod-42", "Deploy")]
    [InlineData("old-backup", "Archive")]
    [InlineData("milestone", "Tag")]
    [InlineData("", "Tag")]
    public void ChoosesTheTagIcon(string tag, string name) =>
        Assert.Equal(name, FileIcons.TagNamed(tag).Name);

    [Fact]
    public void ALightweightTagIsOutlined()
    {
        Assert.Equal("tag.fill", FileIcons.TagNamed("milestone").Value);
        Assert.Equal("tag", FileIcons.TagNamed("milestone", annotated: false).Value);
    }

    [Theory]
    [InlineData("src/components/Button.tsx", "Components")]
    [InlineData("node_modules/react/lib/x.js", "Library")]
    [InlineData("node_modules/react/x.js", "Dependencies")]
    [InlineData(".github/workflows/ci.yml", "CI")]
    [InlineData("src/random/x.ts", "Folder")]
    [InlineData("README.md", null)]
    public void ChoosesTheFolderIcon(string path, string? name) =>
        Assert.Equal(name, FileIcons.FolderContaining(path)?.Name);

    [Fact]
    public void FoldersAreFoundByName()
    {
        Assert.Equal("Source", FileIcons.FolderNamed("src").Name);
        Assert.Equal("Docs", FileIcons.FolderNamed("Docs").Name);
        Assert.Equal("Dependencies", FileIcons.FolderNamed("node_modules").Name);
        Assert.Equal("CI", FileIcons.FolderNamed(".github").Name);
        var other = FileIcons.FolderNamed("randomthing");
        Assert.Equal((IconGlyph.Folder, (string?)null), (other.Glyph, other.Value));
    }

    [Fact]
    public void EveryIconIsWellFormed()
    {
        var all = new[]
        {
            FileIcons.Extensions, FileIcons.Names, FileIcons.Folders, FileIcons.BranchNames, FileIcons.BranchPrefixes,
        }.SelectMany(map => map.Values);

        Assert.All(all, icon =>
        {
            Assert.NotEmpty(icon.Name);
            Assert.True(icon.Color <= 0xFFFFFF);
            if (icon.Glyph == IconGlyph.Letters) Assert.InRange(icon.Value!.Length, 1, 4);
        });
    }
}
