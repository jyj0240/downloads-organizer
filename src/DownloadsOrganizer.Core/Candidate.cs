namespace DownloadsOrganizer.Core;

/// <summary>What the engine knows about an item at the top of the watched folder. File contents are never read.</summary>
public sealed record Candidate(
    string FullPath,
    string Name,
    bool IsDirectory,
    long Bytes,
    DateTime LastWriteUtc,
    string? SourceHost)
{
    public string Extension => IsDirectory ? "" : Path.GetExtension(Name);
}
