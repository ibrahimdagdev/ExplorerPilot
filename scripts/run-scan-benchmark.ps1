$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
dotnet run --project (Join-Path $repo "tools\ExplorerPilot.ScanBenchmark\ExplorerPilot.ScanBenchmark.csproj") -c Release -- @args
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
