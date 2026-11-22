# ExplorerPilot — User guide (v2.0.0)

## Requirements

- Windows 10 (1809+) or Windows 11, **x64**
- For the portable self-contained build: **no separate .NET install required**
- For framework-dependent builds from source: [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download)

## Install / run (portable)

1. Unzip `ExplorerPilot-2.0.0-win-x64-portable.zip` (or the previous v1.1.0 zip) to a folder you can write to.
2. Verify the SHA-256 checksum (see companion `.sha256` file).
3. Run `ExplorerPilot.exe`.
4. Windows may show SmartScreen for unsigned builds — see `docs/CODE_SIGNING.md`.

Settings, snapshots, schedules, and logs are stored under:

`%LocalAppData%\ExplorerPilot\`

## Basic workflow

1. Select a drive or **Browse Folder**.
2. Click **Start Scan** (Cancel stops the scan; totals may be incomplete).
3. Use the **left sidebar** to open Dashboard, Drives, Explorer, Largest Files, File Types, Search, Duplicates, Treemap, Comparison, Trends, Scheduled Scans, or Cleanup.
4. Use **Search** / **Duplicates** against the last completed scan.
5. Save snapshots from **Comparison**; review growth in **Trends**.
6. **Cleanup** lists recommendations only — ExplorerPilot does **not** delete files for you.
7. Export CSV / HTML / JSON reports from the toolbar when a scan is available.
8. Optional: create **Scheduled Scans** (runs only while the app is open).

## Language, theme & sidebar

Use the toolbar selectors for EN/TR and Light/Dark/System themes. Use **Collapse sidebar** to switch icon-only mode. Preferences (including sidebar expand/collapse) persist across launches.

## Privacy

- No telemetry is sent by default.
- Logs redact full filesystem paths where practical.
- Reports you export may contain paths you choose to include — treat them as sensitive.

## Limitations

- Directory junctions / directory symlinks are skipped to avoid cycles.
- Sizes are logical (`FileInfo.Length`), not allocated clusters.
- Long paths need Windows long-path support (enabled in the app manifest; OS policy must allow it).
