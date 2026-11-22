# Changelog

## 2.0.0 — 2026-11-22

### Added
- Stable ExplorerPilot **v2.0.0** (ProductVersion `2.0.0`, FileVersion `2.0.0.0`)
- Modern **left sidebar navigation** with Overview / Disk Analysis / Monitoring / Tools + pinned **Settings**
- Collapsible sidebar with persisted expand/collapse preference
- Headerless workspace host keeps ViewModel/session state across page switches
- **Settings** page for Appearance (Light/Dark, English/Türkçe) and Application info (version, GitHub)
- Multi-drive discovery and Drives overview
- Professional Dashboard (KPIs, volumes, last scan, shortcuts)
- Scheduled Scans (Daily/Weekly/Monthly/Manual) with busy/skip and auto-snapshot on success
- Snapshot Comparison, Trends, Cleanup preview-only recommendations
- CSV / HTML / JSON report export
- EN/TR localization and Light/Dark/System themes (System remains backward-compatible in settings store)

### Improved
- Deterministic DiskScanner cancellation handling for Preparing/progress races
- Toolbar horizontal scrollbar removed at the source (theme/language moved to Settings)
- Scan Activity column sizing refined for common desktop layouts

### Notes
- Portable package: `artifacts/v2.0/ExplorerPilot-2.0.0-win-x64-portable.zip`
- Does **not** replace `artifacts/v1.1` / v1.1.0 or `artifacts/v2.0-rc1`
- Builds are **unsigned** and may trigger Windows SmartScreen (see `docs/CODE_SIGNING.md`)
- Snapshot schema remains **1.0**
- Scheduled scans run only while the application is open

## 2.0.0-rc.1 — 2026-11-22

### Added / promoted
- Release Candidate packaging for ExplorerPilot **v2.0** (`2.0.0-rc.1`, FileVersion `2.0.0.1`)
- Modern **left sidebar navigation** with Overview / Disk Analysis / Monitoring / Tools + pinned **Settings**
- Collapsible sidebar with persisted expand/collapse preference
- Headerless workspace host keeps ViewModel/session state across page switches
- **Settings** page for Appearance (Light/Dark, English/Türkçe) and Application info (version, GitHub)

### Includes (from v2.0 alpha → beta)
- Multi-drive discovery and Drives overview
- Professional Dashboard (KPIs, volumes, last scan, shortcuts)
- Scheduled Scans (Daily/Weekly/Monthly/Manual) with busy/skip and auto-snapshot on success
- Snapshot Comparison, Trends, Cleanup preview-only recommendations
- CSV / HTML / JSON report export
- EN/TR localization and Light/Dark/System themes (System remains backward-compatible in settings store)

### Fixed / improved (RC hardening)
- Deterministic DiskScanner cancellation handling for Preparing/progress races
- Toolbar horizontal scrollbar removed at the source (no ScrollViewer; theme/language left the toolbar)
- Scan Activity column sizing refined for 1280×720–1920×1080 layouts

### Notes
- Branch: `release/v2.0-rc1` (from `feature/v2.0-beta-stabilization`)
- Portable package under `artifacts/v2.0-rc1` — **does not** replace `artifacts/v1.1` / v1.1.0 Release
- Unsigned builds may trigger SmartScreen (see `docs/CODE_SIGNING.md`)
- No public GitHub Release for this RC stage unless explicitly published later
- Snapshot schema remains **1.0**

## 2.0.0-beta.1 — 2026-11-22

### Fixed / improved
- Scheduled Scans: `SkippedBusy` defers NextRun (BusyRetryDelay) instead of silent per-tick retry churn
- Scheduled Scans: run gate prevents overlapping timer/RunNow executions and CTS dispose races
- Scheduled Scans: shutdown cancels the active run before awaiting the poll loop
- Scheduled Scans: clock/offset jump recalculates NextRunUtc; snapshot id only updates on success
- Dashboard: refreshes on snapshot catalog changes while visible; incomplete scans use `!IsComplete`
- Scheduled Scans UI: recycling virtualization and long-path tooltip on editor

### Notes
- Development build on `feature/v2.0-beta-stabilization`; no GitHub Release for this beta
- Focused on reliability/integration — no new major modules

## 2.0.0-alpha.3 — 2026-11-22

### Added
- Scheduled Scans tab: create/edit/delete/enable plans with Daily, Weekly, Monthly, or Manual frequency
- In-process scheduler (runs only while ExplorerPilot is open; no Windows Service / Task Scheduler)
- Missed-while-closed informational banner (does not auto-start overdue plans)
- Shared concurrency guard with manual scans (`SkippedBusy` when another scan is active)
- Automatic snapshot on successful completed scheduled scans (schema 1.0); incomplete/failed/cancelled are not snapshotted
- `IScheduledScanService` / `IScheduledScanStore` with atomic JSON persistence under `%LocalAppData%\ExplorerPilot\schedules`

### Notes
- Development build on `feature/v2.0-scheduled-scans`; no GitHub Release for this alpha
- Timer does not run when the application is closed

## 2.0.0-alpha.2 — 2026-11-22

### Added
- Professional Dashboard tab: KPI cards, volume capacity, last-scan summary, top files/folders, category bars
- Snapshot/trend and Cleanup preview panels with navigation to existing modules
- `IDashboardSummaryService` / `DashboardSummaryService` (background aggregation, bounded top-N, session guards)

### Notes
- Development build on `feature/v2.0-dashboard`; no GitHub Release for this alpha

## 2.0.0-alpha.1 — 2026-11-22

### Added
- Multi-drive foundation: `IDriveDiscoveryService` / `DriveInformation` with Fixed, Removable, Network, Optical, and other volume kinds
- Drives overview tab with capacity bars, Ready/Not Ready status, Refresh, and Scan Drive (single active scan)
- Volume vs file-scan capacity disclaimer (EN/TR)
- Overflow-safe capacity helpers (`DriveCapacityMath`)

### Notes
- Development version on `feature/v2.0-multi-drive`; v1.1.0 release package unchanged
- Concurrent multi-drive scanning is intentionally not included in this phase

## 1.1.0 — 2026-11-21

### Improved
- DiskScanner metadata I/O: use `DirectoryInfo.EnumerateFileSystemInfos` to reuse Find* attributes/times/length instead of per-entry `GetAttributes` + `FileInfo` / `GetLastWriteTimeUtc` (legacy mode retained for A/B)
- Optimized scanner is the DI default (`AddSingleton<IDiskScanner, DiskScanner>()` → `FileSystemInfoMetadata`)
- Explorer TreeView materializes child nodes lazily on expand, cutting ApplyResult UI-thread cost on large trees
- Scan timing logs separate `ScanCore` vs `ApplyResult` durations
- Benchmark harness compares Legacy vs Optimized with shuffled engine order (`tools/ExplorerPilot.ScanBenchmark`)
- Portable publish defaults to `artifacts/v1.1` (does not replace `artifacts/v1.0`)

### Fixed / validated
- Legacy vs Optimized parity for hidden/system/read-only, Unicode names, long paths, reparse points, access denied, delete-during-scan, cancel/restart
- Metadata cache semantics documented as enumeration-time snapshot (no live Refresh after scan)

## 1.0.0 — 2026-11-21

### Added
- Nine-module workspace: Explorer, Largest Files, File Types, Search, Duplicates, Treemap, Comparison, Cleanup, Trends
- Snapshot schema 1.0 with import/export, comparison, and disk-usage trends
- Local file logging under `%LocalAppData%\ExplorerPilot\logs` (no telemetry)
- Global UI / domain exception handlers with localized recoverable error dialogs
- Path redaction in diagnostic logs
- Portable Windows x64 self-contained publish script (`scripts/publish-portable.ps1`)
- Application icon and product version **1.0.0**

### Improved
- Scan engine long-path enumeration via `\\?\` when needed
- Trends chart downsampling (first/last/peaks) instead of discarding history
- Toolbar horizontal scroll on narrow / high-DPI layouts
- ViewModel dispose hygiene for catalog notifiers and cancellation tokens
- Comparison snapshot list auto-refresh on catalog changes

### Security / privacy
- Snapshot import path-traversal rejection retained
- CSV formula injection and HTML escaping retained
- No automatic file deletion / cleanup actions that mutate user data
- Telemetry remains opt-in only (currently not implemented / off by default)

### Known limitations
- Directory junctions and directory symbolic links are skipped (not traversed)
- Logical file sizes only (not NTFS allocated size)
- Builds are not Authenticode-signed unless you sign them yourself
