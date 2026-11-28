using System.Diagnostics;
using GitWolf.App.Services;
using GitWolf.Core;
using GitWolf.Store;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace GitWolf.App.Views;

// The commit box: signing, message helpers and ignore rules.
public sealed partial class ChangesView
{
    internal PanelSplitter HeightSplitter => MessageSplitter;

    internal double MessageHeight
    {
        get => Message.ActualHeight;
        set => Message.Height = value;
    }

    internal bool SpellCheck { set => Message.IsSpellCheckEnabled = value; }

    private SigningConfig? _signing;
    /// <summary>The user's own choice; null follows the repository's setting.</summary>
    private bool? _sign;

    private async Task LoadSigningAsync(RepositoryStore store)
    {
        (_signing, _sign) = (null, null);
        Sign.Visibility = Visibility.Collapsed;
        try
        {
            var config = await Task.Run(store.Repo.SigningConfigAsync);
            if (!ReferenceEquals(store, _store)) return;
            _signing = config;
            Sign.IsChecked = config.SignsByDefault;
            Sign.Visibility = config.IsConfigured ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception e)
        {
            // The box stays hidden and git follows its own configuration.
            Trace.TraceWarning($"Reading signing settings failed: {e.GetType().Name}");
        }
    }

    private void OnSign(object sender, RoutedEventArgs e) => _sign = Sign.IsChecked == true;

    private void OnMessageChanged(object sender, TextChangedEventArgs e)
    {
        if (_store is not { } store) return;
        store.DraftMessage = Normalize(Message.Text);
        ShowSubjectWarning(store.DraftMessage);
    }

    private void ShowSubjectWarning(string message)
    {
        var length = CommitMessage.LongSubject(message);
        SubjectWarning.Visibility = length is null ? Visibility.Collapsed : Visibility.Visible;
        if (length is not null)
            SubjectWarning.Text =
                $"The first line is {length} characters. Keep it to {CommitMessage.SubjectLimit} or fewer.";
    }

    private void OnMessageKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter
            || !InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                .HasFlag(CoreVirtualKeyStates.Down)) return;
        e.Handled = true; // No line break.
        if (CommitButton.IsEnabled && !e.KeyStatus.WasKeyDown) Commit();
    }

    private void OnCommit(object sender, RoutedEventArgs e) => Commit();

    private void Commit()
    {
        // Passed to git only when it differs from what git would do anyway.
        var sign = _sign == _signing?.SignsByDefault ? null : _sign;
        _ = _store?.CommitAsync(sign);
    }

    private async void OnMessageMenu(object sender, RoutedEventArgs e)
    {
        if (_store is not { } store || sender is not FrameworkElement anchor) return;
        try
        {
            var repo = store.Repo;
            var (template, recent) = await Task.Run(async () =>
                (await repo.CommitTemplateAsync(), await repo.RecentMessagesAsync()));
            if (!ReferenceEquals(store, _store)) return;

            var menu = new MenuFlyout();
            var types = new MenuFlyoutSubItem { Text = "Type prefix" };
            var scopes = CommitMessage.SuggestedScopes(store.Status.Staged.Select(f => f.Path));
            foreach (var (type, summary) in CommitMessage.ConventionalTypes)
            {
                var label = $"{type}: {summary}";
                if (scopes.Count == 0)
                {
                    types.Items.Add(Item(label, () => Prefix(type, null)));
                    continue;
                }
                var group = new MenuFlyoutSubItem { Text = label };
                group.Items.Add(Item("No scope", () => Prefix(type, null)));
                foreach (var scope in scopes) group.Items.Add(Item(scope, () => Prefix(type, scope)));
                types.Items.Add(group);
            }
            menu.Items.Add(types);

            var use = Item("Use repository template", () => Message.Text = template ?? "");
            use.IsEnabled = template is not null;
            menu.Items.Add(use);

            var reuse = new MenuFlyoutSubItem { Text = "Reuse recent message", IsEnabled = recent.Count > 0 };
            foreach (var message in recent)
            {
                var subject = message.Split('\n')[0];
                if (subject.Length > 60) subject = subject[..60] + "…";
                reuse.Items.Add(Item(subject, () => Message.Text = message));
            }
            menu.Items.Add(reuse);
            if (store.Supports(CollabKind.Issue)) menu.Items.Add(ReferenceMenu(store));
            var breaking = new ToggleMenuFlyoutItem
            {
                Text = "Breaking change",
                IsChecked = CommitMessage.IsBreaking(Normalize(Message.Text)),
            };
            breaking.Click += (_, _) =>
            {
                if (ReferenceEquals(store, _store))
                    Message.Text = CommitMessage.SetBreaking(breaking.IsChecked, Normalize(Message.Text));
            };
            menu.Items.Add(breaking);
            menu.ShowAt(anchor);
        }
        catch (Exception error)
        {
            // An event handler can't pass the failure on.
            Trace.TraceWarning($"The message menu failed: {error.GetType().Name}");
        }
    }

    /// <summary>"Closes #12" and the like. The menu opens at once; the issues arrive after it.</summary>
    private MenuFlyoutSubItem ReferenceMenu(RepositoryStore store)
    {
        var menu = new MenuFlyoutSubItem { Text = "Reference issue" };
        menu.Items.Add(new MenuFlyoutItem { Text = "Loading…", IsEnabled = false });
        _ = FillReferenceMenuAsync(store, menu);
        return menu;
    }

    private async Task FillReferenceMenuAsync(RepositoryStore store, MenuFlyoutSubItem menu)
    {
        try
        {
            var issues = await store.OpenIssuesAsync();
            menu.Items.Clear();
            if (issues.Count == 0) menu.Items.Add(new MenuFlyoutItem { Text = "No open issues", IsEnabled = false });
            foreach (var issue in issues)
            {
                var title = issue.Title.Length > 60 ? issue.Title[..60] + "…" : issue.Title;
                var entry = new MenuFlyoutSubItem { Text = $"#{issue.Number} {title}" };
                foreach (var keyword in new[] { "Closes", "Fixes", "Refs" })
                    entry.Items.Add(Item($"{keyword} #{issue.Number}", () =>
                    {
                        if (ReferenceEquals(store, _store)) store.AddIssueReference(keyword, issue.Number);
                    }));
                menu.Items.Add(entry);
            }
        }
        catch (Exception e)
        {
            // Fire-and-forget: the menu says there is nothing to offer.
            Trace.TraceWarning($"Listing issues for the message failed: {e.GetType().Name}");
        }
    }

    private void Prefix(string type, string? scope) =>
        Message.Text = CommitMessage.ApplyPrefix(type, scope, Normalize(Message.Text));

    private static MenuFlyoutItem Item(string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();
        return item;
    }

    private MenuFlyoutSubItem IgnoreMenu(RepositoryStore store, FileChange file, bool tracked)
    {
        var menu = new MenuFlyoutSubItem { Text = "Ignore", IsEnabled = !store.IsBusy };
        var local = new MenuFlyoutSubItem { Text = "Only on this machine" };
        foreach (var option in IgnoreRules.Options(file.Path))
        {
            menu.Items.Add(Item(option.Title, () => _ = IgnoreAsync(store, option, false, tracked)));
            local.Items.Add(Item(option.Title, () => _ = IgnoreAsync(store, option, true, tracked)));
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(local);
        return menu;
    }

    private async Task IgnoreAsync(RepositoryStore store, IgnoreOption option, bool local, bool tracked)
    {
        if (tracked && !await Dialogs.ConfirmAsync(XamlRoot, $"Stop tracking “{option.Untracks}”?",
                "Git already tracks it, so ignoring it also removes it from the repository in your next commit. "
                    + "It stays on this computer.", "Ignore and stop tracking"))
            return;
        if (ReferenceEquals(store, _store))
            await store.IgnoreAsync(option.Pattern, local, tracked ? option.Untracks : null);
    }
}
