# Changelog

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
