# ExplorerPilot

**ExplorerPilot** is a modern disk space analyzer for Windows. It helps you understand which folders and files consume the most space on a drive or in a selected directory.

| | |
|---|---|
| **Application** | ExplorerPilot |
| **Version** | 2.0.0-rc.1 (Release Candidate) / 1.1.0 (latest stable release) |
| **Developer** | İbrahim Dağ |
| **Publisher** | Tasarisoft |
| **Website** | [tasarisoft.com](https://tasarisoft.com) |
| **Repository** | [github.com/ibrahimdagdev/ExplorerPilot](https://github.com/ibrahimdagdev/ExplorerPilot) |
| **Description** | A modern disk space analyzer for Windows. |

Inspired by the *purpose* of tools such as TreeSize, WizTree, and WinDirStat — but implemented independently, without copying code, assets, branding, or UI designs.

## Features (v2.0 Release Candidate)

- Scan a complete drive or a selected folder (background worker, cancellable)
- **Sidebar navigation** (collapsible) across **12 screens**: Dashboard, Drives, Directory Explorer, Largest Files, File Types, Search, Duplicates, Treemap, Comparison, Trends, Scheduled Scans, Cleanup
- Multi-drive discovery, Dashboard KPIs, and **Scheduled Scans** (in-app Daily/Weekly/Monthly/Manual with auto-snapshot on success)
- Snapshot save/import/export, comparison, cleanup recommendations (**preview / report-only** — never auto-deletes), trends
- CSV / HTML / JSON reporting with formula-injection and HTML escaping protections
- EN / TR localization and Light / Dark / System themes (persisted with sidebar expand/collapse)
- Per-monitor DPI awareness; portable Windows x64 self-contained package
- Local diagnostic logs only — **no telemetry by default**

### v2.0 highlights (vs 1.1.0)

- Multi-drive overview + professional Dashboard
- Scheduled Scans with busy/skip concurrency guard
- Modern left sidebar replacing the overflowing tab strip
- Stabilized cancel/restart, theme/language persistence (Settings page), and RC portable packaging under `artifacts/v2.0-rc1`

### v1.1.0 highlights (still in the product)

- **Faster scanning:** `DirectoryInfo.EnumerateFileSystemInfos` reuses Find* metadata
- **Optimized scanner is the default** via DI; legacy mode retained for A/B benchmarks
- **Responsive Explorer tree:** lazy child materialization on expand
- **Scan timing:** separate `ScanCore` vs `ApplyResult` durations in diagnostic logs
- Stable portable package under `artifacts/v1.1` (unchanged by v2.0 RC packaging)

## Technology stack

| Area | Choice |
|------|--------|
| Language | C# |
| Framework | .NET 10 |
| UI | WPF |
| Architecture | MVVM + DI |
| Tests | xUnit |
| Target OS | Windows 10 / 11 (x64) |

## Requirements

- Windows 10 or Windows 11 (**x64**)
- Portable release: no separate runtime install
- To build from source: [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Solution structure

```text
ExplorerPilot/
├── ExplorerPilot.sln
├── src/
│   ├── ExplorerPilot.App/             # WPF UI, ViewModels, DI composition
│   ├── ExplorerPilot.Core/            # Models, enums, interfaces, reporting helpers
│   └── ExplorerPilot.Infrastructure/  # Scanner, snapshots, drive/system info
├── tests/
│   └── ExplorerPilot.Tests/
├── tools/
│   ├── ExplorerPilot.ScanBenchmark/   # Legacy vs Optimized A/B harness
│   └── measure-ui-apply/              # ScanCore vs ApplyResult timing helper
├── scripts/publish-portable.ps1
└── docs/
```

## Build & test

```powershell
dotnet restore ExplorerPilot.sln
dotnet build ExplorerPilot.sln -c Release
dotnet test ExplorerPilot.sln -c Release
```

Run from source:

```powershell
dotnet run --project src/ExplorerPilot.App/ExplorerPilot.App.csproj -c Release
```

## Portable release package

```powershell
# v2.0 Release Candidate
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-portable.ps1 -Version 2.0.0-rc.1 -OutRoot artifacts/v2.0-rc1

# v1.1.0 stable (unchanged packaging path)
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-portable.ps1 -Version 1.1.0
```

Outputs (RC1):

- `artifacts/v2.0-rc1/publish-win-x64/` — published files
- `artifacts/v2.0-rc1/ExplorerPilot-2.0.0-rc.1-win-x64-portable.zip`
- `artifacts/v2.0-rc1/ExplorerPilot-2.0.0-rc.1-win-x64-portable.sha256`

v1.1.0 stable outputs remain under `artifacts/v1.1/` and are not overwritten by RC packaging.

**Code signing:** release scripts do **not** Authenticode-sign binaries. Builds are **unsigned** and may trigger Windows SmartScreen. See [docs/CODE_SIGNING.md](docs/CODE_SIGNING.md).

## Usage

See [docs/USER_GUIDE.md](docs/USER_GUIDE.md) and [CHANGELOG.md](CHANGELOG.md).

## Current limitations

- Reports **logical** file sizes (`FileInfo.Length`). Physical allocated size may differ.
- Single controlled scan worker (no multi-threaded enumeration).
- Directory junctions and directory symbolic links are **not traversed** (cycle-safe).
- Long paths require OS long-path policy in addition to the app manifest.
- Incomplete or cancelled scans are labeled — do not treat those totals as final.
- Cleanup recommendations never auto-delete files.
- Unsigned builds may trigger SmartScreen until you sign them.

## Privacy & data locations

| Data | Location |
|------|----------|
| Settings | `%LocalAppData%\ExplorerPilot\settings` |
| Snapshots | `%LocalAppData%\ExplorerPilot\` (snapshot store) |
| Logs | `%LocalAppData%\ExplorerPilot\logs` |

No network telemetry is enabled in v1.1.

## License

See [LICENSE](LICENSE).

## Acknowledgments

ExplorerPilot is an independent project. Names of other disk-analysis tools are mentioned only to describe the problem domain.
