using GitWolf.Core;
using Xunit;

namespace GitWolfCore.Tests;

public sealed class BadgeLabelTests
{
    [Theory]
    [InlineData(-1, null)]
    [InlineData(0, null)]
    [InlineData(1, "1")]
    [InlineData(99, "99")]
    [InlineData(100, "99+")]
    public void ShowsCountsUpToNinetyNine(int changes, string? text) =>
        Assert.Equal(text, BadgeLabel.Text(changes));
}
