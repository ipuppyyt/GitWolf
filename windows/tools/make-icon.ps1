# Renders assets/gitwolf-icon.svg to the Windows app icon, cropped to the tile so it fills the
# taskbar like other apps, and to the installer's pictures. Run again after the artwork changes:
#   powershell -ExecutionPolicy Bypass -File windows/tools/make-icon.ps1
# Reads only what that file uses: one rounded rect, then paths and circles in one translate/scale group.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

$root = Split-Path (Split-Path $PSScriptRoot)
[xml]$svg = Get-Content -Raw -LiteralPath "$root\assets\gitwolf-icon.svg"
$out = "$root\windows\src\GitWolf.App\Assets\app.ico"
$culture = [Globalization.CultureInfo]::InvariantCulture

function Read-Number($text) { [double]::Parse($text, $culture) }
function Get-Brush($colour) {
    New-Object Windows.Media.SolidColorBrush ([Windows.Media.ColorConverter]::ConvertFromString($colour))
}

$rect = $svg.svg.rect
$group = $svg.svg.g
if ($group.transform -notmatch '^translate\(([\d.]+) ([\d.]+)\) scale\(([\d.]+)\)$') { throw 'Unexpected transform' }
$move = New-Object Windows.Media.TranslateTransform (Read-Number $Matches[1]), (Read-Number $Matches[2])
$grow = New-Object Windows.Media.ScaleTransform (Read-Number $Matches[3]), (Read-Number $Matches[3])
$left = Read-Number $rect.x
$width = Read-Number $rect.width

# The tile and its mark, $size across, drawn at the context's origin.
function Add-Tile($dc, [double]$size) {
    $dc.PushTransform((New-Object Windows.Media.ScaleTransform ($size / $width), ($size / $width)))
    $dc.PushTransform((New-Object Windows.Media.TranslateTransform (-$left), (-$left)))
    $radius = Read-Number $rect.rx
    $tile = New-Object Windows.Rect $left, $left, $width, $width
    $dc.DrawRoundedRectangle((Get-Brush $rect.fill), $null, $tile, $radius, $radius)
    $dc.PushTransform($move)
    $dc.PushTransform($grow)
    foreach ($node in $group.ChildNodes) {
        if ($node.LocalName -eq 'path') {
            # F0 is even-odd and F1 non-zero, which is the SVG default.
            $rule = if ($node.GetAttribute('fill-rule') -eq 'evenodd') { 'F0 ' } else { 'F1 ' }
            $geometry = [Windows.Media.Geometry]::Parse($rule + $node.d)
            $fill = if ($node.fill) { Get-Brush $node.fill }
            $pen = if ($node.stroke) { New-Object Windows.Media.Pen (Get-Brush $node.stroke), 1 }
            $dc.DrawGeometry($fill, $pen, $geometry)
        }
        elseif ($node.LocalName -eq 'circle') {
            $centre = New-Object Windows.Point (Read-Number $node.cx), (Read-Number $node.cy)
            $r = Read-Number $node.r
            $dc.DrawEllipse((Get-Brush $node.fill), $null, $centre, $r, $r)
        }
        else { throw "Unexpected element: $($node.LocalName)" }
    }
    # The tile's two transforms and the mark's two.
    foreach ($i in 1..4) { $dc.Pop() }
}

function New-Point([double]$x, [double]$y) { New-Object Windows.Point $x, $y }

# One centred line of white-or-tinted text in the system font.
function Add-Line($dc, [string]$text, [double]$size, $weight, $colour, [double]$middle, [double]$top) {
    $face = New-Object Windows.Media.Typeface (New-Object Windows.Media.FontFamily 'Segoe UI'),
        ([Windows.FontStyles]::Normal), $weight, ([Windows.FontStretches]::Normal)
    $line = New-Object Windows.Media.FormattedText $text, $culture, ([Windows.FlowDirection]::LeftToRight),
        $face, $size, (Get-Brush $colour), 1.0
    $dc.DrawText($line, (New-Point ($middle - $line.Width / 2) $top))
}

# The installer's side picture, drawn 164 by 314: the mark, the name, and a small commit graph. All flat.
function Add-Welcome($dc) {
    $dc.PushTransform((New-Object Windows.Media.TranslateTransform 18, 22))
    Add-Tile $dc 128
    $dc.Pop()
    Add-Line $dc 'GitWolf' 25 ([Windows.FontWeights]::SemiBold) '#FFFFFF' 82 150
    $accent = New-Object Windows.Rect 70, 191, 24, 2
    $dc.DrawRoundedRectangle((Get-Brush '#6F93FF'), $null, $accent, 1, 1)
    Add-Line $dc 'Your repositories,' 10.5 ([Windows.FontWeights]::Normal) '#C3D0FA' 82 201
    Add-Line $dc 'always in sync.' 10.5 ([Windows.FontWeights]::Normal) '#C3D0FA' 82 216

    $pen = New-Object Windows.Media.Pen (Get-Brush '#3D62CC'), 2
    $dc.DrawLine($pen, (New-Point 0 284), (New-Point 164 284))
    $branch = [Windows.Media.Geometry]::Parse('M 34,284 C 50,284 46,260 62,260 L 102,260 C 118,260 114,284 130,284')
    $dc.DrawGeometry($null, $pen, $branch)
    foreach ($node in @(@(18, 284), @(34, 284), @(66, 260), @(98, 260), @(80, 284), @(130, 284))) {
        $dc.DrawEllipse((Get-Brush $rect.fill), (New-Object Windows.Media.Pen (Get-Brush '#7E9CF0'), 2),
            (New-Point $node[0] $node[1]), 3.5, 3.5)
    }
    $dc.DrawEllipse((Get-Brush '#FFFFFF'), $null, (New-Point 148 284), 4.5, 4.5)
}

# A picture on a flat background. Without its own drawing, the tile spans the width, centred from top to
# bottom.
function Get-Picture([int]$wide, [int]$high, $background, $encoder, [scriptblock]$draw) {
    $visual = New-Object Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    if ($background) {
        $dc.DrawRectangle((Get-Brush $background), $null, (New-Object Windows.Rect 0, 0, $wide, $high))
    }
    if ($draw) { & $draw $dc }
    else {
        $dc.PushTransform((New-Object Windows.Media.TranslateTransform 0, (($high - $wide) / 2)))
        Add-Tile $dc $wide
    }
    $dc.Close()
    $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap $wide, $high, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    # Inno Setup reads .bmp files without transparency.
    if ($encoder -is [Windows.Media.Imaging.BmpBitmapEncoder]) {
        $bitmap = New-Object Windows.Media.Imaging.FormatConvertedBitmap $bitmap, ([Windows.Media.PixelFormats]::Bgr24), $null, 0
    }
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object IO.MemoryStream
    $encoder.Save($stream)
    , $stream.ToArray()
}

# An .ico is a directory of images; each entry here is a PNG.
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($size in $sizes) {
    , (Get-Picture $size $size $null (New-Object Windows.Media.Imaging.PngBitmapEncoder))
}
$file = New-Object IO.MemoryStream
$writer = New-Object IO.BinaryWriter $file
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $side = [byte]($sizes[$i] % 256) # 0 stands for 256
    $writer.Write($side); $writer.Write($side); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write([byte[]]$image) }
$writer.Flush()
[IO.File]::WriteAllBytes($out, $file.ToArray())
"Wrote $out ($($file.Length) bytes)"

# The installer's side picture and corner picture, at the common display scalings.
foreach ($scale in 1, 1.5, 2) {
    $welcome = {
        param($dc)
        $dc.PushTransform((New-Object Windows.Media.ScaleTransform $scale, $scale))
        Add-Welcome $dc
    }
    $pictures = @(
        @{ Name = 'wizard'; Wide = 164; High = 314; Background = $rect.fill; Draw = $welcome },
        @{ Name = 'wizard-small'; Wide = 55; High = 55; Background = '#FFFFFF' })
    foreach ($picture in $pictures) {
        $path = "$root\windows\installer\$($picture.Name)-$($scale * 100).bmp"
        $bytes = Get-Picture ($picture.Wide * $scale) ($picture.High * $scale) $picture.Background `
            (New-Object Windows.Media.Imaging.BmpBitmapEncoder) $picture.Draw
        [IO.File]::WriteAllBytes($path, $bytes)
        "Wrote $path ($($bytes.Length) bytes)"
    }
}
