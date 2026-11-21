<#
.SYNOPSIS
  Builds a portable Windows x64 self-contained Release package for ExplorerPilot.
.DESCRIPTION
  Publishes self-contained + single-file, zips the output, and writes SHA-256 checksums.
  Code signing is NOT performed (see docs/CODE_SIGNING.md).
  Defaults to v1.1 artifacts; pass -OutRoot to override. Never deletes other version folders.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "1.1.0",
    [string]$OutRoot = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutRoot)) {
    $versionFolder = "v" + ($Version -replace '^(\d+\.\d+).*$', '$1')
    $OutRoot = Join-Path $repoRoot "artifacts\$versionFolder"
}

$publishDir = Join-Path $OutRoot "publish-$Runtime"
$zipPath = Join-Path $OutRoot "ExplorerPilot-$Version-$Runtime-portable.zip"
$checksumPath = Join-Path $OutRoot "ExplorerPilot-$Version-$Runtime-portable.sha256"
$project = Join-Path $repoRoot "src\ExplorerPilot.App\ExplorerPilot.App.csproj"

Write-Host "Repo: $repoRoot"
Write-Host "Publish dir: $publishDir"
Write-Host "Version: $Version"

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}
if (Test-Path $checksumPath) {
    Remove-Item $checksumPath -Force
}
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

Write-Host "Running tests..."
dotnet test (Join-Path $repoRoot "ExplorerPilot.sln") -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "Tests failed." }

Write-Host "Publishing self-contained single-file ($Runtime)..."
dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

$exe = Join-Path $publishDir "ExplorerPilot.exe"
if (-not (Test-Path $exe)) {
    throw "Published executable not found: $exe"
}

$infoVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion
if ($infoVersion -notlike "$Version*") {
    throw "ProductVersion mismatch: expected '$Version*', got '$infoVersion'"
}

Write-Host "Creating ZIP..."
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

$hash = (Get-FileHash -Algorithm SHA256 -Path $zipPath).Hash.ToLowerInvariant()
$line = "$hash  $(Split-Path -Leaf $zipPath)"
Set-Content -Path $checksumPath -Value $line -Encoding ascii

$exeHash = (Get-FileHash -Algorithm SHA256 -Path $exe).Hash.ToLowerInvariant()
Add-Content -Path $checksumPath -Value "$exeHash  ExplorerPilot.exe" -Encoding ascii

Write-Host ""
Write-Host "Portable package ready:"
Write-Host "  ZIP:      $zipPath"
Write-Host "  SHA256:   $checksumPath"
Write-Host "  ZIP hash: $hash"
Write-Host "  EXE hash: $exeHash"
Write-Host "  ProductVersion: $infoVersion"
Write-Host ""
Write-Host "NOTE: This build is NOT Authenticode-signed. See docs/CODE_SIGNING.md."
