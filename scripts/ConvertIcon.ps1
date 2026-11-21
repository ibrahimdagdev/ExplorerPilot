$ErrorActionPreference = "Stop"
$src = "C:\Users\Pc Hocam\.cursor\projects\c-Users-Pc-Hocam-Desktop-ExplorerPilot\assets\ExplorerPilot-icon.jpg"
$outDir = "c:\Users\Pc Hocam\Desktop\ExplorerPilot\src\ExplorerPilot.App\Assets"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$icoPath = Join-Path $outDir "ExplorerPilot.ico"

Add-Type -AssemblyName System.Drawing
$img = [System.Drawing.Image]::FromFile($src)
$sizes = @(16, 32, 48, 256)
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$sizes.Count)
$imageData = New-Object System.Collections.Generic.List[byte[]]
$offset = 6 + (16 * $sizes.Count)

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($img, 0, 0, $s, $s)
    $g.Dispose()

    $pngMs = New-Object System.IO.MemoryStream
    $bmp.Save($pngMs, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $pngMs.ToArray()
    $pngMs.Dispose()
    $bmp.Dispose()
    $imageData.Add($bytes)

    $dim = 0
    if ($s -ne 256) { $dim = $s }
    $bw.Write([byte]$dim)
    $bw.Write([byte]$dim)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$bytes.Length)
    $bw.Write([UInt32]$offset)
    $offset += $bytes.Length
}

foreach ($b in $imageData) { $bw.Write($b) }
[IO.File]::WriteAllBytes($icoPath, $ms.ToArray())
$bw.Dispose()
$ms.Dispose()
$img.Dispose()
Get-Item $icoPath | Format-List FullName, Length
