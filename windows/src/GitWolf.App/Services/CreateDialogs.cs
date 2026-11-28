using System.Diagnostics;
using GitWolf.App.Views;
using GitWolf.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace GitWolf.App.Services;

/// <summary>Clone and new-repository dialogs. Ported from CreateRepositorySheets.swift.</summary>
internal static class CreateDialogs
{
    /// <param name="lastFolder">Where the last clone went; used when it is a local folder that still exists.</param>
    /// <returns>The cloned folder, or null when cancelled.</returns>
    public static Task<string?> CloneAsync(
        XamlRoot root, IntPtr window, string? gitPath, AccountStore accounts, string lastFolder)
    {
        var url = new TextBox { Header = "Repository URL", PlaceholderText = "https://host/owner/repo.git" };
        var name = new TextBox { Header = "Folder name" };
        // The name follows the URL until the user types their own.
        var suggested = "";
        url.TextChanged += (_, _) =>
        {
            if (name.Text != suggested) return;
            name.Text = suggested = GitRepository.SuggestedName(url.Text);
        };
        var options = new CloneOptionFields();
        _ = OfferCopiedAddressAsync(url);
        return ShowAsync(root, window, "Clone repository", "Clone", [url], name, () =>
        {
            var (from, folder, chosen) = (url.Text.Trim(), name.Text.Trim(), options.Read());
            return async (parent, progress, token) =>
            {
                // Only an account saved for the URL's own server; SSH remotes use the user's keys.
                var credential = GitCredential.HttpAuthority(from) is { } authority
                    ? await accounts.CredentialForRemoteAsync(authority) : null;
                var askpass = credential is null ? null : AccountStore.WriteAskpass(AppSettings.Folder);
                return await GitRepository.CloneAsync(
                    from, parent, folder, gitPath, credential, askpass, progress, token, chosen);
            };
        }, options.View,
            extra: accounts.Accounts.Any(a => a.Kind.SupportsRepositoryListing()) ? RepositoryPicker(accounts, url) : null,
            // A network folder isn't checked: an unreachable share would stall the window.
            location: AppSettings.IsLocalFolder(lastFolder) ? lastFolder : null);
    }

    /// <summary>Fills in a repository address from the clipboard, unless something was typed meanwhile.</summary>
    private static async Task OfferCopiedAddressAsync(TextBox url)
    {
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text)) return;
            var offered = CloneAddress.FromClipboard(await content.GetTextAsync());
            if (offered is not null && url.Text.Length == 0) url.Text = offered;
        }
        catch (Exception e)
        {
            // Another program can hold the clipboard; the field just stays empty.
            Trace.TraceWarning($"Reading the clipboard failed: {e.GetType().Name}");
        }
    }

    /// <summary>The repositories of a saved account; picking one fills in its URL.</summary>
    private static StackPanel RepositoryPicker(AccountStore accounts, TextBox url)
    {
        var listed = accounts.Accounts.Where(a => a.Kind.SupportsRepositoryListing()).ToList();
        var account = new ComboBox { Header = "Or choose from an account", HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var a in listed) account.Items.Add($"{a.Title} · {a.Username}");
        var search = new TextBox { PlaceholderText = "Search repositories", Visibility = Visibility.Collapsed };
        var list = new ListView { Height = 150, Visibility = Visibility.Collapsed, SelectionMode = ListViewSelectionMode.Single };
        var note = Ui.Text("", 12, secondary: true);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(list, "Repositories");
        List<RemoteRepository> found = [];

        void Filter()
        {
            list.Items.Clear();
            foreach (var repo in found.Where(r => r.FullName.Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase))
                         .Take(100))
                list.Items.Add(new TextBlock
                {
                    Text = repo.IsPrivate ? $"{repo.FullName} (private)" : repo.FullName, Tag = repo, FontSize = 12.5,
                });
        }
        search.TextChanged += (_, _) => Filter();
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is TextBlock { Tag: RemoteRepository repo }) url.Text = repo.HttpsUrl;
        };
        account.SelectionChanged += async (_, _) =>
        {
            var index = account.SelectedIndex;
            if (index < 0) return;
            found = [];
            Filter();
            search.Visibility = list.Visibility = Visibility.Visible;
            note.Text = "Loading repositories…";
            try
            {
                var chosen = listed[index];
                var secret = await accounts.SecretAsync(chosen)
                    ?? throw new ProviderException("Sign in to this account again in Settings → Accounts.");
                var repositories = await Task.Run(() => new ProviderClient(chosen, secret).RepositoriesAsync());
                if (account.SelectedIndex != index) return;
                found = repositories;
                note.Text = found.Count == 0 ? "No repositories found." : "";
                Filter();
            }
            catch (Exception e)
            {
                // An event handler can't pass the failure on.
                Trace.TraceWarning($"Listing repositories failed: {e.GetType().Name}");
                if (account.SelectedIndex == index)
                    note.Text = e is ProviderException ? e.Message : "The repositories couldn’t be listed.";
            }
        };
        return new StackPanel { Spacing = 8, Children = { account, search, list, note } };
    }

    /// <returns>The new repository's folder, or null when cancelled.</returns>
    public static Task<string?> NewAsync(XamlRoot root, IntPtr window, string? gitPath)
    {
        var name = new TextBox { Header = "Folder name" };
        var branch = new TextBox { Header = "Initial branch", Text = "main" };
        return ShowAsync(root, window, "New repository", "Create", [], name, () =>
        {
            var (folder, initial) = (name.Text.Trim(), branch.Text);
            return (parent, _, _) => GitRepository.CreateAsync(parent, folder, initial, gitPath);
        }, branch);
    }

    /// <param name="prepare">
    /// Reads the fields on the UI thread and returns the work, which runs on another thread.
    /// </param>
    private static async Task<string?> ShowAsync(
        XamlRoot root, IntPtr window, string title, string verb, IReadOnlyList<Control> before, TextBox name,
        Func<Func<string, Action<string>, CancellationToken, Task<string>>> prepare, Control? after = null,
        UIElement? extra = null, string? location = null)
    {
        var locationBox = new TextBox
        {
            Header = "Location",
            Text = location ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        var choose = new Button { Content = "Choose…", VerticalAlignment = VerticalAlignment.Bottom };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, MaxHeight = 120 };
        var working = new ProgressBar { IsIndeterminate = true, Visibility = Visibility.Collapsed };
        var row = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } },
            Children = { locationBox, choose },
        };
        Grid.SetColumn(choose, 1);
        var content = new StackPanel { Spacing = 12, Width = 420 };
        foreach (var control in before) content.Children.Add(control);
        if (extra is not null) content.Children.Add(extra);
        content.Children.Add(name);
        content.Children.Add(row);
        if (after is not null) content.Children.Add(after);
        content.Children.Add(working);
        content.Children.Add(status);

        var dialog = new ContentDialog
        {
            RequestedTheme = (root.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
            XamlRoot = root, Title = title, PrimaryButtonText = verb,
            Content = new ScrollViewer { Content = content, MaxHeight = 540 },
            CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary,
        };
        choose.Click += async (_, _) =>
        {
            try
            {
                var picker = new FolderPicker();
                picker.FileTypeFilter.Add("*");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, window);
                if (await picker.PickSingleFolderAsync() is { } folder) locationBox.Text = folder.Path;
            }
            catch (Exception e)
            {
                Trace.TraceWarning($"Choosing a folder failed: {e.GetType().Name}");
                status.Text = "The folder picker couldn’t be opened. Type the location instead.";
            }
        };

        string? result = null;
        CancellationTokenSource? running = null;
        // The dialog stays open while git runs, showing progress or the failure.
        dialog.PrimaryButtonClick += async (_, e) =>
        {
            var deferral = e.GetDeferral();
            e.Cancel = true;
            try
            {
                if (running is not null) return;
                if (before.OfType<TextBox>().Any(box => box.Text.Trim().Length == 0)
                    || name.Text.Trim().Length == 0 || !Path.IsPathFullyQualified(locationBox.Text.Trim()))
                {
                    status.Text = "Fill in every field, with a full folder path as the location.";
                    return;
                }
                running = new CancellationTokenSource();
                var token = running.Token;
                dialog.IsPrimaryButtonEnabled = false;
                working.Visibility = Visibility.Visible;
                status.Text = "";
                // Deferral ends now so Cancel keeps working while git runs.
                deferral.Complete();
                deferral = null;
                var parent = locationBox.Text.Trim();
                void Progress(string chunk)
                {
                    var line = chunk.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                    if (line is not null) dialog.DispatcherQueue.TryEnqueue(() => status.Text = line);
                }
                var run = prepare();
                var folder = await Task.Run(() => run(parent, Progress, token), token);
                if (token.IsCancellationRequested) return;
                result = folder;
                dialog.Hide();
            }
            catch (OperationCanceledException)
            {
                // Cancelled by the user.
            }
            catch (Exception error)
            {
                // An event handler can't pass the failure on, so every one is shown or logged here.
                Trace.TraceWarning($"{title} failed: {error.GetType().Name}");
                var message = error is GitException or IOException or UnauthorizedAccessException
                    ? error.Message : "That didn’t work. Check the fields and try again.";
                // Posted after any queued progress, so the failure is what stays visible.
                dialog.DispatcherQueue.TryEnqueue(() => status.Text = message);
            }
            finally
            {
                deferral?.Complete();
                running?.Dispose();
                running = null;
                working.Visibility = Visibility.Collapsed;
                dialog.IsPrimaryButtonEnabled = true;
            }
        };
        dialog.CloseButtonClick += (_, _) => running?.Cancel();
        try
        {
            await dialog.ShowAsync();
        }
        catch (Exception e)
        {
            Trace.TraceWarning($"{title} dialog could not be shown: {e.GetType().Name}");
        }
        // Escape closes without the Cancel button.
        if (result is null) running?.Cancel();
        return result;
    }
}
