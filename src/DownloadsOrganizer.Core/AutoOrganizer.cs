namespace DownloadsOrganizer.Core;

/// <summary>
/// Keeps the watched folder tidy in the background. A folder watcher marks the folder as changed; a timer then plans
/// and moves. When nothing changed and nothing is waiting (too recent, in use, downloading), a tick does no work at all.
/// </summary>
public sealed class AutoOrganizer : IDisposable
{
    static readonly HashSet<SkipReason> Waiting = [SkipReason.TooRecent, SkipReason.InUse, SkipReason.StillDownloading];

    readonly Organizer _organizer;
    readonly MoveJournal _journal;
    readonly OrganizerSettings _settings;
    readonly TimeProvider _time;
    readonly object _gate;
    FileSystemWatcher? _watcher;
    ITimer? _timer;
    bool _changed = true;
    bool _waiting;
    bool _stopped;

    /// <param name="gate">Lock shared with other folders' organizers so their rounds and undo never overlap.</param>
    public AutoOrganizer(OrganizerSettings settings, MoveJournal journal, TimeProvider? time = null, object? gate = null)
    {
        _settings = settings;
        _journal = journal;
        _time = time ?? TimeProvider.System;
        _gate = gate ?? new object();
        _organizer = new Organizer(settings, journal, _time);
    }

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(30);

    public bool Paused { get; set; }

    /// <summary>Raised after a tick that moved something. Called on a background thread.</summary>
    public event Action<IReadOnlyList<MoveOutcome>>? Moved;

    /// <summary>Raised when a tick fails unexpectedly. Called on a background thread.</summary>
    public event Action<Exception>? Failed;

    public void Start()
    {
        _watcher = new FileSystemWatcher(_settings.WatchFolder)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Created += (_, _) => MarkChanged();
        _watcher.Changed += (_, _) => MarkChanged();
        _watcher.Renamed += (_, _) => MarkChanged();
        _watcher.Deleted += (_, _) => MarkChanged();
        _watcher.Error += (_, _) => MarkChanged();   // event buffer overflow: some changes were lost, so look again
        _watcher.EnableRaisingEvents = true;
        _timer = _time.CreateTimer(_ => SafeTick(), null, TimeSpan.Zero, PollInterval);
    }

    public void MarkChanged()
    {
        lock (_gate)
        {
            _changed = true;
        }
    }

    /// <summary>
    /// One round: plan and move what is ready. Returns null when skipped (paused, or nothing changed and nothing waiting).
    /// <paramref name="force"/> runs even then, for "organize now", and also moves items left alone since the first run.
    /// </summary>
    public IReadOnlyList<MoveOutcome>? Tick(bool force = false)
    {
        IReadOnlyList<MoveOutcome> outcomes;
        lock (_gate)
        {
            if (_stopped || (!force && (Paused || (!_changed && !_waiting))))
            {
                return null;
            }
            _changed = false;
            var plan = _organizer.Plan(includeLeftAlone: force);
            outcomes = _organizer.Apply(plan);
            _waiting = plan.Any(p => p.Skipped is SkipReason s && Waiting.Contains(s)) || outcomes.Any(o => !o.Moved);
        }
        var moved = outcomes.Where(o => o.Moved).ToList();
        if (moved.Count > 0)
        {
            Moved?.Invoke(moved);
        }
        return outcomes;
    }

    /// <summary>Number of items in the last batch that can still be undone.</summary>
    public int UndoableCount()
    {
        lock (_gate)
        {
            return _journal.LastBatch().Count;
        }
    }

    /// <summary>Puts the last batch back. Runs under the same lock as a tick, so the two never overlap.</summary>
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

    void SafeTick()
    {
        try
        {
            Tick();
        }
        catch (Exception e)
        {
            Failed?.Invoke(e);
        }
    }

    /// <summary>Stops watching and waits for a round already under way, so exiting (or an update) never cuts a move in half.</summary>
    public void Dispose()
    {
        _timer?.Dispose();
        _watcher?.Dispose();
        lock (_gate)
        {
            _stopped = true;
        }
    }
}
