using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace DownloadsOrganizer.Core;

/// <summary>A release newer than the running copy: its version and the exe to download.</summary>
public sealed record AvailableUpdate(Version Version, Uri Download, string Sha256, long Size);

/// <summary>
/// Asks GitHub for the latest release and downloads its exe. This is the app's only network use.
/// A release counts only if its tag is a plain version (v1.2.3) higher than ours and its exe comes with
/// the SHA-256 GitHub publishes for release files; the download must match that checksum.
/// </summary>
public sealed class UpdateChecker(HttpClient http, Uri latestRelease)
{
    public const string ExeName = "DownloadsOrganizer.exe";

    public static readonly Uri GitHubLatest = new("https://api.github.com/repos/jyj0240/downloads-organizer/releases/latest");

    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    sealed record Release(string? TagName, bool Draft, bool Prerelease, List<Asset>? Assets);

    sealed record Asset(string? Name, string? BrowserDownloadUrl, string? Digest, long Size);

    /// <summary>The newer release, or null when this copy is up to date. Throws when GitHub can't be reached
    /// or answers with an error (including 404 while the repository is private or has no release).</summary>
    public async Task<AvailableUpdate?> FindNewerAsync(Version current, CancellationToken cancel = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, latestRelease);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, cancel);
        response.EnsureSuccessStatusCode();
        var release = await response.Content.ReadFromJsonAsync<Release>(Json, cancel);
        if (release is null || release.Draft || release.Prerelease || ParseTag(release.TagName) is not { } version
            || Normalize(version) <= Normalize(current))
        {
            return null;
        }
        var exe = release.Assets?.FirstOrDefault(a => a.Name == ExeName);
        if (exe is null
            || !Uri.TryCreate(exe.BrowserDownloadUrl, UriKind.Absolute, out var download)
            || !(download.Scheme == Uri.UriSchemeHttps || download.IsLoopback)
            || exe.Digest is not { } digest
            || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            return null;   // no exe, or nothing to check it against: not worth installing
        }
        return new AvailableUpdate(version, download, digest["sha256:".Length..].ToLowerInvariant(), exe.Size);
    }

    /// <summary>Downloads the exe into <paramref name="folder"/> and returns its path. A file that does not match the
    /// published size and SHA-256 is deleted and an <see cref="InvalidDataException"/> thrown.</summary>
    public async Task<string> DownloadAsync(AvailableUpdate update, string folder, CancellationToken cancel = default)
    {
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, $"DownloadsOrganizer-{update.Version}.exe");
        var partial = target + ".part";
        try
        {
            using (var response = await http.GetAsync(update.Download, HttpCompletionOption.ResponseHeadersRead, cancel))
            {
                response.EnsureSuccessStatusCode();
                await using var file = File.Create(partial);
                await response.Content.CopyToAsync(file, cancel);
            }
            string sha256;
            await using (var file = File.OpenRead(partial))
            {
                if (update.Size > 0 && file.Length != update.Size)
                {
                    throw new InvalidDataException($"Update {update.Version}: expected {update.Size} bytes, got {file.Length}");
                }
                sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancel));
            }
            if (sha256 != update.Sha256)
            {
                throw new InvalidDataException($"Update {update.Version}: SHA-256 {sha256} does not match the published {update.Sha256}");
            }
            File.Move(partial, target, overwrite: true);
            return target;
        }
        finally
        {
            File.Delete(partial);   // no-op once moved
        }
    }

    /// <summary>"v1.2.3" → 1.2.3. Pre-release tags such as "v1.2.3-beta" are not versions and return null.</summary>
    static Version? ParseTag(string? tag) =>
        tag is { Length: > 1 } && Version.TryParse(tag.TrimStart('v', 'V'), out var version) ? version : null;

    /// <summary>1.2.3 and 1.2.3.0 are the same version; <see cref="Version"/> alone would rank 1.2.3 lower.</summary>
    static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
