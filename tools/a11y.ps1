# Sets the Windows settings core's claims depend on, for the current user: client-area animations (Settings,
# Accessibility, Visual effects, Animation effects), the text size (Accessibility, Text size) and a contrast theme
# (Accessibility, Contrast themes). Run it before launching the app; the app reads them at start.
param([ValidateSet("on", "off")][string]$Animations = "on", [ValidateRange(100, 225)][int]$TextScale = 100, [ValidateSet("on", "off")][string]$Contrast = "off")
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class OuroSpi {
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct HIGHCONTRAST { public uint cbSize; public uint dwFlags; public string lpszDefaultScheme; }
  [DllImport("user32.dll", SetLastError = true)] public static extern bool SystemParametersInfo(uint action, uint uiParam, IntPtr pvParam, uint winIni);
  [DllImport("user32.dll", SetLastError = true)] public static extern bool SystemParametersInfo(uint action, uint uiParam, ref bool pvParam, uint winIni);
  [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern bool SystemParametersInfo(uint action, uint uiParam, ref HIGHCONTRAST pvParam, uint winIni);
}
"@
$SPI_SETCLIENTAREAANIMATION = 0x1043; $SPI_GETCLIENTAREAANIMATION = 0x1042; $SPI_GETHIGHCONTRAST = 0x42; $SPI_SETHIGHCONTRAST = 0x43
$on = if ($Animations -eq "on") { 1 } else { 0 }
[void][OuroSpi]::SystemParametersInfo($SPI_SETCLIENTAREAANIMATION, 0, [IntPtr]$on, 3)
$now = $false; [void][OuroSpi]::SystemParametersInfo($SPI_GETCLIENTAREAANIMATION, 0, [ref]$now, 0)
New-Item -Path "HKCU:\Software\Microsoft\Accessibility" -Force | Out-Null
Set-ItemProperty -Path "HKCU:\Software\Microsoft\Accessibility" -Name TextScaleFactor -Value $TextScale -Type DWord
$hc = New-Object OuroSpi+HIGHCONTRAST
$hc.cbSize = [Runtime.InteropServices.Marshal]::SizeOf([type][OuroSpi+HIGHCONTRAST])
$hc.dwFlags = if ($Contrast -eq "on") { 1 } else { 0 }
$hc.lpszDefaultScheme = "High Contrast Black"
[void][OuroSpi]::SystemParametersInfo($SPI_SETHIGHCONTRAST, $hc.cbSize, [ref]$hc, 3)
$read = New-Object OuroSpi+HIGHCONTRAST
$read.cbSize = $hc.cbSize
[void][OuroSpi]::SystemParametersInfo($SPI_GETHIGHCONTRAST, $read.cbSize, [ref]$read, 0)
Start-Sleep -Milliseconds 1500
Write-Host "a11y: animations=$now textScale=$((Get-ItemProperty 'HKCU:\Software\Microsoft\Accessibility').TextScaleFactor) contrast=$(($read.dwFlags -band 1) -eq 1)"
