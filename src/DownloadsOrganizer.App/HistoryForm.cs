using System.Diagnostics;
using DownloadsOrganizer.Core;

namespace DownloadsOrganizer.App;

/// <summary>Every move, newest first: search by name, undo the chosen ones, open where a file went.</summary>
sealed class HistoryForm : Form
{
    readonly Func<IReadOnlyList<HistoryRow>> _load;
    readonly Func<IEnumerable<JournalEntry>, IReadOnlyList<UndoResult>> _undo;
    readonly TextBox _search = new() { Dock = DockStyle.Fill };
    readonly CheckBox _showUndone = new() { AutoSize = true, Anchor = AnchorStyles.Left, Checked = true };
    readonly ListView _list = new()
    {
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = true,
        HideSelection = false,
        Dock = DockStyle.Fill,
    };
    readonly Label _count = Ui.NewLabel("");
    readonly Button _undoButton;
    readonly Button _openButton;
    IReadOnlyList<HistoryRow> _rows = [];

    public HistoryForm(Func<IReadOnlyList<HistoryRow>> load, Func<IEnumerable<JournalEntry>, IReadOnlyList<UndoResult>> undo)
    {
        _load = load;
        _undo = undo;
        Ui.SetUpWindow(this, UiText.Get("HistoryTitle"));
        Size = new Size(LogicalToDeviceUnits(900), LogicalToDeviceUnits(520));
        MinimumSize = new Size(LogicalToDeviceUnits(600), LogicalToDeviceUnits(320));

        _search.PlaceholderText = UiText.Get("HistorySearch");
        _search.TextChanged += (_, _) => Fill();
        _showUndone.Text = UiText.Get("HistoryShowUndone");
        _showUndone.CheckedChanged += (_, _) => Fill();

        foreach (var key in new[] { "ColTime", "ColFile", "ColMovedTo", "ColFrom", "ColRule", "ColStatus" })
        {
            _list.Columns.Add(UiText.Get(key));
        }
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.DoubleClick += (_, _) => OpenLocation();

        _undoButton = Ui.NewButton(UiText.Get("BtnUndoSelected"), (_, _) => UndoSelected());
        _openButton = Ui.NewButton(UiText.Get("BtnOpenLocation"), (_, _) => OpenLocation());
        var close = Ui.NewButton(UiText.Get("BtnClose"), (_, _) => Close());
        CancelButton = close;

        var top = Ui.NewGrid(3);
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Dock = DockStyle.Top;
        top.Controls.Add(Ui.NewLabel(UiText.Get("HistorySearchLabel")), 0, 0);
        top.Controls.Add(_search, 1, 0);
        top.Controls.Add(_showUndone, 2, 0);

        var bottom = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Bottom, Padding = new Padding(0, 8, 0, 0) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        buttons.Controls.AddRange([_undoButton, _openButton, close]);
        bottom.Controls.Add(_count, 0, 0);
        bottom.Controls.Add(buttons, 1, 0);

        var listHolder = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
        listHolder.Controls.Add(_list);
        Controls.Add(listHolder);
        Controls.Add(top);
        Controls.Add(bottom);

        Load += (_, _) => Reload();
    }

    /// <summary>Re-reads the history, e.g. after the organizer moved something while this window is open.</summary>
    public void RefreshRows() => Reload();

    void Reload()
    {
        _rows = _load();
        Fill();
    }

    void Fill()
    {
        var term = _search.Text.Trim();
        var shown = _rows
            .Where(r => _showUndone.Checked || !r.Undone)
            .Where(r => term.Length == 0 || Path.GetFileName(r.Move.To).Contains(term, StringComparison.CurrentCultureIgnoreCase))
            .ToList();

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var row in shown)
        {
            var move = row.Move;
            var item = new ListViewItem(move.TimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")) { Tag = row };
            item.SubItems.Add(Path.GetFileName(move.To));
            item.SubItems.Add(MovedTo(move));
            item.SubItems.Add(CameFrom(move));
            item.SubItems.Add(move.Rule ?? UiText.Get("RuleByType"));
            item.SubItems.Add(UiText.Get(row.Undone ? "StatusUndone" : "StatusMoved"));
            if (row.Undone)
            {
                item.ForeColor = SystemColors.GrayText;
            }
            _list.Items.Add(item);
        }
        foreach (ColumnHeader column in _list.Columns)
        {
            column.Width = -2;   // fit the wider of header and content
        }
        _list.EndUpdate();

        _count.Text = shown.Count == 0 ? UiText.Get("HistoryEmpty") : UiText.Format("HistoryCount", shown.Count, _rows.Count);
        UpdateButtons();
    }

    /// <summary>Where a file went, shown inside the folder it came from ("Documents") when possible, else in full.</summary>
    static string MovedTo(JournalEntry move)
    {
        var from = Path.GetDirectoryName(move.From);
        var to = Path.GetDirectoryName(move.To);
        if (from is null || to is null)
        {
            return to ?? "";
        }
        var relative = Path.GetRelativePath(from, to);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? to : relative;
    }

    /// <summary>The watched folder a file came from, by its short name ("Downloads", "Desktop").</summary>
    static string CameFrom(JournalEntry move) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(move.From) ?? ""));

    IEnumerable<HistoryRow> Selected() => _list.SelectedItems.Cast<ListViewItem>().Select(i => (HistoryRow)i.Tag!);

    void UpdateButtons()
    {
        var selected = Selected().ToList();
        _undoButton.Enabled = selected.Any(r => !r.Undone);
        _openButton.Enabled = selected.Count == 1;
    }

    void UndoSelected()
    {
        var moves = Selected().Where(r => !r.Undone).Select(r => r.Move).ToList();
        if (moves.Count == 0)
        {
            return;
        }
        var results = _undo(moves);
        var restored = results.Count(r => r.Restored);
        var problems = results.Where(r => !r.Restored).Select(r => Path.GetFileName(r.Move.To) + " - " + r.Problem).ToList();
        var message = UiText.Format("UndoText", restored, problems.Count);
        if (problems.Count > 0)
        {
            message += "\n\n" + string.Join("\n", problems.Take(10));
        }
        MessageBox.Show(this, message, UiText.Get("UndoTitle"), MessageBoxButtons.OK, problems.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        Reload();
    }

    void OpenLocation()
    {
        if (Selected().FirstOrDefault() is not { } row)
        {
            return;
        }
        var path = row.Undone ? row.Move.From : row.Move.To;
        var arguments = File.Exists(path) || Directory.Exists(path) ? $"/select,\"{path}\"" : $"\"{Path.GetDirectoryName(path)}\"";
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            AppLog.Write(e);
        }
    }
}
