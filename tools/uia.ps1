# UI Automation helpers for CI: find a process's window, read its tree, invoke a button.
# Dot-source it from Windows PowerShell 5.1 (`shell: powershell`), which carries UIAutomationClient.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class OuroDpi { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); [DllImport("user32.dll")] public static extern int GetDpiForSystem(); [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); }
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
  $seen = $false
  while ($sw.ElapsedMilliseconds -lt $TimeoutMs) {
    $w = Get-OuroWindow $ProcessName
    if ($w) { $e = Find-ByAutomationId $w $Id; if ($e) { return $e } }
    if (Get-Process -Name $ProcessName -ErrorAction SilentlyContinue) { $seen = $true } elseif ($seen) { break }
    Start-Sleep -Milliseconds 10
  }
  throw "No element '$Id' in a '$ProcessName' window after $($sw.ElapsedMilliseconds) ms. $(Get-OuroWhy $ProcessName)"
}

# Why a wait failed, in one line: whether the process is running and responding, what window UI Automation sees and
# how much is in it, and the app's own crash log when it wrote one. Also saves the whole screen beside the evidence.
function Get-OuroWhy([string]$ProcessName) {
  $p = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
  $why = if ($p) { "Running: pid $($p[0].Id), responding $($p[0].Responding), main window '$($p[0].MainWindowTitle)'." } else { "Not running." }
  $w = Get-OuroWindow $ProcessName
  if ($w) { $why += " UI Automation sees '$($w.Current.Name)' ($($w.Current.ClassName)) with $($w.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition).Count) elements." }
  $crash = Join-Path $env:LOCALAPPDATA "OuroShop\crash.txt"
  if (Test-Path $crash) { $why += " Crash log: $(Get-Content -Raw $crash)" }
  if ($env:EV) {
    Add-Type -AssemblyName System.Drawing, System.Windows.Forms
    $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen(0, 0, 0, 0, $bmp.Size)
    $bmp.Save((Join-Path $env:EV "timeout-$ProcessName.png"), [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
  }
  return $why
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
  $wr = $w.Current.BoundingRectangle
  return [ordered]@{ process = $ProcessName; window = $w.Current.Name; systemDpi = [OuroDpi]::GetDpiForSystem(); windowRect = [ordered]@{ x = $wr.X; y = $wr.Y; width = $wr.Width; height = $wr.Height }; elements = @($items) }
}

function Invoke-OuroButton($Element) {
  $pattern = $Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
  $pattern.Invoke()
}
