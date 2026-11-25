# Renders candidate key icons for the Aldis Lamp action.
# Everything is drawn with GDI+ so the whole look is reproducible from source:
# run this, then look at tools\icon-drafts\contact-sheet.png.
#   pwsh -File tools\make-icons.ps1                 -> the A-E set
#   pwsh -File tools\make-icons.ps1 -sets shutter  -> the B + shutter set
# Each design is rendered for every lens palette in $palettes.

param([string[]]$sets = @('all'))

Add-Type -AssemblyName System.Drawing

$here = if ($PSScriptRoot) { $PSScriptRoot } else { "$PWD\tools" }
$out = Join-Path $here 'icon-drafts'
New-Item -ItemType Directory -Force -Path $out | Out-Null

$S = 144

function Col { param([int]$r, [int]$g, [int]$b, [int]$a = 255)
  [System.Drawing.Color]::FromArgb($a, $r, $g, $b) }

# Hardware tones, shared by every design.
$steelDark = Col 32 36 42
$steel     = Col 59 65 74
$steelEdge = Col 123 133 146
$brass     = Col 201 162 75
$brassDark = Col 141 108 43

# The two lens moods: warm = a lamp that is lit, cyan = the ring's colour.
# 'stop' is the unlit lens behind a closed shutter, used by the Stop designs.
$palettes = [ordered]@{
  'warm' = @{ lens = (Col 255 207 94); dark = (Col 255 176 32); pale = (Col 255 227 168) }
  'cyan' = @{ lens = (Col 78 201 255); dark = (Col 42 152 205); pale = (Col 200 238 255) }
  'stop' = @{ lens = (Col 44 49 57);   dark = (Col 62 69 79);   pale = (Col 96 106 118) }
}

function Pen { param($color, [double]$width)
  New-Object System.Drawing.Pen -ArgumentList $color, ([single]$width) }
function Brush { param($color) New-Object System.Drawing.SolidBrush -ArgumentList $color }
function FillEllipse { param($g, $brush, [double]$x, [double]$y, [double]$w, [double]$h)
  $g.FillEllipse($brush, [single]$x, [single]$y, [single]$w, [single]$h) }
function DrawEllipse { param($g, $pen, [double]$x, [double]$y, [double]$w, [double]$h)
  $g.DrawEllipse($pen, [single]$x, [single]$y, [single]$w, [single]$h) }
function Line { param($g, $pen, [double]$x1, [double]$y1, [double]$x2, [double]$y2)
  $g.DrawLine($pen, [single]$x1, [single]$y1, [single]$x2, [single]$y2) }
function DrawArc { param($g, $pen, [double]$x, [double]$y, [double]$w, [double]$h,
                            [double]$start, [double]$sweep)
  $g.DrawArc($pen, [single]$x, [single]$y, [single]$w, [single]$h, [single]$start, [single]$sweep) }

function RoundedPath { param([double]$x, [double]$y, [double]$w, [double]$h, [double]$r)
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = [single]($r * 2)
  $p.AddArc([single]$x, [single]$y, $d, $d, [single]180, [single]90)
  $p.AddArc([single]($x + $w - $r * 2), [single]$y, $d, $d, [single]270, [single]90)
  $p.AddArc([single]($x + $w - $r * 2), [single]($y + $h - $r * 2), $d, $d, [single]0, [single]90)
  $p.AddArc([single]$x, [single]($y + $h - $r * 2), $d, $d, [single]90, [single]90)
  $p.CloseFigure()
  return $p
}
function FillRounded { param($g, $brush, [double]$x, [double]$y, [double]$w, [double]$h, [double]$r)
  $p = RoundedPath $x $y $w $h $r
  $g.FillPath($brush, $p)
  $g.DrawPath((Pen $steelEdge 1.5), $p)
  $p.Dispose() }
function DotDash { param($g, $brush, [double]$x, [double]$y, [string]$pattern, [double]$h)
  $cx = $x
  foreach ($ch in $pattern.ToCharArray()) {
    if ($ch -eq '.') { FillEllipse $g $brush $cx $y $h $h; $cx += $h + $h * 0.6 }
    elseif ($ch -eq '-') { $p = RoundedPath $cx $y ($h * 2.2) $h ($h / 2); $g.FillPath($brush, $p); $p.Dispose(); $cx += $h * 2.2 + $h * 0.6 }
    elseif ($ch -eq ' ') { $cx += $h * 1.2 }
  }
}
function Triangle { param([double]$x1, [double]$y1, [double]$x2, [double]$y2, [double]$x3, [double]$y3)
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $pts = New-Object System.Drawing.PointF[] 3
  $pts[0] = New-Object System.Drawing.PointF -ArgumentList ([single]$x1), ([single]$y1)
  $pts[1] = New-Object System.Drawing.PointF -ArgumentList ([single]$x2), ([single]$y2)
  $pts[2] = New-Object System.Drawing.PointF -ArgumentList ([single]$x3), ([single]$y3)
  $p.AddPolygon($pts)
  return $p
}
function New-Canvas {
  $bmp = New-Object System.Drawing.Bitmap $S, $S
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.Clear([System.Drawing.Color]::Transparent)
  return @($bmp, $g)
}
function Glass { param($g, $x, $y, $w, $h)
  FillEllipse $g (Brush (Col 255 255 255 80)) $x $y $w $h }

# A - signal lamp in side profile: hooded lens on a yoke, beam leaving it.
function Design-A { param($pal)
  $c = New-Canvas; $bmp = $c[0]; $g = $c[1]
  FillRounded $g (Brush $steel) 30 112 74 12 6
  FillRounded $g (Brush $steel) 58 88 18 26 5
  FillEllipse $g (Brush $steel) 54 78 26 26
  DrawEllipse $g (Pen $steelEdge 3) 54 78 26 26
  FillRounded $g (Brush $steel) 20 46 64 40 13
  FillRounded $g (Brush $steelDark) 12 52 14 28 5
  DrawEllipse $g (Pen $steelEdge 6) 62 38 52 52
  FillEllipse $g (Brush $pal.lens) 67 43 42 42
  Glass $g 74 49 16 11
  DrawArc $g (Pen $steelDark 11) 56 32 64 64 196 148
  $beam = Brush (Color_With $pal.lens 235)
  $p = RoundedPath 118 44 22 8 4; $g.FillPath($beam, $p); $p.Dispose()
  $p = RoundedPath 118 62 14 8 4; $g.FillPath($beam, $p); $p.Dispose()
  return $bmp
}
function Color_With { param($color, [int]$alpha)
  [System.Drawing.Color]::FromArgb($alpha, $color.R, $color.G, $color.B) }

# B - the lens head-on: hood, rim bolts, fresnel rings.
function Design-B { param($pal)
  $c = New-Canvas; $bmp = $c[0]; $g = $c[1]
  FillEllipse $g (Brush $steelDark) 14 14 116 116
  DrawEllipse $g (Pen $steelEdge 7) 16 16 112 112
  FillEllipse $g (Brush $pal.lens) 32 32 80 80
  foreach ($w in 60, 40, 20) {
    DrawEllipse $g (Pen (Color_With $pal.dark 170) 3) (72 - $w / 2) (72 - $w / 2) $w $w
  }
  Glass $g 44 40 30 16
  DrawArc $g (Pen $steel 15) 8 6 128 128 202 136
  foreach ($pt in @(32, 32), @(112, 32), @(32, 112), @(112, 112)) {
    FillEllipse $g (Brush $brass) ($pt[0] - 6) ($pt[1] - 6) 12 12
  }
  DotDash $g (Brush (Col 240 240 240 215)) 52 108 '.-' 9
  return $bmp
}

# The shutter: horizontal blades clipped to the lens circle, glow showing in
# the gaps between them. This is the part that says "signal lamp" rather than
# "headlight".
function Shutters { param($g, [double]$x, [double]$y, [double]$d, [int]$count, [double]$h)
  $lens = New-Object System.Drawing.Drawing2D.GraphicsPath
  $lens.AddEllipse([single]$x, [single]$y, [single]$d, [single]$d)
  $g.SetClip($lens)
  $gap = ($d - $count * $h) / ($count + 1)
  $row = $y + $gap
  for ($i = 0; $i -lt $count; $i++) {
    $b = RoundedPath ($x - 8) $row ($d + 16) $h ($h / 2)
    $g.FillPath((Brush $steel), $b)
    $g.DrawPath((Pen (Col 150 160 172) 1.5), $b)
    $b.Dispose()
    $row += $h + $gap
  }
  $g.ResetClip()
  $lens.Dispose()
}

# B4 / B5 - the settled direction: shutter blades over fresnel rings, with the
# morse marks kept under the lens and no lever. 4 blades vs 5 blades.
function ShutterLens { param($pal, [int]$blades, [double]$h)
  $c = New-Canvas; $bmp = $c[0]; $g = $c[1]
  FillEllipse $g (Brush $steelDark) 14 14 116 116
  DrawEllipse $g (Pen $steelEdge 7) 16 16 112 112
  FillEllipse $g (Brush $pal.lens) 32 32 80 80
  foreach ($w in 66, 46, 26) {
    DrawEllipse $g (Pen (Color_With $pal.dark 205) 3) (72 - $w / 2) (72 - $w / 2) $w $w
  }
  Glass $g 42 38 34 18
  Shutters $g 32 32 80 $blades $h
  DotDash $g (Brush (Col 240 240 240 230)) 52 106 '.-' 9
  DrawArc $g (Pen $steel 15) 8 6 128 128 202 136
  foreach ($pt in @(32, 32), @(112, 32), @(32, 112), @(112, 112)) {
    FillEllipse $g (Brush $brass) ($pt[0] - 6) ($pt[1] - 6) 12 12
  }
  return $bmp
}
function Design-B4 { param($pal) ShutterLens $pal 4 9 }
function Design-B5 { param($pal) ShutterLens $pal 5 8 }

# B4 with a dark strip along the bottom of the key. Stream Deck draws the
# button title there and the bright lens washed it out, so the lamp sits a
# little higher and the strip gives the title something to sit on. The morse
# marks move into the top of the strip. $dy lifts the lamp, $bandTop is where
# the strip starts, -fade lets it fade in instead of starting on an edge.
function ShutterLensStrip { param($pal, [int]$blades, [double]$h, [double]$dy, [double]$bandTop,
                                     [switch]$fade, [switch]$noBoltsB, [double]$scale = 1.0, [switch]$full,
                                     [switch]$noMarks)
  $c = New-Canvas; $bmp = $c[0]; $g = $c[1]
  if ($scale -ne 1.0) {
    $g.TranslateTransform([single]72, [single]72)
    $g.ScaleTransform([single]$scale, [single]$scale)
    $g.TranslateTransform([single]-72, [single]-72)
  }
  $g.TranslateTransform([single]0, [single]$dy)
  FillEllipse $g (Brush $steelDark) 14 14 116 116
  DrawEllipse $g (Pen $steelEdge 7) 16 16 112 112
  FillEllipse $g (Brush $pal.lens) 32 32 80 80
  foreach ($w in 66, 46, 26) {
    DrawEllipse $g (Pen (Color_With $pal.dark 205) 3) (72 - $w / 2) (72 - $w / 2) $w $w
  }
  Glass $g 42 38 34 18
  Shutters $g 32 32 80 $blades $h
  DrawArc $g (Pen $steel 15) 8 6 128 128 202 136
  foreach ($pt in @(32, 32), @(112, 32)) {
    FillEllipse $g (Brush $brass) ($pt[0] - 6) ($pt[1] - 6) 12 12
  }
  if (-not $noBoltsB) {
    foreach ($pt in @(32, 112), @(112, 112)) {
      FillEllipse $g (Brush $brass) ($pt[0] - 6) ($pt[1] - 6) 12 12
    }
  }
  $g.ResetTransform()

  # The title plaque. Inset with rounded corners, or -full edge to edge, which
  # is what long titles need.
  $gradH = 12
  $sx = 6; $sw = 132
  if ($full) { $sx = 0; $sw = 144 }
  $solidTop = $bandTop
  if ($fade) {
    $lg = New-Object System.Drawing.Drawing2D.LinearGradientBrush -ArgumentList (
        New-Object System.Drawing.RectangleF([single]$sx, [single]$bandTop, [single]$sw, [single]$gradH)),
        (Col 18 21 26 0), (Col 18 21 26 255), ([System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $g.FillRectangle($lg, [single]$sx, [single]$bandTop, [single]$sw, [single]$gradH)
    $lg.Dispose()
    $solidTop = $bandTop + $gradH
  }
  $stripBrush = Brush (Col 18 21 26)
  if ($full) {
    $g.FillRectangle($stripBrush, [single]$sx, [single]$solidTop, [single]$sw, [single](144 - $solidTop))
  }
  else {
    $sp = RoundedPath $sx $solidTop $sw (146 - $solidTop) 7
    $g.FillPath($stripBrush, $sp)
    $sp.Dispose()
  }
  Line $g (Pen (Col 62 68 76) 1.5) $sx ($solidTop + 1) ($sx + $sw) ($solidTop + 1)
  if (-not $noMarks) {
    DotDash $g (Brush (Col 240 240 240 235)) 52 ($solidTop + 5) '.-' 9
  }
  return $bmp
}
function Design-T1 { param($pal) ShutterLensStrip $pal 4 9 -9 112 }
function Design-T2 { param($pal) ShutterLensStrip $pal 4 9 -12 104 -noBoltsB }
function Design-T3 { param($pal) ShutterLensStrip $pal 4 9 -10 96 -fade }
function Design-T4 { param($pal) ShutterLensStrip $pal 4 9 -14 100 -noBoltsB -scale 0.9 }
function Design-T5 { param($pal) ShutterLensStrip $pal 4 9 -12 104 -noBoltsB -full }
function Design-T6 { param($pal) ShutterLensStrip $pal 4 9 -14 98 -noBoltsB -full }
function Design-T7 { param($pal) ShutterLensStrip $pal 4 9 -12 104 -noBoltsB -full -noMarks }

# The Stop key in the same language: the shutter is closed, so the blades are
# wide enough to nearly touch and the lens behind them is dark - a lamp that
# is not signalling.
function ClosedShutter { param($pal, [int]$blades, [double]$h, [switch]$red, [switch]$slash,
                                  [double]$dy = 0, [double]$bandTop = 0, [switch]$full, [switch]$noBoltsB)
  $c = New-Canvas; $bmp = $c[0]; $g = $c[1]
  $g.TranslateTransform([single]0, [single]$dy)
  FillEllipse $g (Brush $steelDark) 14 14 116 116
  DrawEllipse $g (Pen $steelEdge 7) 16 16 112 112
  if ($red) {
    FillEllipse $g (Brush (Col 116 32 28)) 32 32 80 80
    foreach ($w in 66, 46, 26) {
      DrawEllipse $g (Pen (Col 78 20 18) 3) (72 - $w / 2) (72 - $w / 2) $w $w
    }
  }
  else {
    FillEllipse $g (Brush $pal.lens) 32 32 80 80
    foreach ($w in 66, 46, 26) {
      DrawEllipse $g (Pen $pal.dark 3) (72 - $w / 2) (72 - $w / 2) $w $w
    }
  }
  Shutters $g 32 32 80 $blades $h
  DrawArc $g (Pen $steel 15) 8 6 128 128 202 136
  foreach ($pt in @(32, 32), @(112, 32)) {
    FillEllipse $g (Brush $brass) ($pt[0] - 6) ($pt[1] - 6) 12 12
  }
  if (-not $noBoltsB) {
    foreach ($pt in @(32, 112), @(112, 112)) {
      FillEllipse $g (Brush $brass) ($pt[0] - 6) ($pt[1] - 6) 12 12
    }
  }
  if ($slash) {
    Line $g (Pen (Col 20 22 26) 13) 36 108 108 36
    Line $g (Pen (Col 226 84 66) 8) 36 108 108 36
  }
  $g.ResetTransform()
  if ($bandTop -gt 0) {
    $sx = 6; $sw = 132
    if ($full) { $sx = 0; $sw = 144 }
    $g.FillRectangle((Brush (Col 18 21 26)), [single]$sx, [single]$bandTop, [single]$sw, [single](144 - $bandTop))
    Line $g (Pen (Col 62 68 76) 1.5) $sx ($bandTop + 1) ($sx + $sw) ($bandTop + 1)
  }
  return $bmp
}
function Design-S1 { param($pal) ClosedShutter $pal 4 15 }
function Design-S2 { param($pal) ClosedShutter $pal 4 15 -red }
function Design-S3 { param($pal) ClosedShutter $pal 4 15 -slash }
function Design-S4 { param($pal) ClosedShutter $pal 4 15 -dy -12 -bandTop 104 -full -noBoltsB }

# B1 - lens head-on with four shutter blades, glow behind them.
function Design-B1 { param($pal)
  $c = New-Canvas; $bmp = $c[0]; $g = $c[1]
  FillEllipse $g (Brush $steelDark) 14 14 116 116
  DrawEllipse $g (Pen $steelEdge 7) 16 16 112 112
  FillEllipse $g (Brush $pal.lens) 32 32 80 80
  Glass $g 42 38 34 18
  Shutters $g 32 32 80 4 9
  DrawArc $g (Pen $steel 15) 8 6 128 128 202 136
  foreach ($pt in @(32, 32), @(112, 32), @(32, 112), @(112, 112)) {
    FillEllipse $g (Brush $brass) ($pt[0] - 6) ($pt[1] - 6) 12 12
  }
  return $bmp
}

# B2 - same but five finer blades with the fresnel rings still faintly visible.
function Design-B2 { param($pal)
  $c = New-Canvas; $bmp = $c[0]; $g = $c[1]
  FillEllipse $g (Brush $steelDark) 14 14 116 116
  DrawEllipse $g (Pen $steelEdge 7) 16 16 112 112
  FillEllipse $g (Brush $pal.lens) 32 32 80 80
  foreach ($w in 62, 42) {
    DrawEllipse $g (Pen (Color_With $pal.dark 150) 3) (72 - $w / 2) (72 - $w / 2) $w $w
  }
  Glass $g 42 38 34 18
  Shutters $g 32 32 80 5 8
  DrawArc $g (Pen $steel 15) 8 6 128 128 202 136
  foreach ($pt in @(32, 32), @(112, 32), @(32, 112), @(112, 112)) {
    FillEllipse $g (Brush $brass) ($pt[0] - 6) ($pt[1] - 6) 12 12
  }
  return $bmp
}

# B3 - four blades plus the shutter's operating lever on the rim.
function Design-B3 { param($pal)
  $c = New-Canvas; $bmp = $c[0]; $g = $c[1]
  FillEllipse $g (Brush $steelDark) 14 14 116 116
  DrawEllipse $g (Pen $steelEdge 7) 16 16 112 112
  FillEllipse $g (Brush $pal.lens) 32 32 80 80
  Glass $g 42 38 34 18
  Shutters $g 32 32 80 4 9
  DrawArc $g (Pen $steel 15) 8 6 128 128 202 136
  FillRounded $g (Brush $steel) 104 64 30 16 6       # shutter lever
  FillEllipse $g (Brush $brass) 122 64 16 16         # knob
  foreach ($pt in @(32, 32), @(112, 32), @(32, 112)) {
    FillEllipse $g (Brush $brass) ($pt[0] - 6) ($pt[1] - 6) 12 12
  }
  return $bmp
}

# C - lamp throwing a beam that carries the morse.
function Design-C { param($pal)
  $c = New-Canvas; $bmp = $c[0]; $g = $c[1]
  $cone = Triangle 72 56 138 18 138 122
  $g.FillPath((Brush (Color_With $pal.lens 50)), $cone); $cone.Dispose()
  $cone = Triangle 72 62 132 36 132 106
  $g.FillPath((Brush (Color_With $pal.lens 55)), $cone); $cone.Dispose()
  FillRounded $g (Brush $steel) 22 48 48 44 11
  FillRounded $g (Brush $steel) 34 90 14 20 4
  FillRounded $g (Brush $steel) 16 108 52 10 5
  DrawArc $g (Pen $steelDark 8) 46 40 56 56 200 140
  DrawEllipse $g (Pen $steelEdge 5) 54 52 40 40
  FillEllipse $g (Brush $pal.lens) 58 56 32 32
  Glass $g 63 60 12 9
  DotDash $g (Brush (Col 245 245 245 240)) 94 66 '.-' 11
  return $bmp
}

# D - shutter lamp on a bracket: blade across the lens, morse lit below it.
function Design-D { param($pal)
  $c = New-Canvas; $bmp = $c[0]; $g = $c[1]
  FillRounded $g (Brush $steel) 30 116 84 12 6
  FillRounded $g (Brush $steel) 62 100 20 18 4
  FillEllipse $g (Brush $steelDark) 18 14 96 96
  DrawEllipse $g (Pen $steelEdge 6) 18 14 96 96
  FillEllipse $g (Brush $pal.lens) 30 26 72 72
  $shut = RoundedPath 22 40 88 16 8
  $g.FillPath((Brush $steel), $shut)
  $g.DrawPath((Pen $steelEdge 1.5), $shut)
  $shut.Dispose()
  DotDash $g (Brush (Col 32 36 42 240)) 40 70 '.-.' 11
  DrawArc $g (Pen $steelDark 12) 14 8 116 116 214 112
  Glass $g 44 58 24 12
  return $bmp
}

# E - brass marine fitting: heavy rim, bolts, sighting cross.
function Design-E { param($pal)
  $c = New-Canvas; $bmp = $c[0]; $g = $c[1]
  FillEllipse $g (Brush $brassDark) 16 16 112 112
  DrawEllipse $g (Pen $brass 9) 18 18 108 108
  FillEllipse $g (Brush $steelDark) 32 32 80 80
  FillEllipse $g (Brush $pal.pale) 46 46 52 52
  DrawEllipse $g (Pen $brass 3) 46 46 52 52
  $cross = Pen (Color_With $brassDark 215) 2.5
  Line $g $cross 46 72 98 72
  Line $g $cross 72 46 72 98
  DrawArc $g (Pen $brassDark 13) 12 8 120 120 200 140
  foreach ($deg in -90, -30, 30, 90, 150, 210) {
    $rad = $deg * [Math]::PI / 180
    $bx = 72 + 54 * [Math]::Cos($rad) - 6
    $by = 72 + 54 * [Math]::Sin($rad) - 6
    FillEllipse $g (Brush $brass) $bx $by 12 12
  }
  return $bmp
}

$all = [ordered]@{
  'A-profile'  = @{ fn = { param($pal) Design-A $pal };  note = 'barrel, hood, yoke, beam' }
  'B-lens'     = @{ fn = { param($pal) Design-B $pal };  note = 'fresnel rings, rim bolts, hood' }
  'C-beam'     = @{ fn = { param($pal) Design-C $pal };  note = 'lamp plus light cone' }
  'D-shutter'  = @{ fn = { param($pal) Design-D $pal };  note = 'blade across the lens' }
  'E-brass'    = @{ fn = { param($pal) Design-E $pal };  note = 'heavy rim, bolts, sighting cross' }
  'B1-blades4' = @{ fn = { param($pal) Design-B1 $pal }; note = 'four shutter blades' }
  'B2-blades5' = @{ fn = { param($pal) Design-B2 $pal }; note = 'five finer blades, rings behind' }
  'B3-lever'   = @{ fn = { param($pal) Design-B3 $pal }; note = 'four blades plus shutter lever' }
  'B4-blades4' = @{ fn = { param($pal) Design-B4 $pal }; note = 'previous lamp: 4 blades, rings, morse'; pal = 'warm' }
  'B5-blades5' = @{ fn = { param($pal) Design-B5 $pal }; note = '5 blades over rings, morse kept' }
  'T1-strip'   = @{ fn = { param($pal) Design-T1 $pal }; note = 'lamp lifted, dark title strip, morse in strip'; pal = 'warm' }
  'T2-strip'   = @{ fn = { param($pal) Design-T2 $pal }; note = 'taller title strip, lamp lifted more'; pal = 'warm' }
  'T3-strip'   = @{ fn = { param($pal) Design-T3 $pal }; note = 'strip fading in from the top, no hard edge'; pal = 'warm' }
  'T4-strip'   = @{ fn = { param($pal) Design-T4 $pal }; note = 'smaller lamp, taller strip, marks clear of the title'; pal = 'warm' }
  'T5-strip'   = @{ fn = { param($pal) Design-T5 $pal }; note = 'T2 with the strip edge to edge'; pal = 'warm' }
  'T6-strip'   = @{ fn = { param($pal) Design-T6 $pal }; note = 'edge to edge, taller strip, lamp lifted more'; pal = 'warm' }
  'T7-strip'   = @{ fn = { param($pal) Design-T7 $pal }; note = 'installed lamp: edge to edge band, no morse marks'; pal = 'warm' }
  'S1-closed'  = @{ fn = { param($pal) Design-S1 $pal }; note = 'shutter closed, dark lens'; pal = 'stop' }
  'S2-red'     = @{ fn = { param($pal) Design-S2 $pal }; note = 'closed, red light behind the blades'; pal = 'stop' }
  'S3-slash'   = @{ fn = { param($pal) Design-S3 $pal }; note = 'closed plus a stop slash'; pal = 'stop' }
  'S4-closed'  = @{ fn = { param($pal) Design-S4 $pal }; note = 'closed shutter with the matching title strip'; pal = 'stop' }
}

$designs = [ordered]@{}
foreach ($key in $all.Keys) {
  $take = $false
  foreach ($set in $sets) {
    if ($set -eq 'all') { $take = $true }
    elseif ($set -eq 'shutter' -and $key -like 'B*') { $take = $true }
    elseif ($key -like "$set*") { $take = $true }
  }
  if ($take) { $designs[$key] = $all[$key] }
}

# Which tiles to draw: a design that names its own palette is drawn once, in
# that palette only. Everything else is drawn for every lens palette.
$pairs = @()
foreach ($name in $designs.Keys) {
  $fixed = $designs[$name]['pal']
  if ($fixed) {
    $pairs += [pscustomobject]@{ name = $name; pal = $fixed }
  }
  else {
    foreach ($palName in $palettes.Keys) { $pairs += [pscustomobject]@{ name = $name; pal = $palName } }
  }
}
$paletteRows = @()
foreach ($p in $pairs) { if ($paletteRows -notcontains $p.pal) { $paletteRows += $p.pal } }
$paletteTitles = @{ warm = 'warm lens'; cyan = 'cyan lens'; stop = 'shutter closed' }

# Contact sheet and a web page, both built from what this run rendered, so
# whatever was just drawn is what you are asked to look at. Tiles sit on the
# dark key background because that is how they will be seen.
$pad = 10
$tile = 156
$labelH = 24
$cols = $designs.Keys.Count
$rows = $paletteRows.Count
$sheetW = $cols * ($tile + $pad) + $pad
$sheetH = $rows * ($tile + $labelH + $pad) + $pad
$sheet = New-Object System.Drawing.Bitmap $sheetW, $sheetH
$sg = [System.Drawing.Graphics]::FromImage($sheet)
$sg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$sg.Clear((Col 22 24 27))
$font = New-Object System.Drawing.Font -ArgumentList 'Segoe UI', ([single]13), ([System.Drawing.FontStyle]::Bold)
$keyBrush = Brush (Col 40 43 48)
$textBrush = Brush (Col 214 217 222)

# A sample title is painted onto the sheet and the page, the way Stream Deck
# draws it, so a draft can be judged with text on it. It is NOT in the saved
# PNG - the app draws the real title itself.
$titleFont = New-Object System.Drawing.Font -ArgumentList 'Segoe UI', ([single]17), ([System.Drawing.FontStyle]::Bold)
$titleSf = New-Object System.Drawing.StringFormat
$titleSf.Alignment = [System.Drawing.StringAlignment]::Center
$titleSf.LineAlignment = [System.Drawing.StringAlignment]::Far
$titleBrush = Brush (Col 245 245 245)
$titleText = 'HEAVE TO'

$rendered = @()
$row = 0
foreach ($palName in $paletteRows) {
  $col = 0
  foreach ($name in $designs.Keys) {
    $x = $pad + $col * ($tile + $pad)
    $y = $pad + $row * ($tile + $labelH + $pad)
    $tp = RoundedPath $x $y $tile $tile 12
    $sg.FillPath($keyBrush, $tp)
    $tp.Dispose()

    $tilePair = $pairs | Where-Object { $_.name -eq $name -and $_.pal -eq $palName } | Select-Object -First 1
    if ($tilePair) {
      $sb = $designs[$name]['fn']
      $bmp = & $sb $palettes[$palName]
      $path = Join-Path $out "$name-$palName.png"
      $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
      "wrote $path"
      $sg.DrawImage($bmp, [int]($x + ($tile - $S) / 2), [int]($y + ($tile - $S) / 2), $S, $S)
      $tr = New-Object System.Drawing.RectangleF ([single]($x + ($tile - $S) / 2)),
        ([single]($y + ($tile - $S) / 2 + 104)), ([single]$S), ([single]34)
      $sg.DrawString($titleText, $titleFont, $titleBrush, $tr, $titleSf)
      $sg.DrawString("$name / $palName", $font, $textBrush, [single]($x + 2), [single]($y + $tile + 4))
      $rendered += [pscustomobject]@{ name = $name; pal = $palName; note = $designs[$name]['note'] }
      $bmp.Dispose()
    }
    $col++
  }
  $row++
}
$sheetPath = Join-Path $out 'contact-sheet.png'
$sheet.Save($sheetPath, [System.Drawing.Imaging.ImageFormat]::Png)
"wrote $sheetPath"
$sheet.Dispose()

$html = New-Object System.Text.StringBuilder
[void]$html.AppendLine('<!doctype html><html><head><meta charset="utf-8"><title>Aldis Lamp - icon drafts</title>')
[void]$html.AppendLine('<style>body{background:#14171a;color:#d6d9de;font:14px/1.45 "Segoe UI",sans-serif;margin:24px}')
[void]$html.AppendLine('h1{font-size:19px;margin:0 0 4px}p{color:#9aa3ad;margin:0 0 22px}')
[void]$html.AppendLine('.grid{display:grid;grid-template-columns:repeat(' + $cols + ',minmax(0,1fr));gap:18px}')
[void]$html.AppendLine('.cell{text-align:center}.tile{background:#282b30;border-radius:14px;padding:8px;display:inline-block;position:relative}')
[void]$html.AppendLine('.tile img{width:158px;height:158px;display:block}')
[void]$html.AppendLine('.ttl{position:absolute;left:12px;right:12px;bottom:12px;text-align:center;color:#f5f5f5;font-size:18px;font-weight:600;line-height:1.1}')
[void]$html.AppendLine('.cap{margin-top:8px;font-weight:600}')
[void]$html.AppendLine('.sub{color:#8f98a3;font-size:12px}h2{font-size:14px;margin:26px 0 10px;color:#9aa3ad;font-weight:600}')
[void]$html.AppendLine('</style></head><body><h1>Aldis Lamp &mdash; key icon drafts</h1>')
[void]$html.AppendLine('<p>Each tile is a Stream Deck key at real size. Pick a design and a lens colour.</p>')
foreach ($palName in $paletteRows) {
  $title = if ($paletteTitles[$palName]) { $paletteTitles[$palName] } else { "$palName lens" }
  [void]$html.AppendLine("<h2>$title</h2><div class=`"grid`">")
  foreach ($item in $rendered) {
    if ($item.pal -ne $palName) { continue }
    [void]$html.AppendLine(('<div class="cell"><span class="tile"><img src="{0}-{1}.png"><span class="ttl">HEAVE TO</span></span><div class="cap">{0}</div><div class="sub">{2}</div></div>' -f $item.name, $item.pal, $item.note))
  }
  [void]$html.AppendLine('</div>')
}
[void]$html.AppendLine('</body></html>')
$htmlPath = Join-Path $out 'preview.html'
Set-Content -Path $htmlPath -Value $html.ToString() -Encoding UTF8
"wrote $htmlPath"
