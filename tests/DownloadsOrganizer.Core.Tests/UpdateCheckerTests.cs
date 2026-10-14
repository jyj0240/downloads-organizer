using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DownloadsOrganizer.Core;

namespace DownloadsOrganizer.Core.Tests;

/// <summary>Answers requests from a table instead of the network; anything else is 404.</summary>
sealed class FakeServer : HttpMessageHandler
{
    readonly Dictionary<string, byte[]> _files = [];

    public void Serve(string url, byte[] body) => _files[url] = body;
    public void Serve(string url, string body) => Serve(url, Encoding.UTF8.GetBytes(body));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel) =>
        Task.FromResult(_files.TryGetValue(request.RequestUri!.ToString(), out var body)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
            : new HttpResponseMessage(HttpStatusCode.NotFound));
}

public class UpdateCheckerTests
{
    const string Latest = "https://api.example.test/releases/latest";
    const string ExeUrl = "https://example.test/download/DownloadsOrganizer.exe";
    static readonly Version Current = new(0, 4, 1, 0);
    static readonly byte[] Exe = Encoding.ASCII.GetBytes("pretend this is the new exe");

    static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    static string ReleaseJson(string tag, bool prerelease = false, string? digest = "default", string url = ExeUrl, string name = UpdateChecker.ExeName) =>
        JsonSerializer.Serialize(new
        {
            tag_name = tag,
            draft = false,
            prerelease,
            assets = new[]
            {
                new { name, browser_download_url = url, digest = digest == "default" ? "sha256:" + Sha256(Exe) : digest, size = Exe.Length },
            },
        });

    static (UpdateChecker Checker, FakeServer Server) Make(string? releaseJson)
    {
        var server = new FakeServer();
        if (releaseJson is not null)
        {
            server.Serve(Latest, releaseJson);
        }
        server.Serve(ExeUrl, Exe);
        return (new UpdateChecker(new HttpClient(server), new Uri(Latest)), server);
    }

    [Fact]
    public async Task A_higher_release_with_a_checksum_is_offered()
    {
        var (checker, _) = Make(ReleaseJson("v0.4.2"));

        var update = await checker.FindNewerAsync(Current);

        Assert.NotNull(update);
        Assert.Equal(new Version(0, 4, 2), update.Version);
        Assert.Equal(new Uri(ExeUrl), update.Download);
        Assert.Equal(Sha256(Exe), update.Sha256);
    }

    [Theory]
    [InlineData("v0.4.1")]          // same version: 0.4.1 is 0.4.1.0
    [InlineData("v0.4.0")]          // older
    [InlineData("v0.5.0-beta")]     // not a plain version
    [InlineData("latest")]
    public async Task Same_older_or_odd_tags_are_not_offered(string tag)
    {
        var (checker, _) = Make(ReleaseJson(tag));
        Assert.Null(await checker.FindNewerAsync(Current));
    }

    [Fact]
    public async Task Pre_releases_and_exes_without_a_checksum_or_over_plain_http_are_not_offered()
    {
        Assert.Null(await Make(ReleaseJson("v0.5.0", prerelease: true)).Checker.FindNewerAsync(Current));
        Assert.Null(await Make(ReleaseJson("v0.5.0", digest: null)).Checker.FindNewerAsync(Current));
        Assert.Null(await Make(ReleaseJson("v0.5.0", url: "http://example.test/DownloadsOrganizer.exe")).Checker.FindNewerAsync(Current));
        Assert.Null(await Make(ReleaseJson("v0.5.0", name: "Something.exe")).Checker.FindNewerAsync(Current));
    }

    [Fact]
    public async Task A_local_test_server_over_http_is_allowed()
    {
        var (checker, _) = Make(ReleaseJson("v0.5.0", url: "http://127.0.0.1:8123/DownloadsOrganizer.exe"));
        Assert.NotNull(await checker.FindNewerAsync(Current));
    }

    [Fact]
    public async Task No_release_or_a_private_repository_is_an_error_not_up_to_date()
    {
        var (checker, _) = Make(releaseJson: null);   // GitHub answers 404 for both
        await Assert.ThrowsAsync<HttpRequestException>(() => checker.FindNewerAsync(Current));
    }

    [Fact]
    public async Task A_download_that_matches_its_checksum_is_kept()
    {
        using var t = new TempFolder();
        var (checker, _) = Make(ReleaseJson("v0.4.2"));
        var update = (await checker.FindNewerAsync(Current))!;

        var path = await checker.DownloadAsync(update, t.Path("updates"));

        Assert.Equal(t.Path("updates", "DownloadsOrganizer-0.4.2.exe"), path);
        Assert.Equal(Exe, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task A_download_that_does_not_match_is_deleted_and_refused()
    {
        using var t = new TempFolder();
        var (checker, server) = Make(ReleaseJson("v0.4.2"));
        var update = (await checker.FindNewerAsync(Current))!;
        server.Serve(ExeUrl, Encoding.ASCII.GetBytes("tampered with, same length!"));   // same size, different bytes

        await Assert.ThrowsAsync<InvalidDataException>(() => checker.DownloadAsync(update, t.Path("updates")));

        Assert.Empty(Directory.GetFiles(t.Path("updates")));
    }
}
