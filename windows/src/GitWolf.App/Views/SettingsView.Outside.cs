using GitWolf.App.Services;
using Microsoft.UI.Xaml.Controls;

namespace GitWolf.App.Views;

/// <summary>The `gitwolf` command and `gitwolf://` links.</summary>
public sealed partial class SettingsView
{
    private void OpenFromOutside()
    {
        var problem = new TextBlock
        {
            FontSize = 12, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap, Foreground = Palette.Orange,
            Padding = new Microsoft.UI.Xaml.Thickness(12, 0, 12, 0),
            Visibility = Microsoft.UI.Xaml.Visibility.Collapsed,
        };
        ToggleSwitch Switch(bool on, Func<bool, string?> change, Func<bool> state)
        {
            var toggle = new ToggleSwitch { IsOn = on, MinWidth = 0, OnContent = null, OffContent = null };
            toggle.Toggled += (_, _) =>
            {
                // Putting the switch back after a failure raises this again, with nothing to change.
                if (toggle.IsOn == state()) return;
                var message = change(toggle.IsOn);
                problem.Text = message ?? "";
                problem.Visibility = message is null
                    ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
                toggle.IsOn = state();
            };
            return toggle;
        }

        Group("Open from outside",
            Row("gitwolf command", "Run “gitwolf” or “gitwolf C:\\folder” in a terminal",
                Switch(OutsideAccess.CommandInstalled, OutsideAccess.SetCommand, () => OutsideAccess.CommandInstalled)),
            Row("gitwolf:// links", "GitWolf asks before opening the folder a link names",
                Switch(OutsideAccess.LinksRegistered, OutsideAccess.SetLinks, () => OutsideAccess.LinksRegistered)));
        Body.Children.Add(problem);
    }
}
