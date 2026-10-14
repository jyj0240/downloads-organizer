using DownloadsOrganizer.Core;

namespace DownloadsOrganizer.Core.Tests;

public class JournalTests
{
    [Fact]
    public void A_damaged_line_is_skipped_and_the_rest_still_reads()
    {
        using var t = new TempFolder();
        var journal = t.Journal();
        journal.Append(new JournalEntry { From = @"C:\a", To = @"C:\b", Action = JournalAction.Move });
        File.AppendAllText(journal.Path, "{\"from\": \"cut off by a cra" + Environment.NewLine);
        journal.Append(new JournalEntry { From = @"C:\c", To = @"C:\d", Action = JournalAction.Move });

        Assert.Equal(2, journal.ReadAll().Count);
    }

    [Fact]
    public void History_lists_moves_newest_first_and_marks_undone_ones()
    {
        using var t = new TempFolder();
        t.File(@"Downloads\first.pdf");
        var journal = t.Journal();
        var organizer = t.Organizer(journal: journal);
        organizer.Apply(organizer.Plan());
        journal.Undo(journal.LastBatch());
        t.File(@"Downloads\second.png");
        // first.pdf was put back by undo and is left alone; only second.png moves now.
        var again = t.Organizer(journal: journal);
        again.Apply(again.Plan());

        var history = journal.History();

        Assert.Equal(["second.png", "first.pdf"], history.Select(h => Path.GetFileName(h.Move.From)));
        Assert.False(history[0].Undone);
        Assert.True(history[1].Undone);
    }

    [Fact]
    public void Undoing_chosen_moves_leaves_the_others_and_ignores_already_undone()
    {
        using var t = new TempFolder();
        var a = t.File(@"Downloads\a.pdf");
        var b = t.File(@"Downloads\b.pdf");
        var journal = t.Journal();
        var settings = new OrganizerSettings { WatchFolder = t.Path("Downloads") };
        var auto = new AutoOrganizer(settings, journal, t.Clock);
        auto.Tick();
        var moveOfA = journal.History().Single(h => h.Move.From == a).Move;

        var first = auto.UndoMoves([moveOfA]);
        var second = auto.UndoMoves([moveOfA]);

        Assert.True(Assert.Single(first).Restored);
        Assert.Empty(second);
        Assert.True(File.Exists(a));
        Assert.False(File.Exists(b));
    }

    [Fact]
    public void A_rule_without_conditions_never_matches_and_is_dropped_on_load()
    {
        using var t = new TempFolder();
        var catchAll = new Rule { Name = "everything", Destination = "Junk" };
        var item = new Candidate(@"C:\x\a.pdf", "a.pdf", false, 1, DateTime.UtcNow, null);
        Assert.False(catchAll.Matches(item));

        var path = t.Path("settings.json");
        var watch = t.Folder("Downloads");
        SettingsStore.Save(path, new AppSettings
        {
            Folders = [new WatchedFolder(watch)],
            Rules = [catchAll, new Rule { Name = "pdfs", Destination = "Pdf", NamePattern = "*.pdf" }],
        });
        var loaded = SettingsStore.LoadOrCreate(path, watch, (f, _) => Directory.Exists(f) ? null : FolderProblem.Missing);
        Assert.Equal("pdfs", Assert.Single(loaded.Rules).Name);
    }
}
