namespace DownloadsOrganizer.App;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => AppLog.Write(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.Write(e.ExceptionObject.ToString() ?? "unhandled");

        // Settings > Apps > Uninstall runs the installed exe with --uninstall.
        if (args.Contains("--uninstall"))
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                UiText.Use(Core.SettingsStore.LoadOrCreate(AppPaths.SettingsFile, AppPaths.Downloads()).Language);
            }
            var quiet = args.Contains("--quiet");
            if (!quiet && MessageBox.Show(UiText.Get("UninstallConfirm"), Core.AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            var deleteData = !quiet && MessageBox.Show(UiText.Get("UninstallDeleteData"), Core.AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
            SelfInstaller.Uninstall(deleteData, stopRunningCopy: true);
            if (!quiet)
            {
                MessageBox.Show(UiText.Get("UninstallDone"), Core.AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return;
        }

        // A downloaded exe installs itself, starts the installed copy and gets out of the way.
        if (SelfInstaller.IsBundled && !SelfInstaller.IsRunningInstalled && !args.Contains("--no-install"))
        {
            try
            {
                SelfInstaller.InstallAndLaunch();
            }
            catch (Exception e)
            {
                AppLog.Write(e);
                MessageBox.Show(UiText.Get("InstallFailed") + "\n\n" + e.Message, Core.AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return;
        }

        // One copy per user session: a second launch exits quietly instead of adding a second tray icon.
        using var singleInstance = new Mutex(true, SelfInstaller.MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            return;
        }
        SelfInstaller.CleanUpAfterUpdate();
        // The exe that just installed this copy may still be closing; try again once it surely has.
        _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ => SelfInstaller.CleanUpAfterUpdate());
        var settings = Core.SettingsStore.LoadOrCreate(AppPaths.SettingsFile, AppPaths.Downloads());
        UiText.Use(settings.Language);
        Application.Run(new TrayContext(settings, firstRun: args.Contains("--first-run"), updated: args.Contains("--updated")));
    }
}
