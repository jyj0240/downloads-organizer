using System.Diagnostics;
using Microsoft.Win32;
using DownloadsOrganizer.Core;

namespace DownloadsOrganizer.App;

/// <summary>
/// Installs and removes the app without an installer and without admin rights: the exe copies itself into
/// %LOCALAPPDATA%\Programs, adds a Start menu shortcut, a run-at-sign-in entry and an "Apps" uninstall entry (all HKCU).
/// </summary>
static class SelfInstaller
{
    public const string MutexName = @"Local\DownloadsOrganizer.SingleInstance";
    public const string ExitEventName = @"Local\DownloadsOrganizer.ExitRequest";

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "DownloadsOrganizer";
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DownloadsOrganizer";

    static string StartMenuShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppInfo.Name + ".lnk");

    /// <summary>True for the published single-file exe: its code is inside the exe, so no DownloadsOrganizer.dll sits next to it.
    /// During development (dotnet run) the dll is there and the app runs in place without installing.</summary>
    public static bool IsBundled => !File.Exists(Path.Combine(AppContext.BaseDirectory, "DownloadsOrganizer.dll"));

    public static string CurrentExe => Environment.ProcessPath!;

    public static bool IsRunningInstalled =>
        string.Equals(Path.GetFullPath(CurrentExe), AppPaths.InstalledExe, StringComparison.OrdinalIgnoreCase);

    public static bool IsInstalled => File.Exists(AppPaths.InstalledExe);

    public static Version CurrentVersion => typeof(SelfInstaller).Assembly.GetName().Version!;

    static bool IsInstalledBefore;

    static string OldExe => AppPaths.InstalledExe + ".old";

    /// <summary>Copies this exe into place when missing or older, registers it and starts the installed copy.
    /// If anything fails, the copy that was installed before is started again, so the app is never left stopped.</summary>
    public static void InstallAndLaunch()
    {
        var installed = IsInstalled && Version.TryParse(FileVersionInfo.GetVersionInfo(AppPaths.InstalledExe).FileVersion, out var v) ? v : null;
        var firstInstall = installed is null;
        var update = !firstInstall && CurrentVersion > installed;
        IsInstalledBefore = !firstInstall;
        try
        {
            if (firstInstall || update)
            {
                StopRunningInstance();
                Directory.CreateDirectory(AppPaths.InstallDir);
                ReplaceInstalledExe();
                AppLog.Write($"installed {CurrentVersion} (was {installed?.ToString() ?? "not installed"})");
            }
            Register();
        }
        catch
        {
            if (File.Exists(AppPaths.InstalledExe))
            {
                Process.Start(new ProcessStartInfo(AppPaths.InstalledExe) { UseShellExecute = true });
            }
            throw;
        }
        Process.Start(new ProcessStartInfo(AppPaths.InstalledExe, firstInstall ? "--first-run" : update ? "--updated" : "") { UseShellExecute = true });
    }

    /// <summary>Puts this exe in place of the installed one (the only file this app ever overwrites: its own).
    /// A process that just exited can hold its exe for a moment, so copying is retried; if the file is still held,
    /// it is renamed out of the way (Windows allows renaming an exe in use) and removed on the next start.</summary>
    static void ReplaceInstalledExe()
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Copy(CurrentExe, AppPaths.InstalledExe, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 10)
            {
                Thread.Sleep(300);
            }
            catch (IOException)
            {
                File.Move(AppPaths.InstalledExe, OldExe, overwrite: true);
                File.Copy(CurrentExe, AppPaths.InstalledExe, overwrite: false);
                return;
            }
        }
    }

    /// <summary>Removes the exe an update renamed out of the way and downloaded update exes that have done their job.
    /// Called when the installed copy starts.</summary>
    public static void CleanUpAfterUpdate()
    {
        var leftovers = new List<string> { OldExe };
        if (Directory.Exists(AppPaths.UpdatesDir))
        {
            leftovers.AddRange(Directory.EnumerateFiles(AppPaths.UpdatesDir, "DownloadsOrganizer-*"));
        }
        foreach (var file in leftovers)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Still held (the update exe may still be closing); next start tries again.
            }
        }
    }

    /// <summary>Removes the registrations and, after this process exits, the installed exe. Sorted files stay where they are.</summary>
    public static void Uninstall(bool deleteUserData, bool stopRunningCopy)
    {
        if (stopRunningCopy)
        {
            StopRunningInstance();
        }
        Registry.CurrentUser.OpenSubKey(RunKey, writable: true)?.DeleteValue(RunValue, throwOnMissingValue: false);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        if (File.Exists(StartMenuShortcut))
        {
            File.Delete(StartMenuShortcut);
        }
        if (deleteUserData)
        {
            DeleteOwnFolder(AppPaths.SettingsDir);
            DeleteOwnFolder(AppPaths.DataDir);
        }
        AppLog.Write("uninstalled");
        if (File.Exists(AppPaths.InstalledExe))
        {
            // The exe may be the one running this code: delete it from a short-lived shell after we exit.
            // "rmdir" without /s only removes the folder if nothing else is in it.
            var cmd = $"/c ping 127.0.0.1 -n 3 >nul & del /q \"{AppPaths.InstalledExe}\" & rmdir \"{AppPaths.InstallDir}\"";
            Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
        }
    }

    /// <summary>Whether Windows starts the installed app at sign-in.</summary>
    public static bool RunsAtStartup =>
        Registry.CurrentUser.OpenSubKey(RunKey)?.GetValue(RunValue) is string;

    public static void SetRunAtStartup(bool on)
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on)
        {
            run.SetValue(RunValue, $"\"{AppPaths.InstalledExe}\" --autostart");
        }
        else
        {
            run.DeleteValue(RunValue, throwOnMissingValue: false);
        }
    }

    static void Register()
    {
        // Keep the user's choice on updates: only first installs turn start-at-sign-in on.
        if (!IsInstalledBefore)
        {
            SetRunAtStartup(true);
        }
        else if (RunsAtStartup)
        {
            SetRunAtStartup(true);   // refresh the path
        }
        using (var app = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            app.SetValue("DisplayName", AppInfo.Name);
            app.SetValue("DisplayVersion", CurrentVersion.ToString(3));
            app.SetValue("Publisher", "jyj0240");
            app.SetValue("DisplayIcon", AppPaths.InstalledExe);
            app.SetValue("InstallLocation", AppPaths.InstallDir);
            app.SetValue("UninstallString", $"\"{AppPaths.InstalledExe}\" --uninstall");
            app.SetValue("QuietUninstallString", $"\"{AppPaths.InstalledExe}\" --uninstall --quiet");
            app.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            app.SetValue("EstimatedSize", (int)(new FileInfo(AppPaths.InstalledExe).Length / 1024), RegistryValueKind.DWord);
            app.SetValue("NoModify", 1, RegistryValueKind.DWord);
            app.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        CreateShortcut(StartMenuShortcut, AppPaths.InstalledExe);
    }

    /// <summary>Asks a running copy to exit (via a named event it listens on) and waits up to 10 s for it to go.</summary>
    static void StopRunningInstance()
    {
        if (!EventWaitHandle.TryOpenExisting(ExitEventName, out var exitRequest))
        {
            return;
        }
        using (exitRequest)
        {
            exitRequest.Set();
        }
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && Mutex.TryOpenExisting(MutexName, out var stillRunning))
        {
            stillRunning.Dispose();
            Thread.Sleep(200);
        }
        // The mutex goes away before the process has fully ended; wait for the process itself.
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(AppPaths.InstalledExe)))
        {
            using (process)
            {
                try
                {
                    if (process.Id != Environment.ProcessId
                        && string.Equals(process.MainModule?.FileName, AppPaths.InstalledExe, StringComparison.OrdinalIgnoreCase))
                    {
                        process.WaitForExit(Math.Max(0, (int)(deadline - DateTime.UtcNow).TotalMilliseconds));
                    }
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Exited meanwhile, or not ours to inspect.
                }
            }
        }
    }

    static void CreateShortcut(string lnkPath, string target)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            return;
        }
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic link = shell.CreateShortcut(lnkPath);
        link.TargetPath = target;
        link.WorkingDirectory = Path.GetDirectoryName(target);
        link.Description = AppInfo.Name;
        link.Save();
    }

    /// <summary>Deletes only our own data folder, and only if its name is ours.</summary>
    static void DeleteOwnFolder(string dir)
    {
        if (Directory.Exists(dir) && Path.GetFileName(dir) == AppInfo.DataFolderName)
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
