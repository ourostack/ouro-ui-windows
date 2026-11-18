# Drives Ouro Shop's composed page as a person would, through UI Automation: types into the card's fields, ticks its box
# and picks an option, then presses a button that runs, one that confirms (Cancel first, then the confirm) and one that
# hands off. After each step it records the status line, the data model the page shows, the audit's length (one line per
# call the runtime saw) and, for the confirm, the dialog. tools/composed.mts judges the record.
param([Parameter(Mandatory)][string]$Command, [Parameter(Mandatory)][string]$ProcessName, [Parameter(Mandatory)][string]$Out, [string]$Screenshot)
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\uia.ps1"
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
$audit = Join-Path $env:LOCALAPPDATA "OuroShop\audit.jsonl"
$steps = New-Object System.Collections.ArrayList
$dialog = $null

function Get-AuditLines { if (Test-Path $audit) { @(Get-Content $audit).Count } else { 0 } }
function Get-Composed([string]$Id) {
  $w = Get-OuroWindow $ProcessName
  $e = if ($w) { Find-ByAutomationId $w $Id } else { $null }
  if (-not $e) { throw "No '$Id' on Ouro Shop's composed page. $(Get-OuroWhy $ProcessName)" }
  return $e
}
function Wait-Element([string]$Id, [int]$TimeoutMs = 15000) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
    $w = Get-OuroWindow $ProcessName
    if ($w) { $e = Find-ByAutomationId $w $Id; if ($e) { return $e } }
    Start-Sleep -Milliseconds 100
  }
  throw "No '$Id' after $TimeoutMs ms. $(Get-OuroWhy $ProcessName)"
}
function Get-Status { (Get-Composed "status").Current.Name }
function Wait-Status([string]$Before, [int]$TimeoutMs = 15000) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.ElapsedMilliseconds -lt $TimeoutMs) { $s = Get-Status; if ($s -ne $Before) { return $s }; Start-Sleep -Milliseconds 100 }
  return (Get-Status)
}
function Add-Step([string]$Name, [hashtable]$Extra = @{}) {
  $step = [ordered]@{ step = $Name; status = (Get-Status); data = (Get-Composed "composed.data").Current.Name; auditLines = (Get-AuditLines) }
  foreach ($k in $Extra.Keys) { $step[$k] = $Extra[$k] }
  [void]$steps.Add($step)
  Write-Host "composed: $Name -> $($step.status)"
}
function Set-Field([string]$Id, [string]$Text) { (Get-Composed $Id).GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Text) }
function Press([string]$Id) { Invoke-OuroButton (Get-Composed $Id) }
function Read-Dialog {
  $primary = Wait-Element "PrimaryButton"
  Start-Sleep -Milliseconds 600
  $w = Get-OuroWindow $ProcessName
  $rows = @(0..9 | ForEach-Object { Find-ByAutomationId $w "confirm.row.$_" } | Where-Object { $_ } | ForEach-Object { $_.Current.Name })
  $texts = @($w.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($script:Uia::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))) | ForEach-Object { $_.Current.Name })
  $close = Find-ByAutomationId $w "CloseButton"
  $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
  return [ordered]@{ primary = $primary.Current.Name; close = if ($close) { $close.Current.Name } else { $null }; focused = $focused.Current.Name; rows = $rows; texts = $texts }
}

Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
Start-Process -FilePath $Command -ArgumentList "--page composed" | Out-Null
[void](Wait-OuroElement $ProcessName "composed.add_item")
Start-Sleep -Milliseconds 1500
$auditAtStart = Get-AuditLines
Add-Step "start"

# The person answers: a field, a number, a box and an option. Nothing runs; the data model changes.
Set-Field "composed.item" "eggs"
Set-Field "composed.quantity" "3"
(Get-Composed "composed.organic").GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle()
$store = Get-Composed "composed.store"
$store.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
Start-Sleep -Milliseconds 800
$w = Get-OuroWindow $ProcessName
$option = $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.AndCondition((New-Object System.Windows.Automation.PropertyCondition($script:Uia::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)), (New-Object System.Windows.Automation.PropertyCondition($script:Uia::NameProperty, "Market Hall")))))
if (-not $option) { throw "The Store list didn't open with Market Hall in it" }
$option.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 500
$expand = $store.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
if ($expand.Current.ExpandCollapseState -ne [System.Windows.Automation.ExpandCollapseState]::Collapsed) { $expand.Collapse() }
Start-Sleep -Milliseconds 500
Add-Step "answered"

# A write action runs at once, with what the person typed.
$before = Get-Status
Press "composed.add_item"
[void](Wait-Status $before)
Add-Step "run"

# A consequential action shows what it will send and runs only on the person's yes: Cancel sends nothing.
Set-Field "composed.share_with" "pat@example.com"
Start-Sleep -Milliseconds 300
$before = Get-Status
Press "composed.share"
$dialog = Read-Dialog
if ($Screenshot) {
  $r = (Get-OuroWindow $ProcessName).Current.BoundingRectangle
  $bmp = New-Object System.Drawing.Bitmap ([int]$r.Width), ([int]$r.Height)
  $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
  $bmp.Save($Screenshot, [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
}
Invoke-OuroButton (Get-Composed "CloseButton")
Start-Sleep -Milliseconds 1500
Add-Step "cancelled"
Press "composed.share"
[void](Read-Dialog)
Invoke-OuroButton (Get-Composed "PrimaryButton")
[void](Wait-Status $before)
Add-Step "confirmed"

# A person-only action hands the person to the app's own screen, and never presses.
$before = Get-Status
Press "composed.order"
[void](Wait-Status $before)
Start-Sleep -Milliseconds 500
Add-Step "handoff"

[ordered]@{ auditAtStart = $auditAtStart; dialog = $dialog; steps = @($steps) } | ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 $Out
Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Host "composed: $Out"
