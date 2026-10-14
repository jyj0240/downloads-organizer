using DownloadsOrganizer.Core;

// Command-line companion to the tray app. "preview" shows what would move and why, and changes nothing.
const string Usage = "usage: dorg preview <folder> [--settle-minutes N] [--move-folders]";

if (args.Length < 2 || args[0] != "preview")
{
    Console.Error.WriteLine(Usage);
    return 2;
}

var folder = Path.GetFullPath(args[1]);
if (!Directory.Exists(folder))
{
    Console.Error.WriteLine("folder not found: " + folder);
    return 2;
}

var settle = TimeSpan.FromMinutes(5);
var moveFolders = false;
for (var i = 2; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--settle-minutes" when i + 1 < args.Length && double.TryParse(args[i + 1], out var minutes):
            settle = TimeSpan.FromMinutes(minutes);
            i++;
            break;
        case "--move-folders":
            moveFolders = true;
            break;
        default:
            Console.Error.WriteLine("unknown option: " + args[i]);
            Console.Error.WriteLine(Usage);
            return 2;
    }
}

var settings = new OrganizerSettings { WatchFolder = folder, SettleDelay = settle, MoveFolders = moveFolders };
// Planning never writes the journal; the path is only needed to construct the organizer.
var journal = new MoveJournal(Path.Combine(Path.GetTempPath(), AppInfo.DataFolderName, "preview-unused.jsonl"));
var plan = new Organizer(settings, journal).Plan();

foreach (var item in plan.OrderBy(p => p.WillMove ? 0 : 1).ThenBy(p => p.Item.Name, StringComparer.OrdinalIgnoreCase))
{
    if (item.WillMove)
    {
        var target = Path.GetRelativePath(folder, item.DestinationFolder!);
        Console.WriteLine($"MOVE\t{target}\t{item.Item.Name}{(item.RuleName is null ? "" : "\trule: " + item.RuleName)}");
    }
    else
    {
        Console.WriteLine($"SKIP\t{item.Skipped}\t{item.Item.Name}");
    }
}

Console.WriteLine();
Console.WriteLine("summary:");
foreach (var group in plan.Where(p => p.WillMove).GroupBy(p => Path.GetRelativePath(folder, p.DestinationFolder!)).OrderBy(g => g.Key))
{
    Console.WriteLine($"  {group.Key,-12} {group.Count(),5}");
}
var skipped = plan.Count(p => !p.WillMove);
Console.WriteLine($"  {"(skipped)",-12} {skipped,5}");
return 0;
