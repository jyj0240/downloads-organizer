using System.Text.Json;
using System.Text.Json.Serialization;

namespace DownloadsOrganizer.Core;

public enum JournalAction { Move, Undo }

/// <summary>One line of the journal. Every move and every undo is recorded; nothing is moved without a record.</summary>
public sealed record JournalEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid Batch { get; init; }
    public DateTime TimeUtc { get; init; }
    public JournalAction Action { get; init; }
    public required string From { get; init; }
    public required string To { get; init; }
    public bool IsDirectory { get; init; }
    public string? Rule { get; init; }

    /// <summary>The item's last-modified time when it was moved. A move keeps it, so an item put back by undo
    /// can be recognised as "the same item, untouched" and left alone.</summary>
    public DateTime? ItemLastWriteUtc { get; init; }

    /// <summary>For an undo: the move it reversed.</summary>
    public Guid? UndoOf { get; init; }
}

public sealed record UndoResult(JournalEntry Move, bool Restored, string? Problem);

public sealed record HistoryRow(JournalEntry Move, bool Undone);

/// <summary>Append-only journal in JSON Lines: one entry per line, readable in any text editor.</summary>
public sealed class MoveJournal(string path, TimeProvider? time = null)
{
    static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    readonly TimeProvider _time = time ?? TimeProvider.System;
    readonly object _gate = new();

    public string Path { get; } = path;

    public void Append(JournalEntry entry)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.AppendAllText(Path, JsonSerializer.Serialize(entry, Json) + Environment.NewLine);
        }
    }

    public IReadOnlyList<JournalEntry> ReadAll()
    {
        lock (_gate)
        {
            if (!File.Exists(Path))
            {
                return [];
            }
            var entries = new List<JournalEntry>();
            foreach (var line in File.ReadLines(Path))
            {
                if (line.Length == 0)
                {
                    continue;
                }
                try
                {
                    if (JsonSerializer.Deserialize<JournalEntry>(line, Json) is { } entry)
                    {
                        entries.Add(entry);
                    }
                }
                catch (JsonException)
                {
                    // A damaged line (cut off by a crash, or hand-edited) is skipped; the rest of the history stays usable.
                }
            }
            return entries;
        }
    }

    /// <summary>Moves that have not been undone yet, newest first.</summary>
    public IReadOnlyList<JournalEntry> OpenMoves()
    {
        var all = ReadAll();
        var undone = all.Where(e => e.Action == JournalAction.Undo && e.UndoOf is not null).Select(e => e.UndoOf!.Value).ToHashSet();
        return all.Where(e => e.Action == JournalAction.Move && !undone.Contains(e.Id)).Reverse().ToList();
    }

    /// <summary>Moves for the history screen, newest first, each marked whether it has been undone.</summary>
    public IReadOnlyList<HistoryRow> History(int max = 2000)
    {
        var all = ReadAll();
        var undone = all.Where(e => e.Action == JournalAction.Undo && e.UndoOf is not null).Select(e => e.UndoOf!.Value).ToHashSet();
        return all.Where(e => e.Action == JournalAction.Move).Reverse().Take(max)
            .Select(e => new HistoryRow(e, undone.Contains(e.Id)))
            .ToList();
    }

    /// <summary>Items the user put back with undo, as (path, last-modified time) at the time of the undo.</summary>
    public IReadOnlySet<(string Path, DateTime LastWriteUtc)> RestoredItems() =>
        ReadAll()
            .Where(e => e.Action == JournalAction.Undo && e.ItemLastWriteUtc is not null)
            .Select(e => (e.To.ToUpperInvariant(), e.ItemLastWriteUtc!.Value))
            .ToHashSet();

    /// <summary>The most recent batch of moves that has not been undone.</summary>
    public IReadOnlyList<JournalEntry> LastBatch()
    {
        var open = OpenMoves();
        return open.Count == 0 ? [] : open.Where(e => e.Batch == open[0].Batch).ToList();
    }

    /// <summary>Puts items back where they were, newest first. Skips an item that is gone or whose original place is taken.</summary>
    public IReadOnlyList<UndoResult> Undo(IEnumerable<JournalEntry> moves)
    {
        var batch = Guid.NewGuid();
        var results = new List<UndoResult>();
        foreach (var move in moves.OrderByDescending(m => m.TimeUtc))
        {
            var exists = move.IsDirectory ? Directory.Exists(move.To) : File.Exists(move.To);
            if (!exists)
            {
                results.Add(new UndoResult(move, false, "no longer at the moved-to place"));
                continue;
            }
            if (File.Exists(move.From) || Directory.Exists(move.From))
            {
                results.Add(new UndoResult(move, false, "original place is taken"));
                continue;
            }
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(move.From)!);
                if (move.IsDirectory)
                {
                    Directory.Move(move.To, move.From);
                }
                else
                {
                    File.Move(move.To, move.From, overwrite: false);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                results.Add(new UndoResult(move, false, e.Message));
                continue;
            }
            Append(new JournalEntry
            {
                Batch = batch,
                TimeUtc = _time.GetUtcNow().UtcDateTime,
                Action = JournalAction.Undo,
                From = move.To,
                To = move.From,
                IsDirectory = move.IsDirectory,
                Rule = move.Rule,
                UndoOf = move.Id,
                ItemLastWriteUtc = move.ItemLastWriteUtc,
            });
            results.Add(new UndoResult(move, true, null));
        }
        return results;
    }
}
