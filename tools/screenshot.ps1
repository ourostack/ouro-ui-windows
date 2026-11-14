# Saves a PNG of the screen, or of one process's window when -ProcessName is given.
param([Parameter(Mandatory)][string]$Out, [string]$ProcessName)
. "$PSScriptRoot\uia.ps1"
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
$bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$x = $bounds.X; $y = $bounds.Y; $w = $bounds.Width; $h = $bounds.Height
if ($ProcessName) {
  $win = Get-OuroWindow $ProcessName
  if ($win) { $r = $win.Current.BoundingRectangle; $x = [int]$r.X; $y = [int]$r.Y; $w = [int]$r.Width; $h = [int]$r.Height }
}
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($x, $y, 0, 0, $bmp.Size)
New-Item -ItemType Directory -Force -Path (Split-Path $Out) | Out-Null
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Host "screenshot: $Out ($w x $h)"
