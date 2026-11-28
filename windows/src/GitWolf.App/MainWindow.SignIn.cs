using GitWolf.App.Services;
using GitWolf.Store;
using Microsoft.UI.Xaml.Controls;

namespace GitWolf.App;

/// <summary>The error banner's offer to sign in again after a server refused a change.</summary>
public sealed partial class MainWindow
{
    private void ShowReauth()
    {
        if (_store is not { ReauthAccount: { } account } store)
        {
            Error.ActionButton = null;
            return;
        }
        var button = new Button { Content = "Sign In Again" };
        button.Click += async (_, _) =>
        {
            // Clearing the message also withdraws the offer.
            store.ErrorMessage = null;
            await AccountDialogs.AddAsync(Root.XamlRoot, _settings.Accounts, account);
        };
        Error.ActionButton = button;
    }
}
