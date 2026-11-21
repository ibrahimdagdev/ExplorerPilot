# ExplorerPilot 1.0 — User guide

## Requirements

- Windows 10 (1809+) or Windows 11, **x64**
- For the portable self-contained build: **no separate .NET install required**
- For framework-dependent builds from source: [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download)

## Install / run (portable)

1. Unzip `ExplorerPilot-1.0.0-win-x64-portable.zip` to a folder you can write to.
2. Verify the SHA-256 checksum (see companion `.sha256` file).
3. Run `ExplorerPilot.exe`.
4. Windows may show SmartScreen for unsigned builds — see `docs/CODE_SIGNING.md`.

Settings, snapshots, and logs are stored under:

`%LocalAppData%\ExplorerPilot\`

## Basic workflow

1. Select a drive or **Browse Folder**.
2. Click **Start Scan** (Cancel stops the scan; totals may be incomplete).
3. Explore sizes in **Explorer**, **Largest Files**, **File Types**, and **Treemap**.
4. Use **Search** / **Duplicates** against the last completed scan.
5. Save snapshots from **Comparison**; review growth in **Trends**.
6. **Cleanup** lists recommendations only — ExplorerPilot does **not** delete files for you.
7. Export CSV/HTML reports from the toolbar when a scan is available.

## Language & theme

Use the toolbar selectors for EN/TR and Light/Dark/System themes. Changes apply immediately.

## Privacy

- No telemetry is sent by default.
- Logs redact full filesystem paths where practical.
- Reports you export may contain paths you choose to include — treat them as sensitive.

## Limitations

- Directory junctions / directory symlinks are skipped to avoid cycles.
- Sizes are logical (`FileInfo.Length`), not allocated clusters.
- Long paths need Windows long-path support (enabled in the app manifest; OS policy must allow it).
