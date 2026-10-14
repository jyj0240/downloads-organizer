namespace DownloadsOrganizer.Core;

/// <summary>
/// Reads the "downloaded from the internet" mark Windows keeps in the Zone.Identifier stream
/// of a downloaded file. Browsers record the site there (HostUrl, ReferrerUrl). Read-only: never written or removed.
/// </summary>
public static class MarkOfTheWeb
{
    public const string StreamName = "Zone.Identifier";

    /// <summary>Host of the download (HostUrl, else ReferrerUrl), e.g. "www.example.com"; null when there is no mark.</summary>
    public static string? ReadSourceHost(string filePath)
    {
        string text;
        try
        {
            text = File.ReadAllText(filePath + ":" + StreamName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return null;
        }
        return HostFrom(Value(text, "HostUrl")) ?? HostFrom(Value(text, "ReferrerUrl"));
    }

    static string? Value(string text, string key)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[(key.Length + 1)..];
            }
        }
        return null;
    }

    static string? HostFrom(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host) ? uri.Host.ToLowerInvariant() : null;
}
