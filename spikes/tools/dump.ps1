# Launches a build, waits for its first action button, and writes its UI Automation tree as JSON.
param([Parameter(Mandatory)][string]$Command, [string]$Arguments, [Parameter(Mandatory)][string]$ProcessName, [Parameter(Mandatory)][string]$Out, [string]$Screenshot, [int]$SettleMs = 1500)
. "$PSScriptRoot\uia.ps1"
Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
if ($Arguments) { Start-Process -FilePath $Command -ArgumentList $Arguments | Out-Null } else { Start-Process -FilePath $Command | Out-Null }
[void](Wait-OuroElement $ProcessName "action.add_shopping_list_item")
Start-Sleep -Milliseconds $SettleMs
$tree = Get-OuroTree $ProcessName
New-Item -ItemType Directory -Force -Path (Split-Path $Out) | Out-Null
$tree | ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 $Out
if ($Screenshot) { & "$PSScriptRoot\screenshot.ps1" -Out $Screenshot -ProcessName $ProcessName }
Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Host "dump: $Out ($($tree.elements.Count) elements)"
