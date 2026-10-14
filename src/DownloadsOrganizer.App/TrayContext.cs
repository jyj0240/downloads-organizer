using System.Diagnostics;
using DownloadsOrganizer.Core;

namespace DownloadsOrganizer.App;

/// <summary>Runs the app with no main window: a tray icon, its menu, notifications, the history and settings windows,
/// and the background organizer.</summary>
sealed class TrayContext : ApplicationContext
{
    readonly NotifyIcon _trayIcon;
    readonly ContextMenuStrip _menu = new();
    readonly SynchronizationContext _ui;
    readonly MoveJournal _journal = new(AppPaths.JournalFile);
    readonly EventWaitHandle _exitRequest;
    readonly RegisteredWaitHandle _exitWait;
    AppSettings _settings;
    OrganizerGroup _organizer;
    ToolStripMenuItem _pauseItem = null!;
    ToolStripMenuItem _undoItem = null!;
    HistoryForm? _history;
    SettingsForm? _settingsWindow;
    readonly Updater? _updater;

    public TrayContext(AppSettings settings, bool firstRun, bool updated = false)
    {
        // Creating the menu installs the WinForms synchronization context; capture it to come back to the UI thread.
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _settings = settings;
        _organizer = StartOrganizer(settings);
        // Only the installed copy updates itself; a development build (dotnet run) never does.
        if (SelfInstaller.IsRunningInstalled)
        {
            _updater = new Updater(busy: () => _history is { IsDisposed: false } || _settingsWindow is { IsDisposed: false })
            {
                Automatic = settings.AutoUpdate,
            };
        }

        _trayIcon = new NotifyIcon { Icon = AppIcon.Tray(), ContextMenuStrip = _menu, Visible = true };
        _trayIcon.BalloonTipClicked += (_, _) => OpenHistory();
        _trayIcon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                OpenHistory();
            }
        };
        _menu.Opening += (_, _) => RefreshUndoItem();
        BuildMenu();

        // A newer copy being installed, or an uninstall from Settings > Apps, asks this copy to exit through this event.
        _exitRequest = new EventWaitHandle(false, EventResetMode.AutoReset, SelfInstaller.ExitEventName);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitRequest, (_, _) => _ui.Post(_ => ExitThread(), null), null, Timeout.Infinite, executeOnlyOnce: true);

        if (firstRun)
        {
            ShowBalloon(UiText.Get("FirstRunTitle"), UiText.Format("FirstRunText", (int)_settings.SettleDelay.TotalMinutes));
        }
        else if (updated)
        {
            ShowBalloon(UiText.Get("UpdateTitle"), UiText.Format("UpdatedText", SelfInstaller.CurrentVersion.ToString(3)));
        }
    }

    OrganizerGroup StartOrganizer(AppSettings settings)
    {
        var organizer = new OrganizerGroup(settings, _journal);
        organizer.Moved += moved => _ui.Post(_ => OnMoved(moved), null);
        organizer.Failed += AppLog.Write;
        organizer.Start();
        return organizer;
    }

    /// <summary>Builds the menu in the current language. Called again when the language changes.</summary>
    void BuildMenu()
    {
        _menu.Items.Clear();
        _menu.Items.Add(UiText.Get("MenuOrganizeNow"), null, (_, _) => OrganizeNow());
        _pauseItem = new ToolStripMenuItem(UiText.Get(_organizer.Paused ? "MenuResume" : "MenuPause"), null, (_, _) => TogglePause());
        _menu.Items.Add(_pauseItem);
        _undoItem = new ToolStripMenuItem(UiText.Get("MenuUndoNothing"), null, (_, _) => UndoLast());
        _menu.Items.Add(_undoItem);
        _menu.Items.Add(new ToolStripSeparator());
        var history = new ToolStripMenuItem(UiText.Get("MenuHistory"), null, (_, _) => OpenHistory());
        history.Font = new Font(history.Font, FontStyle.Bold);   // what a double-click on the icon opens
        _menu.Items.Add(history);
        _menu.Items.Add(UiText.Get("MenuSettings"), null, (_, _) => OpenSettings());
        if (_settings.Folders.Count == 1)
        {
            var only = _settings.Folders[0].Location;
            _menu.Items.Add(UiText.Format("MenuOpenFolderNamed", ShortName(only)), null, (_, _) => Open("explorer.exe", only));
        }
        else
        {
            var open = new ToolStripMenuItem(UiText.Get("MenuOpenFolders"));
            foreach (var folder in _settings.Folders)
            {
                var path = folder.Location;
                open.DropDownItems.Add(ShortName(path), null, (_, _) => Open("explorer.exe", path)).ToolTipText = path;
            }
            _menu.Items.Add(open);
        }
        _menu.Items.Add(new ToolStripSeparator());
        if (_updater is not null)
        {
            _menu.Items.Add(UiText.Get("MenuCheckUpdates"), null, (_, _) => CheckForUpdates());
        }
        if (SelfInstaller.IsRunningInstalled)
        {
            _menu.Items.Add(UiText.Get("MenuUninstall"), null, (_, _) => UninstallFromTray());
        }
        _menu.Items.Add(UiText.Get("MenuExit"), null, (_, _) => ExitThread());
        _trayIcon.Text = UiText.Get(_organizer.Paused ? "TooltipPaused" : "TooltipWatching");
    }

    void OnMoved(IReadOnlyList<MoveOutcome> moved)
    {
        if (_settings.Notify)
        {
            ShowBalloon(UiText.Get("MovedTitle"), UiText.Format("MovedText", moved.Count));
        }
        if (_history is { IsDisposed: false })
        {
            _history.RefreshRows();
        }
    }

    void OrganizeNow()
    {
        var organizer = _organizer;
        Task.Run(() =>
        {
            try
            {
                var outcomes = organizer.TickAll(force: true);
                if (!outcomes.Any(o => o.Moved))
                {
                    _ui.Post(_ => ShowBalloon(UiText.Get("MovedTitle"), UiText.Get("NothingToMove")), null);
                }
            }
            catch (Exception e)
            {
                AppLog.Write(e);
            }
        });
    }

    void TogglePause()
    {
        _organizer.Paused = !_organizer.Paused;
        _pauseItem.Text = UiText.Get(_organizer.Paused ? "MenuResume" : "MenuPause");
        _trayIcon.Text = UiText.Get(_organizer.Paused ? "TooltipPaused" : "TooltipWatching");
        if (!_organizer.Paused)
        {
            _organizer.MarkChanged();
        }
    }

    void RefreshUndoItem()
    {
        var count = _organizer.UndoableCount();
        _undoItem.Enabled = count > 0;
        _undoItem.Text = count > 0 ? UiText.Format("MenuUndoLast", count) : UiText.Get("MenuUndoNothing");
    }

    void UndoLast()
    {
        var results = _organizer.UndoLastBatch();
        var restored = results.Count(r => r.Restored);
        ShowBalloon(UiText.Get("UndoTitle"), UiText.Format("UndoText", restored, results.Count - restored));
        foreach (var problem in results.Where(r => !r.Restored))
        {
            AppLog.Write($"undo skipped {problem.Move.To}: {problem.Problem}");
        }
        _history?.RefreshRows();
    }

    void OpenHistory()
    {
        if (_history is { IsDisposed: false })
        {
            _history.WindowState = FormWindowState.Normal;
            _history.Activate();
            return;
        }
        _history = new HistoryForm(() => _journal.History(), moves => _organizer.UndoMoves(moves));
        _history.Show();
    }

    void OpenSettings()
    {
        if (_settingsWindow is { IsDisposed: false })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsForm(_settings, SelfInstaller.IsRunningInstalled, SelfInstaller.RunsAtStartup);
        _settingsWindow.FormClosed += (_, _) =>
        {
            var window = _settingsWindow;
            _settingsWindow = null;
            if (window is { DialogResult: DialogResult.OK, Result: { } updated })
            {
                if (SelfInstaller.IsRunningInstalled && window.RunAtStartup != SelfInstaller.RunsAtStartup)
                {
                    SelfInstaller.SetRunAtStartup(window.RunAtStartup);
                }
                ApplySettings(updated);
            }
        };
        _settingsWindow.Show();
    }

    /// <summary>Saves new settings and swaps in an organizer that uses them. No restart needed.</summary>
    void ApplySettings(AppSettings updated)
    {
        try
        {
            SettingsStore.Save(AppPaths.SettingsFile, updated);
        }
        catch (Exception e)
        {
            AppLog.Write(e);
            MessageBox.Show(UiText.Get("SaveFailed") + "\n\n" + e.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        var languageChanged = updated.Language != _settings.Language;
        var paused = _organizer.Paused;
        _organizer.Dispose();
        _settings = updated;
        _organizer = StartOrganizer(updated);
        _organizer.Paused = paused;
        if (_updater is not null)
        {
            _updater.Automatic = updated.AutoUpdate;
        }
        if (languageChanged)
        {
            UiText.Use(updated.Language);
            _history?.Close();
        }
        BuildMenu();   // folder list and language both show in the menu
    }

    /// <summary>"Check for updates": says whether this is the latest version; if not, asks before installing.</summary>
    async void CheckForUpdates()
    {
        var current = SelfInstaller.CurrentVersion.ToString(3);
        AvailableUpdate? update;
        try
        {
            update = await _updater!.FindAsync();
        }
        catch (Exception e)
        {
            AppLog.Write("update check: " + e.Message);
            ShowBalloon(UiText.Get("UpdateTitle"), UiText.Get("UpdateCheckFailed"));
            return;
        }
        if (update is null)
        {
            ShowBalloon(UiText.Get("UpdateTitle"), UiText.Format("UpdateUpToDate", current));
            return;
        }
        var newer = update.Version.ToString(3);
        if (MessageBox.Show(UiText.Format("UpdateAsk", newer, current), AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }
        ShowBalloon(UiText.Get("UpdateTitle"), UiText.Format("UpdateDownloading", newer));
        try
        {
            await _updater.InstallAsync(update);   // the new version asks this copy to exit once it is ready
        }
        catch (Exception e)
        {
            AppLog.Write(e);
            ShowBalloon(UiText.Get("UpdateTitle"), UiText.Get("UpdateInstallFailed"));
        }
    }

    void UninstallFromTray()
    {
        if (MessageBox.Show(UiText.Get("UninstallConfirm"), AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }
        var deleteData = MessageBox.Show(UiText.Get("UninstallDeleteData"), AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        SelfInstaller.Uninstall(deleteData, stopRunningCopy: false);
        MessageBox.Show(UiText.Get("UninstallDone"), AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        ExitThread();
    }

    void ShowBalloon(string title, string text) => _trayIcon.ShowBalloonTip(5000, title, text, ToolTipIcon.Info);

    static string ShortName(string folder) => Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)) is { Length: > 0 } name ? name : folder;

    static void Open(string program, string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(program, $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception e)
        {
            AppLog.Write(e);
        }
    }

    protected override void ExitThreadCore()
    {
        _exitWait.Unregister(null);
        _exitRequest.Dispose();
        _updater?.Dispose();
        _organizer.Dispose();   // waits for a move under way
        _history?.Close();
        _settingsWindow?.Close();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _menu.Dispose();
        base.ExitThreadCore();
    }
}
