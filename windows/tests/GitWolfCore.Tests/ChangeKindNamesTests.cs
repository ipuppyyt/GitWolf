using GitWolf.Core;
using Xunit;

namespace GitWolfCore.Tests;

public sealed class ChangeKindNamesTests
{
    [Fact]
    public void EveryKindHasItsOwnSpokenName()
    {
        var names = Enum.GetValues<ChangeKind>().Select(k => k.SpokenName()).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Equal("New file", ChangeKind.Untracked.SpokenName());
    }
}
