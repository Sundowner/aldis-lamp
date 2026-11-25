# Builds Aldis Lamp into an installable .streamDeckPlugin bundle.
# Requires only Windows built-ins: .NET Framework csc.exe + System.Drawing.
# Run from anywhere; paths are resolved relative to this file.
#
# Icons are NOT redrawn by default: plugin\assets\*.png is hand-authored by
# tools\make-icons.ps1. Pass -regenIcons to fall back to the simple built-in
# drawings (useful only if the artwork is ever lost).

param([switch]$regenIcons)

$ErrorActionPreference = "Stop"
$root    = Split-Path -Parent $MyInvocation.MyCommand.Path
$src     = Join-Path $root "src\Bridge.cs"
$plugin  = Join-Path $root "plugin"
$assets  = Join-Path $plugin "assets"
$winDir  = Join-Path $plugin "windows"
$csc     = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$exe     = Join-Path $winDir "aldis-bridge.exe"
$out     = Join-Path $root "AldisLamp.streamDeckPlugin"

New-Item -ItemType Directory -Force -Path $assets, $winDir | Out-Null

# ---------------------------------------------------------------- compile
Write-Host "Compiling bridge..."
& $csc /nologo /target:exe /platform:x64 /optimize+ /reference:System.Drawing.dll /out:$exe $src
if ($LASTEXITCODE -ne 0) { throw "csc failed with $LASTEXITCODE" }

# ---------------------------------------------------------------- icons
Add-Type -AssemblyName System.Drawing

function New-LampIcon([string]$path, [int]$size, [bool]$isStop) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::FromArgb(24, 24, 28))

    $s      = $size                     # shorthand
    $pad    = [Math]::Max(1, [int]($s * 0.10))
    $radius = [Math]::Max(2, [int]($s * 0.14))

    # rounded backdrop
    $back = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bw = $s - 2 * $pad
    $back.AddArc($pad, $pad, $radius * 2, $radius * 2, 180, 90)
    $back.AddArc($pad + $bw - $radius*2, $pad, $radius*2, $radius*2, 270, 90)
    $back.AddArc($pad + $bw - $radius*2, $pad + $bw - $radius*2, $radius*2, $radius*2, 0, 90)
    $back.AddArc($pad, $pad + $bw - $radius*2, $radius*2, $radius*2, 90, 90)
    $back.CloseFigure()
    $backBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 34, 37, 44))
    $g.FillPath($backBrush, $back)

    $amber     = [System.Drawing.Color]::FromArgb(255, 224, 177, 62)
    $accBrush  = New-Object System.Drawing.SolidBrush $amber
    $accColor  = $amber

    if (-not $isStop) {
        # Aldis signal lamp: amber lens upper-left + two radiating arcs
        $cx = [float]($s * 0.36); $cy = [float]($s * 0.36)
        $lr = [Math]::Max(2, [int]($s * 0.14))
        $g.FillEllipse($accBrush, [float]($cx - $lr), [float]($cy - $lr), [float](2 * $lr), [float](2 * $lr))

        $pen = New-Object System.Drawing.Pen $accColor, ([float]([Math]::Max(1, $s * 0.055)))
        foreach ($rr in @(0.30, 0.46)) {
            $ar = [float]($s * $rr)
            $g.DrawArc($pen, [float]($cx - $ar), [float]($cy - $ar), [float](2 * $ar), [float](2 * $ar), -55, 130)
        }
        $pen.Dispose()
    }
    else {
        # Stop: amber ring with a diagonal slash
        $pen = New-Object System.Drawing.Pen $accColor, ([float]([Math]::Max(2, $s * 0.08)))
        $m  = [float]($s * 0.26)
        $d  = [float]($s - 2 * $m)
        $g.DrawEllipse($pen, $m, $m, $d, $d)
        $g.DrawLine($pen, [float]($s * 0.32), [float]($s * 0.32), [float]($s * 0.68), [float]($s * 0.68))
        $pen.Dispose()
    }

    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  icon: $path"
}

if ($regenIcons) {
    New-LampIcon (Join-Path $assets "action.png") 72 $false
    New-LampIcon (Join-Path $assets "plugin.png") 96 $false
    New-LampIcon (Join-Path $assets "bridge.png") 28 $false
    New-LampIcon (Join-Path $assets "stop.png")   56 $true
} else {
    Write-Host "  icons: keeping the artwork in plugin\assets (tools\make-icons.ps1)"
}

# ---------------------------------------------------------------- package
# Stream Deck requires the payload inside a top-level "<UUID>.sdPlugin/"
# folder, manifest.json first.
if (Test-Path $out) { Remove-Item $out -Force }
$uuid = (Get-Content (Join-Path $plugin "manifest.json") -Raw | ConvertFrom-Json).UUID
Add-Type -AssemblyName System.IO.Compression.FileSystem
$fs = [IO.File]::Open($out, [IO.FileMode]::Create)
$za = New-Object IO.Compression.ZipArchive($fs, [IO.Compression.ZipArchiveMode]::Create)
$prefix = "$uuid.sdPlugin/"
$names = Get-ChildItem $plugin -Recurse -File | ForEach-Object { $_.FullName.Substring($plugin.Length + 1) -replace '\\', '/' }
foreach ($name in (@("manifest.json") + ($names | Where-Object { $_ -ne "manifest.json" } | Sort-Object))) {
    $entry = $za.CreateEntry($prefix + $name, [IO.Compression.CompressionLevel]::Optimal)
    $src = Join-Path $plugin ($name -replace '/', '\')
    $es = $entry.Open(); $bytes = [IO.File]::ReadAllBytes($src); $es.Write($bytes, 0, $bytes.Length); $es.Dispose()
}
$za.Dispose(); $fs.Dispose()

Write-Host ""
Write-Host "Bundle: $out"
Write-Host "Double-click it to install into Stream Deck (enable 'Allow plugins from community' if prompted)."
