param([Parameter(Mandatory)] [string] $OutPath)

# Builds a multi-size .ico (PNG-compressed entries): a dark pill with a light outline
# and a small green dot, readable on both light and dark taskbars.
Add-Type -AssemblyName System.Drawing

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $w = [single]($s * 0.92); $h = [single]($s * 0.46)
    $x = [single](($s - $w) / 2); $y = [single](($s - $h) / 2)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($x, $y, $h, $h, 90, 180)
    $path.AddArc($x + $w - $h, $y, $h, $h, 270, 180)
    $path.CloseFigure()

    $fill = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 18, 18, 20))
    $g.FillPath($fill, $path)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(235, 255, 255, 255)), ([single][Math]::Max(1, $s / 20))
    $g.DrawPath($pen, $path)

    $d = [single]($h * 0.34)
    $dot = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 48, 209, 88))
    $g.FillEllipse($dot, [single]($x + $w - $h / 2 - $d / 2 - $h * 0.12), [single]($y + ($h - $d) / 2), $d, $d)

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    [pscustomobject]@{ Size = $s; Bytes = $ms.ToArray() }
}

$out = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $out
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($img in $images) {
    $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
    $writer.Write([byte]$dim); $writer.Write([byte]$dim)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$img.Bytes.Length); $writer.Write([uint32]$offset)
    $offset += $img.Bytes.Length
}
foreach ($img in $images) { $writer.Write($img.Bytes) }
$writer.Flush()

New-Item -ItemType Directory -Force (Split-Path $OutPath) | Out-Null
[System.IO.File]::WriteAllBytes($OutPath, $out.ToArray())
Write-Output "Wrote $OutPath ($($out.Length) bytes)"
