# Composites ad creatives over frames rendered out of the game.
# ASCII only in this file: PS 5.1 reads .ps1 as ANSI without a BOM, so every piece of
# Russian copy lives in texts.txt and is read back as UTF-8 explicitly.

Add-Type -AssemblyName System.Drawing

$Root   = Split-Path -Parent $MyInvocation.MyCommand.Path
$Frames = Join-Path $Root 'frames'
$Out    = Join-Path $Root 'creatives'
New-Item -ItemType Directory -Force -Path $Out | Out-Null

# ---- copy ----------------------------------------------------------------------------
$T = @{}
foreach ($line in [System.IO.File]::ReadAllLines((Join-Path $Root 'texts.txt'), [Text.Encoding]::UTF8)) {
    if ($line -match '^\s*([^=]+?)\s*=\s*(.*)$') { $T[$Matches[1]] = $Matches[2] }
}

# ---- palette, taken from the game's own HUD ------------------------------------------
$Ink       = [System.Drawing.Color]::FromArgb(255, 35, 50, 74)
$White     = [System.Drawing.Color]::White
$Yellow    = [System.Drawing.Color]::FromArgb(255, 255, 209, 71)
$Green     = [System.Drawing.Color]::FromArgb(255, 74, 201, 96)
$GreenLip  = [System.Drawing.Color]::FromArgb(255, 45, 148, 66)
$Orange    = [System.Drawing.Color]::FromArgb(255, 249, 128, 40)
$OrangeLip = [System.Drawing.Color]::FromArgb(255, 198, 88, 16)
$Cyan      = [System.Drawing.Color]::FromArgb(255, 34, 190, 208)

$Family = New-Object System.Drawing.FontFamily('Segoe UI Black')

# ---- helpers -------------------------------------------------------------------------

function Get-Frame([string]$name) {
    # Loaded through a copy so the PNG on disk is not left locked by GDI+.
    $src = [System.Drawing.Image]::FromFile((Join-Path $Frames "$name.png"))
    $bmp = New-Object System.Drawing.Bitmap($src.Width, $src.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.DrawImage($src, 0, 0, $src.Width, $src.Height)
    $g.Dispose(); $src.Dispose()
    $bmp
}

function New-Canvas([int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    @{ Bitmap = $bmp; G = $g; W = $w; H = $h }
}

# Fills the destination rectangle with the image, cropping the overflow rather than
# squashing it. biasY 0 keeps the top of the frame, 1 the bottom.
function Draw-Cover($g, $img, [float]$dx, [float]$dy, [float]$dw, [float]$dh, [float]$biasY) {
    $s = [Math]::Max($dw / $img.Width, $dh / $img.Height)
    $w = $img.Width * $s
    $h = $img.Height * $s
    $x = $dx + ($dw - $w) / 2
    $y = $dy + ($dh - $h) * $biasY
    $clip = New-Object System.Drawing.RectangleF($dx, $dy, $dw, $dh)
    $g.SetClip($clip)
    $g.DrawImage($img, $x, $y, $w, $h)
    $g.ResetClip()
}

function New-RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

# A band of darkening that fades out, so white type keeps its contrast over the pale
# terminal floor without the frame reading as dimmed.
function Draw-Scrim($g, [float]$x, [float]$y0, [float]$y1, [float]$w, [int]$alpha, [bool]$fromTop) {
    $c0 = [System.Drawing.Color]::FromArgb($alpha, 20, 32, 52)
    $c1 = [System.Drawing.Color]::FromArgb(0, 20, 32, 52)
    $p0 = New-Object System.Drawing.Point(0, [int]$y0)
    $p1 = New-Object System.Drawing.Point(0, [int]$y1)
    if ($fromTop) { $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush($p0, $p1, $c0, $c1) }
    else          { $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush($p0, $p1, $c1, $c0) }
    $g.FillRectangle($br, $x, $y0, $w, ($y1 - $y0))
    $br.Dispose()
}

# Type as a filled path with a thick ink stroke around it — the sticker look the HUD is
# built on. Scaled to the box rather than set in points: the copy changes per creative and
# per language, and a fixed size would either overflow or float in the middle of the band.
function Draw-StickerText($g, [string]$text, [float]$cx, [float]$cy, [float]$maxW, [float]$maxH,
                          $fill, [float]$stroke, [bool]$shadow = $true) {
    $sf = [System.Drawing.StringFormat]::GenericTypographic
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $origin = New-Object System.Drawing.PointF(0, 0)
    $path.AddString($text, $Family, 0, 200.0, $origin, $sf)

    $b = $path.GetBounds()
    if ($b.Width -le 0 -or $b.Height -le 0) { return }
    $scale = [Math]::Min($maxW / $b.Width, $maxH / $b.Height)

    $m = New-Object System.Drawing.Drawing2D.Matrix
    $m.Scale($scale, $scale)
    $path.Transform($m)

    $b = $path.GetBounds()
    $m2 = New-Object System.Drawing.Drawing2D.Matrix
    $m2.Translate(($cx - ($b.X + $b.Width / 2)), ($cy - ($b.Y + $b.Height / 2)))
    $path.Transform($m2)

    if ($shadow) {
        $sp = $path.Clone()
        $ms = New-Object System.Drawing.Drawing2D.Matrix
        $ms.Translate(0, ($stroke * 0.55))
        $sp.Transform($ms)
        $shadowBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(70, 20, 32, 52))
        $shadowPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(70, 20, 32, 52), $stroke)
        $shadowPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $g.DrawPath($shadowPen, $sp)
        $g.FillPath($shadowBrush, $sp)
        $shadowPen.Dispose(); $shadowBrush.Dispose(); $sp.Dispose()
    }

    $pen = New-Object System.Drawing.Pen($Ink, $stroke)
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $brush = New-Object System.Drawing.SolidBrush($fill)
    $g.DrawPath($pen, $path)
    $g.FillPath($brush, $path)
    $pen.Dispose(); $brush.Dispose(); $path.Dispose()
}

# Solid shape, one ink outline all the way round, a darker lip below for thickness —
# never translucent. Returns nothing; the caption is drawn by the caller.
function Draw-Sticker($g, [float]$x, [float]$y, [float]$w, [float]$h, [float]$r,
                      $fill, $lip, [float]$stroke) {
    if ($lip -ne $null) {
        $lipPath = New-RoundRect $x ($y + $stroke) $w $h $r
        $lipBrush = New-Object System.Drawing.SolidBrush($lip)
        $lipPen = New-Object System.Drawing.Pen($Ink, $stroke)
        $lipPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $g.DrawPath($lipPen, $lipPath)
        $g.FillPath($lipBrush, $lipPath)
        $lipBrush.Dispose(); $lipPen.Dispose(); $lipPath.Dispose()
    }
    $path = New-RoundRect $x $y $w $h $r
    $brush = New-Object System.Drawing.SolidBrush($fill)
    $pen = New-Object System.Drawing.Pen($Ink, $stroke)
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawPath($pen, $path)
    $g.FillPath($brush, $path)
    $brush.Dispose(); $pen.Dispose(); $path.Dispose()
}

# A fat arrow, drawn pointing right and then turned to the piece's own facing. Same
# vocabulary as the arrow the game already draws on every suitcase, one size up.
function Draw-Arrow($g, [float]$x, [float]$y, [float]$len, [float]$thick, [float]$deg, $fill, [float]$stroke) {
    $h = $thick * 1.9          # half-height of the head
    $hl = $thick * 1.8         # head length
    $t = $thick / 2
    $pts = @(
        (New-Object System.Drawing.PointF(0, -$t)),
        (New-Object System.Drawing.PointF(($len - $hl), -$t)),
        (New-Object System.Drawing.PointF(($len - $hl), -$h)),
        (New-Object System.Drawing.PointF($len, 0)),
        (New-Object System.Drawing.PointF(($len - $hl), $h)),
        (New-Object System.Drawing.PointF(($len - $hl), $t)),
        (New-Object System.Drawing.PointF(0, $t))
    )
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddPolygon([System.Drawing.PointF[]]$pts)
    $path.CloseFigure()

    $m = New-Object System.Drawing.Drawing2D.Matrix
    $m.Translate($x, $y)
    $m.Rotate($deg)
    $path.Transform($m)

    $pen = New-Object System.Drawing.Pen($Ink, $stroke)
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $brush = New-Object System.Drawing.SolidBrush($fill)
    $g.DrawPath($pen, $path)
    $g.FillPath($brush, $path)
    $pen.Dispose(); $brush.Dispose(); $path.Dispose()
}

# Tap ripples plus a cartoon hand: the one thing every ad in this genre shows is the move
# being made, and a ring on its own reads as a highlight rather than a finger.
#
# Palm and finger are stroked separately rather than merged into one path — merged, the
# seam between them disappears under the shared outline and the whole thing reads as a
# white blob. The fingertip sits on the tap point and the hand hangs down and to the
# right of it, so the suitcase being tapped is still visible.
function Draw-Tap($g, [float]$x, [float]$y, [float]$r, [float]$stroke) {
    foreach ($pair in @(@(0.95, 230), @(1.35, 150), @(1.75, 80))) {
        $rr = $r * $pair[0]
        $pen = New-Object System.Drawing.Pen(
            [System.Drawing.Color]::FromArgb([int]$pair[1], 255, 255, 255), ($stroke * 1.1))
        $g.DrawEllipse($pen, ($x - $rr), ($y - $rr), ($rr * 2), ($rr * 2))
        $pen.Dispose()
    }

    $m = New-Object System.Drawing.Drawing2D.Matrix
    $m.Translate($x, $y)
    $m.Rotate(16)

    $palm = New-RoundRect (-$r * 0.62) ($r * 0.62) ($r * 1.30) ($r * 1.35) ($r * 0.44)
    $finger = New-RoundRect (-$r * 0.24) 0 ($r * 0.50) ($r * 1.20) ($r * 0.25)
    $palm.Transform($m)
    $finger.Transform($m)

    $pen = New-Object System.Drawing.Pen($Ink, $stroke)
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $brush = New-Object System.Drawing.SolidBrush($White)
    foreach ($p in @($palm, $finger)) {
        $g.DrawPath($pen, $p)
        $g.FillPath($brush, $p)
    }
    $pen.Dispose(); $brush.Dispose(); $palm.Dispose(); $finger.Dispose()
}

# Three arrowheads walking away from the piece in its facing direction. A single long
# arrow was tried first and it lay across the suitcases in the row below — the board is
# seen at a tilt, so a route that is clear on the grid is not a clear line on screen.
function Draw-Chevrons($g, [float]$x, [float]$y, [float]$deg, [float]$size,
                       [float]$gap, [float]$stroke) {
    $rad = $deg * [Math]::PI / 180
    for ($i = 0; $i -lt 2; $i++) {
        $d = $gap * ($i + 1)
        Draw-Arrow $g ($x + [Math]::Cos($rad) * $d) ($y + [Math]::Sin($rad) * $d) `
                   ($size * 1.15) $size $deg $Yellow $stroke
    }
}

function Save-Canvas($canvas, [string]$name) {
    $file = Join-Path $Out "$name.png"
    $canvas.Bitmap.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $canvas.G.Dispose(); $canvas.Bitmap.Dispose()
    Write-Output ("{0}  {1}x{2}" -f $name, $canvas.W, $canvas.H)
}

# ---- creative A: the hook is the jam itself ------------------------------------------
function Build-A([string]$bg, [int]$w, [int]$h, [string]$name, [string]$k1, [string]$k2,
                 [string]$ksub, [string]$kcta) {
    $c = New-Canvas $w $h
    $g = $c.G
    $img = Get-Frame $bg
    Draw-Cover $g $img 0 0 $w $h 0.5
    $img.Dispose()

    $unit = $h / 1920.0
    Draw-Scrim $g 0 0 ($h * 0.30) $w 150 $true
    Draw-Scrim $g 0 ($h * 0.70) $h $w 165 $false

    # A square crop has a third of the vertical room a 9:16 one has; the sub line is what
    # goes, rather than everything being squeezed until the headline stops carrying.
    $tall = ($h / $w) -ge 1.4
    if ($tall) { $y1 = 0.085; $y2 = 0.175 } else { $y1 = 0.075; $y2 = 0.185 }
    Draw-StickerText $g $T[$k1] ($w * 0.5) ($h * $y1) ($w * 0.86) ($h * 0.085) $White  (26 * $unit)
    Draw-StickerText $g $T[$k2] ($w * 0.5) ($h * $y2) ($w * 0.86) ($h * 0.095) $Yellow (26 * $unit)
    if ($tall) {
        Draw-StickerText $g $T[$ksub] ($w * 0.5) ($h * 0.245) ($w * 0.72) ($h * 0.030) $White (12 * $unit)
    }

    $bw = $w * 0.62
    $bh = $h * 0.078
    Draw-Sticker $g (($w - $bw) / 2) ($h * 0.855) $bw $bh ($bh * 0.42) $Green $GreenLip (9 * $unit)
    Draw-StickerText $g $T[$kcta] ($w * 0.5) ($h * 0.855 + $bh * 0.5) ($bw * 0.66) ($bh * 0.40) $White (11 * $unit)

    Save-Canvas $c $name
}

# ---- creative B: the same board before and after -------------------------------------
function Build-B([int]$w, [int]$h, [string]$name) {
    $c = New-Canvas $w $h
    $g = $c.G
    $unit = $h / 1920.0
    $split = $h * 0.50

    $top = Get-Frame 'L11_full'
    Draw-Cover $g $top 0 0 $w $split 0.42
    $top.Dispose()

    $bottom = Get-Frame 'L11_left08'
    Draw-Cover $g $bottom 0 $split $w ($h - $split) 0.46
    $bottom.Dispose()

    # The seam reads as an accident without something on it.
    $seam = New-Object System.Drawing.Pen($Ink, (10 * $unit))
    $g.DrawLine($seam, 0, $split, $w, $split)
    $seam.Dispose()

    Draw-Scrim $g 0 0 ($h * 0.16) $w 140 $true
    Draw-Scrim $g 0 ($h * 0.80) $h $w 170 $false

    Draw-StickerText $g $T['B.head'] ($w * 0.5) ($h * 0.075) ($w * 0.86) ($h * 0.062) $White (24 * $unit)

    $pw = $w * 0.34
    $ph = $h * 0.052
    Draw-Sticker $g ($w * 0.055) ($split - $ph - $h * 0.025) $pw $ph ($ph * 0.45) $Orange $OrangeLip (8 * $unit)
    Draw-StickerText $g $T['B.before'] ($w * 0.055 + $pw / 2) ($split - $ph / 2 - $h * 0.025) ($pw * 0.62) ($ph * 0.42) $White (9 * $unit)

    Draw-Sticker $g ($w * 0.055) ($h * 0.735) $pw $ph ($ph * 0.45) $Green $GreenLip (8 * $unit)
    Draw-StickerText $g $T['B.after'] ($w * 0.055 + $pw / 2) ($h * 0.735 + $ph / 2) ($pw * 0.72) ($ph * 0.42) $White (9 * $unit)

    $bw = $w * 0.62
    $bh = $h * 0.078
    Draw-Sticker $g (($w - $bw) / 2) ($h * 0.865) $bw $bh ($bh * 0.42) $Green $GreenLip (9 * $unit)
    Draw-StickerText $g $T['B.cta'] ($w * 0.5) ($h * 0.865 + $bh * 0.5) ($bw * 0.66) ($bh * 0.40) $White (11 * $unit)

    Save-Canvas $c $name
}

# ---- creative C: a straight challenge, with the level called out ---------------------
function Build-C([string]$bg, [int]$w, [int]$h, [string]$name, [string]$khead,
                 [string]$ksub, [string]$kcta, [string]$kbadge) {
    $c = New-Canvas $w $h
    $g = $c.G
    $img = Get-Frame $bg
    Draw-Cover $g $img 0 0 $w $h 0.5
    $img.Dispose()

    $unit = $h / 1920.0
    Draw-Scrim $g 0 0 ($h * 0.32) $w 160 $true
    Draw-Scrim $g 0 ($h * 0.72) $h $w 170 $false

    $pw = $w * 0.34
    $ph = $h * 0.045
    Draw-Sticker $g (($w - $pw) / 2) ($h * 0.045) $pw $ph ($ph * 0.5) $Cyan $null (8 * $unit)
    Draw-StickerText $g $T[$kbadge] ($w * 0.5) ($h * 0.045 + $ph / 2) ($pw * 0.74) ($ph * 0.40) $White (8 * $unit) $false

    Draw-StickerText $g $T[$khead] ($w * 0.5) ($h * 0.155) ($w * 0.88) ($h * 0.075) $White  (26 * $unit)
    Draw-StickerText $g $T[$ksub]  ($w * 0.5) ($h * 0.225) ($w * 0.70) ($h * 0.030) $Yellow (12 * $unit)

    $bw = $w * 0.66
    $bh = $h * 0.080
    Draw-Sticker $g (($w - $bw) / 2) ($h * 0.850) $bw $bh ($bh * 0.42) $Orange $OrangeLip (9 * $unit)
    Draw-StickerText $g $T[$kcta] ($w * 0.5) ($h * 0.850 + $bh * 0.5) ($bw * 0.70) ($bh * 0.40) $White (11 * $unit)

    Save-Canvas $c $name
}

# ---- creative D: show the move ------------------------------------------------------
# The piece position comes from the map the renderer wrote for this exact frame, not from
# reading pixels off it: the same board projects differently at every aspect.
function Build-D([string]$bg, [int]$w, [int]$h, [string]$name, [int]$pieceX, [int]$pieceY,
                 [float]$deg, [float]$arrowLen, [string]$k1, [string]$k2, [string]$ksub, [string]$kcta) {
    $c = New-Canvas $w $h
    $g = $c.G
    $img = Get-Frame $bg
    Draw-Cover $g $img 0 0 $w $h 0.5
    $img.Dispose()

    $unit = $h / 1920.0
    Draw-Scrim $g 0 0 ($h * 0.28) $w 155 $true
    Draw-Scrim $g 0 ($h * 0.74) $h $w 170 $false

    $r = 70 * $unit
    Draw-Chevrons $g ($pieceX + [Math]::Cos($deg * [Math]::PI / 180) * $r * 1.5) `
                     ($pieceY + [Math]::Sin($deg * [Math]::PI / 180) * $r * 1.5) `
                     $deg (26 * $unit) $arrowLen (9 * $unit)
    Draw-Tap $g $pieceX $pieceY $r (10 * $unit)

    Draw-StickerText $g $T[$k1]   ($w * 0.5) ($h * 0.075) ($w * 0.80) ($h * 0.060) $White  (24 * $unit)
    Draw-StickerText $g $T[$k2]   ($w * 0.5) ($h * 0.150) ($w * 0.88) ($h * 0.070) $Yellow (24 * $unit)
    Draw-StickerText $g $T[$ksub] ($w * 0.5) ($h * 0.215) ($w * 0.72) ($h * 0.028) $White  (12 * $unit)

    $bw = $w * 0.62
    $bh = $h * 0.078
    Draw-Sticker $g (($w - $bw) / 2) ($h * 0.860) $bw $bh ($bh * 0.42) $Green $GreenLip (9 * $unit)
    Draw-StickerText $g $T[$kcta] ($w * 0.5) ($h * 0.860 + $bh * 0.5) ($bw * 0.66) ($bh * 0.40) $White (11 * $unit)

    Save-Canvas $c $name
}

# ---- build ---------------------------------------------------------------------------
Build-A 'L11_full'    1080 1920 'RU_A_jam_1080x1920'  'A.head' 'A.head2' 'A.sub' 'A.cta'
Build-A 'L11_full_45' 1080 1350 'RU_A_jam_1080x1350'  'A.head' 'A.head2' 'A.sub' 'A.cta'
Build-A 'L11_full_11' 1080 1080 'RU_A_jam_1080x1080'  'A.head' 'A.head2' 'A.sub' 'A.cta'
Build-A 'L11_full'    1080 1920 'EN_A_jam_1080x1920'  'EN.A.head' 'EN.A.head2' 'EN.A.sub' 'EN.A.cta'

Build-B 1080 1920 'RU_B_beforeafter_1080x1920'

Build-C 'L11_full'    1080 1920 'RU_C_challenge_1080x1920' 'C.head' 'C.sub' 'C.cta' 'C.badge'
Build-C 'L11_full_45' 1080 1350 'RU_C_challenge_1080x1350' 'C.head' 'C.sub' 'C.cta' 'C.badge'
Build-C 'L11_full'    1080 1920 'EN_C_challenge_1080x1920' 'EN.C.head' 'EN.C.sub' 'EN.C.cta' 'EN.C.badge'

# Piece at 421,1207 in L11_mid faces Left with a clear path, per L11_mid.pieces.txt.
Build-D 'L11_mid' 1080 1920 'RU_D_tap_1080x1920' 421 1207 180 62 'D.head' 'D.head2' 'D.sub' 'D.cta'
Build-D 'L11_mid' 1080 1920 'EN_D_tap_1080x1920' 421 1207 180 62 'EN.D.head' 'EN.D.head2' 'EN.D.sub' 'EN.D.cta'
