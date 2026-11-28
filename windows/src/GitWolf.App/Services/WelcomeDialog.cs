using System.ComponentModel;
using System.Diagnostics;
using GitWolf.App.Views;
using GitWolf.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Windows.System;

namespace GitWolf.App.Services;

/// <summary>What the user asked to do on leaving the welcome.</summary>
internal enum WelcomeExit { None, Open, Clone }

/// <summary>
/// The first-run walkthrough: check Git, add an account, open repositories, pick a look. Every step can be
/// skipped. Ported from WelcomeSheet.swift.
/// </summary>
internal sealed class WelcomeDialog(
    XamlRoot root, AppSettings settings, nint window, Func<IReadOnlyList<string>, Task> openAll)
{
    private const int Steps = 4;

    private int _step;
    private readonly StackPanel _body = new() { Spacing = 12, Width = 440 };
    private ContentDialog? _dialog;
    private bool _addingAccount;
    private IReadOnlyList<(string Path, CheckBox Box)> _found = [];
    private string? _scanned;
    private bool _scanning;
    private bool? _gitReady;
    private WelcomeExit _exit;

    public async Task<WelcomeExit> ShowAsync()
    {
        _ = CheckGitAsync();
        while (true)
        {
            var scroller = new ScrollViewer { Content = _body, Height = 420 };
            _dialog = new ContentDialog
            {
                RequestedTheme = (root.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
                XamlRoot = root, DefaultButton = ContentDialogButton.Primary,
                Content = scroller,
            };
            // The buttons move between steps without closing the dialog.
            _dialog.PrimaryButtonClick += (_, e) =>
            {
                if (_step == Steps - 1) return;
                e.Cancel = true;
                _step++;
                Show();
            };
            _dialog.SecondaryButtonClick += (_, e) =>
            {
                e.Cancel = true;
                _step--;
                Show();
            };
            Show();
            await _dialog.ShowAsync();
            scroller.Content = null;
            if (!_addingAccount) return _exit;
            // Only one dialog can be open, so the welcome makes way and returns afterwards.
            _addingAccount = false;
            await AccountDialogs.AddAsync(root, settings.Accounts);
        }
    }

    private void Show()
    {
        if (_dialog is not { } dialog) return;
        _body.Children.Clear();
        var (title, detail) = _step switch
        {
            0 => ("Welcome to GitWolf", "A fast, clean, native Git client."),
            1 => ("Connect your accounts", "Sign in to GitHub, GitLab or another host to clone private repositories, "
                + "see checks and open pull requests. You can do this later in Settings."),
            2 => ("Add your repositories",
                "Open one, clone one, or pick a folder and GitWolf finds the repositories inside it."),
            _ => ("Make it yours", "Pick how GitWolf looks. You can change both in Settings at any time."),
        };
        dialog.Title = title;
        dialog.PrimaryButtonText = _step == Steps - 1 ? "Get started" : "Continue";
        dialog.SecondaryButtonText = _step == 0 ? "" : "Back";
        dialog.CloseButtonText = _step == Steps - 1 ? "" : "Skip";
        _body.Children.Add(Ui.Text(detail, 13, secondary: true, wrap: true));
        if (_step == 0) Intro();
        else if (_step == 1) Accounts();
        else if (_step == 2) Repositories();
        else Look();
        _body.Children.Add(Ui.Text($"Step {_step + 1} of {Steps}", 11, secondary: true));
    }

    private void Intro()
    {
        foreach (var (title, detail) in new[]
        {
            ("Always in sync", "Changes made anywhere, even in a terminal, show up at once."),
            ("Fast and native", "Built for Windows, with no web view."),
            ("Uses your Git", "Every action runs the Git you already have, with your configuration."),
        })
        {
            var point = new StackPanel();
            point.Children.Add(Ui.Text(title, 13, bold: true));
            point.Children.Add(Ui.Text(detail, 12, secondary: true, wrap: true));
            _body.Children.Add(point);
        }
        _body.Children.Add(Ui.Text(_gitReady switch
        {
            null => "Looking for Git…",
            true => "Git is installed.",
            _ => "GitWolf needs Git. Install Git for Windows, or choose another copy in Settings → Git.",
        }, 13, wrap: true));
        if (_gitReady != false) return;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(Ui.Button("Download Git",
            () => _ = Launcher.LaunchUriAsync(new Uri("https://git-scm.com/download/win"))));
        row.Children.Add(Ui.Button("Check again", () => _ = CheckGitAsync()));
        _body.Children.Add(row);
    }

    private async Task CheckGitAsync()
    {
        _gitReady = null;
        try
        {
            var git = new GitRunner(Environment.SystemDirectory, settings.CustomGitPath);
            await Task.Run(() => git.RunAsync(["--version"]));
            _gitReady = true;
        }
        catch (Exception e) when (e is GitException or Win32Exception or IOException)
        {
            Trace.TraceInformation($"Git wasn't found: {e.GetType().Name}");
            _gitReady = false;
        }
        if (_step == 0) Show();
    }

    private void Accounts()
    {
        var accounts = settings.Accounts.Accounts;
        if (accounts.Count == 0) _body.Children.Add(Ui.Text("No accounts yet.", 12, secondary: true));
        foreach (var account in accounts)
            _body.Children.Add(Ui.Text(account.Username.Length == 0
                ? $"{account.Title} · {account.Host}" : $"@{account.Username} · {account.Host}", 13));
        _body.Children.Add(Ui.Button(accounts.Count == 0 ? "Add an account…" : "Add another account…", () =>
        {
            _addingAccount = true;
            _dialog?.Hide();
        }));
        _body.Children.Add(Ui.Text(
            "Tokens are kept in Windows Credential Manager and never written into URLs or config files.", 11,
            secondary: true, wrap: true));
    }

    private void Repositories()
    {
        if (_scanning)
        {
            _body.Children.Add(new ProgressRing { IsActive = true });
            _body.Children.Add(Ui.Text("Looking for repositories…", 12, secondary: true));
            return;
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(Ui.Button("Find in a folder…", () => _ = ScanAsync()));
        row.Children.Add(Ui.Button("Open one…", () => Leave(WelcomeExit.Open)));
        row.Children.Add(Ui.Button("Clone…", () => Leave(WelcomeExit.Clone)));
        _body.Children.Add(row);
        if (_scanned is null) return;
        _body.Children.Add(Ui.Text(_found.Count == 0
            ? $"No repositories found in {_scanned}." : $"Found {_found.Count} in {_scanned}", 13, bold: true,
            wrap: true));
        if (_found.Count == 0) return;
        foreach (var (_, box) in _found) _body.Children.Add(box);
        _body.Children.Add(Ui.Button("Open the selected repositories", () =>
        {
            var chosen = _found.Where(f => f.Box.IsChecked == true).Select(f => f.Path).ToList();
            if (chosen.Count == 0) return;
            _dialog?.Hide();
            _ = openAll(chosen);
        }));
    }

    private void Leave(WelcomeExit exit)
    {
        _exit = exit;
        _dialog?.Hide();
    }

    private async Task ScanAsync()
    {
        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, window);
            if (await picker.PickSingleFolderAsync() is not { } folder) return;
            _scanning = true;
            Show();
            var found = await Task.Run(() => RepositoryScanner.Find(folder.Path));
            _scanned = folder.Name;
            _found = found.Select(path =>
            {
                var box = new CheckBox
                {
                    IsChecked = true, Content = Ui.Text(Path.GetRelativePath(folder.Path, path) is "."
                        ? folder.Name : Path.GetRelativePath(folder.Path, path), 13),
                };
                AutomationProperties.SetName(box, Path.GetFileName(path));
                ToolTipService.SetToolTip(box, path);
                return (path, box);
            }).ToList();
        }
        catch (Exception e)
        {
            // The picker and the search are both optional here.
            Trace.TraceWarning($"Searching for repositories failed: {e.GetType().Name}");
        }
        _scanning = false;
        Show();
    }

    private void Look()
    {
        var theme = new RadioButtons { Header = "Theme", MaxColumns = 3 };
        foreach (var choice in Enum.GetValues<AppearanceChoice>()) theme.Items.Add(choice.ToString());
        theme.SelectedIndex = (int)settings.State.Appearance;
        theme.SelectionChanged += (_, _) =>
        {
            if (theme.SelectedIndex >= 0)
                settings.Update(settings.State with { Appearance = (AppearanceChoice)theme.SelectedIndex });
            if (_dialog is { } dialog && root.Content is FrameworkElement content)
                dialog.RequestedTheme = content.ActualTheme;
        };
        var accent = new RadioButtons { Header = "Accent colour", MaxColumns = 3 };
        var choices = Enum.GetValues<AccentChoice>().OrderBy(c => c != AccentChoice.Wolf).ToList();
        foreach (var choice in choices) accent.Items.Add(choice == AccentChoice.Wolf ? "GitWolf" : choice.ToString());
        accent.SelectedIndex = choices.IndexOf(settings.State.Accent);
        accent.SelectionChanged += (_, _) =>
        {
            if (accent.SelectedIndex >= 0)
                settings.Update(settings.State with { Accent = choices[accent.SelectedIndex] });
        };
        _body.Children.Add(theme);
        _body.Children.Add(accent);
        _body.Children.Add(Ui.Text("You’re all set. Press Ctrl K at any time for the command palette.", 12,
            secondary: true, wrap: true));
    }
}
