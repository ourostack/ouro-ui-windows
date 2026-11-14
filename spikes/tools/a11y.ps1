# Sets the Windows settings the Button claims depend on: client-area animations (Settings > Accessibility > Visual
# effects > Animation effects) and the text size (Settings > Accessibility > Text size), for the current user.
param([ValidateSet("on", "off")][string]$Animations = "on", [ValidateRange(100, 225)][int]$TextScale = 100)
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class OuroSpi {
  [DllImport("user32.dll", SetLastError = true)] public static extern bool SystemParametersInfo(uint action, uint uiParam, IntPtr pvParam, uint winIni);
  [DllImport("user32.dll", SetLastError = true)] public static extern bool SystemParametersInfo(uint action, uint uiParam, ref bool pvParam, uint winIni);
}
"@
$SPI_SETCLIENTAREAANIMATION = 0x1043; $SPI_GETCLIENTAREAANIMATION = 0x1042
$on = if ($Animations -eq "on") { 1 } else { 0 }
[void][OuroSpi]::SystemParametersInfo($SPI_SETCLIENTAREAANIMATION, 0, [IntPtr]$on, 3)
$now = $false; [void][OuroSpi]::SystemParametersInfo($SPI_GETCLIENTAREAANIMATION, 0, [ref]$now, 0)
New-Item -Path "HKCU:\Software\Microsoft\Accessibility" -Force | Out-Null
Set-ItemProperty -Path "HKCU:\Software\Microsoft\Accessibility" -Name TextScaleFactor -Value $TextScale -Type DWord
Write-Host "a11y: animations=$now textScale=$((Get-ItemProperty 'HKCU:\Software\Microsoft\Accessibility').TextScaleFactor)"
