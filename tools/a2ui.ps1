# Drives Ouro Shop's A2UI page (the composed card, arriving as A2UI messages) as a person would, through UI Automation:
# types an item, then presses a button that runs, one that confirms (and confirms it) and one that hands off. After each
# step it records the status line, which says what A2UI action message the press sent, the data model the page shows and
# the audit's length. tools/composed.mts judges the record beside the composed page's.
param([Parameter(Mandatory)][string]$Command, [Parameter(Mandatory)][string]$ProcessName, [Parameter(Mandatory)][string]$Out)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\uia.ps1"
$audit = Join-Path $env:LOCALAPPDATA "OuroShop\audit.jsonl"
$steps = New-Object System.Collections.ArrayList

function Get-AuditLines { if (Test-Path $audit) { @(Get-Content $audit).Count } else { 0 } }
function Find-Id([string]$Id, [int]$TimeoutMs = 15000) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
    $w = Get-OuroWindow $ProcessName
    if ($w) { $e = Find-ByAutomationId $w $Id; if ($e) { return $e } }
    Start-Sleep -Milliseconds 100
  }
  throw "No '$Id' on Ouro Shop's A2UI page after $TimeoutMs ms. $(Get-OuroWhy $ProcessName)"
}
function Get-Status { (Find-Id "status").Current.Name }
function Wait-Status([string]$Before) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.ElapsedMilliseconds -lt 15000) { $s = Get-Status; if ($s -ne $Before) { return $s }; Start-Sleep -Milliseconds 100 }
  return (Get-Status)
}
function Add-Step([string]$Name) {
  $step = [ordered]@{ step = $Name; status = (Get-Status); data = (Find-Id "composed.data").Current.Name; auditLines = (Get-AuditLines) }
  [void]$steps.Add($step)
  Write-Host "a2ui: $Name -> $($step.status)"
}

Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
Start-Process -FilePath $Command -ArgumentList "--page a2ui" | Out-Null
[void](Wait-OuroElement $ProcessName "composed.add_item")
Start-Sleep -Milliseconds 1500
Add-Step "start"

(Find-Id "composed.item").GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("eggs")
Start-Sleep -Milliseconds 300
$before = Get-Status
Invoke-OuroButton (Find-Id "composed.add_item")
[void](Wait-Status $before)
Add-Step "run"

$before = Get-Status
Invoke-OuroButton (Find-Id "composed.share")
[void](Find-Id "PrimaryButton")
Start-Sleep -Milliseconds 600
Invoke-OuroButton (Find-Id "PrimaryButton")
[void](Wait-Status $before)
Add-Step "confirmed"

$before = Get-Status
Invoke-OuroButton (Find-Id "composed.order")
[void](Wait-Status $before)
Start-Sleep -Milliseconds 500
Add-Step "handoff"

[ordered]@{ steps = @($steps) } | ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 $Out
Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Host "a2ui: $Out"
