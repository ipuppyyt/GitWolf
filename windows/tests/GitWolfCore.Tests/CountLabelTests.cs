using GitWolf.Core;
using Xunit;

namespace GitWolfCore.Tests;

public class CountLabelTests
{
    [Theory]
    [InlineData(0, "0 commits")]
    [InlineData(1, "1 commit")]
    [InlineData(2, "2 commits")]
    public void OnlyOneIsSingular(long count, string expected) =>
        Assert.Equal(expected, CountLabel.Text(count, "commit"));
}
