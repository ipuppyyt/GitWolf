using System.Diagnostics;
using GitWolf.App.Services;
using GitWolf.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using Windows.System;

namespace GitWolf.App.Views;

/// <summary>Preferences, shown over the window. Ported from SettingsView.swift.</summary>
public sealed partial class SettingsView : UserControl
{
    private Window? _window;
    private AppSettings? _settings;
    private Workspace? _workspace;

    public SettingsView()
    {
        InitializeComponent();
        // Card borders are picked in code, so they are redrawn when the theme changes.
        ActualThemeChanged += (_, _) => Build();
    }

    /// <summary>The diff layout lives with the workspace, which the diff header also changes.</summary>
    internal event Action<bool>? SplitChanged;

    internal event Action? UpdateRequested;
    internal event Action? AboutRequested;

    internal void Attach(Window window, AppSettings settings, Workspace workspace)
    {
        _window = window;
        _settings = settings;
        _workspace = workspace;
    }

    internal void Open() => Open(null);

    /// <summary>Shows the settings, on the named pane or the last one used.</summary>
    internal void Open(string? pane)
    {
        Visibility = Visibility.Visible;
        if (pane is not null && !Equals(Panes.SelectedItem, pane)) Panes.SelectedItem = pane;
        else if (Panes.SelectedIndex < 0) Panes.SelectedIndex = 0;
        else Build();
        // Focus can only move once the view is visible.
        DispatcherQueue.TryEnqueue(() => CloseButton.Focus(FocusState.Programmatic));
    }

    private void Close()
    {
        Visibility = Visibility.Collapsed;
        Body.Children.Clear();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
    private void OnBackdrop(object sender, TappedRoutedEventArgs e) => Close();
    private void OnPane(object sender, SelectionChangedEventArgs e) => Build();

    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        e.Handled = true;
        Close();
    }

    /// <summary>Controls are rebuilt from the saved values each time a pane is shown.</summary>
    private void Build()
    {
        if (_settings is null || Panes.SelectedItem is not string pane || Visibility != Visibility.Visible) return;
        Body.Children.Clear();
        Scroller.ChangeView(null, 0, null, true);
        Body.Children.Add(new TextBlock { Text = pane, FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        if (pane == "General") General(_settings);
        else if (pane == "Git") Git(_settings);
        else if (pane == "Tools") Tools(_settings);
        else if (pane == "Accounts") Accounts(_settings.Accounts);
        else Shortcuts();
    }

    private void Accounts(AccountStore accounts)
    {
        var rows = new List<UIElement>();
        foreach (var account in accounts.Accounts)
        {
            var remove = new Button { Content = "Remove" };
            remove.Click += async (_, _) => await RemoveAsync(accounts, account);
            rows.Add(Row(account.Title,
                account.Username.Length == 0 ? account.Host : $"@{account.Username} · {account.Host}", remove));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, $"Remove {account.Title}");
        }
        if (rows.Count == 0)
            rows.Add(new TextBlock
            {
                Text = "No accounts yet. Add one to clone private repositories and sync with your host.",
                FontSize = 12.5, Opacity = 0.65, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(12),
            });
        Group("Connected accounts", rows.ToArray());

        var add = new Button { Content = "Add account…" };
        add.Click += async (_, _) =>
        {
            await AccountDialogs.AddAsync(XamlRoot, accounts);
            Build();
        };
        Body.Children.Add(add);
        Body.Children.Add(new TextBlock
        {
            Text = "Tokens are stored in Windows Credential Manager and handed to Git only for matching hosts. "
                + "They are never written into URLs or config files.",
            FontSize = 12, Opacity = 0.65, TextWrapping = TextWrapping.Wrap,
        });
    }

    private async Task RemoveAsync(AccountStore accounts, ProviderAccount account)
    {
        if (!await Dialogs.ConfirmAsync(XamlRoot, "Remove this account?",
                $"The saved sign-in for {account.Host} will be deleted from Windows Credential Manager.", "Remove"))
            return;
        try
        {
            accounts.Remove(account);
        }
        catch (SecretStoreException e)
        {
            Trace.TraceWarning($"Removing an account failed: {e.GetType().Name}");
            await Dialogs.ConfirmAsync(XamlRoot, "The account couldn’t be removed", e.Message, "OK");
        }
        Build();
    }

    private void General(AppSettings settings)
    {
        var s = settings.State;
        // Each change starts from the latest state, not the one the pane was built with.
        void Set(Func<SettingsState, SettingsState> change) => settings.Update(change(settings.State));

        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var dark = ActualTheme == ElementTheme.Dark;
        // GitWolf's own colour comes first.
        foreach (var choice in Enum.GetValues<AccentChoice>().OrderBy(c => c != AccentChoice.Wolf))
            swatches.Children.Add(Swatch(settings, choice, dark, swatches));

        Group("Appearance",
            Row("Theme", "Follow Windows, or always light or dark", Picker(s.Appearance,
                Enum.GetValues<AppearanceChoice>().Select(a => (a.ToString(), a)),
                v => Set(x => x with { Appearance = v }))),
            Row("Accent colour", null, swatches));

        Group("Sync",
            Row("Background fetch", "Keeps ahead/behind counts up to date", Picker(s.AutoFetchMinutes,
                [("Never", 0), ("Every minute", 1), ("Every 5 minutes", 5), ("Every 15 minutes", 15), ("Every 30 minutes", 30)],
                v => Set(x => x with { AutoFetchMinutes = v }))),
            Row("Pull strategy", "Rebase keeps history linear; merge keeps a merge commit", Picker(s.PullRebase,
                [("Merge", false), ("Rebase", true)], v => Set(x => x with { PullRebase = v }))));

        var syntax = new ToggleSwitch { IsOn = s.SyntaxHighlighting, MinWidth = 0, OnContent = null, OffContent = null };
        syntax.Toggled += (_, _) => Set(x => x with { SyntaxHighlighting = syntax.IsOn });

        var pictures = new ToggleSwitch { IsOn = s.ProfilePictures, MinWidth = 0, OnContent = null, OffContent = null };
        pictures.Toggled += (_, _) => Set(x => x with { ProfilePictures = pictures.IsOn });

        var spelling = new ToggleSwitch { IsOn = s.SpellCheck, MinWidth = 0, OnContent = null, OffContent = null };
        spelling.Toggled += (_, _) => Set(x => x with { SpellCheck = spelling.IsOn });

        var tray = new ToggleSwitch { IsOn = s.ShowTrayIcon, MinWidth = 0, OnContent = null, OffContent = null };
        tray.Toggled += (_, _) => Set(x => x with { ShowTrayIcon = tray.IsOn });

        var results = new ToggleSwitch { IsOn = s.NotifyChecks, MinWidth = 0, OnContent = null, OffContent = null };
        results.Toggled += (_, _) => Set(x => x with { NotifyChecks = results.IsOn });

        Group("Windows",
            Row("Icon near the clock", "Open repositories and their changes, from the notification area", tray),
            Row("CI notifications", "When the checked-out commit’s checks finish while GitWolf is in the background",
                results));

        Group("Commit message",
            Row("Check spelling", "Underline misspelled words while typing", spelling));

        var updates = new ToggleSwitch { IsOn = s.CheckForUpdates, MinWidth = 0, OnContent = null, OffContent = null };
        updates.Toggled += (_, _) => Set(x => x with { CheckForUpdates = updates.IsOn });
        var check = new Button { Content = "Check Now" };
        check.Click += (_, _) => UpdateRequested?.Invoke();

        var version = Row($"Version {AppBuild.Version}",
            AppBuild.UpdatesEnabled ? null : "Development build: updates are only available in the packaged app",
            check);
        // A row names its control after its title, which doesn't say what this button does.
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(check, "Check Now");

        var about = new Button { Content = "About…" };
        about.Click += (_, _) => AboutRequested?.Invoke();

        Group("Updates",
            Row("Check for updates", "Once a day. Nothing is installed without asking", updates),
            version,
            Row("About GitWolf", "Version, links and contributors", about));

        OpenFromOutside();

        Group("History & diffs",
            Row("Profile pictures", "From GitHub, for github.com repositories. Turning this off deletes saved pictures", pictures),
            Row("Commits per page", "How many commits to load at a time", Picker(s.CommitPageSize,
                [("500", 500), ("1,000", 1000), ("2,000", 2000), ("5,000", 5000)],
                v => Set(x => x with { CommitPageSize = v }))),
            Row("Diff layout", "How changes are laid out", Picker(_workspace?.SplitDiff == true,
                [("Unified", false), ("Side by side", true)], v => SplitChanged?.Invoke(v))),
            Row("Syntax highlighting", "Colour keywords, strings and comments", syntax),
            Row("Diff context", "Unchanged lines around each change", Picker(s.DiffContext,
                new[] { 1, 3, 4, 6, 10 }.Select(n => (n == 1 ? "1 line" : $"{n} lines", n)),
                v => Set(x => x with { DiffContext = v }))));
    }

    private static Button Swatch(AppSettings settings, AccentChoice choice, bool dark, Panel all)
    {
        var fill = new SolidColorBrush(Theming.Accent(choice, dark));
        var tick = new FontIcon
        {
            Glyph = "", FontSize = 10, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            Opacity = settings.State.Accent == choice ? 1 : 0,
        };
        var title = choice == AccentChoice.Wolf ? "GitWolf" : choice.ToString();
        var button = new Button
        {
            Width = 22, Height = 22, MinWidth = 0, Padding = new Thickness(0), CornerRadius = new CornerRadius(11),
            Background = fill, Content = tick, Tag = tick,
        };
        // Keep the swatch colour while hovered and pressed.
        button.Resources["ButtonBackgroundPointerOver"] = fill;
        button.Resources["ButtonBackgroundPressed"] = fill;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, title);
        ToolTipService.SetToolTip(button, title);
        button.Click += (_, _) =>
        {
            foreach (var other in all.Children.OfType<Button>())
                if (other.Tag is FontIcon mark) mark.Opacity = ReferenceEquals(other, button) ? 1 : 0;
            settings.Update(settings.State with { Accent = choice });
        };
        return button;
    }

    private void Git(AppSettings settings)
    {
        var path = new TextBox
        {
            Text = settings.State.GitPath, PlaceholderText = new GitRunner(Environment.SystemDirectory).GitPath,
            FontFamily = new FontFamily("Consolas"),
        };
        var status = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        void Describe()
        {
            var value = settings.State.GitPath;
            var valid = value.Length == 0 || AppSettings.IsLocalProgram(value);
            status.Text = valid
                ? $"Using {settings.CustomGitPath ?? path.PlaceholderText}"
                : "Not a local .exe file given as a full path. Using the default Git.";
            status.Foreground = valid ? Palette.Green : Palette.Orange;
        }
        // Applied on Enter or when focus leaves, never while a path is half typed.
        void Commit()
        {
            settings.Update(settings.State with { GitPath = path.Text });
            Describe();
        }
        path.LostFocus += (_, _) => Commit();
        path.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter) Commit();
        };

        var choose = new Button { Content = "Choose…" };
        choose.Click += async (_, _) =>
        {
            if (await PickExecutableAsync() is not { } file) return;
            path.Text = file;
            Commit();
        };
        var reset = new Button { Content = "Reset" };
        reset.Click += (_, _) =>
        {
            path.Text = "";
            Commit();
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { choose, reset } };
        var note = new TextBlock
        {
            Text = "Leave empty to use the Git found on PATH, or point to another build of git.exe.",
            FontSize = 12, Opacity = 0.65, TextWrapping = TextWrapping.Wrap,
        };
        Describe();
        Group("Git executable", new StackPanel
        {
            Padding = new Thickness(12), Spacing = 10, Children = { path, buttons, status, note },
        });
    }

    private async Task<string?> PickExecutableAsync()
    {
        if (_window is null) return null;
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".exe");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_window));
            return (await picker.PickSingleFileAsync())?.Path;
        }
        catch (Exception e)
        {
            // Typing the path still works if the picker can't be shown.
            Trace.TraceWarning($"File picker failed: {e.GetType().Name}");
            return null;
        }
    }

    private void Shortcuts()
    {
        void Section(string title, params (string Title, string Keys)[] items) =>
            Group(title, items.Select(i => (UIElement)Row(i.Title, null, new TextBlock
            {
                Text = i.Keys, FontSize = 12, Opacity = 0.65,
            })).ToArray());

        Section("Repositories", ("Open repository…", "Ctrl O"), ("Jump to…", "Ctrl K"));
        Section("Sync", ("Fetch", "Ctrl Shift F"), ("Pull", "Ctrl Shift L"), ("Push", "Ctrl Shift P"));
        Section("Open in", ("Terminal", "Ctrl Alt T"), ("Editor", "Ctrl Alt E"),
            ("File Explorer", "Ctrl Alt Shift R"));
        Section("App", ("Settings", "Ctrl ,"));
    }

    /// <summary>A titled card of rows separated by hairlines.</summary>
    private void Group(string title, params UIElement[] rows)
    {
        var border = Brush("PanelBorderBrush");
        var card = new StackPanel();
        foreach (var row in rows)
        {
            if (card.Children.Count > 0)
                card.Children.Add(new Border { Height = 1, Margin = new Thickness(12, 0, 0, 0), Background = border });
            card.Children.Add(row);
        }
        var heading = new TextBlock
        {
            Text = title.ToUpperInvariant(), FontSize = 11, Opacity = 0.65,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        };
        var frame = new Border
        {
            Child = card, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = border,
        };
        Body.Children.Add(new StackPanel { Spacing = 8, Children = { heading, frame } });
    }

    /// <summary>A theme brush for the theme this view is in, which may differ from the system's.</summary>
    private Brush? Brush(string key)
    {
        var theme = ActualTheme == ElementTheme.Light ? "Light" : "Default";
        foreach (var dictionary in Application.Current.Resources.MergedDictionaries
                     .SelectMany(d => d.MergedDictionaries.Prepend(d)))
            if (dictionary.ThemeDictionaries.TryGetValue(theme, out var found) && found is ResourceDictionary colours
                && colours.ContainsKey(key)) return colours[key] as Brush;
        return null;
    }

    private static Grid Row(string title, string? detail, FrameworkElement trailing)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontSize = 13 });
        if (detail is not null)
            text.Children.Add(new TextBlock { Text = detail, FontSize = 11.5, Opacity = 0.65, TextWrapping = TextWrapping.Wrap });
        trailing.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(trailing, 1);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(trailing, title);
        return new Grid
        {
            MinHeight = 48, Padding = new Thickness(12, 6, 12, 6), ColumnSpacing = 12,
            ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } },
            Children = { text, trailing },
        };
    }

    private static ComboBox Picker<T>(T current, IEnumerable<(string Label, T Value)> options, Action<T> changed)
    {
        var box = new ComboBox { MinWidth = 150 };
        foreach (var (label, value) in options)
        {
            box.Items.Add(new ComboBoxItem { Content = label, Tag = value });
            if (EqualityComparer<T>.Default.Equals(value, current)) box.SelectedIndex = box.Items.Count - 1;
        }
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is ComboBoxItem { Tag: T value }) changed(value);
        };
        return box;
    }
}
