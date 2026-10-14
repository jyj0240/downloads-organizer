using DownloadsOrganizer.Core;

namespace DownloadsOrganizer.Core.Tests;

public class ReadinessTests
{
    static readonly TimeSpan Settle = TimeSpan.FromMinutes(5);

    [Theory]
    [InlineData("movie.mp4.crdownload", SkipReason.StillDownloading)]
    [InlineData("movie.mp4.part", SkipReason.StillDownloading)]
    [InlineData("setup.exe.download", SkipReason.StillDownloading)]
    [InlineData("x.tmp", SkipReason.TempFile)]
    [InlineData("~$report.docx", SkipReason.OfficeLockFile)]
    public void Unfinished_and_lock_files_are_left_alone(string name, SkipReason expected)
    {
        using var t = new TempFolder();
        var file = new FileInfo(t.File(name));
        Assert.Equal(expected, Readiness.CheckFile(file, t.NowUtc, Settle));
    }

    [Fact]
    public void Hidden_files_are_left_alone()
    {
        using var t = new TempFolder();
        var path = t.File("desktop.ini");
        File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.System);
        Assert.Equal(SkipReason.HiddenOrSystem, Readiness.CheckFile(new FileInfo(path), t.NowUtc, Settle));
    }

    [Fact]
    public void A_file_changed_within_the_settle_delay_waits()
    {
        using var t = new TempFolder();
        var fresh = new FileInfo(t.File("fresh.pdf", minutesAgo: 1));
        var settled = new FileInfo(t.File("settled.pdf", minutesAgo: 6));
        Assert.Equal(SkipReason.TooRecent, Readiness.CheckFile(fresh, t.NowUtc, Settle));
        Assert.Null(Readiness.CheckFile(settled, t.NowUtc, Settle));
    }

    [Fact]
    public void A_file_another_program_has_open_waits()
    {
        using var t = new TempFolder();
        var path = t.File("open.xlsx");
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            Assert.Equal(SkipReason.InUse, Readiness.CheckFile(new FileInfo(path), t.NowUtc, Settle));
        }
        Assert.Null(Readiness.CheckFile(new FileInfo(path), t.NowUtc, Settle));
    }

    [Fact]
    public void Folders_move_only_when_allowed_settled_and_not_reserved()
    {
        using var t = new TempFolder();
        var reserved = new HashSet<string>(DefaultCategories.Names, StringComparer.OrdinalIgnoreCase);
        var docs = new DirectoryInfo(t.Folder("Documents"));
        var extracted = new DirectoryInfo(t.Folder("extracted"));
        var extracting = new DirectoryInfo(t.Folder("extracting", minutesAgo: 1));

        Assert.Equal(SkipReason.ReservedFolder, Readiness.CheckFolder(docs, t.NowUtc, Settle, true, reserved));
        Assert.Equal(SkipReason.FoldersNotMoved, Readiness.CheckFolder(extracted, t.NowUtc, Settle, false, reserved));
        Assert.Equal(SkipReason.TooRecent, Readiness.CheckFolder(extracting, t.NowUtc, Settle, true, reserved));
        Assert.Null(Readiness.CheckFolder(extracted, t.NowUtc, Settle, true, reserved));
    }
}
