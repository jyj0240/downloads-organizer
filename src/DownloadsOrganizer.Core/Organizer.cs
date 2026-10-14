namespace DownloadsOrganizer.Core;

/// <summary>An item at the top of the watched folder and what would happen to it.</summary>
public sealed record PlanItem(Candidate Item, string? DestinationFolder, string? RuleName, SkipReason? Skipped)
{
    public bool WillMove => DestinationFolder is not null;
}

public sealed record MoveOutcome(PlanItem Plan, JournalEntry? Entry, string? Problem)
{
    public bool Moved => Entry is not null;
}

/// <summary>Looks at the top of the watched folder, decides where each finished item goes, and moves it.</summary>
public sealed class Organizer(OrganizerSettings settings, MoveJournal journal, TimeProvider? time = null)
{
    readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>What would happen now. Changes nothing.</summary>
    public IReadOnlyList<PlanItem> Plan()
    {
        var root = new DirectoryInfo(settings.WatchFolder);
        var now = _time.GetUtcNow().UtcDateTime;
        var reserved = ReservedFolderNames();
        var restored = journal.RestoredItems();
        var plan = new List<PlanItem>();

        foreach (var entry in root.EnumerateFileSystemInfos())
        {
            if (restored.Contains((entry.FullName.ToUpperInvariant(), entry.LastWriteTimeUtc)))
            {
                var isDir = entry is DirectoryInfo;
                var item = new Candidate(entry.FullName, entry.Name, isDir, isDir ? 0 : ((FileInfo)entry).Length, entry.LastWriteTimeUtc, null);
                plan.Add(new PlanItem(item, null, null, SkipReason.RestoredByUser));
                continue;
            }
            if (entry is FileInfo file)
            {
                var candidate = new Candidate(file.FullName, file.Name, false, file.Length, file.LastWriteTimeUtc, null);
                var skip = Readiness.CheckFile(file, now, settings.SettleDelay);
                if (skip is not null)
                {
                    plan.Add(new PlanItem(candidate, null, null, skip));
                    continue;
                }
                candidate = candidate with { SourceHost = MarkOfTheWeb.ReadSourceHost(file.FullName) };
                var (folder, rule) = Classify(candidate);
                plan.Add(folder is null
                    ? new PlanItem(candidate, null, null, SkipReason.NoMatchingRule)
                    : new PlanItem(candidate, folder, rule, null));
            }
            else if (entry is DirectoryInfo dir)
            {
                var candidate = new Candidate(dir.FullName, dir.Name, true, 0, dir.LastWriteTimeUtc, null);
                // Gathering subfolders into "Folders" is part of sorting by type.
                var skip = Readiness.CheckFolder(dir, now, settings.SettleDelay, settings.MoveFolders && settings.SortByType, reserved);
                plan.Add(skip is null
                    ? new PlanItem(candidate, Path.Combine(settings.WatchFolder, DefaultCategories.Folders), null, null)
                    : new PlanItem(candidate, null, null, skip));
            }
        }
        return plan;
    }

    /// <summary>Moves the planned items that are still ready. Each move is journaled; one batch per call.</summary>
    public IReadOnlyList<MoveOutcome> Apply(IEnumerable<PlanItem> plan)
    {
        var batch = Guid.NewGuid();
        var now = _time.GetUtcNow().UtcDateTime;
        var outcomes = new List<MoveOutcome>();

        foreach (var item in plan.Where(p => p.WillMove))
        {
            var source = item.Item.FullPath;
            // Things change between planning and moving: re-check the file right before touching it.
            if (!item.Item.IsDirectory)
            {
                var file = new FileInfo(source);
                if (!file.Exists)
                {
                    outcomes.Add(new MoveOutcome(item, null, "gone before it could be moved"));
                    continue;
                }
                var skip = Readiness.CheckFile(file, now, settings.SettleDelay);
                if (skip is not null)
                {
                    outcomes.Add(new MoveOutcome(item, null, "not ready: " + skip));
                    continue;
                }
            }
            else if (!Directory.Exists(source))
            {
                outcomes.Add(new MoveOutcome(item, null, "gone before it could be moved"));
                continue;
            }

            try
            {
                Directory.CreateDirectory(item.DestinationFolder!);
                var target = MoveWithoutOverwrite(source, item.DestinationFolder!, item.Item.IsDirectory);
                var entry = new JournalEntry
                {
                    Batch = batch,
                    TimeUtc = now,
                    Action = JournalAction.Move,
                    From = source,
                    To = target,
                    IsDirectory = item.Item.IsDirectory,
                    Rule = item.RuleName,
                    ItemLastWriteUtc = item.Item.LastWriteUtc,
                };
                journal.Append(entry);
                outcomes.Add(new MoveOutcome(item, entry, null));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                outcomes.Add(new MoveOutcome(item, null, e.Message));
            }
        }
        return outcomes;
    }

    /// <summary>Destination folder by the first matching rule, else by file type; null when neither applies.</summary>
    (string? Folder, string? Rule) Classify(Candidate item)
    {
        foreach (var rule in settings.Rules)
        {
            if (rule.Matches(item))
            {
                return (ResolveDestination(rule.Destination, item), rule.Name);
            }
        }
        return settings.SortByType
            ? (Path.Combine(settings.WatchFolder, DefaultCategories.ForExtension(item.Extension)), null)
            : (null, null);
    }

    string ResolveDestination(string destination, Candidate item)
    {
        var local = item.LastWriteUtc.ToLocalTime();
        var filled = destination.Replace("{yyyy}", local.ToString("yyyy")).Replace("{MM}", local.ToString("MM"));
        return Path.IsPathFullyQualified(filled) ? filled : Path.Combine(settings.WatchFolder, filled);
    }

    /// <summary>Top-level folder names that receive files, so they are never moved themselves.</summary>
    HashSet<string> ReservedFolderNames()
    {
        var names = new HashSet<string>(DefaultCategories.Names, StringComparer.OrdinalIgnoreCase);
        foreach (var rule in settings.Rules)
        {
            if (!Path.IsPathFullyQualified(rule.Destination))
            {
                var first = rule.Destination.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (first is not null)
                {
                    names.Add(first);
                }
            }
        }
        return names;
    }

    /// <summary>Moves into the folder; if the name is taken, tries "name (2).ext", "name (3).ext", ... Never overwrites.</summary>
    static string MoveWithoutOverwrite(string source, string folder, bool isDirectory)
    {
        var name = Path.GetFileName(source);
        var stem = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
        var extension = isDirectory ? "" : Path.GetExtension(name);

        for (var n = 1; n < 10_000; n++)
        {
            var target = Path.Combine(folder, n == 1 ? name : $"{stem} ({n}){extension}");
            if (File.Exists(target) || Directory.Exists(target))
            {
                continue;
            }
            try
            {
                if (isDirectory)
                {
                    Directory.Move(source, target);
                }
                else
                {
                    File.Move(source, target, overwrite: false);
                }
                return target;
            }
            catch (IOException) when (File.Exists(target) || Directory.Exists(target))
            {
                // Someone created the same name between the check and the move: try the next number.
            }
        }
        throw new IOException("no free name for " + name);
    }
}
