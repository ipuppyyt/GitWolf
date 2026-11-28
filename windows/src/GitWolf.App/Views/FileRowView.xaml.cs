using GitWolf.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GitWolf.App.Views;

/// <summary>A file, or (when <see cref="File"/> is null) a section header.</summary>
internal sealed record FileRow(FileChange? File, DiffSource Source, string? Header = null, bool IsLfs = false,
    bool Filtered = false);

public sealed partial class FileRowView : UserControl
{
    private Action? _action;

    public FileRowView() => InitializeComponent();

    internal void Show(FileRow row, string? actionLabel, Action? action, string? spokenAction = null)
    {
        _action = action;
        Spoken.Name(Action, spokenAction ?? actionLabel ?? "");
        Action.Content = actionLabel;
        Action.Visibility = actionLabel is null ? Visibility.Collapsed : Visibility.Visible;
        Header.Text = row.Header ?? "";
        Lfs.Visibility = row.IsLfs ? Visibility.Visible : Visibility.Collapsed;

        if (row.File is not { } file)
        {
            Badge.Background = Palette.Clear;
            Kind.Text = NameText.Text = PathText.Text = "";
            TypeIcon.Show(null);
            FolderIcon.Show(null);
            ToolTipService.SetToolTip(this, null);
            return;
        }
        var (letter, brush, fill) = Palette.Status(file.Kind);
        Badge.Background = fill;
        Kind.Text = letter;
        Kind.Foreground = brush;
        NameText.Text = file.FileName;
        PathText.Text = file.Directory;
        TypeIcon.Show(FileIcons.ForPath(file.Path));
        FolderIcon.Show(FileIcons.FolderContaining(file.Path));
        ToolTipService.SetToolTip(this, file.OriginalPath is { } from ? $"{from} → {file.Path}" : file.Path);
    }

    private void OnAction(object sender, RoutedEventArgs e) => _action?.Invoke();
}
