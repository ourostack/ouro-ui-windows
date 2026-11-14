# UI Automation helpers for the spike's CI: find a process's window, read its tree, invoke a button.
# Dot-source it from Windows PowerShell 5.1 (`shell: powershell`), which carries UIAutomationClient.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class OuroDpi { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); [DllImport("user32.dll")] public static extern int GetDpiForSystem(); }
"@
[void][OuroDpi]::SetProcessDPIAware()
$script:Uia = [System.Windows.Automation.AutomationElement]

function Get-OuroWindow([string]$ProcessName) {
  foreach ($p in @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($script:Uia::ProcessIdProperty, $p.Id)
    $w = $script:Uia::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
    if ($w) { return $w }
  }
  return $null
}

function Find-ByAutomationId($Root, [string]$Id) {
  $cond = New-Object System.Windows.Automation.PropertyCondition($script:Uia::AutomationIdProperty, $Id)
  return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Wait-OuroElement([string]$ProcessName, [string]$Id, [int]$TimeoutMs = 60000) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
    $w = Get-OuroWindow $ProcessName
    if ($w) { $e = Find-ByAutomationId $w $Id; if ($e) { return $e } }
    Start-Sleep -Milliseconds 10
  }
  throw "No element '$Id' in a '$ProcessName' window after $TimeoutMs ms"
}

function Get-OuroTree([string]$ProcessName) {
  $w = Get-OuroWindow $ProcessName
  if (-not $w) { throw "No '$ProcessName' window" }
  $all = $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
  $items = foreach ($e in $all) {
    $c = $e.Current
    $r = $c.BoundingRectangle
    [ordered]@{
      automationId = $c.AutomationId; name = $c.Name; controlType = $c.ControlType.ProgrammaticName; className = $c.ClassName
      itemStatus = $c.ItemStatus; helpText = $c.HelpText; isKeyboardFocusable = $c.IsKeyboardFocusable
      rect = [ordered]@{ x = $r.X; y = $r.Y; width = $r.Width; height = $r.Height }
    }
  }
  return [ordered]@{ process = $ProcessName; window = $w.Current.Name; systemDpi = [OuroDpi]::GetDpiForSystem(); elements = @($items) }
}

function Invoke-OuroButton($Element) {
  $pattern = $Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
  $pattern.Invoke()
}
