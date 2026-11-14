# Launches the app, waits for an element, and writes its UI Automation tree as JSON. With -Screenshot it also saves the
# window as a PNG and samples, for each control that reports a fill, the pixel at its left edge's middle (4 px in), so
# the claims can check what was drawn against what the control says it drew.
param([Parameter(Mandatory)][string]$Command, [string]$Arguments, [Parameter(Mandatory)][string]$ProcessName, [Parameter(Mandatory)][string]$Out, [string]$Screenshot, [string]$ReadyId = "action.add_shopping_list_item", [int]$SettleMs = 2000)
. "$PSScriptRoot\uia.ps1"
Add-Type -AssemblyName System.Drawing
Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
[void][OuroDpi]::SetCursorPos(0, 0)
if ($Arguments) { Start-Process -FilePath $Command -ArgumentList $Arguments | Out-Null } else { Start-Process -FilePath $Command | Out-Null }
[void](Wait-OuroElement $ProcessName $ReadyId)
Start-Sleep -Milliseconds $SettleMs
$tree = Get-OuroTree $ProcessName
if ($Screenshot) {
  & "$PSScriptRoot\screenshot.ps1" -Out $Screenshot -ProcessName $ProcessName
  $bmp = New-Object System.Drawing.Bitmap $Screenshot
  foreach ($e in $tree.elements) {
    if ($e.itemStatus -notmatch "ouro:" -or $e.rect.width -le 8) { continue }
    $x = [int]($e.rect.x - $tree.windowRect.x + 4); $y = [int]($e.rect.y - $tree.windowRect.y + $e.rect.height / 2)
    if ($x -ge 0 -and $y -ge 0 -and $x -lt $bmp.Width -and $y -lt $bmp.Height) { $e["sampled"] = "#{0:X8}" -f $bmp.GetPixel($x, $y).ToArgb() }
  }
  $bmp.Dispose()
}
New-Item -ItemType Directory -Force -Path (Split-Path $Out) | Out-Null
$tree | ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 $Out
Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Host "dump: $Out ($($tree.elements.Count) elements)"
