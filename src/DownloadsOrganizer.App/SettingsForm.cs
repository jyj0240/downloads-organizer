using DownloadsOrganizer.Core;

namespace DownloadsOrganizer.App;

/// <summary>Settings: the folders to keep tidy, how long to wait, notifications, start with Windows, updates, language, and the rules.</summary>
sealed class SettingsForm : Form
{
    readonly AppSettings _current;
    readonly ListView _folders = new() { View = View.Details, CheckBoxes = true, FullRowSelect = true, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill };
    readonly NumericUpDown _wait = new() { Minimum = 0, Maximum = 60, Anchor = AnchorStyles.Left };
    readonly CheckBox _moveFolders = new() { AutoSize = true };
    readonly CheckBox _notify = new() { AutoSize = true };
    readonly CheckBox _startup = new() { AutoSize = true };
    readonly CheckBox _autoUpdate = new() { AutoSize = true };
    readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left };
    readonly ListView _rules = new() { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill };
    readonly List<Rule> _ruleList;
    readonly Button _removeFolder, _edit, _delete, _up, _down;
    bool _filling;

    static readonly (string Code, string Key)[] Languages = [("auto", "LangAuto"), ("ko", "LangKorean"), ("en", "LangEnglish")];

    public AppSettings? Result { get; private set; }
    public bool RunAtStartup => _startup.Checked;

    public SettingsForm(AppSettings current, bool canRunAtStartup, bool runAtStartup)
    {
        _current = current;
        _ruleList = [.. current.Rules];
        Ui.SetUpWindow(this, UiText.Get("SettingsTitle"));
        Size = new Size(LogicalToDeviceUnits(780), LogicalToDeviceUnits(780));
        MinimumSize = new Size(LogicalToDeviceUnits(640), LogicalToDeviceUnits(720));   // room for all five rule buttons
        _wait.Width = LogicalToDeviceUnits(70);
        _language.Width = LogicalToDeviceUnits(200);

        // --- folders
        _folders.Columns.Add(UiText.Get("ColFolder"));
        _folders.Columns.Add(UiText.Get("ColFolderMode"));
        _folders.ItemChecked += (_, e) =>
        {
            if (!_filling)
            {
                e.Item.SubItems[1].Text = ModeText(e.Item.Checked);
            }
        };
        _folders.SelectedIndexChanged += (_, _) => UpdateFolderButtons();
        _removeFolder = Ui.NewButton(UiText.Get("BtnRemoveFolder"), (_, _) => RemoveFolder());
        var folderButtons = VerticalButtons(Ui.NewButton(UiText.Get("BtnAddFolder"), (_, _) => AddFolder()), _removeFolder);
        var foldersHint = Hint(UiText.Get("HintFolders"));
        var foldersBox = Box(UiText.Get("GroupFolders"), _folders, folderButtons, foldersHint);
        foldersBox.Dock = DockStyle.Top;
        foldersBox.Height = LogicalToDeviceUnits(170);

        // --- general
        var general = Ui.NewGrid(2);
        general.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        general.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var wait = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        wait.Controls.AddRange([_wait, Ui.NewLabel(UiText.Get("LblMinutes")), Ui.NewLabel(UiText.Get("HintWait"), hint: true)]);
        general.Controls.Add(Ui.NewLabel(UiText.Get("LblWait")), 0, 0);
        general.Controls.Add(wait, 1, 0);
        general.Controls.Add(Ui.NewLabel(UiText.Get("LblLanguage")), 0, 1);
        general.Controls.Add(_language, 1, 1);
        foreach (var (check, key, row) in new[] { (_moveFolders, "ChkMoveFolders", 2), (_notify, "ChkNotify", 3), (_startup, "ChkRunAtStartup", 4), (_autoUpdate, "ChkAutoUpdate", 5) })
        {
            check.Text = UiText.Get(key);
            general.Controls.Add(check, 1, row);
        }
        var generalBox = new GroupBox { Text = UiText.Get("GroupGeneral"), Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        generalBox.Controls.Add(general);

        // --- rules
        foreach (var key in new[] { "ColRuleName", "ColRuleCondition", "ColRuleDestination" })
        {
            _rules.Columns.Add(UiText.Get(key));
        }
        _rules.SelectedIndexChanged += (_, _) => UpdateRuleButtons();
        _rules.DoubleClick += (_, _) => EditRule();
        _edit = Ui.NewButton(UiText.Get("BtnEdit"), (_, _) => EditRule());
        _delete = Ui.NewButton(UiText.Get("BtnDelete"), (_, _) => DeleteRule());
        _up = Ui.NewButton(UiText.Get("BtnUp"), (_, _) => MoveRule(-1));
        _down = Ui.NewButton(UiText.Get("BtnDown"), (_, _) => MoveRule(+1));
        var ruleButtons = VerticalButtons(Ui.NewButton(UiText.Get("BtnAdd"), (_, _) => AddRule()), _edit, _delete, _up, _down);
        var rulesBox = Box(UiText.Get("GroupRules"), _rules, ruleButtons, Hint(UiText.Get("RulesHint")));
        rulesBox.Dock = DockStyle.Fill;

        // --- OK / Cancel
        var ok = Ui.NewButton(UiText.Get("BtnOk"), (_, _) => Save());
        var cancel = Ui.NewButton(UiText.Get("BtnCancel"), (_, _) => Close());
        cancel.DialogResult = DialogResult.Cancel;
        AcceptButton = ok;
        CancelButton = cancel;

        // Docked controls are laid out in reverse order of adding: last added sits on top.
        Controls.Add(rulesBox);
        Controls.Add(Spacer());
        Controls.Add(generalBox);
        Controls.Add(Spacer());
        Controls.Add(foldersBox);
        Controls.Add(Ui.ButtonRow(ok, cancel));

        _wait.Value = (decimal)Math.Clamp(current.SettleDelay.TotalMinutes, 0, 60);
        _moveFolders.Checked = current.MoveFolders;
        _notify.Checked = current.Notify;
        _startup.Checked = runAtStartup;
        _autoUpdate.Checked = current.AutoUpdate;
        // Starting with Windows and updating both need the installed copy.
        foreach (var check in new[] { _startup, _autoUpdate })
        {
            check.Enabled = canRunAtStartup;
            if (!canRunAtStartup)
            {
                check.Text += " " + UiText.Get("OnlyWhenInstalled");
            }
        }
        foreach (var (_, key) in Languages)
        {
            _language.Items.Add(UiText.Get(key));
        }
        _language.SelectedIndex = Math.Max(0, Array.FindIndex(Languages, l => l.Code == current.Language));
        foreach (var folder in current.Folders)
        {
            AddFolderRow(folder);
        }
        FitColumns(_folders);
        FillRules();
        UpdateFolderButtons();
    }

    Panel Spacer() => new() { Dock = DockStyle.Top, Height = LogicalToDeviceUnits(8) };

    static Label Hint(string text)
    {
        var hint = Ui.NewLabel(text, hint: true);
        hint.Dock = DockStyle.Top;
        hint.Margin = new Padding(3, 0, 3, 6);
        hint.Padding = new Padding(0, 0, 0, 4);
        return hint;
    }

    FlowLayoutPanel VerticalButtons(params Button[] buttons)
    {
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Right };
        foreach (var b in buttons)
        {
            b.AutoSize = false;
            b.Width = LogicalToDeviceUnits(120);
            b.Height = LogicalToDeviceUnits(30);
            panel.Controls.Add(b);
        }
        return panel;
    }

    static GroupBox Box(string title, Control list, Control buttons, Control hint)
    {
        var box = new GroupBox { Text = title, Padding = new Padding(8) };
        var body = new Panel { Dock = DockStyle.Fill };
        body.Controls.Add(list);
        body.Controls.Add(buttons);
        box.Controls.Add(body);
        box.Controls.Add(hint);
        return box;
    }

    static string ModeText(bool sortByType) => UiText.Get(sortByType ? "ModeByTypeAndRules" : "ModeRulesOnly");

    static void FitColumns(ListView list)
    {
        foreach (ColumnHeader column in list.Columns)
        {
            column.Width = -2;
        }
    }

    // ----- folders

    void AddFolderRow(WatchedFolder folder)
    {
        _filling = true;
        var item = new ListViewItem(folder.Location) { Checked = folder.SortByType, Tag = folder };
        item.SubItems.Add(ModeText(folder.SortByType));
        _folders.Items.Add(item);
        _filling = false;
    }

    IEnumerable<string> FolderPaths() => _folders.Items.Cast<ListViewItem>().Select(i => ((WatchedFolder)i.Tag!).Location);

    void UpdateFolderButtons() => _removeFolder.Enabled = _folders.SelectedItems.Count == 1 && _folders.Items.Count > 1;

    void AddFolder()
    {
        using var dialog = new FolderBrowserDialog { UseDescriptionForTitle = true, Description = UiText.Get("BtnAddFolder") };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        var path = dialog.SelectedPath;
        if (FolderSafety.Check(path, FolderPaths()) is { } problem)
        {
            MessageBox.Show(this, UiText.Format("ErrFolder" + problem, path), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (FolderSafety.IsCloudSynced(path)
            && MessageBox.Show(this, UiText.Format("WarnCloudFolder", path), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }
        AddFolderRow(new WatchedFolder(path));
        FitColumns(_folders);
    }

    void RemoveFolder()
    {
        if (_folders.SelectedItems.Count == 1 && _folders.Items.Count > 1)
        {
            _folders.Items.Remove(_folders.SelectedItems[0]);
            UpdateFolderButtons();
        }
    }

    // ----- rules

    void FillRules(int select = -1)
    {
        _rules.BeginUpdate();
        _rules.Items.Clear();
        foreach (var rule in _ruleList)
        {
            var item = new ListViewItem(rule.Name);
            item.SubItems.Add(Describe(rule));
            item.SubItems.Add(rule.Destination);
            _rules.Items.Add(item);
        }
        FitColumns(_rules);
        if (_ruleList.Count == 0)
        {
            _rules.Items.Add(new ListViewItem(UiText.Get("NoRules")) { ForeColor = SystemColors.GrayText });
            _rules.Columns[0].Width = -1;
        }
        _rules.EndUpdate();
        if (select >= 0 && select < _ruleList.Count)
        {
            _rules.Items[select].Selected = true;
            _rules.Items[select].Focused = true;
        }
        UpdateRuleButtons();
    }

    /// <summary>One line describing what a rule matches, e.g. "name *statement* · from example.com · at least 1 MB".</summary>
    internal static string Describe(Rule rule)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(rule.NamePattern))
        {
            parts.Add(UiText.Format("CondName", rule.NamePattern));
        }
        if (!string.IsNullOrWhiteSpace(rule.SourceDomain))
        {
            parts.Add(UiText.Format("CondSite", rule.SourceDomain));
        }
        if (rule.MinBytes is long min)
        {
            parts.Add(UiText.Format("CondMin", (min / 1048576d).ToString("0.##")));
        }
        if (rule.MaxBytes is long max)
        {
            parts.Add(UiText.Format("CondMax", (max / 1048576d).ToString("0.##")));
        }
        return string.Join(" · ", parts);
    }

    int SelectedRule => _ruleList.Count > 0 && _rules.SelectedIndices.Count == 1 ? _rules.SelectedIndices[0] : -1;

    void UpdateRuleButtons()
    {
        var i = SelectedRule;
        _edit.Enabled = _delete.Enabled = i >= 0;
        _up.Enabled = i > 0;
        _down.Enabled = i >= 0 && i < _ruleList.Count - 1;
    }

    void AddRule()
    {
        using var editor = new RuleEditorForm(null, [.. FolderPaths()]);
        if (editor.ShowDialog(this) == DialogResult.OK && editor.Result is { } rule)
        {
            _ruleList.Add(rule);
            FillRules(_ruleList.Count - 1);
        }
    }

    void EditRule()
    {
        var i = SelectedRule;
        if (i < 0)
        {
            return;
        }
        using var editor = new RuleEditorForm(_ruleList[i], [.. FolderPaths()]);
        if (editor.ShowDialog(this) == DialogResult.OK && editor.Result is { } rule)
        {
            _ruleList[i] = rule;
            FillRules(i);
        }
    }

    void DeleteRule()
    {
        var i = SelectedRule;
        if (i >= 0 && MessageBox.Show(this, UiText.Format("ConfirmDeleteRule", _ruleList[i].Name), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            _ruleList.RemoveAt(i);
            FillRules(Math.Min(i, _ruleList.Count - 1));
        }
    }

    void MoveRule(int by)
    {
        var i = SelectedRule;
        var j = i + by;
        if (i < 0 || j < 0 || j >= _ruleList.Count)
        {
            return;
        }
        (_ruleList[i], _ruleList[j]) = (_ruleList[j], _ruleList[i]);
        FillRules(j);
    }

    void Save()
    {
        Result = _current with
        {
            Folders = [.. _folders.Items.Cast<ListViewItem>().Select(i => (WatchedFolder)i.Tag! with { SortByType = i.Checked })],
            SettleDelay = TimeSpan.FromMinutes((double)_wait.Value),
            MoveFolders = _moveFolders.Checked,
            Notify = _notify.Checked,
            Language = Languages[Math.Max(0, _language.SelectedIndex)].Code,
            AutoUpdate = _autoUpdate.Checked,
            Rules = [.. _ruleList],
        };
        DialogResult = DialogResult.OK;
        Close();
    }
}
