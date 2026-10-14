using DownloadsOrganizer.Core;

namespace DownloadsOrganizer.Core.Tests;

public class SettingsStoreTests
{
    // Temp folders live under AppData, which the real safety check rules out; tests only require that folders exist.
    static FolderProblem? ExistsOnly(string folder, IEnumerable<string> _) => Directory.Exists(folder) ? null : FolderProblem.Missing;

    [Fact]
    public void First_load_writes_defaults_and_everything_round_trips()
    {
        using var t = new TempFolder();
        var path = t.Path("settings.json");
        var downloads = t.Folder("Downloads");
        var desktop = t.Folder("Desktop");

        var first = SettingsStore.LoadOrCreate(path, downloads, ExistsOnly);
        Assert.True(File.Exists(path));
        Assert.Equal(downloads, Assert.Single(first.Folders).Location);

        SettingsStore.Save(path, first with
        {
            Folders = [new WatchedFolder(downloads), new WatchedFolder(desktop, SortByType: false)],
            SettleDelay = TimeSpan.FromMinutes(2),
            Rules = [new Rule { Name = "bank", Destination = "Finance", SourceDomain = "example.com" }],
            Language = "ko",
            AutoUpdate = false,
        });
        var again = SettingsStore.LoadOrCreate(path, downloads, ExistsOnly);

        Assert.Equal([downloads, desktop], again.Folders.Select(f => f.Location));
        Assert.False(again.Folders[1].SortByType);
        Assert.Equal(TimeSpan.FromMinutes(2), again.SettleDelay);
        Assert.Equal("example.com", Assert.Single(again.Rules).SourceDomain);
        Assert.Equal("ko", again.Language);
        Assert.False(again.AutoUpdate);
    }

    [Fact]
    public void A_single_folder_file_from_older_versions_is_carried_over()
    {
        using var t = new TempFolder();
        var downloads = t.Folder("Downloads");
        var other = t.Folder("Other place");
        var old = t.File("old.json", content: "{\"watchFolder\": " + System.Text.Json.JsonSerializer.Serialize(other) + ", \"settleMinutes\": 3}");

        var loaded = SettingsStore.LoadOrCreate(old, downloads, ExistsOnly);

        Assert.Equal(other, Assert.Single(loaded.Folders).Location);
        Assert.True(loaded.Folders[0].SortByType);
        Assert.Equal(TimeSpan.FromMinutes(3), loaded.SettleDelay);
        Assert.True(loaded.AutoUpdate);   // files from before updates existed get them turned on
    }

    [Fact]
    public void Broken_files_unsafe_or_missing_folders_fall_back_to_defaults()
    {
        using var t = new TempFolder();
        var downloads = t.Folder("Downloads");
        var broken = t.File("broken.json", content: "{ not json");
        var unsafeFolders = t.File("unsafe.json", content:
            "{\"folders\": [{\"path\": \"C:\\\\Windows\"}, {\"path\": \"Z:\\\\no\\\\such\\\\folder\"}], \"settleMinutes\": -3}");

        Assert.Equal(downloads, Assert.Single(SettingsStore.LoadOrCreate(broken, downloads, ExistsOnly).Folders).Location);
        var fixedUp = SettingsStore.LoadOrCreate(unsafeFolders, downloads);   // real safety check
        Assert.Equal(downloads, Assert.Single(fixedUp.Folders).Location);
        Assert.Equal(TimeSpan.FromMinutes(5), fixedUp.SettleDelay);
    }
}
