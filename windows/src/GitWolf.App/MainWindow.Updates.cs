using System.ComponentModel;
using System.Diagnostics;
using GitWolf.App.Services;
using GitWolf.Core;
using Microsoft.UI.Dispatching;

namespace GitWolf.App;

/// <summary>In-app updates: a daily check, and installing a release once the user agrees.</summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromDays(1);

    private Updater? _updater;
    private DispatcherQueueTimer? _updateTimer;
    private bool _updating;

    private void SetUpUpdates()
    {
        Settings.UpdateRequested += () => _ = CheckForUpdatesAsync(manual: true);
        Settings.AboutRequested += () => _ = AboutDialog.ShowAsync(Root.XamlRoot);
        if (!AppBuild.UpdatesEnabled) return;
        _updater = new Updater(Updater.CreateClient(), AppBuild.Version!);
        // The version that was replaced can only be removed once it has exited.
        _ = Task.Run(() => UpdateInstaller.CleanUp(AppContext.BaseDirectory));
        // Hourly, so a check that failed or became due while the app stayed open isn't missed.
        _updateTimer = DispatcherQueue.CreateTimer();
        _updateTimer.Interval = TimeSpan.FromHours(1);
        _updateTimer.Tick += (_, _) => CheckIfDue();
        _updateTimer.Start();
        Root.Loaded += (_, _) => CheckIfDue();
        Closed += (_, _) => _updateTimer.Stop();
    }

    private void CheckIfDue()
    {
        if (!_settings.State.CheckForUpdates) return;
        var since = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - _settings.State.LastUpdateCheck;
        // A time in the future means the clock was changed.
        if (since < 0 || since >= CheckEvery.TotalSeconds) _ = CheckForUpdatesAsync(manual: false);
    }

    /// <summary>A check the user asked for always answers; a daily one speaks only about a new release.</summary>
    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_updater is not { } updater)
        {
            if (manual)
                await UpdateDialogs.InformAsync(Root.XamlRoot, "This is a development build",
                    "Updates are only available in the packaged app.");
            return;
        }
        if (_updating) return;
        _updating = true;
        try
        {
            if (manual) ShowNotice("Checking for updates…");
            var release = await updater.CheckAsync();
            if (release is null)
            {
                RecordCheck();
                if (manual)
                    await UpdateDialogs.InformAsync(Root.XamlRoot, "You’re up to date",
                        $"GitWolf {AppBuild.Version} is the newest version.");
                return;
            }
            if (!manual && release.Version == _settings.State.SkippedUpdate)
            {
                RecordCheck();
                return;
            }
            // Not shown (another dialog is open) leaves the check due, so it is offered later.
            if (await UpdateDialogs.OfferAsync(Root.XamlRoot, AppBuild.Version!, release) is not { } choice) return;
            RecordCheck();
            if (choice == UpdateChoice.Skip)
                _settings.Update(_settings.State with { SkippedUpdate = release.Version });
            else if (choice == UpdateChoice.Install)
                await InstallUpdateAsync(updater, release);
        }
        catch (UpdateException e)
        {
            Trace.TraceWarning($"Update failed: {e.InnerException?.GetType().Name ?? e.GetType().Name}");
            if (manual) await UpdateDialogs.InformAsync(Root.XamlRoot, "Couldn’t update", e.Message);
        }
        catch (Exception e)
        {
            // Started from a timer or a click: report rather than lose the failure.
            Trace.TraceError($"Update failed: {e.GetType().Name}");
            if (manual) ShowError("The update check failed.");
        }
        finally
        {
            _updating = false;
        }
    }

    private void RecordCheck() => _settings.Update(
        _settings.State with { LastUpdateCheck = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });

    /// <summary>Why restarting now would lose something, or null.</summary>
    private string? RestartBlock()
    {
        if (_workspace.Repos.FirstOrDefault(r => r.IsBusy) is { } busy)
            return $"{busy.Name} is still working. Check for updates again when it has finished.";
        if (_workspace.Repos.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.DraftMessage)) is { } draft)
            return $"{draft.Name} has an unfinished commit message, which a restart would lose. "
                + "Commit or clear it, then check for updates again.";
        return null;
    }

    /// <summary>Failures are shown here, because the user asked for the installation.</summary>
    private async Task InstallUpdateAsync(Updater updater, AppcastItem release)
    {
        try
        {
            if (RestartBlock() is { } before)
            {
                await UpdateDialogs.InformAsync(Root.XamlRoot, "Not installed yet", before);
                return;
            }
            string? blocked = null;
            var installed = await UpdateDialogs.ProgressAsync(Root.XamlRoot, release.Version,
                async (progress, installing, token) =>
                {
                    var update = await updater.DownloadAsync(release, progress, token);
                    token.ThrowIfCancellationRequested();
                    // Something may have started while downloading.
                    if ((blocked = RestartBlock()) is not null) return false;
                    installing();
                    await Task.Run(() => UpdateInstaller.Install(update, AppContext.BaseDirectory));
                    return true;
                });
            if (blocked is not null)
                await UpdateDialogs.InformAsync(Root.XamlRoot, "Not installed yet", blocked);
            if (installed) Relaunch();
        }
        catch (UpdateException e)
        {
            Trace.TraceWarning($"Installing failed: {e.InnerException?.GetType().Name ?? e.GetType().Name}");
            await UpdateDialogs.FailedAsync(Root.XamlRoot, e.Message);
        }
    }

    private void Relaunch()
    {
        try
        {
            // Otherwise the new copy would hand over to this one and exit.
            ReleaseInstance?.Invoke();
            using var next = Process.Start(new ProcessStartInfo(
                Path.Combine(AppContext.BaseDirectory, UpdateInstaller.Executable))
            {
                UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory,
            });
            Close();
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            Trace.TraceError($"Relaunch failed: {e.GetType().Name}");
            ShowError("The update is installed. Quit GitWolf and open it again to finish.");
        }
    }
}
