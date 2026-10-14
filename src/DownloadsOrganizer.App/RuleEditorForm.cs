using DownloadsOrganizer.Core;

namespace DownloadsOrganizer.App;

/// <summary>Adds or edits one rule: what to match (name, site, size) and where it goes.</summary>
sealed class RuleEditorForm : Form
{
    readonly IReadOnlyList<string> _watchFolders;
    readonly TextBox _name = new() { Dock = DockStyle.Fill };
    readonly TextBox _pattern = new() { Dock = DockStyle.Fill };
    readonly TextBox _site = new() { Dock = DockStyle.Fill };
    readonly TextBox _minMb = new() { Width = 80 };
    readonly TextBox _maxMb = new() { Width = 80 };
    readonly TextBox _destination = new() { Dock = DockStyle.Fill };

    public Rule? Result { get; private set; }

    public RuleEditorForm(Rule? existing, IReadOnlyList<string> watchFolders)
    {
        _watchFolders = watchFolders;
        Ui.SetUpWindow(this, UiText.Get(existing is null ? "RuleTitleAdd" : "RuleTitleEdit"));
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _minMb.Width = _maxMb.Width = LogicalToDeviceUnits(80);

        var grid = Ui.NewGrid(3);
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(360)));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var row = 0;
        void Add(string label, Control field, Control? extra = null)
        {
            grid.Controls.Add(Ui.NewLabel(label), 0, row);
            grid.Controls.Add(field, 1, row);
            if (extra is not null)
            {
                grid.Controls.Add(extra, 2, row);
            }
            row++;
        }
        void Hint(string text)
        {
            var hint = Ui.NewLabel(text, hint: true);
            hint.Margin = new Padding(3, 0, 3, 8);
            grid.Controls.Add(hint, 1, row);
            grid.SetColumnSpan(hint, 2);
            row++;
        }

        Add(UiText.Get("LblRuleName"), _name);
        Hint(UiText.Get("HintRuleName"));
        Add(UiText.Get("LblNamePattern"), _pattern);
        Hint(UiText.Get("HintNamePattern"));
        Add(UiText.Get("LblSourceSite"), _site);
        Hint(UiText.Get("HintSourceSite"));
        var size = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        size.Controls.AddRange([Ui.NewLabel(UiText.Get("LblSizeMin")), _minMb, Ui.NewLabel(UiText.Get("LblSizeMax")), _maxMb, Ui.NewLabel("MB")]);
        Add(UiText.Get("LblSize"), size);
        Hint(UiText.Get("HintConditions"));
        Add(UiText.Get("LblDestination"), _destination, Ui.NewButton(UiText.Get("BtnBrowse"), (_, _) => BrowseDestination()));
        Hint(UiText.Get("HintDestination"));

        var ok = Ui.NewButton(UiText.Get("BtnOk"), (_, _) => Save());
        var cancel = Ui.NewButton(UiText.Get("BtnCancel"), (_, _) => Close());
        cancel.DialogResult = DialogResult.Cancel;
        AcceptButton = ok;
        CancelButton = cancel;

        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        layout.Controls.Add(grid);
        layout.Controls.Add(Ui.ButtonRow(ok, cancel));
        Controls.Add(layout);

        if (existing is not null)
        {
            _name.Text = existing.Name;
            _pattern.Text = existing.NamePattern ?? "";
            _site.Text = existing.SourceDomain ?? "";
            _minMb.Text = ToMb(existing.MinBytes);
            _maxMb.Text = ToMb(existing.MaxBytes);
            _destination.Text = existing.Destination;
        }
    }

    static string ToMb(long? bytes) => bytes is long b ? (b / 1024d / 1024d).ToString("0.##") : "";

    void BrowseDestination()
    {
        using var dialog = new FolderBrowserDialog { InitialDirectory = _watchFolders.FirstOrDefault() ?? "", UseDescriptionForTitle = true, Description = UiText.Get("LblDestination") };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }
        // A folder inside one of the watched folders is stored by its short name: the rule then files into that
        // name inside whichever watched folder the file is in.
        foreach (var watched in _watchFolders)
        {
            var relative = Path.GetRelativePath(watched, dialog.SelectedPath);
            if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative) && relative != ".")
            {
                _destination.Text = relative;
                return;
            }
        }
        _destination.Text = dialog.SelectedPath;
    }

    void Save()
    {
        string? error = null;
        long? min = null, max = null;
        if (!TryMb(_minMb.Text, out min) || !TryMb(_maxMb.Text, out max))
        {
            error = UiText.Get("ErrSizeNumber");
        }
        else if (min is not null && max is not null && min > max)
        {
            error = UiText.Get("ErrSizeOrder");
        }

        var rule = new Rule
        {
            Name = _name.Text.Trim(),
            Destination = _destination.Text.Trim(),
            NamePattern = Blank(_pattern.Text),
            SourceDomain = Blank(_site.Text)?.Replace("https://", "").Replace("http://", "").TrimEnd('/'),
            MinBytes = min,
            MaxBytes = max,
        };
        error ??= rule.Name.Length == 0 ? UiText.Get("ErrNameRequired")
            : rule.Destination.Length == 0 ? UiText.Get("ErrDestinationRequired")
            : rule.Destination.IndexOfAny(Path.GetInvalidPathChars()) >= 0 ? UiText.Get("ErrDestinationInvalid")
            : !rule.HasCondition ? UiText.Get("ErrConditionRequired")
            : null;
        if (error is not null)
        {
            MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Result = rule;
        DialogResult = DialogResult.OK;
        Close();
    }

    static string? Blank(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    static bool TryMb(string text, out long? bytes)
    {
        bytes = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }
        if (double.TryParse(text.Trim(), out var mb) && mb >= 0)
        {
            bytes = (long)(mb * 1024 * 1024);
            return true;
        }
        return false;
    }
}
