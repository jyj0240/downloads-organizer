namespace DownloadsOrganizer.Core;

/// <summary>What the engine needs to tidy one folder.</summary>
public sealed record OrganizerSettings
{
    /// <summary>Folder to keep tidy, e.g. the user's Downloads.</summary>
    public required string WatchFolder { get; init; }

    /// <summary>How long a file must stay unchanged before it is moved. Gives time to open a file right after downloading it,
    /// and covers "still growing": every write moves the last-modified time forward.</summary>
    public TimeSpan SettleDelay { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Also move subfolders (for example an extracted archive) into "Folders".</summary>
    public bool MoveFolders { get; init; }

    /// <summary>Send files that match no rule to a folder for their type. Off: only files matching a rule move.</summary>
    public bool SortByType { get; init; } = true;

    public IReadOnlyList<Rule> Rules { get; init; } = [];

    /// <summary>Items that arrived in the folder before this time are left where they are, except by "Organize now".
    /// Set when the user chooses, on first run, to leave the files that were already there.</summary>
    public DateTime? LeaveAloneBeforeUtc { get; init; }
}

/// <summary>A folder the user asked to keep tidy.</summary>
public sealed record WatchedFolder(string Location, bool SortByType = true, DateTime? LeaveAloneBeforeUtc = null);

/// <summary>Everything in settings.json: the folders, what they share, and app preferences.</summary>
public sealed record AppSettings
{
    public IReadOnlyList<WatchedFolder> Folders { get; init; } = [];
    public TimeSpan SettleDelay { get; init; } = TimeSpan.FromMinutes(5);
    public bool MoveFolders { get; init; }

    /// <summary>Shared by every folder. A relative destination is created inside each folder.</summary>
    public IReadOnlyList<Rule> Rules { get; init; } = [];

    /// <summary>Show a notification after each round that moved something.</summary>
    public bool Notify { get; init; } = true;

    /// <summary>"auto" (follow Windows), "en" or "ko".</summary>
    public string Language { get; init; } = "auto";

    /// <summary>Check GitHub for a newer release once a day and install it.</summary>
    public bool AutoUpdate { get; init; } = true;

    public OrganizerSettings For(WatchedFolder folder) => new()
    {
        WatchFolder = folder.Location,
        SortByType = folder.SortByType,
        LeaveAloneBeforeUtc = folder.LeaveAloneBeforeUtc,
        SettleDelay = SettleDelay,
        MoveFolders = MoveFolders,
        Rules = Rules,
    };
}
