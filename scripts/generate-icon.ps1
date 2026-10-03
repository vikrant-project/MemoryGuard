Add-Type -AssemblyName System.Drawing

New-Item -ItemType Directory -Force -Path 'src/MemoryGuard.App/Resources' | Out-Null

$bmp = New-Object System.Drawing.Bitmap 256, 256
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)

# Background Rounded Hexagon / Shield
$rect = New-Object System.Drawing.Rectangle 16, 16, 224, 224
$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(37, 99, 235)), ([System.Drawing.Color]::FromArgb(30, 58, 138)), 45.0
$g.FillEllipse($brush, 20, 20, 216, 216)

# Outer Ring
$ringPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(96, 165, 250)), 6.0
$g.DrawEllipse($ringPen, 24, 24, 208, 208)

# Central RAM / Memory Chip
$chipBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(248, 250, 252))
$g.FillRectangle($chipBrush, 78, 68, 100, 120)

# Chip Core Details
$coreBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(30, 58, 138))
$g.FillRectangle($coreBrush, 90, 80, 76, 96)

# Microcircuit Traces / Pins
$pinPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(147, 197, 253)), 5.0

# Left Pins
$g.DrawLine($pinPen, 54, 90, 78, 90)
$g.DrawLine($pinPen, 54, 114, 78, 114)
$g.DrawLine($pinPen, 54, 138, 78, 138)
$g.DrawLine($pinPen, 54, 162, 78, 162)

# Right Pins
$g.DrawLine($pinPen, 178, 90, 202, 90)
$g.DrawLine($pinPen, 178, 114, 202, 114)
$g.DrawLine($pinPen, 178, 138, 202, 138)
$g.DrawLine($pinPen, 178, 162, 202, 162)

# Memory Trace Dots
$dotBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(52, 211, 153))
$g.FillEllipse($dotBrush, 118, 98, 20, 20)
$g.FillEllipse($dotBrush, 118, 138, 20, 20)

$g.Dispose()

$bmp.Save('src/MemoryGuard.App/Resources/app.png', [System.Drawing.Imaging.ImageFormat]::Png)

# Save as .ico
$hIcon = $bmp.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($hIcon)
$stream = [System.IO.File]::OpenWrite('src/MemoryGuard.App/Resources/app.ico')
$icon.Save($stream)
$stream.Close()
$icon.Dispose()
$bmp.Dispose()

Write-Host "Icons generated successfully at src/MemoryGuard.App/Resources/app.ico"
