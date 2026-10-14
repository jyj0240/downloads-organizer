namespace DownloadsOrganizer.Core;

/// <summary>
/// One <see cref="AutoOrganizer"/> per watched folder, sharing one journal and one lock, so rounds in different folders
/// and undo never run at the same time.
/// </summary>
public sealed class OrganizerGroup : IDisposable
{
    readonly object _gate = new();
    readonly MoveJournal _journal;
    readonly List<AutoOrganizer> _organizers;
    bool _paused;

    public OrganizerGroup(AppSettings settings, MoveJournal journal, TimeProvider? time = null)
    {
        _journal = journal;
        _organizers = settings.Folders
            .Where(f => Directory.Exists(f.Location))
            .Select(f => new AutoOrganizer(settings.For(f), journal, time, _gate))
            .ToList();
        foreach (var organizer in _organizers)
        {
            organizer.Moved += moved => Moved?.Invoke(moved);
            organizer.Failed += e => Failed?.Invoke(e);
        }
    }

    /// <summary>Raised after a round that moved something. Called on a background thread.</summary>
    public event Action<IReadOnlyList<MoveOutcome>>? Moved;

    public event Action<Exception>? Failed;

    public int FolderCount => _organizers.Count;

    public bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            foreach (var organizer in _organizers)
            {
                organizer.Paused = value;
            }
        }
    }

    public void Start()
    {
        foreach (var organizer in _organizers)
        {
            organizer.Start();
        }
    }

    public void MarkChanged()
    {
        foreach (var organizer in _organizers)
        {
            organizer.MarkChanged();
        }
    }

    /// <summary>One round in every folder; <paramref name="force"/> runs even when paused or nothing changed.</summary>
    public IReadOnlyList<MoveOutcome> TickAll(bool force = false) =>
        _organizers.SelectMany(o => o.Tick(force) ?? []).ToList();

    public int UndoableCount()
    {
        lock (_gate)
        {
            return _journal.LastBatch().Count;
        }
    }

    public IReadOnlyList<UndoResult> UndoLastBatch()
    {
        lock (_gate)
        {
            return _journal.Undo(_journal.LastBatch());
        }
    }

    /// <summary>Puts chosen moves back (from the history screen). Moves already undone are ignored.</summary>
    public IReadOnlyList<UndoResult> UndoMoves(IEnumerable<JournalEntry> moves)
    {
        lock (_gate)
        {
            var open = _journal.OpenMoves().Select(m => m.Id).ToHashSet();
            return _journal.Undo(moves.Where(m => open.Contains(m.Id)));
        }
    }

    public void Dispose()
    {
        foreach (var organizer in _organizers)
        {
            organizer.Dispose();
        }
    }
}
