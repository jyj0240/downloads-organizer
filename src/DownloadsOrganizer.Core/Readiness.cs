namespace DownloadsOrganizer.Core;

/// <summary>Why an item at the top of the watched folder was left alone.</summary>
public enum SkipReason
{
    StillDownloading,   // .crdownload, .part, .download, .opdownload, .partial
    TempFile,           // .tmp
    OfficeLockFile,     // ~$name.docx, exists while the document is open
    HiddenOrSystem,     // desktop.ini and other hidden or system files
    TooRecent,          // changed within the settle delay
    InUse,              // another program has the file open
    ReservedFolder,     // a folder this app files things into
    LinkFolder,         // a junction or symbolic link
    FoldersNotMoved,    // a subfolder while moving folders is off
    RestoredByUser,     // put back with undo and not changed since: the user wants it here
    Shortcut,           // .lnk, .url: moving desktop shortcuts would make app icons disappear
    NoMatchingRule,     // sorting by type is off for this folder and no rule matched
    ThereBeforeStart,   // already in the folder when the app started, and the user chose to leave those; "Organize now" still moves it
}

/// <summary>Decides whether an item is finished and safe to move. Never changes anything.</summary>
public static class Readiness
{
    static readonly HashSet<string> DownloadingExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".crdownload", ".part", ".partial", ".download", ".opdownload",
    };

    static readonly HashSet<string> ShortcutExtensions = new(StringComparer.OrdinalIgnoreCase) { ".lnk", ".url", ".appref-ms" };

    public static SkipReason? CheckFile(FileInfo file, DateTime nowUtc, TimeSpan settleDelay)
    {
        if (ShortcutExtensions.Contains(file.Extension))
        {
            return SkipReason.Shortcut;
        }
        if (DownloadingExtensions.Contains(file.Extension))
        {
            return SkipReason.StillDownloading;
        }
        if (file.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase))
        {
            return SkipReason.TempFile;
        }
        if (file.Name.StartsWith("~$", StringComparison.Ordinal))
        {
            return SkipReason.OfficeLockFile;
        }
        if ((file.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
        {
            return SkipReason.HiddenOrSystem;
        }
        if (nowUtc - file.LastWriteTimeUtc < settleDelay)
        {
            return SkipReason.TooRecent;
        }
        if (IsInUse(file.FullName))
        {
            return SkipReason.InUse;
        }
        return null;
    }

    public static SkipReason? CheckFolder(DirectoryInfo folder, DateTime nowUtc, TimeSpan settleDelay, bool moveFolders, ISet<string> reservedNames)
    {
        if (reservedNames.Contains(folder.Name))
        {
            return SkipReason.ReservedFolder;
        }
        if ((folder.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            return SkipReason.LinkFolder;
        }
        if ((folder.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
        {
            return SkipReason.HiddenOrSystem;
        }
        if (!moveFolders)
        {
            return SkipReason.FoldersNotMoved;
        }
        // A folder still being filled (e.g. an archive being extracted) keeps updating its own last-modified time.
        if (nowUtc - folder.LastWriteTimeUtc < settleDelay)
        {
            return SkipReason.TooRecent;
        }
        return null;
    }

    /// <summary>True when another process holds the file open. Opening for read with no sharing fails in that case.</summary>
    static bool IsInUse(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
}
