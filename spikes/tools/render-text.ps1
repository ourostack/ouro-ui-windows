# Renders a text file to a PNG, for evidence that has no window of its own (what Windows registered for the package).
param([Parameter(Mandatory)][string]$In, [Parameter(Mandatory)][string]$Out, [string]$Title = "")
Add-Type -AssemblyName System.Drawing
$lines = @($Title, "") + (Get-Content $In)
$font = New-Object System.Drawing.Font "Consolas", 11
$w = 1100; $h = [math]::Min(4000, 24 + 18 * $lines.Count)
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::White)
$y = 12
foreach ($l in $lines) { $g.DrawString($l, $font, [System.Drawing.Brushes]::Black, 12, $y); $y += 18 }
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
