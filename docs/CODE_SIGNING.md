# Code signing (ExplorerPilot)

Official portable packages produced by `scripts/publish-portable.ps1` are **not Authenticode-signed** unless you run an additional signing step.

## Why this matters

Windows SmartScreen may warn on first launch of an unsigned `.exe`. That does not mean the file was modified; verify the SHA-256 checksum from the release package.

## How to sign (optional)

If you have an Authenticode certificate:

```powershell
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a `
  artifacts\v1.0\publish-win-x64\ExplorerPilot.exe
```

Then re-zip and regenerate checksums:

```powershell
Get-FileHash -Algorithm SHA256 artifacts\v1.0\ExplorerPilot-1.0.0-win-x64-portable.zip
```

## Verification without a signature

1. Download the ZIP and `.sha256` file.
2. Run `Get-FileHash -Algorithm SHA256 <zip>` and compare to the published hash.
3. Prefer copies obtained from the publisher’s site ([tasarisoft.com](https://tasarisoft.com)).
