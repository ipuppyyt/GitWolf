using System.Text.Json;
using GitWolf.Core;
using Xunit;

namespace GitWolfCore.Tests;

public class HomeLayoutTests
{
    [Fact]
    public void PinAndGroups()
    {
        var layout = new HomeLayout();
        layout.TogglePin("/a");
        layout.TogglePin("/b");
        layout.TogglePin("/A");
        Assert.Equal(["/b"], layout.Pinned);

        Assert.Null(layout.AddGroup("   "));
        var work = Assert.NotNull(layout.AddGroup(" Work "));
        Assert.Equal("Work", layout.Groups[0].Name);
        layout.ToggleInGroup("/b", work);
        layout.ToggleInGroup("/c", work);
        layout.ToggleInGroup("/c", work);
        Assert.Equal(["/b"], layout.Groups[0].Paths);
        layout.RenameGroup(work, "Day job");
        Assert.Equal("Day job", layout.Groups[0].Name);

        layout.Remove("/b");
        Assert.Empty(layout.Pinned);
        Assert.Empty(layout.Groups[0].Paths);
        layout.DeleteGroup(work);
        Assert.Empty(layout.Groups);
    }

    [Fact]
    public void SavedAndPruned()
    {
        var layout = new HomeLayout();
        layout.TogglePin("/keep");
        layout.TogglePin("/gone");
        layout.AddGroup("G", "/keep");
        var read = HomeLayout.Sanitized(JsonSerializer.Deserialize<HomeLayout>(JsonSerializer.Serialize(layout)));
        Assert.Equal(["/keep", "/gone"], read.Pinned);
        Assert.Equal(["/keep"], read.Pruned(p => p == "/keep").Pinned);
        Assert.Equal(["/keep"], read.Groups[0].Paths);
    }

    [Fact]
    public void DamagedEntriesAreDropped()
    {
        var read = HomeLayout.Sanitized(JsonSerializer.Deserialize<HomeLayout>(
            """{"Pinned":[null,"","/a","/A"],"Groups":[null,{"Name":null},{"Name":"G","Paths":null}]}"""));
        Assert.Equal(["/a"], read.Pinned);
        Assert.Empty(Assert.Single(read.Groups).Paths);
        Assert.Empty(HomeLayout.Sanitized(null).Groups);
    }
}
