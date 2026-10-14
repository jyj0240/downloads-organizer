using DownloadsOrganizer.Core;

namespace DownloadsOrganizer.Core.Tests;

public class MultiFolderTests
{
    [Fact]
    public void System_profile_and_appdata_folders_are_refused()
    {
        string Special(Environment.SpecialFolder f) => Environment.GetFolderPath(f);

        Assert.Equal(FolderProblem.DriveRoot, FolderSafety.Check(Path.GetPathRoot(Special(Environment.SpecialFolder.Windows))!, []));
        Assert.Equal(FolderProblem.SystemFolder, FolderSafety.Check(Special(Environment.SpecialFolder.Windows), []));
        Assert.Equal(FolderProblem.SystemFolder, FolderSafety.Check(Special(Environment.SpecialFolder.ProgramFiles), []));
        Assert.Equal(FolderProblem.ProfileRoot, FolderSafety.Check(Special(Environment.SpecialFolder.UserProfile), []));
        Assert.Equal(FolderProblem.AppData, FolderSafety.Check(Special(Environment.SpecialFolder.LocalApplicationData), []));
        Assert.Equal(FolderProblem.AppData, FolderSafety.Check(Path.GetTempPath(), []));
        Assert.Equal(FolderProblem.Missing, FolderSafety.Check(@"Z:\no\such\folder", []));
    }

    [Fact]
    public void Nested_and_duplicate_folders_are_refused()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var inside = Directory.EnumerateDirectories(desktop).FirstOrDefault();

        Assert.Equal(FolderProblem.Duplicate, FolderSafety.Check(desktop, [desktop + Path.DirectorySeparatorChar]));
        if (inside is not null)
        {
            Assert.Equal(FolderProblem.InsideAnother, FolderSafety.Check(inside, [desktop]));
            Assert.Equal(FolderProblem.ContainsAnother, FolderSafety.Check(desktop, [inside]));
        }
        Assert.Null(FolderSafety.Check(desktop, []));
    }

    [Fact]
    public void Cloud_synced_folders_are_recognised_by_name()
    {
        Assert.True(FolderSafety.IsCloudSynced(@"C:\Users\someone\OneDrive - Contoso\Documents"));
        Assert.True(FolderSafety.IsCloudSynced(@"D:\Dropbox\Inbox"));
        Assert.False(FolderSafety.IsCloudSynced(@"D:\Work\Inbox"));
    }

    [Fact]
    public void Shortcuts_are_never_moved()
    {
        using var t = new TempFolder();
        foreach (var name in new[] { "App.lnk", "Site.url" })
        {
            var file = new FileInfo(t.File(name));
            Assert.Equal(SkipReason.Shortcut, Readiness.CheckFile(file, t.NowUtc, TimeSpan.FromMinutes(5)));
        }
    }

    [Fact]
    public void With_sorting_by_type_off_only_rule_matches_move()
    {
        using var t = new TempFolder();
        t.File(@"Downloads\Screenshot 2026-10-14.png");
        t.File(@"Downloads\notes.txt");
        t.Folder(@"Downloads\some folder");
        var organizer = t.Organizer(s => s with
        {
            SortByType = false,
            MoveFolders = true,
            Rules = [new Rule { Name = "screenshots", Destination = "Screenshots", NamePattern = "Screenshot*" }],
        });

        var plan = organizer.Plan();
        organizer.Apply(plan);

        Assert.True(File.Exists(t.Path("Downloads", "Screenshots", "Screenshot 2026-10-14.png")));
        Assert.True(File.Exists(t.Path("Downloads", "notes.txt")));
        Assert.Equal(SkipReason.NoMatchingRule, plan.Single(p => p.Item.Name == "notes.txt").Skipped);
        Assert.True(Directory.Exists(t.Path("Downloads", "some folder")));
    }

    [Fact]
    public void A_group_tidies_each_folder_and_undoes_across_them()
    {
        using var t = new TempFolder();
        var a = t.File(@"Downloads\a.pdf");
        var b = t.File(@"Desktop\b.pdf");
        var settings = new AppSettings { Folders = [new WatchedFolder(t.Path("Downloads")), new WatchedFolder(t.Path("Desktop"))] };
        var journal = t.Journal();
        using var group = new OrganizerGroup(settings, journal, t.Clock);

        var moved = group.TickAll(force: true).Where(o => o.Moved).ToList();

        Assert.Equal(2, moved.Count);
        Assert.True(File.Exists(t.Path("Downloads", "Documents", "a.pdf")));
        Assert.True(File.Exists(t.Path("Desktop", "Documents", "b.pdf")));
        var undone = group.UndoMoves(journal.History().Select(h => h.Move));
        Assert.Equal(2, undone.Count(r => r.Restored));
        Assert.True(File.Exists(a) && File.Exists(b));
    }
}
