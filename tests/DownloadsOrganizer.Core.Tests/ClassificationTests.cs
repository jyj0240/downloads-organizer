using DownloadsOrganizer.Core;

namespace DownloadsOrganizer.Core.Tests;

public class ClassificationTests
{
    [Theory]
    [InlineData(".pdf", "Documents")]
    [InlineData(".PDF", "Documents")]
    [InlineData(".hwpx", "Documents")]
    [InlineData(".jpg", "Images")]
    [InlineData(".zip", "Archives")]
    [InlineData(".exe", "Installers")]
    [InlineData(".mp4", "Media")]
    [InlineData(".csv", "Data")]
    [InlineData(".srt", "Subtitles")]
    [InlineData(".xyz", "Other")]
    [InlineData("", "Other")]
    public void File_type_picks_the_built_in_folder(string extension, string expected) =>
        Assert.Equal(expected, DefaultCategories.ForExtension(extension));

    static Candidate Item(string name, long bytes = 100, string? host = null) =>
        new(@"C:\x\" + name, name, false, bytes, DateTime.UtcNow, host);

    [Fact]
    public void Name_pattern_is_case_insensitive()
    {
        var rule = new Rule { Name = "statements", Destination = "Finance", NamePattern = "*statement*.pdf" };
        Assert.True(rule.Matches(Item("Bank STATEMENT 2026-09.PDF")));
        Assert.False(rule.Matches(Item("statement.docx")));
    }

    [Fact]
    public void Source_domain_matches_subdomains_but_not_lookalikes()
    {
        var rule = new Rule { Name = "bank", Destination = "Finance", SourceDomain = "example.com" };
        Assert.True(rule.Matches(Item("a.pdf", host: "example.com")));
        Assert.True(rule.Matches(Item("a.pdf", host: "files.example.com")));
        Assert.False(rule.Matches(Item("a.pdf", host: "notexample.com")));
        Assert.False(rule.Matches(Item("a.pdf", host: null)));
    }

    [Fact]
    public void Size_limits_and_all_conditions_must_hold()
    {
        var rule = new Rule { Name = "big zips", Destination = "Big", NamePattern = "*.zip", MinBytes = 1_000 };
        Assert.True(rule.Matches(Item("a.zip", bytes: 5_000)));
        Assert.False(rule.Matches(Item("a.zip", bytes: 10)));
        Assert.False(rule.Matches(Item("a.7z", bytes: 5_000)));
    }

    [Fact]
    public void Mark_of_the_web_gives_the_download_site()
    {
        using var t = new TempFolder();
        var withHost = t.File("a.pdf");
        TempFolder.MarkDownloadedFrom(withHost, "https://Files.Example.com/doc/a.pdf", "https://www.example.com/page");
        var referrerOnly = t.File("b.pdf");
        System.IO.File.WriteAllText(referrerOnly + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://ref.example.org/x\r\n");
        var unmarked = t.File("c.pdf");

        Assert.Equal("files.example.com", MarkOfTheWeb.ReadSourceHost(withHost));
        Assert.Equal("ref.example.org", MarkOfTheWeb.ReadSourceHost(referrerOnly));
        Assert.Null(MarkOfTheWeb.ReadSourceHost(unmarked));
    }
}
