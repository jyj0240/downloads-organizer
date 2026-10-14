namespace DownloadsOrganizer.Core;

public enum FolderProblem
{
    Missing,            // does not exist
    DriveRoot,          // C:\ and the like
    SystemFolder,       // Windows, Program Files, ProgramData
    ProfileRoot,        // C:\Users or the user's own profile folder itself
    AppData,            // inside AppData: programs keep their own files there
    InsideAnother,      // inside a folder already on the list
    ContainsAnother,    // a folder already on the list is inside it
    Duplicate,          // already on the list
}

/// <summary>Decides which folders are safe to tidy. Moving files out of system or program folders breaks things.</summary>
public static class FolderSafety
{
    /// <summary>A problem that rules the folder out, or null when it can be added.</summary>
    public static FolderProblem? Check(string folder, IEnumerable<string> alreadyWatched)
    {
        if (!Directory.Exists(folder))
        {
            return FolderProblem.Missing;
        }
        var full = Normalize(folder);
        if (Path.GetPathRoot(full) is { } root && Same(full, Normalize(root)))
        {
            return FolderProblem.DriveRoot;
        }
        foreach (var system in SystemFolders())
        {
            if (Same(full, system) || IsInside(full, system))
            {
                return FolderProblem.SystemFolder;
            }
        }
        var profile = Normalize(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (Same(full, profile) || Same(full, Normalize(Path.GetDirectoryName(profile) ?? profile)))
        {
            return FolderProblem.ProfileRoot;
        }
        foreach (var appData in new[] { Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData })
        {
            var dir = Normalize(Environment.GetFolderPath(appData));
            var appDataRoot = Normalize(Path.GetDirectoryName(dir) ?? dir);   // ...\AppData
            if (Same(full, appDataRoot) || IsInside(full, appDataRoot))
            {
                return FolderProblem.AppData;
            }
        }
        foreach (var other in alreadyWatched.Select(Normalize))
        {
            if (Same(full, other))
            {
                return FolderProblem.Duplicate;
            }
            if (IsInside(full, other))
            {
                return FolderProblem.InsideAnother;
            }
            if (IsInside(other, full))
            {
                return FolderProblem.ContainsAnother;
            }
        }
        return null;
    }

    /// <summary>True for folders a sync client mirrors elsewhere (OneDrive, Dropbox, Google Drive, iCloud):
    /// a move there also happens on the user's other devices.</summary>
    public static bool IsCloudSynced(string folder)
    {
        var full = Normalize(folder);
        foreach (var variable in new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" })
        {
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } root && (Same(full, Normalize(root)) || IsInside(full, Normalize(root))))
            {
                return true;
            }
        }
        var parts = full.Split(Path.DirectorySeparatorChar);
        return parts.Any(p => p.StartsWith("OneDrive", StringComparison.OrdinalIgnoreCase)
            || p.Equals("Dropbox", StringComparison.OrdinalIgnoreCase)
            || p.Equals("Google Drive", StringComparison.OrdinalIgnoreCase)
            || p.Equals("My Drive", StringComparison.OrdinalIgnoreCase)
            || p.Equals("iCloudDrive", StringComparison.OrdinalIgnoreCase));
    }

    static IEnumerable<string> SystemFolders() =>
        new[]
        {
            Environment.SpecialFolder.Windows,
            Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.CommonApplicationData,
        }
        .Select(Environment.GetFolderPath)
        .Where(p => p.Length > 0)
        .Select(Normalize);

    static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    static bool IsInside(string path, string folder) =>
        path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
