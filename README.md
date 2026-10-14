# Downloads Organizer

Windows tray app that sorts finished downloads into subfolders by file type, name pattern or the site a file came from, and lets you undo any move.

[![Download for Windows](https://img.shields.io/badge/Download-Windows_exe-2563eb?style=for-the-badge)](https://github.com/jyj0240/downloads-organizer/releases/latest/download/DownloadsOrganizer.exe)

**Status:** in development.

## What it does

- Watches your Downloads folder (or any folders you pick) and waits until a file is completely downloaded and not in use
- Moves it into a subfolder by rule: file type by default, plus your own rules by name pattern, source site or size
- Never overwrites and never deletes; every move is journaled and can be undone from the tray
- Keeps the Windows "downloaded from the internet" mark (Mark of the Web) on every file
- Reads only file names, sizes and the source address; no telemetry, no network use except the update check

## Install

**[Download DownloadsOrganizer.exe](https://github.com/jyj0240/downloads-organizer/releases/latest/download/DownloadsOrganizer.exe)**
(latest version; Windows 10 or 11, 64-bit; about 50 MB) and run it. That is all: it installs itself for your user
account, starts with Windows and sits in the tray. No admin rights and no .NET install needed. Release notes and
older versions are on the [Releases](https://github.com/jyj0240/downloads-organizer/releases) page.

The exe is not signed yet, so Windows may say "Windows protected your PC". Choose **More info**, then **Run anyway**.
You see this only once, because the app then updates itself: it checks GitHub for a new version once a day and
installs it (turn this off in Settings; **Check for updates** in the tray menu asks first).

Uninstall from the tray menu or Settings > Apps. Files it already sorted stay where they are.

한국어: 위 링크로 `DownloadsOrganizer.exe`를 받아 실행하면 설치가 끝납니다. "Windows의 PC 보호" 창이 뜨면
**추가 정보**를 누른 뒤 **실행**을 누르세요.

## Use

Right-click the tray icon for the menu; double-click it for the move history, where you can search,
undo chosen moves and show a file in its folder. **Settings...** sets the folders to keep tidy, the wait time,
notifications, start with Windows, updates, language (English, Korean) and your own rules (file name, source site,
size). Changes apply right away.

You can tidy several folders (say Downloads and Desktop). For each one, choose whether files that match no rule
go to a folder for their type, or whether only files matching a rule move. Shortcuts are never moved, and system,
program and AppData folders cannot be added.

Settings are stored in `%APPDATA%\DownloadsOrganizer\settings.json`; the move history in
`%LOCALAPPDATA%\DownloadsOrganizer\journal.jsonl`. The app itself lives in `%LOCALAPPDATA%\Programs\DownloadsOrganizer`.

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
