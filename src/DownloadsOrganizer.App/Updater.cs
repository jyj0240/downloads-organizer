using System.Diagnostics;
using DownloadsOrganizer.Core;

namespace DownloadsOrganizer.App;

/// <summary>
/// Looks for a newer release a minute after start and then once a day. An update is downloaded, checked against the
/// SHA-256 GitHub publishes, and started: the new exe then replaces this copy the same way as running a newer exe by
/// hand (<see cref="SelfInstaller.InstallAndLaunch"/>). The app downloads the file itself, so Windows does not mark it
/// as coming from the internet and SmartScreen does not ask again.
/// </summary>
sealed class Updater : IDisposable
{
    static readonly TimeSpan FirstCheck = TimeSpan.FromMinutes(1);
    static readonly TimeSpan Daily = TimeSpan.FromDays(1);
    static readonly TimeSpan AfterProblem = TimeSpan.FromHours(1);

    readonly UpdateChecker _checker;
    readonly Func<bool> _busy;
    readonly System.Threading.Timer _timer;
    readonly SemaphoreSlim _oneAtATime = new(1, 1);
    string? _lastProblem;

    /// <param name="busy">True while the user has a window open; an automatic update then waits.</param>
    public Updater(Func<bool> busy)
    {
        _busy = busy;
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"DownloadsOrganizer/{SelfInstaller.CurrentVersion.ToString(3)}");
        // DORG_UPDATE_URL points the check at a local server, to try the whole update path before a release.
        var feed = Environment.GetEnvironmentVariable("DORG_UPDATE_URL") is { Length: > 0 } url ? new Uri(url) : UpdateChecker.GitHubLatest;
        _checker = new UpdateChecker(http, feed);
        _timer = new System.Threading.Timer(_ => _ = CheckOnScheduleAsync(), null, FirstCheck, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Install new versions without asking. Off: only "Check for updates" in the tray menu looks.</summary>
    public bool Automatic { get; set; } = true;

    /// <summary>For "Check for updates": the newer release, or null when up to date. Throws when GitHub can't be reached.</summary>
    public Task<AvailableUpdate?> FindAsync() => _checker.FindNewerAsync(SelfInstaller.CurrentVersion);

    /// <summary>Downloads, checks and starts <paramref name="update"/>. Throws if the file is not the expected one.</summary>
    public async Task InstallAsync(AvailableUpdate update)
    {
        await _oneAtATime.WaitAsync();
        try
        {
            var exe = await _checker.DownloadAsync(update, AppPaths.UpdatesDir);
            var info = FileVersionInfo.GetVersionInfo(exe);
            if (info.ProductName != AppInfo.Name || !Version.TryParse(info.FileVersion, out var fileVersion) || fileVersion.ToString(3) != update.Version.ToString(3))
            {
                File.Delete(exe);
                throw new InvalidDataException($"Update {update.Version}: the file says {info.ProductName} {info.FileVersion}");
            }
            AppLog.Write($"update {update.Version} downloaded and checked; installing");
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = AppPaths.UpdatesDir });
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    async Task CheckOnScheduleAsync()
    {
        var next = Daily;
        try
        {
            if (Automatic && await FindAsync() is { } update)
            {
                if (_busy())
                {
                    next = AfterProblem;   // a window is open; don't pull the app away from under the user
                }
                else
                {
                    await InstallAsync(update);
                }
            }
            _lastProblem = null;
        }
        catch (Exception e)
        {
            next = AfterProblem;
            // Offline, or the repository can't be read: log once, not every hour.
            if (e.Message != _lastProblem)
            {
                AppLog.Write("update check: " + e.Message);
                _lastProblem = e.Message;
            }
        }
        try
        {
            _timer.Change(next, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Exiting.
        }
    }

    public void Dispose() => _timer.Dispose();
}
