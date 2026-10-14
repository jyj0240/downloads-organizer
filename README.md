# Downloads Organizer

Windows tray app that sorts finished downloads into subfolders by file type, name pattern or the site a file came from, and lets you undo any move.

**Status:** in development.

## What it does

- Watches your Downloads folder and waits until a file is completely downloaded and not in use
- Moves it into a subfolder by rule: file type by default, plus your own rules by name pattern or source site
- Never overwrites and never deletes; every move is journaled and can be undone from the tray
- Keeps the Windows "downloaded from the internet" mark (Mark of the Web) on every file
- Reads only file names, sizes and the source address; no telemetry, no network use except an optional update check

## Install

Download `DownloadsOrganizer.exe` from [Releases](../../releases/latest) and run it. It copies itself to
`%LOCALAPPDATA%\Programs\DownloadsOrganizer`, starts with Windows, and sits in the tray. No admin rights and no
.NET install needed. It checks GitHub for a new version once a day and updates itself (turn this off in Settings;
**Check for updates** in the tray menu asks first). The exe is not signed yet, so Windows may say "Windows protected
your PC"; choose **More info**, then **Run anyway**.
Uninstall from the tray menu or Settings > Apps. Files it already sorted stay where they are.

Right-click the tray icon for the menu; double-click it for the move history, where you can search,
undo chosen moves and show a file in its folder. **Settings...** sets the folders to keep tidy, the wait time,
notifications, start with Windows, language (English, Korean) and your own rules (file name, source site, size).
Changes apply right away.

You can tidy several folders (say Downloads and Desktop). For each one, choose whether files that match no rule
go to a folder for their type, or whether only files matching a rule move. Shortcuts are never moved, and system,
program and AppData folders cannot be added.

Settings are stored in `%APPDATA%\DownloadsOrganizer\settings.json`; the move history in
`%LOCALAPPDATA%\DownloadsOrganizer\journal.jsonl`.

`dorg preview <folder>` shows what would move and why, without moving anything.

## Build

Requires the .NET 10 SDK on Windows.

```
dotnet build
dotnet test
dotnet run --project src/DownloadsOrganizer.App
```

## Release

Raise `<Version>` in `src/DownloadsOrganizer.App/DownloadsOrganizer.App.csproj`, commit, then push an annotated tag
with the same version. The tag message becomes the release notes.

```
git tag -a v0.4.2 -m "What changed"
git push origin v0.4.2
```

GitHub Actions runs the tests, builds the single exe and publishes it as a release. It stops if the tag and
`<Version>` differ. An installed copy only accepts an exe with a higher file version.

## License

[MIT](LICENSE)
