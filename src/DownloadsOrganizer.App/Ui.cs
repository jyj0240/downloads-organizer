namespace DownloadsOrganizer.App;

/// <summary>Small helpers so every window looks the same: system font, auto-sized controls, no fixed pixel layouts.</summary>
static class Ui
{
    public static void SetUpWindow(Form form, string title)
    {
        form.Text = title;
        form.Font = SystemFonts.MessageBoxFont ?? form.Font;
        form.StartPosition = FormStartPosition.CenterScreen;
        form.Icon = AppIcon.Window();
        form.Padding = new Padding(form.LogicalToDeviceUnits(12));
        form.AutoScaleMode = AutoScaleMode.None;   // layouts below size themselves from the font, which already follows the screen DPI
    }

    public static Label NewLabel(string text, bool hint = false) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(3, 7, 3, 3),
        ForeColor = hint ? SystemColors.GrayText : SystemColors.ControlText,
    };

    public static Button NewButton(string text, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 2, 10, 2),
            Margin = new Padding(3),
        };
        button.Click += onClick;
        return button;
    }

    public static TableLayoutPanel NewGrid(int columns) => new()
    {
        ColumnCount = columns,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Dock = DockStyle.Fill,
    };

    /// <summary>A right-aligned row of buttons for the bottom of a window.</summary>
    public static FlowLayoutPanel ButtonRow(params Button[] buttons)
    {
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Bottom,
            Padding = new Padding(0, 8, 0, 0),
            WrapContents = false,
        };
        foreach (var b in buttons.Reverse())
        {
            row.Controls.Add(b);
        }
        return row;
    }
}

/// <summary>The app icon, embedded in the exe.</summary>
static class AppIcon
{
    public static Icon Window() => Load(new Size(32, 32));

    /// <summary>Tray size for this screen (16 px at 100 %, 20 at 125 %, 24 at 150 %, 32 at 200 %).</summary>
    public static Icon Tray() => Load(SystemInformation.SmallIconSize);

    static Icon Load(Size size)
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("DownloadsOrganizer.App.app.ico");
        return stream is null ? SystemIcons.Application : new Icon(stream, size);
    }
}
