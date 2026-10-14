using DownloadsOrganizer.Core;

namespace DownloadsOrganizer.App;

/// <summary>Shown once after installing: where the items already in the folders would go, and the choice to sort them
/// now or leave them where they are. New downloads are sorted either way; closing the window leaves them.</summary>
sealed class FirstRunForm : Form
{
    readonly ListView _list = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill };

    /// <summary>True when the user chose to sort the existing items now.</summary>
    public bool SortNow { get; private set; }

    /// <param name="moves">Planned moves, each with the watched folder it is in.</param>
    public FirstRunForm(IReadOnlyList<(string Folder, PlanItem Item)> moves)
    {
        Ui.SetUpWindow(this, UiText.Get("FirstRunWindowTitle"));
        Size = new Size(LogicalToDeviceUnits(720), LogicalToDeviceUnits(500));
        MinimumSize = new Size(LogicalToDeviceUnits(520), LogicalToDeviceUnits(340));

        var severalFolders = moves.Select(m => m.Folder).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
        _list.Columns.Add(UiText.Get("ColFile"));
        _list.Columns.Add(UiText.Get("ColGoesTo"));
        if (severalFolders)
        {
            _list.Columns.Add(UiText.Get("ColFolder"));
        }
        _list.BeginUpdate();
        foreach (var (folder, item) in moves.OrderBy(m => m.Item.DestinationFolder, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Item.Item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var row = new ListViewItem(item.Item.Name);
            row.SubItems.Add(Shorten(folder, item.DestinationFolder!));
            if (severalFolders)
            {
                row.SubItems.Add(folder);
            }
            _list.Items.Add(row);
        }
        foreach (ColumnHeader column in _list.Columns)
        {
            column.Width = -2;
        }
        _list.EndUpdate();

        var text = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 0, 0, 8) };
        foreach (var label in new[] { Ui.NewLabel(UiText.Format("FirstRunIntro", moves.Count)), Ui.NewLabel(UiText.Get("FirstRunHint"), hint: true) })
        {
            label.MaximumSize = new Size(LogicalToDeviceUnits(680), 0);   // wrap instead of running off the window
            text.Controls.Add(label);
        }

        var sortNow = Ui.NewButton(UiText.Get("BtnSortNow"), (_, _) => { SortNow = true; Close(); });
        var leave = Ui.NewButton(UiText.Get("BtnLeaveThem"), (_, _) => Close());
        AcceptButton = sortNow;
        CancelButton = leave;

        Controls.Add(_list);
        Controls.Add(text);
        Controls.Add(Ui.ButtonRow(sortNow, leave));
    }

    /// <summary>"Documents" for a folder inside the watched one; the full path for a rule that files elsewhere.</summary>
    static string Shorten(string watched, string destination)
    {
        var relative = Path.GetRelativePath(watched, destination);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathFullyQualified(relative) ? destination : relative;
    }
}
