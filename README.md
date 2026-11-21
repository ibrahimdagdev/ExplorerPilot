# ExplorerPilot

**ExplorerPilot** is a modern disk space analyzer for Windows. It helps you understand which folders and files consume the most space on a drive or in a selected directory.

| | |
|---|---|
| **Application** | ExplorerPilot |
| **Version** | 2.0.0-alpha.2 (development) / 1.1.0 (latest release) |
| **Developer** | İbrahim Dağ |
| **Publisher** | Tasarisoft |
| **Website** | [tasarisoft.com](https://tasarisoft.com) |
| **Repository** | [github.com/ibrahimdagdev/ExplorerPilot](https://github.com/ibrahimdagdev/ExplorerPilot) |
| **Description** | A modern disk space analyzer for Windows. |

Inspired by the *purpose* of tools such as TreeSize, WizTree, and WinDirStat — but implemented independently, without copying code, assets, branding, or UI designs.

## Features (v1.1.0)

- Scan a complete drive or a selected folder (background worker, cancellable)
- Nine-module workspace: Explorer, Largest Files, File Types, Search, Duplicates, Treemap, Comparison, Cleanup, Trends
- Snapshot save/import/export, comparison, cleanup recommendations (report-only), trends
- CSV / HTML reporting with formula-injection and HTML escaping protections
- EN / TR localization and Light / Dark / System themes
- Per-monitor DPI awareness; portable Windows x64 self-contained package
- Local diagnostic logs only — **no telemetry by default**

### v1.1.0 highlights

- **Faster scanning:** `DirectoryInfo.EnumerateFileSystemInfos` reuses Find* metadata (attributes, times, length) instead of per-entry `GetAttributes` + `FileInfo` round-trips
- **Optimized scanner is the default** via DI (`IDiskScanner` → `DiskScanner` / `FileSystemInfoMetadata`); legacy mode retained for A/B benchmarks
- **Responsive Explorer tree:** child nodes materialize lazily on expand so large trees do not freeze the UI on ApplyResult
- **Scan timing:** separate `ScanCore` vs `ApplyResult` durations in diagnostic logs
- Portable package published under `artifacts/v1.1` (does not replace v1.0 packages)

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
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish-portable.ps1 -Version 1.1.0
```

Outputs (default for 1.1.0):

- `artifacts/v1.1/publish-win-x64/` — published files
- `artifacts/v1.1/ExplorerPilot-1.1.0-win-x64-portable.zip`
- `artifacts/v1.1/ExplorerPilot-1.1.0-win-x64-portable.sha256`

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
