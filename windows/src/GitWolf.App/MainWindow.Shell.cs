using System.ComponentModel;
using System.Runtime.InteropServices;
using GitWolf.App.Services;
using GitWolf.App.Services.Shell;
using GitWolf.Core;
using GitWolf.Store;

namespace GitWolf.App;

/// <summary>The tray icon, taskbar badge, jump list and CI notifications.</summary>
public sealed partial class MainWindow
{
    private delegate nint WindowProcedure(nint window, int message, nint wParam, nint lParam, nint id, nint data);

    /// <summary>Kept in a field: Windows holds the only other reference.</summary>
    private WindowProcedure? _procedure;
    private TrayIcon? _tray;
    private TaskbarBadge? _badge;
    private readonly Dictionary<RepositoryStore, Action<CheckState>> _followed = [];
    private List<string> _jumpList = [];

    private void SetUpShell()
    {
        var window = WindowHandle;
        _tray = new TrayIcon(window, Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"))
        {
            Menu = TrayMenu,
        };
        _tray.Activated += BringToFront;
        _badge = new TaskbarBadge(window);
        var explorerRestarted = RegisterWindowMessage("TaskbarCreated");
        var buttonCreated = RegisterWindowMessage("TaskbarButtonCreated");
        _procedure = (handle, message, wParam, lParam, _, _) =>
        {
            if (message == TrayIcon.Message) _tray?.Receive(wParam, lParam);
            else if (message == explorerRestarted) _tray?.Restore();
            else if (message == buttonCreated) _badge?.Apply();
            return DefSubclassProc(handle, message, wParam, lParam);
        };
        SetWindowSubclass(window, _procedure, 1, 0);
        _tray.Visible = _settings.State.ShowTrayIcon;
        _settings.Changed += (_, now) =>
        {
            if (_tray is { } tray) tray.Visible = now.ShowTrayIcon;
        };
        _workspace.Changed += FollowRepositories;
        Closed += (_, _) =>
        {
            _tray?.Dispose();
            _tray = null;
            RemoveWindowSubclass(window, _procedure, 1);
        };
    }

    /// <summary>Keeps the count, the jump list and notifications in step with the open repositories.</summary>
    private void FollowRepositories()
    {
        foreach (var (store, finished) in _followed.Where(f => !_workspace.Repos.Contains(f.Key)).ToList())
        {
            store.PropertyChanged -= OnAnyStoreChanged;
            store.HeadChecksFinished -= finished;
            _followed.Remove(store);
        }
        foreach (var store in _workspace.Repos.Where(r => !_followed.ContainsKey(r)))
        {
            Action<CheckState> finished = state => NotifyChecks(store, state);
            store.PropertyChanged += OnAnyStoreChanged;
            store.HeadChecksFinished += finished;
            _followed[store] = finished;
        }
        ShowChangeCount();
        var recents = _workspace.Recents.Take(JumpList.MaxEntries).ToList();
        // Rewritten only when it differs, as it is saved to disk by Windows.
        if (recents.Count == 0 || recents.SequenceEqual(_jumpList)) return;
        _jumpList = recents;
        JumpList.Show(recents);
    }

    private void OnAnyStoreChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RepositoryStore.Status)) ShowChangeCount();
    }

    private void ShowChangeCount()
    {
        var changes = _workspace.Repos.Sum(r => r.Status.ChangeCount);
        _badge?.Show(BadgeLabel.Text(changes));
        if (_tray is { } tray)
            tray.Tip = BadgeLabel.Text(changes) is { } label
                ? $"GitWolf: {label} changed {(changes == 1 ? "file" : "files")}" : "GitWolf";
    }

    private void NotifyChecks(RepositoryStore store, CheckState state)
    {
        // Only while another program is in front; otherwise the tile already shows the result.
        if (!_settings.State.NotifyChecks || GetForegroundWindow() == WindowHandle) return;
        var failed = state == CheckState.Failure;
        _tray?.Notify(failed ? "Checks failed" : "Checks passed",
            $"{store.Name} · {store.Status.BranchDisplay}", failed, () =>
            {
                BringToFront();
                if (_workspace.Repos.Contains(store)) _workspace.Select(store);
            });
    }

    private IReadOnlyList<TrayItem> TrayMenu()
    {
        var items = new List<TrayItem>();
        foreach (var store in _workspace.Repos)
        {
            var count = store.Status.ChangeCount;
            var detail = count == 0 ? "" : $" · {count} {(count == 1 ? "change" : "changes")}";
            items.Add(new TrayItem($"{store.Name} · {store.Status.BranchDisplay}{detail}", () =>
            {
                BringToFront();
                _workspace.Select(store);
            }));
        }
        if (items.Count > 0) items.Add(new TrayItem());
        var recents = _workspace.Recents
            .Where(p => !_workspace.Repos.Any(r => string.Equals(r.Id, p, StringComparison.OrdinalIgnoreCase)))
            .Take(JumpList.MaxEntries)
            .Select(p => new TrayItem(Path.GetFileName(Path.TrimEndingDirectorySeparator(p)), () =>
            {
                BringToFront();
                _ = OpenAsync(p);
            }))
            .ToList();
        if (recents.Count > 0) items.Add(new TrayItem("Open recent", Children: recents));
        items.Add(new TrayItem("Open repository…", () =>
        {
            BringToFront();
            _ = PickAsync();
        }));
        items.Add(new TrayItem("Show GitWolf", BringToFront));
        items.Add(new TrayItem());
        items.Add(new TrayItem("Quit GitWolf", Close));
        return items;
    }

    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(nint window, WindowProcedure procedure, nint id, nint data);

    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(nint window, WindowProcedure procedure, nint id);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint window, int message, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
}
