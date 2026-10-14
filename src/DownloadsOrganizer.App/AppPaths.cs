using System.Runtime.InteropServices;
using DownloadsOrganizer.Core;

namespace DownloadsOrganizer.App;

/// <summary>Where the app lives and keeps its files. All per-user, so no admin rights are needed.</summary>
static class AppPaths
{
    public static string InstallDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppInfo.DataFolderName);

    public static string InstalledExe { get; } = Path.Combine(InstallDir, "DownloadsOrganizer.exe");

    /// <summary>Settings roam with the user profile; the journal and log stay on this PC.</summary>
    public static string SettingsDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppInfo.DataFolderName);

    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.DataFolderName);

    public static string SettingsFile { get; } = Path.Combine(SettingsDir, "settings.json");
    public static string JournalFile { get; } = Path.Combine(DataDir, "journal.jsonl");
    public static string LogFile { get; } = Path.Combine(DataDir, "app.log");

    /// <summary>Downloaded update exes wait here until they have installed themselves.</summary>
    public static string UpdatesDir { get; } = Path.Combine(DataDir, "updates");

    /// <summary>The user's Downloads folder, wherever they moved it.</summary>
    public static string Downloads()
    {
        var downloadsId = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        if (SHGetKnownFolderPath(downloadsId, 0, IntPtr.Zero, out var raw) == 0)
        {
            try
            {
                return Marshal.PtrToStringUni(raw)!;
            }
            finally
            {
                Marshal.FreeCoTaskMem(raw);
            }
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr token, out IntPtr path);
}

/// <summary>A plain text log for errors, next to the journal. Never throws.</summary>
static class AppLog
{
    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.AppendAllText(AppPaths.LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Logging must never take the app down.
        }
    }

    public static void Write(Exception e) => Write(e.ToString());
}
