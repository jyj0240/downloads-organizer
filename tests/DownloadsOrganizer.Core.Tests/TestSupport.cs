using DownloadsOrganizer.Core;

namespace DownloadsOrganizer.Core.Tests;

/// <summary>A clock that stays where the test puts it.</summary>
sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>A throw-away folder with real files, deleted after the test.</summary>
sealed class TempFolder : IDisposable
{
    public string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dorg-tests", Guid.NewGuid().ToString("N"));
    public DateTime NowUtc { get; } = DateTime.UtcNow;

    public TempFolder() => Directory.CreateDirectory(Root);

    public string Path(params string[] parts) => System.IO.Path.Combine([Root, .. parts]);

    /// <summary>Creates a file whose last change was <paramref name="minutesAgo"/> minutes before <see cref="NowUtc"/>.</summary>
    public string File(string relative, string content = "x", double minutesAgo = 60)
    {
        var full = Path(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        System.IO.File.SetLastWriteTimeUtc(full, NowUtc.AddMinutes(-minutesAgo));
        return full;
    }

    public string Folder(string relative, double minutesAgo = 60)
    {
        var full = Path(relative);
        Directory.CreateDirectory(full);
        Directory.SetLastWriteTimeUtc(full, NowUtc.AddMinutes(-minutesAgo));
        return full;
    }

    /// <summary>Adds the browser's "downloaded from" mark, as Chrome and Edge write it.</summary>
    public static void MarkDownloadedFrom(string file, string hostUrl, string? referrerUrl = null)
    {
        var text = "[ZoneTransfer]\r\nZoneId=3\r\n" + (referrerUrl is null ? "" : $"ReferrerUrl={referrerUrl}\r\n") + $"HostUrl={hostUrl}\r\n";
        var lastWrite = System.IO.File.GetLastWriteTimeUtc(file);
        System.IO.File.WriteAllText(file + ":" + MarkOfTheWeb.StreamName, text);
        System.IO.File.SetLastWriteTimeUtc(file, lastWrite);
    }

    public TimeProvider Clock => new FixedTime(new DateTimeOffset(NowUtc));

    public MoveJournal Journal() => new(Path("_journal", "journal.jsonl"), Clock);

    public Organizer Organizer(Func<OrganizerSettings, OrganizerSettings>? tweak = null, MoveJournal? journal = null)
    {
        var settings = new OrganizerSettings { WatchFolder = Path("Downloads"), SettleDelay = TimeSpan.FromMinutes(5) };
        Directory.CreateDirectory(settings.WatchFolder);
        return new Organizer(tweak is null ? settings : tweak(settings), journal ?? Journal(), Clock);
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                System.IO.File.SetAttributes(f, FileAttributes.Normal);
            }
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A file still held by a failed test; the OS temp cleanup will take it.
        }
    }
}
