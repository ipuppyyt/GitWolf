using GitWolf.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;

namespace GitWolf.App.Views;

/// <summary>
/// What a screen reader says for list rows. Without a name a row is announced as its data object's
/// debug text. Containers are reused, so every row that is shown must be named.
/// </summary>
internal static class Spoken
{
    public static void Name(DependencyObject element, string name) => AutomationProperties.SetName(element, name);

    public static string File(FileRow row) => row.File is not { } file ? row.Header ?? ""
        : $"{file.Kind.SpokenName()}: {file.Path}"
            + (file.OriginalPath is { } from ? $", from {from}" : "") + (row.IsLfs ? ", stored with Git LFS" : "");

    public static string Commit(Commit commit) =>
        $"{commit.Subject}, by {commit.AuthorName}, {commit.Date.LocalDateTime:f}, {commit.ShortHash}"
        + string.Concat(commit.Refs.Select(r => $", {r.Name}"));

    public static string Sidebar(SidebarRow row) => row.Kind switch
    {
        SidebarKind.Header or SidebarKind.RemoteGroup =>
            $"{row.Title} {row.Trailing}".TrimEnd() + (row.IsOpen ? ", expanded" : ", collapsed"),
        _ => $"{row.Title} {row.Trailing}".TrimEnd() + (row.Branch is { IsCurrent: true } ? ", current branch" : ""),
    };

    public static string Line(DiffLine? line) => line is null ? "" : line.Kind switch
    {
        DiffLineKind.Addition => $"Added line {line.NewNumber}: {line.Text}",
        DiffLineKind.Deletion => $"Removed line {line.OldNumber}: {line.Text}",
        DiffLineKind.Context => $"Line {line.NewNumber}: {line.Text}",
        _ => line.Text,
    };

    public static string Split(SplitRow row) => row.Header is { } header ? header.Text
        : row.Left?.Id == row.Right?.Id ? Line(row.Right)
        : string.Join(". ", new[] { Line(row.Left), Line(row.Right) }.Where(s => s.Length > 0));
}
