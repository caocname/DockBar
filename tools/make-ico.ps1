# Build a multi-size .ico from logo.png. PNG is embedded raw inside the ICO,
# which Windows handles fine on Vista+ and gives a sharp 256x256 icon without
# the BMP+AND-mask faff.
param(
  [string]$Src = "$PSScriptRoot/../logo.png",
  [string]$Dst = "$PSScriptRoot/../src/DockBar/app.ico"
)

Add-Type -AssemblyName System.Drawing

$Sizes = 256,128,64,48,32,24,16
$srcImg = [System.Drawing.Image]::FromFile((Resolve-Path $Src))

# Render each size into a PNG byte[]
$pngBytes = @()
foreach ($s in $Sizes) {
  $bmp = New-Object System.Drawing.Bitmap $s,$s
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.InterpolationMode  = 'HighQualityBicubic'
  $g.SmoothingMode      = 'HighQuality'
  $g.PixelOffsetMode    = 'HighQuality'
  $g.CompositingQuality = 'HighQuality'
  $g.DrawImage($srcImg, 0, 0, $s, $s)
  $g.Dispose()

  $pms = New-Object System.IO.MemoryStream
  $bmp.Save($pms, [System.Drawing.Imaging.ImageFormat]::Png)
  $pngBytes += ,($pms.ToArray())
  $bmp.Dispose()
  $pms.Dispose()
}

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ms

# ICONDIR (6 bytes): reserved=0, type=1 (icon), count
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$Sizes.Count)

# Header table (16 bytes per entry)
$offset = 6 + 16 * $Sizes.Count
for ($i = 0; $i -lt $Sizes.Count; $i++) {
  $s = $Sizes[$i]
  # 256 must be encoded as 0 in width/height bytes
  $w = if ($s -ge 256) { 0 } else { $s }
  $h = $w
  $bw.Write([byte]$w)            # width
  $bw.Write([byte]$h)            # height
  $bw.Write([byte]0)              # color count (0 = >=256 colors)
  $bw.Write([byte]0)              # reserved
  $bw.Write([uint16]1)            # planes
  $bw.Write([uint16]32)           # bits/pixel
  $bw.Write([uint32]$pngBytes[$i].Length)  # bytes in resource
  $bw.Write([uint32]$offset)      # offset
  $offset += $pngBytes[$i].Length
}

foreach ($b in $pngBytes) { $bw.Write($b) }

[System.IO.File]::WriteAllBytes($Dst, $ms.ToArray())
$srcImg.Dispose()
$ms.Dispose()
"$Dst : $((Get-Item $Dst).Length) bytes"
