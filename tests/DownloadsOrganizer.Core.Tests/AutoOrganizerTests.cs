using DownloadsOrganizer.Core;

namespace DownloadsOrganizer.Core.Tests;

/// <summary>A clock the test can move forward.</summary>
sealed class MovableTime(DateTimeOffset start) : TimeProvider
{
    DateTimeOffset _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

public class AutoOrganizerTests
{
    static (AutoOrganizer Auto, MovableTime Clock, MoveJournal Journal) Make(TempFolder t)
    {
        var clock = new MovableTime(new DateTimeOffset(t.NowUtc));
        var journal = new MoveJournal(t.Path("_journal", "journal.jsonl"), clock);
        var settings = new OrganizerSettings { WatchFolder = t.Path("Downloads"), SettleDelay = TimeSpan.FromMinutes(5) };
        Directory.CreateDirectory(settings.WatchFolder);
        return (new AutoOrganizer(settings, journal, clock), clock, journal);
    }

    [Fact]
    public void A_tick_with_no_change_and_nothing_waiting_does_no_work()
    {
        using var t = new TempFolder();
        t.File(@"Downloads\a.pdf");
        var (auto, _, _) = Make(t);

        Assert.NotNull(auto.Tick());          // first tick always looks
        Assert.Null(auto.Tick());             // nothing changed, nothing waiting

        auto.MarkChanged();
        Assert.NotNull(auto.Tick());
    }

    [Fact]
    public void A_recent_file_is_moved_once_it_settles()
    {
        using var t = new TempFolder();
        var path = t.File(@"Downloads\fresh.pdf", minutesAgo: 1);
        var (auto, clock, _) = Make(t);

        Assert.DoesNotContain(auto.Tick()!, o => o.Moved);
        clock.Advance(TimeSpan.FromMinutes(5));
        var moved = auto.Tick()!.Where(o => o.Moved).ToList();   // still waiting, so the tick runs without a change event

        Assert.Single(moved);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Paused_skips_unless_forced_and_reports_moves()
    {
        using var t = new TempFolder();
        t.File(@"Downloads\a.pdf");
        var (auto, _, _) = Make(t);
        IReadOnlyList<MoveOutcome>? reported = null;
        auto.Moved += m => reported = m;

        auto.Paused = true;
        Assert.Null(auto.Tick());
        Assert.NotNull(auto.Tick(force: true));
        Assert.Single(reported!);
    }

    [Fact]
    public void An_undone_file_is_left_alone_until_the_user_changes_it()
    {
        using var t = new TempFolder();
        var path = t.File(@"Downloads\keep-here.pdf");
        var (auto, clock, journal) = Make(t);
        auto.Tick();
        journal.Undo(journal.LastBatch());
        Assert.True(File.Exists(path));

        auto.MarkChanged();
        auto.Tick();
        Assert.True(File.Exists(path));        // not moved again

        File.SetLastWriteTimeUtc(path, clock.GetUtcNow().UtcDateTime.AddMinutes(-10));   // the user edited it
        auto.MarkChanged();
        auto.Tick();
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Disposing_waits_for_a_round_under_way_and_no_round_runs_after()
    {
        using var t = new TempFolder();
        var path = t.File(@"Downloads\a.pdf");
        var gate = new object();
        var clock = new MovableTime(new DateTimeOffset(t.NowUtc));
        var journal = new MoveJournal(t.Path("_journal", "journal.jsonl"), clock);
        var auto = new AutoOrganizer(new OrganizerSettings { WatchFolder = t.Path("Downloads") }, journal, clock, gate);

        Task disposing;
        lock (gate)   // stands in for a round holding the lock
        {
            disposing = Task.Run(auto.Dispose);
            Assert.False(disposing.Wait(300));
        }
        Assert.True(disposing.Wait(5000));

        Assert.Null(auto.Tick(force: true));
        Assert.True(File.Exists(path));
    }
}
