using System.IO.Enumeration;

namespace DownloadsOrganizer.Core;

/// <summary>
/// A user rule. Every condition that is set must match; unset conditions are ignored.
/// Rules are checked in order before the built-in file-type folders.
/// </summary>
public sealed record Rule
{
    public required string Name { get; init; }

    /// <summary>Folder to move into: relative to the watched folder, or an absolute path.
    /// May contain {yyyy} and {MM}, filled from the file's last-modified date.</summary>
    public required string Destination { get; init; }

    /// <summary>Wildcard on the file name, e.g. "*statement*.pdf" (case-insensitive).</summary>
    public string? NamePattern { get; init; }

    /// <summary>Site the file was downloaded from; matches the host or any subdomain ("example.com" matches "www.example.com").</summary>
    public string? SourceDomain { get; init; }

    public long? MinBytes { get; init; }
    public long? MaxBytes { get; init; }

    /// <summary>A rule without any condition would match every file, so it is never used.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasCondition =>
        !string.IsNullOrWhiteSpace(NamePattern) || !string.IsNullOrWhiteSpace(SourceDomain) || MinBytes is not null || MaxBytes is not null;

    public bool Matches(Candidate item)
    {
        if (!HasCondition)
        {
            return false;
        }
        if (NamePattern is { Length: > 0 } && !FileSystemName.MatchesSimpleExpression(NamePattern, item.Name, ignoreCase: true))
        {
            return false;
        }
        if (SourceDomain is { Length: > 0 } && !DomainMatches(item.SourceHost, SourceDomain))
        {
            return false;
        }
        if (MinBytes is long min && item.Bytes < min)
        {
            return false;
        }
        if (MaxBytes is long max && item.Bytes > max)
        {
            return false;
        }
        return true;
    }

    static bool DomainMatches(string? host, string domain)
    {
        if (host is null)
        {
            return false;
        }
        var d = domain.Trim().TrimStart('.').ToLowerInvariant();
        return host == d || host.EndsWith("." + d, StringComparison.Ordinal);
    }
}
