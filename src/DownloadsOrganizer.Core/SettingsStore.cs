using System.Text.Json;

namespace DownloadsOrganizer.Core;

/// <summary>Reads and writes settings.json. Missing, unreadable or unsafe values fall back to defaults,
/// so a bad hand edit never stops the app or points it at a system folder.</summary>
public static class SettingsStore
{
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    static readonly string[] Languages = ["auto", "en", "ko"];

    sealed record StoredFolder
    {
        public string? Path { get; init; }
        public bool? SortByType { get; init; }
    }

    sealed record Stored
    {
        /// <summary>Single folder, as written by 0.2 and 0.3. Read for migration, never written.</summary>
        public string? WatchFolder { get; init; }
        public List<StoredFolder>? Folders { get; init; }
        public double? SettleMinutes { get; init; }
        public bool? MoveFolders { get; init; }
        public List<Rule>? Rules { get; init; }
        public bool? Notify { get; init; }
        public string? Language { get; init; }
        public bool? AutoUpdate { get; init; }
    }

    /// <summary>Loads settings; writes a default file first if there is none, so users can see it.</summary>
    /// <param name="folderCheck">Safety check for each folder; tests replace it to use temp folders.</param>
    public static AppSettings LoadOrCreate(string path, string defaultFolder, Func<string, IEnumerable<string>, FolderProblem?>? folderCheck = null)
    {
        folderCheck ??= FolderSafety.Check;
        var defaults = new AppSettings { Folders = [new WatchedFolder(defaultFolder)] };
        if (!File.Exists(path))
        {
            Save(path, defaults);
            return defaults;
        }
        Stored? stored;
        try
        {
            stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(path), Json);
        }
        catch (JsonException)
        {
            return defaults;
        }
        if (stored is null)
        {
            return defaults;
        }

        var wanted = new List<WatchedFolder>();
        if (stored.Folders is { } listed)
        {
            wanted.AddRange(listed.Where(f => !string.IsNullOrWhiteSpace(f.Path)).Select(f => new WatchedFolder(f.Path!, f.SortByType ?? true)));
        }
        else if (!string.IsNullOrWhiteSpace(stored.WatchFolder))
        {
            wanted.Add(new WatchedFolder(stored.WatchFolder));   // settings from 0.2 / 0.3
        }
        var folders = new List<WatchedFolder>();
        foreach (var folder in wanted)
        {
            if (folderCheck(folder.Location, folders.Select(f => f.Location)) is null)
            {
                folders.Add(folder);
            }
        }
        if (folders.Count == 0)
        {
            folders = [.. defaults.Folders];
        }

        return defaults with
        {
            Folders = folders,
            SettleDelay = stored.SettleMinutes is double m && m >= 0 && m <= 24 * 60 ? TimeSpan.FromMinutes(m) : defaults.SettleDelay,
            MoveFolders = stored.MoveFolders ?? defaults.MoveFolders,
            Rules = stored.Rules is { } rules ? rules.Where(r => !string.IsNullOrWhiteSpace(r.Destination) && r.HasCondition).ToList() : defaults.Rules,
            Notify = stored.Notify ?? defaults.Notify,
            Language = stored.Language is { } lang && Languages.Contains(lang) ? lang : defaults.Language,
            AutoUpdate = stored.AutoUpdate ?? defaults.AutoUpdate,
        };
    }

    public static void Save(string path, AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var stored = new Stored
        {
            Folders = [.. settings.Folders.Select(f => new StoredFolder { Path = f.Location, SortByType = f.SortByType })],
            SettleMinutes = settings.SettleDelay.TotalMinutes,
            MoveFolders = settings.MoveFolders,
            Rules = [.. settings.Rules],
            Notify = settings.Notify,
            Language = settings.Language,
            AutoUpdate = settings.AutoUpdate,
        };
        // Write to a temp file, then swap, so a crash mid-save never leaves a half-written settings file.
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(stored, Json));
        File.Move(temp, path, overwrite: true);
    }
}
