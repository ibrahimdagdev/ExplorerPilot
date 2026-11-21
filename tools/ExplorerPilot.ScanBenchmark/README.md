# ExplorerPilot.ScanBenchmark

Performance harness for the existing `DiskScanner` (no algorithm changes).

## Run

From repo root:

```powershell
dotnet run --project tools/ExplorerPilot.ScanBenchmark -c Release -- --keep-fixtures
```

Optional larger fixture:

```powershell
dotnet run --project tools/ExplorerPilot.ScanBenchmark -c Release -- --optional-250k
```

Outputs:

- `artifacts/benchmarks/diskscanner-benchmark-*.md`
- `artifacts/benchmarks/diskscanner-benchmark-*.csv`

Fixtures are written only under `%TEMP%\ExplorerPilot.ScanBenchmark\` and deleted unless `--keep-fixtures` is set.
