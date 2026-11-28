using GitWolf.App.Views;
using GitWolf.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;

namespace GitWolf.App.Services;

/// <summary>About GitWolf, and the people who built it. Ported from AboutPanel.swift and ContributorsView.swift.</summary>
internal static class AboutDialog
{
    private const string Project = "https://github.com/" + Contributors.Repository;

    private static readonly ContributorList People = new(System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GitWolf", "contributors.json"));

    public static async Task ShowAsync(XamlRoot root)
    {
        var content = new StackPanel { Spacing = 10, Width = 420 };
        content.Children.Add(Ui.Text($"Version {AppBuild.Version}", 12, secondary: true));
        content.Children.Add(Ui.Text(
            "A fast, clean, native Git client.\nFree and open source under the MIT licence.", 13, wrap: true));
        var links = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (title, path) in new[]
        {
            ("GitHub", ""), ("Documentation", "/tree/main/docs"), ("Report an issue", "/issues"),
            ("Licence", "/blob/main/LICENSE"),
        })
            links.Children.Add(new HyperlinkButton
            {
                Content = title, NavigateUri = new Uri(Project + path), Padding = new Thickness(0, 2, 14, 2),
            });
        content.Children.Add(links);
        await Dialog(root, "About GitWolf", content, "Contributors", async () => await ShowPeopleAsync(root));
    }

    private static async Task ShowPeopleAsync(XamlRoot root)
    {
        var rows = new StackPanel { Spacing = 2, Padding = new Thickness(0, 0, 14, 0) };
        var pictures = Avatars.Enabled;
        void Fill(IReadOnlyList<Contributor> people)
        {
            rows.Children.Clear();
            foreach (var person in people) rows.Children.Add(Row(person, pictures));
        }
        Fill(People.Cached());
        var content = new StackPanel { Spacing = 10, Width = 420 };
        content.Children.Add(Ui.Text("GitWolf is built by these people. Thank you.", 13, secondary: true, wrap: true));
        content.Children.Add(new ScrollViewer { Content = rows, MaxHeight = 420 });
        // The saved list shows at once; a newer one replaces it when it arrives.
        async Task RefreshAsync()
        {
            if (await People.RefreshAsync() is { } fresh) Fill(fresh);
        }
        _ = RefreshAsync();
        await Dialog(root, "Contributors", content, "Become a contributor",
            async () => await Launcher.LaunchUriAsync(new Uri(Project + "/blob/main/CONTRIBUTING.md")));
    }

    private static Button Row(Contributor person, bool pictures)
    {
        var picture = new PersonPicture { Width = 32, Height = 32, DisplayName = person.Login };
        if (pictures && person.Avatar is { } avatar)
        {
            // Ask for a 96 px picture, enough for 32 px at any scaling.
            var sized = new UriBuilder(avatar);
            sized.Query = (sized.Query.TrimStart('?') + "&s=96").TrimStart('&');
            picture.ProfilePicture = new BitmapImage { DecodePixelWidth = 96, UriSource = sized.Uri };
        }
        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(Ui.Text(person.Login, 13, bold: true));
        if (person.IsMaintainer) name.Children.Add(Ui.Text("Maintainer", 11, secondary: true));
        var commits = person.Contributions == 1 ? "1 commit" : $"{person.Contributions:N0} commits";
        var count = Ui.Text(person.Contributions > 0 ? commits : "", 12, secondary: true);
        count.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid
        {
            ColumnSpacing = 12,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition(),
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        Grid.SetColumn(name, 1);
        Grid.SetColumn(count, 2);
        grid.Children.Add(picture);
        grid.Children.Add(name);
        grid.Children.Add(count);
        var button = new Button
        {
            Content = grid, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            BorderThickness = new Thickness(0),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        AutomationProperties.SetName(button, person.Contributions > 0 ? $"{person.Login}, {commits}" : person.Login);
        ToolTipService.SetToolTip(button, $"Open {person.Login} on GitHub");
        button.Click += (_, _) => _ = Launcher.LaunchUriAsync(person.Profile);
        return button;
    }

    /// <summary>Shows a dialog; <paramref name="next"/> runs after it has closed through the primary button.</summary>
    private static async Task Dialog(XamlRoot root, string title, object content, string primary, Func<Task> next)
    {
        var dialog = new ContentDialog
        {
            RequestedTheme = (root.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
            XamlRoot = root, Title = title, Content = content, PrimaryButtonText = primary,
            CloseButtonText = "Close", DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await next();
    }
}
