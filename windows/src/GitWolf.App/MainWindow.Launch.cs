using System.Diagnostics;
using System.Runtime.InteropServices;
using GitWolf.App.Services;
using GitWolf.Core;
using Microsoft.UI.Windowing;

namespace GitWolf.App;

/// <summary>Requests to open a folder from the command line, a link or another copy of the app.</summary>
public sealed partial class MainWindow
{
    /// <summary>Completed once last session's repositories are back; later requests wait for it.</summary>
    private readonly TaskCompletionSource _started = new();

    /// <summary>Lets the next copy of the app run, before this one restarts itself.</summary>
    internal Action? ReleaseInstance { get; set; }

    /// <summary>Restores the last session, then opens what the command line asked for.</summary>
    internal async Task StartAsync(string[] arguments)
    {
        // Dialogs need the window's content to be on screen; subscribed before anything is awaited.
        var loaded = new TaskCompletionSource();
        if (Root.IsLoaded) loaded.SetResult();
        else Root.Loaded += (_, _) => loaded.TrySetResult();
        try
        {
            // Restore first: opening saves the list, which would otherwise replace the saved one.
            await _workspace.RestoreAsync();
            Diff.IsSplit = _workspace.SplitDiff;
        }
        catch (Exception e)
        {
            // Fire-and-forget from launch: report rather than lose the failure.
            Trace.TraceError($"Startup failed: {e.GetType().Name}");
            ShowError("The last session couldn’t be restored.");
        }
        finally
        {
            await loaded.Task;
            _started.TrySetResult();
        }
        if (arguments.Length > 0) await LaunchAsync(arguments);
        else await WelcomeAsync();
    }

    /// <summary>Shown once, to someone with nothing set up yet.</summary>
    private async Task WelcomeAsync()
    {
        if (_settings.State.WelcomeSeen) return;
        if (_workspace.Repos.Count > 0 || _settings.Accounts.Accounts.Count > 0)
        {
            _settings.Update(_settings.State with { WelcomeSeen = true });
            return;
        }
        try
        {
            if (Root.XamlRoot is null) return;
            var exit = await new WelcomeDialog(Root.XamlRoot, _settings, WindowHandle, OpenAllAsync).ShowAsync();
            _settings.Update(_settings.State with { WelcomeSeen = true });
            if (exit == WelcomeExit.Open) await PickAsync();
            else if (exit == WelcomeExit.Clone) await CreateAsync(clone: true);
        }
        catch (Exception e)
        {
            // The app works without it; it is offered again next time.
            Trace.TraceWarning($"The welcome couldn't be shown: {e.GetType().Name}");
        }
    }

    /// <summary>Called on any thread with a later copy's command line.</summary>
    internal void Receive(string[] arguments) => DispatcherQueue.TryEnqueue(() =>
    {
        BringToFront();
        _ = LaunchAsync(arguments);
    });

    internal void BringToFront()
    {
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
            presenter.Restore();
        Activate();
        SetForegroundWindow(WindowHandle);
    }

    private async Task LaunchAsync(string[] arguments)
    {
        try
        {
            await _started.Task;
            if (arguments.Length == 0) return;
            if (await Task.Run(() => LaunchRequest.Parse(arguments)) is not { } target)
            {
                ShowError("There’s no folder GitWolf can open in that request.");
                return;
            }
            // A web page can follow a link, and opening a repository runs Git inside it.
            if (target.FromLink && !await Dialogs.ConfirmAsync(Root.XamlRoot, "Open this folder?",
                    $"A link asked GitWolf to open:\n\n{target.Folder}\n\nOnly continue if you trust this folder.",
                    "Open")) return;
            await OpenAsync(target.Folder);
        }
        catch (Exception e)
        {
            Trace.TraceError($"Opening a requested folder failed: {e.GetType().Name}");
            ShowError("The folder couldn’t be opened.");
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
}
