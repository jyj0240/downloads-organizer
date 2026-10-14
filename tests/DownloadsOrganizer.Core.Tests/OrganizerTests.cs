using DownloadsOrganizer.Core;

namespace DownloadsOrganizer.Core.Tests;

public class OrganizerTests
{
    [Fact]
    public void Plan_changes_nothing()
    {
        using var t = new TempFolder();
        var pdf = t.File(@"Downloads\a.pdf");
        var plan = t.Organizer().Plan();

        Assert.Equal(t.Path("Downloads", "Documents"), Assert.Single(plan).DestinationFolder);
        Assert.True(File.Exists(pdf));
        Assert.False(Directory.Exists(t.Path("Downloads", "Documents")));
    }

    [Fact]
    public void Apply_moves_by_file_type_and_journals_each_move()
    {
        using var t = new TempFolder();
        t.File(@"Downloads\a.pdf");
        t.File(@"Downloads\b.zip");
        t.File(@"Downloads\c.unknownext");
        var journal = t.Journal();
        var organizer = t.Organizer(journal: journal);

        var outcomes = organizer.Apply(organizer.Plan());

        Assert.All(outcomes, o => Assert.True(o.Moved, o.Problem));
        Assert.True(File.Exists(t.Path("Downloads", "Documents", "a.pdf")));
        Assert.True(File.Exists(t.Path("Downloads", "Archives", "b.zip")));
        Assert.True(File.Exists(t.Path("Downloads", "Other", "c.unknownext")));
        Assert.Equal(3, journal.OpenMoves().Count);
    }

    [Fact]
    public void Never_overwrites_an_existing_file()
    {
        using var t = new TempFolder();
        t.File(@"Downloads\Documents\a.pdf", content: "older");
        t.File(@"Downloads\Documents\a (2).pdf", content: "older 2");
        t.File(@"Downloads\a.pdf", content: "newer");
        var organizer = t.Organizer();

        organizer.Apply(organizer.Plan());

        Assert.Equal("older", File.ReadAllText(t.Path("Downloads", "Documents", "a.pdf")));
        Assert.Equal("older 2", File.ReadAllText(t.Path("Downloads", "Documents", "a (2).pdf")));
        Assert.Equal("newer", File.ReadAllText(t.Path("Downloads", "Documents", "a (3).pdf")));
    }

    [Fact]
    public void User_rules_come_before_file_type_and_their_folder_is_not_moved()
    {
        using var t = new TempFolder();
        t.File(@"Downloads\Bank statement 2026-09.pdf");
        t.File(@"Downloads\notes.pdf");
        t.Folder(@"Downloads\Finance");
        var organizer = t.Organizer(s => s with
        {
            MoveFolders = true,
            Rules = [new Rule { Name = "statements", Destination = "Finance", NamePattern = "*statement*" }],
        });

        var plan = organizer.Plan();
        organizer.Apply(plan);

        Assert.True(File.Exists(t.Path("Downloads", "Finance", "Bank statement 2026-09.pdf")));
        Assert.True(File.Exists(t.Path("Downloads", "Documents", "notes.pdf")));
        Assert.Equal(SkipReason.ReservedFolder, plan.Single(p => p.Item.Name == "Finance").Skipped);
    }

    [Fact]
    public void Source_site_rule_works_and_the_download_mark_survives_the_move()
    {
        using var t = new TempFolder();
        var file = t.File(@"Downloads\invoice.pdf");
        TempFolder.MarkDownloadedFrom(file, "https://billing.example.com/invoice.pdf");
        var organizer = t.Organizer(s => s with
        {
            Rules = [new Rule { Name = "example billing", Destination = @"Bills\{yyyy}", SourceDomain = "example.com" }],
        });

        var outcome = Assert.Single(organizer.Apply(organizer.Plan()));

        var moved = outcome.Entry!.To;
        Assert.Equal(t.Path("Downloads", "Bills", t.NowUtc.AddMinutes(-60).ToLocalTime().ToString("yyyy"), "invoice.pdf"), moved);
        Assert.Equal("billing.example.com", MarkOfTheWeb.ReadSourceHost(moved));
    }

    [Fact]
    public void Folders_move_only_when_turned_on()
    {
        using var t = new TempFolder();
        t.Folder(@"Downloads\extracted");
        t.Folder(@"Downloads\Images");

        var off = t.Organizer();
        off.Apply(off.Plan());
        Assert.True(Directory.Exists(t.Path("Downloads", "extracted")));

        var on = t.Organizer(s => s with { MoveFolders = true });
        on.Apply(on.Plan());
        Assert.True(Directory.Exists(t.Path("Downloads", "Folders", "extracted")));
        Assert.True(Directory.Exists(t.Path("Downloads", "Images")));
    }

    [Fact]
    public void A_file_opened_after_planning_is_not_moved()
    {
        using var t = new TempFolder();
        var path = t.File(@"Downloads\report.xlsx");
        var organizer = t.Organizer();
        var plan = organizer.Plan();

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            var outcome = Assert.Single(organizer.Apply(plan));
            Assert.False(outcome.Moved);
        }
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Undo_puts_the_last_batch_back()
    {
        using var t = new TempFolder();
        var a = t.File(@"Downloads\a.pdf");
        var b = t.File(@"Downloads\b.png");
        var journal = t.Journal();
        var organizer = t.Organizer(journal: journal);
        organizer.Apply(organizer.Plan());

        var results = journal.Undo(journal.LastBatch());

        Assert.All(results, r => Assert.True(r.Restored, r.Problem));
        Assert.True(File.Exists(a));
        Assert.True(File.Exists(b));
        Assert.Empty(journal.OpenMoves());
    }

    [Fact]
    public void Undo_does_not_overwrite_a_file_that_took_the_original_place()
    {
        using var t = new TempFolder();
        var original = t.File(@"Downloads\a.pdf", content: "first");
        var journal = t.Journal();
        var organizer = t.Organizer(journal: journal);
        organizer.Apply(organizer.Plan());
        File.WriteAllText(original, "new download with the same name");

        var result = Assert.Single(journal.Undo(journal.LastBatch()));

        Assert.False(result.Restored);
        Assert.Equal("new download with the same name", File.ReadAllText(original));
        Assert.Equal("first", File.ReadAllText(t.Path("Downloads", "Documents", "a.pdf")));
        Assert.Single(journal.OpenMoves());
    }
}
