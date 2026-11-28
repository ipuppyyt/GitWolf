using System.Diagnostics;
using GitWolf.App.Views;
using GitWolf.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace GitWolf.App.Services;

/// <summary>
/// Adding an account, with a token, a password or a browser sign-in. Ported from AccountsUI.swift and
/// OAuthSignIn.swift. Secrets only go to the account's own server and to Credential Manager.
/// </summary>
internal static class AccountDialogs
{
    private enum Method { Browser, Token, Password }

    private sealed class Form
    {
        public readonly ComboBox Kind = new() { Header = "Provider", HorizontalAlignment = HorizontalAlignment.Stretch };
        public readonly ComboBox Method = new() { Header = "Sign in with", HorizontalAlignment = HorizontalAlignment.Stretch };
        public readonly TextBox Host = new() { Header = "Host" };
        public readonly TextBox Username = new() { Header = "Username" };
        public readonly PasswordBox Secret = new() { PlaceholderText = "Paste here" };
        public readonly TextBox ClientId = new() { Header = "OAuth client ID" };
        public readonly PasswordBox ClientSecret = new() { Header = "Client secret" };
        public readonly TextBlock Hint = Ui.Text("", 12, secondary: true);
        public readonly HyperlinkButton TokenPage = new() { Padding = new Thickness(0), FontSize = 12 };
        public readonly TextBox Redirect = new()
        {
            Header = "Redirect URI", Text = OAuthLoopback.RedirectUri, IsReadOnly = true,
        };
        public readonly TextBlock Code = new()
        {
            FontSize = 26, FontFamily = new FontFamily("Consolas"), IsTextSelectionEnabled = true,
        };
        public readonly StackPanel Device = new() { Spacing = 8, Visibility = Visibility.Collapsed };
        public readonly TextBlock Status = Ui.Text("", 12.5);
        public readonly ProgressBar Working = new() { IsIndeterminate = true, Visibility = Visibility.Collapsed };
    }

    /// <param name="again">A browser-signed-in account to fill the form with, to sign in to it again.</param>
    public static async Task AddAsync(XamlRoot root, AccountStore accounts, ProviderAccount? again = null)
    {
        var form = new Form();
        foreach (var kind in Enum.GetValues<ProviderKind>()) form.Kind.Items.Add(kind.Title());
        ProviderKind Kind() => (ProviderKind)Math.Max(0, form.Kind.SelectedIndex);
        Method Chosen() => (form.Method.SelectedItem as ComboBoxItem)?.Tag as Method? ?? Method.Token;
        (string Host, string? Scheme)? Server() => ProviderAccount.CleanHost(form.Host.Text);

        Uri? devicePage = null;
        var copy = Ui.Button("Copy code", () => Dialogs.Copy(form.Code.Text));
        var open = Ui.Button("Open page", () => { if (devicePage is not null) _ = Launcher.LaunchUriAsync(devicePage); });
        form.Device.Children.Add(Ui.Text("Enter this code in your browser, then approve GitWolf.", 12, secondary: true));
        form.Device.Children.Add(form.Code);
        form.Device.Children.Add(Ui.Row(8, copy, open));

        var content = new StackPanel { Spacing = 12, Width = 420 };
        foreach (var element in new UIElement[]
        {
            form.Kind, form.Method, form.Host, form.Username, form.ClientId, form.ClientSecret, form.Secret,
            form.Redirect, form.Hint, form.TokenPage, form.Device, form.Working, form.Status,
        })
            content.Children.Add(element);

        var dialog = new ContentDialog
        {
            RequestedTheme = (root.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
            XamlRoot = root, Title = again is null ? "Add account" : "Sign in again", PrimaryButtonText = "Add", CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = new ScrollViewer { Content = content, MaxHeight = 520 },
        };
        CancellationTokenSource? running = null;
        var updating = false;

        void Update()
        {
            if (updating) return;
            updating = true;
            try
            {
                var kind = Kind();
                var server = Server();
                var host = server?.Host ?? "";
                var oauth = kind.SupportsOAuth(host);
                if (form.Method.Items.OfType<ComboBoxItem>().Any(i => i.Tag is Method.Browser) != oauth
                    || form.Method.Items.Count == 0)
                {
                    var previous = form.Method.Items.Count == 0 ? Method.Browser : Chosen();
                    form.Method.Items.Clear();
                    foreach (var method in Enum.GetValues<Method>())
                        if (method != Method.Browser || oauth)
                            form.Method.Items.Add(new ComboBoxItem { Content = method.ToString(), Tag = method });
                    form.Method.SelectedItem = form.Method.Items.OfType<ComboBoxItem>()
                        .FirstOrDefault(i => i.Tag is Method m && m == previous) ?? form.Method.Items[0];
                }
                var chosen = Chosen();
                var browser = chosen == Method.Browser;
                Show(form.Username, !browser && (kind.NeedsUsername() || chosen == Method.Password));
                Show(form.Secret, !browser);
                form.Secret.Header = chosen == Method.Password ? "Password" : kind.SecretName();
                Show(form.ClientId, browser);
                Show(form.ClientSecret, browser && kind.OAuthNeedsClientSecret());
                Show(form.Redirect, browser && kind.Flow() == OAuthFlow.Pkce);
                form.Hint.Text = browser
                    ? kind.OAuthSetupHint(host.Length > 0 ? host : kind.DefaultHost())
                    : chosen == Method.Password
                        ? "Many hosts (GitHub, GitLab) no longer accept passwords for Git over HTTPS; use a token there."
                        : kind.ScopeHint();
                var page = chosen == Method.Token && server is { } s && s.Scheme is null
                    && Uri.TryCreate(kind.TokenPageUrl(s.Host), UriKind.Absolute, out var url) ? url : null;
                form.TokenPage.NavigateUri = page;
                form.TokenPage.Content = $"Create a {kind.SecretName().ToLowerInvariant()} on {host}";
                Show(form.TokenPage, page is not null);

                dialog.PrimaryButtonText = browser ? "Sign in" : "Add";
                dialog.IsPrimaryButtonEnabled = running is null && server is not null && chosen switch
                {
                    Method.Browser => form.ClientId.Text.Trim().Length > 0
                        && (!kind.OAuthNeedsClientSecret() || form.ClientSecret.Password.Trim().Length > 0),
                    Method.Password => form.Secret.Password.Trim().Length > 0 && form.Username.Text.Trim().Length > 0,
                    _ => form.Secret.Password.Trim().Length > 0
                        && (!kind.NeedsUsername() || form.Username.Text.Trim().Length > 0),
                };
            }
            finally
            {
                updating = false;
            }
        }

        // A client ID typed for a host before is filled in again.
        void RecallClientId() => form.ClientId.Text = Server() is { } s ? accounts.ClientId(s.Host) ?? "" : "";

        form.Kind.SelectionChanged += (_, _) =>
        {
            var current = form.Host.Text.Trim();
            if (current.Length == 0 || Enum.GetValues<ProviderKind>().Any(k => k.DefaultHost() == current))
                form.Host.Text = Kind().DefaultHost();
            // A secret typed for one provider must not be sent to another.
            Clear(form);
            RecallClientId();
            Update();
        };
        var lastHost = "";
        form.Host.TextChanged += (_, _) =>
        {
            var host = Server()?.Host ?? "";
            if (host != lastHost)
            {
                lastHost = host;
                Clear(form);
                RecallClientId();
            }
            Update();
        };
        // Self-hosted servers: the provider is picked from the name once the field is left.
        form.Host.LostFocus += (_, _) =>
        {
            if (Server() is { } s && ProviderKindExtensions.Guess(s.Host) is { } guessed && guessed != Kind())
                form.Kind.SelectedIndex = (int)guessed;
        };
        form.Method.SelectionChanged += (_, _) => Update();
        form.Username.TextChanged += (_, _) => Update();
        form.ClientId.TextChanged += (_, _) => Update();
        form.Secret.PasswordChanged += (_, _) => Update();
        form.ClientSecret.PasswordChanged += (_, _) => Update();
        AutomationProperties.SetName(form.Secret, "Secret");
        form.Kind.SelectedIndex = 0;
        if (again is not null)
        {
            // In this order: choosing the provider and typing the host reset what comes after them.
            form.Kind.SelectedIndex = (int)again.Kind;
            form.Host.Text = again.Scheme is { } scheme ? $"{scheme}://{again.Host}" : again.Host;
        }
        Update();
        if (again?.OAuthClientId is { } known) form.ClientId.Text = known;

        // The dialog stays open while signing in, showing the code or the failure.
        dialog.PrimaryButtonClick += async (_, e) =>
        {
            e.Cancel = true;
            if (running is not null || Server() is not { } server) return;
            var (kind, method) = (Kind(), Chosen());
            var (username, secret) = (form.Username.Text.Trim(), form.Secret.Password.Trim());
            var (clientId, clientSecret) = (form.ClientId.Text.Trim(), form.ClientSecret.Password.Trim());
            running = new CancellationTokenSource();
            var token = running.Token;
            Control[] inputs = [form.Kind, form.Method, form.Host, form.Username, form.Secret, form.ClientId, form.ClientSecret];
            foreach (var input in inputs) input.IsEnabled = false;
            dialog.IsPrimaryButtonEnabled = false;
            Show(form.Working, true);
            form.Status.Foreground = Ui.Theme("TextFillColorPrimaryBrush");
            form.Status.Text = "";
            try
            {
                if (method == Method.Browser)
                {
                    var account = new ProviderAccount(kind, server.Host, "", scheme: server.Scheme, oauthClientId: clientId);
                    var tokens = await SignInAsync(account, clientId,
                        kind.OAuthNeedsClientSecret() ? clientSecret : null, form, page => devicePage = page, token);
                    form.Status.Text = "Finishing sign-in…";
                    var signedIn = account;
                    var login = await Task.Run(() => new ProviderClient(signedIn, tokens.AccessToken).CurrentUserAsync(), token);
                    token.ThrowIfCancellationRequested();
                    account = account.With(login);
                    accounts.RememberClientId(account.Host, clientId);
                    accounts.AddOAuth(account, tokens);
                }
                else
                {
                    var password = method == Method.Password;
                    form.Status.Text = "Verifying…";
                    var account = await VerifyAsync(new ProviderAccount(kind, server.Host,
                        kind.NeedsUsername() || password ? username : "", password, server.Scheme), secret, token);
                    token.ThrowIfCancellationRequested();
                    accounts.Add(account, secret);
                }
                dialog.Hide();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Cancelled by the user.
            }
            catch (Exception error)
            {
                // An event handler can't pass the failure on, so every one is shown or logged here.
                Trace.TraceWarning($"Adding an account failed: {error.GetType().Name}");
                form.Status.Foreground = Palette.Orange;
                form.Status.Text = error is ProviderException or OAuthException or SecretStoreException
                    ? error.Message : "That didn’t work. Check the fields and try again.";
            }
            finally
            {
                running?.Dispose();
                running = null;
                foreach (var input in inputs) input.IsEnabled = true;
                Show(form.Working, false);
                Show(form.Device, false);
                Update();
            }
        };
        dialog.CloseButtonClick += (_, _) => running?.Cancel();
        try
        {
            await dialog.ShowAsync();
        }
        catch (Exception e)
        {
            Trace.TraceWarning($"The account dialog could not be shown: {e.GetType().Name}");
        }
        // Escape closes without the Cancel button.
        running?.Cancel();
        Clear(form);
    }

    private static void Show(UIElement element, bool visible) =>
        element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private static void Clear(Form form)
    {
        form.Secret.Password = "";
        form.ClientSecret.Password = "";
        form.Status.Text = "";
    }

    /// <summary>The account with the name its secret belongs to; throws if the server rejects it.</summary>
    private static async Task<ProviderAccount> VerifyAsync(ProviderAccount account, string secret, CancellationToken token)
    {
        var password = account.UsesPassword == true;
        if (!account.Kind.SupportsRepositoryListing())
            return account.Username.Length == 0 ? account.With("git") : account;
        try
        {
            var login = await Task.Run(() => new ProviderClient(account, secret).CurrentUserAsync(), token);
            return account.Kind.NeedsUsername() || password ? account : account.With(login);
        }
        catch (ProviderException e) when (e.IsUnauthorized && password
            && account.Kind is ProviderKind.GitHub or ProviderKind.GitLab)
        {
            // These APIs reject passwords; Git itself may still accept them, so the account is kept.
            return account;
        }
    }

    private static async Task<OAuthTokens> SignInAsync(
        ProviderAccount account, string clientId, string? clientSecret, Form form, Action<Uri> showPage,
        CancellationToken token)
    {
        var client = new OAuthClient(account.Kind, account.Origin, clientId, clientSecret);
        if (account.Kind.Flow() == OAuthFlow.Device)
        {
            var device = await client.StartDeviceFlowAsync(token);
            showPage(device.VerificationUrl);
            form.Code.Text = device.UserCode;
            Show(form.Device, true);
            form.Status.Text = $"Waiting for you to approve on {device.VerificationUrl.Host}… The code is already copied.";
            Dialogs.Copy(device.UserCode);
            await Launcher.LaunchUriAsync(device.VerificationUrl);
            return await client.PollDeviceFlowAsync(device, token);
        }
        // Listening starts before the browser opens.
        using var loopback = new OAuthLoopback();
        var pkce = Pkce.Generate();
        var state = Pkce.NewState();
        var url = client.AuthorizationUrl(OAuthLoopback.RedirectUri, state, pkce.Challenge)
            ?? throw new OAuthException("That server address can’t be used to sign in.");
        form.Status.Text = "Complete the sign-in in your browser…";
        await Launcher.LaunchUriAsync(url);
        var code = await loopback.WaitForCodeAsync(state, token);
        return await client.ExchangeAsync(code, pkce.Verifier, OAuthLoopback.RedirectUri, token);
    }
}
