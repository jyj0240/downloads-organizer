using DownloadsOrganizer.Core;

namespace DownloadsOrganizer.App.Tests;

/// <summary>
/// Builds each window with sample data in both languages, lays it out off screen, saves a picture to
/// %TEMP%\dorg-ui for a human to look at, and fails if any text is missing (shown as ‹Key›).
/// </summary>
public class WindowRenderTests
{
    static readonly string OutDir = Path.Combine(Path.GetTempPath(), "dorg-ui");
    const string Watch = @"C:\Users\Someone\Downloads";
    const string Desktop = @"C:\Users\Someone\Desktop";

    static readonly Rule[] SampleRules =
    [
        new() { Name = "Bank statements", Destination = "Finance", NamePattern = "*statement*.pdf" },
        new() { Name = "Big videos", Destination = @"Media\{yyyy}", NamePattern = "*.mp4", MinBytes = 500L * 1024 * 1024 },
        new() { Name = "From example.com", Destination = @"D:\Archive\Example", SourceDomain = "example.com" },
    ];

    static readonly AppSettings SampleSettings = new()
    {
        Folders = [new WatchedFolder(Watch), new WatchedFolder(Desktop, SortByType: false)],
        Rules = SampleRules,
    };

    static IReadOnlyList<HistoryRow> SampleHistory()
    {
        var now = DateTime.UtcNow;
        JournalEntry Move(string name, string folder, string? rule, int minutesAgo) => new()
        {
            From = Path.Combine(Watch, name),
            To = Path.Combine(Watch, folder, name),
            Rule = rule,
            TimeUtc = now.AddMinutes(-minutesAgo),
            Action = JournalAction.Move,
        };
        return
        [
            new(Move("Annual report 2026.pdf", "Documents", null, 3), false),
            new(Move("Bank statement September.pdf", "Finance", "Bank statements", 30), false),
            new(Move("holiday.jpg", "Images", null, 90), true),
            new(Move("setup-tool.exe", "Installers", null, 600), false),
        ];
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ko")]
    public void History_window_renders_with_all_text(string language) =>
        Render(language, "history", () => new HistoryForm(SampleHistory, _ => []));

    [Theory]
    [InlineData("en")]
    [InlineData("ko")]
    public void Settings_window_renders_with_all_text(string language) =>
        Render(language, "settings", () => new SettingsForm(SampleSettings, true, true));

    [Theory]
    [InlineData("en")]
    [InlineData("ko")]
    public void Rule_editor_renders_with_all_text(string language) =>
        Render(language, "rule-editor", () => new RuleEditorForm(SampleRules[1], [Watch, Desktop]));

    [Fact]
    public void Rule_summary_lists_each_condition()
    {
        RunOnUiThread(() =>
        {
            UiText.Use("en");
            Assert.Equal("name *.mp4 · at least 500 MB", SettingsForm.Describe(SampleRules[1]));
            Assert.Equal("from example.com", SettingsForm.Describe(SampleRules[2]));
        });
    }

    static void Render(string language, string name, Func<Form> create)
    {
        RunOnUiThread(() =>
        {
            UiText.Use(language);
            using var form = create();
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);   // laid out for real, but never on anyone's screen
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();

            Directory.CreateDirectory(OutDir);
            using var picture = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(picture, new Rectangle(Point.Empty, form.Size));
            picture.Save(Path.Combine(OutDir, $"{name}-{language}.png"));

            var missing = AllText(form).Where(t => t.Contains('‹')).ToList();
            form.Close();
            Assert.Empty(missing);
        });
    }

    static IEnumerable<string> AllText(Control root)
    {
        yield return root.Text;
        if (root is ListView list)
        {
            foreach (ColumnHeader column in list.Columns)
            {
                yield return column.Text;
            }
            foreach (ListViewItem item in list.Items)
            {
                foreach (ListViewItem.ListViewSubItem sub in item.SubItems)
                {
                    yield return sub.Text;
                }
            }
        }
        if (root is TextBox box)
        {
            yield return box.PlaceholderText;
        }
        foreach (Control child in root.Controls)
        {
            foreach (var text in AllText(child))
            {
                yield return text;
            }
        }
    }

    /// <summary>WinForms needs a single-threaded apartment; xUnit runs tests on pool threads.</summary>
    static void RunOnUiThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                failure = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException("UI thread failed: " + failure);
        }
    }
}
