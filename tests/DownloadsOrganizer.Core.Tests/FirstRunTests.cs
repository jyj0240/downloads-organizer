using DownloadsOrganizer.Core;

namespace DownloadsOrganizer.Core.Tests;

/// <summary>"Leave them" on first run: items that were already in the folder stay, new arrivals are sorted,
/// and "Organize now" still sorts everything.</summary>
public class FirstRunTests
{
    static (string Old, string New, OrganizerSettings Settings, MoveJournal Journal) Make(TempFolder t)
    {
        var cutOff = t.NowUtc.AddHours(-1);
        var old = t.File(@"Downloads\was-already-here.pdf");
        File.SetCreationTimeUtc(old, cutOff.AddHours(-1));
        var arrived = t.File(@"Downloads\arrived-later.pdf");   // created now, after the cut-off
        var settings = new OrganizerSettings { WatchFolder = t.Path("Downloads"), LeaveAloneBeforeUtc = cutOff };
        return (old, arrived, settings, new MoveJournal(t.Path("_journal", "journal.jsonl")));
    }

    [Fact]
    public void Items_from_before_the_cut_off_are_left_and_new_ones_sorted()
    {
        using var t = new TempFolder();
        var (old, arrived, settings, journal) = Make(t);

        var plan = new Organizer(settings, journal).Plan();

        Assert.Equal(SkipReason.ThereBeforeStart, plan.Single(p => p.Item.FullPath == old).Skipped);
        Assert.True(plan.Single(p => p.Item.FullPath == arrived).WillMove);
    }

    [Fact]
    public void Organize_now_also_sorts_what_was_left()
    {
        using var t = new TempFolder();
        var (old, arrived, settings, journal) = Make(t);
        var auto = new AutoOrganizer(settings, journal);

        auto.Tick();
        Assert.True(File.Exists(old));
        Assert.False(File.Exists(arrived));

        auto.Tick(force: true);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(t.Path("Downloads", "Documents", "was-already-here.pdf")));
    }

    [Fact]
    public void The_cut_off_is_saved_per_folder()
    {
        using var t = new TempFolder();
        var path = t.Path("settings.json");
        var downloads = t.Folder("Downloads");
        var cutOff = new DateTime(2026, 10, 14, 13, 30, 0, DateTimeKind.Utc);

        SettingsStore.Save(path, new AppSettings { Folders = [new WatchedFolder(downloads, LeaveAloneBeforeUtc: cutOff)] });
        var loaded = SettingsStore.LoadOrCreate(path, downloads, (f, _) => Directory.Exists(f) ? null : FolderProblem.Missing);

        Assert.Equal(cutOff, Assert.Single(loaded.Folders).LeaveAloneBeforeUtc);
        Assert.Equal(cutOff, loaded.For(loaded.Folders[0]).LeaveAloneBeforeUtc);
    }
}
