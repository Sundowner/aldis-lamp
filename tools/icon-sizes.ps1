# Show icon drafts at the sizes the Stream Deck app really uses them.
#   tools\icon-sizes.ps1 -Names P1,P2,P3,P4
# plugin.png is drawn at 144 but shown small in the plugin list, and on both a
# dark and a light background, so a draft that only looks good at 144 on dark
# is not actually usable. Each draft is placed on both backgrounds at 96, 48
# and 28 px.
#>
param([string[]]$Names = @('P1-plate', 'P2-plate'), [int[]]$Sizes = @(96, 48, 28))

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$drafts = Join-Path $root 'tools\icon-drafts'
$out = Join-Path $drafts 'size-sheet.png'

function Col { param($r, $g, $b, $a = 255) [System.Drawing.Color]::FromArgb($a, $r, $g, $b) }

$bgDark  = Col 22 24 27
$bgLight = Col 236 237 240
$label   = Col 200 204 210
$cell = 108
$pad = 8
$labelW = 116

$rows = @()
foreach ($n in $Names) {
  $f = Join-Path $drafts "$n-warm.png"
  if (-not (Test-Path $f)) { Write-Warning "no draft $f"; continue }
  $rows += [pscustomobject]@{ name = $n; file = $f }
}
if (-not $rows) { throw 'no drafts matched' }

$colsPerSize = 2   # dark, light
$cellW = $cell + $pad
$sheetW = $labelW + ($Sizes.Count * $colsPerSize * $cellW) + $pad
$sheetH = ($rows.Count + 1) * ($cellW + 22) + $pad
$sheet = New-Object System.Drawing.Bitmap $sheetW, $sheetH
$g = [System.Drawing.Graphics]::FromImage($sheet)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.Clear((Col 12 13 15))

$font = New-Object System.Drawing.Font -ArgumentList 'Segoe UI', ([single]11), ([System.Drawing.FontStyle]::Regular)
$bold = New-Object System.Drawing.Font -ArgumentList 'Segoe UI', ([single]11), ([System.Drawing.FontStyle]::Bold)
$lb = New-Object System.Drawing.SolidBrush $label

# header: which size, which background
$x0 = $labelW
$hy = $pad
foreach ($sz in $Sizes) {
  foreach ($which in 'dark', 'light') {
    $g.DrawString("$sz px $which", $bold, $lb, [single]$x0, [single]$hy)
    $x0 += $cellW
  }
}

$y = $hy + 26
foreach ($r in $rows) {
  $g.DrawString($r.name, $bold, $lb, [single]$pad, [single]($y + $cell / 2 - 8))
  $x = $labelW
  $img = [System.Drawing.Image]::FromFile($r.file)
  foreach ($sz in $Sizes) {
    foreach ($which in 'dark', 'light') {
      $back = if ($which -eq 'dark') { $bgDark } else { $bgLight }
      $g.FillRectangle((New-Object System.Drawing.SolidBrush $back), [single]$x, [single]$y, [single]$cell, [single]$cell)
      $off = [single](($cell - $sz) / 2)
      $g.DrawImage($img, [System.Drawing.RectangleF]::new($x + $off, $y + $off, [single]$sz, [single]$sz))
      $g.DrawRectangle((New-Object System.Drawing.Pen (Col 70 74 80)), [single]$x, [single]$y, [single]$cell, [single]$cell)
      $x += $cellW
    }
  }
  $img.Dispose()
  $y += $cellW + 22
}

$sheet.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $sheet.Dispose()
Write-Output "wrote $out"
