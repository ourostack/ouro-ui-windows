# Drives Ouro Shop (-Grab row is the control: a drag from the row's own words) with a real mouse, as a person's hand would: it drags the first row of the lists page's Reorderable from its handle past the last
# row, and records the order of the handles before and after. Windows' UI Automation cannot say whether a drag starts, so this presses, moves and
# releases the mouse itself. Writes the record to -Out and exits 1 when the row did not move.
param([Parameter(Mandatory)][string]$Command, [Parameter(Mandatory)][string]$ProcessName, [Parameter(Mandatory)][string]$Out, [ValidateSet("handle","row")][string]$Grab = "handle")
$ErrorActionPreference = "Stop"
. "$PSScriptRoot\uia.ps1"
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class OuroMouse {
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
  public const uint Down = 0x0002, Up = 0x0004, Move = 0x0001, Absolute = 0x8000;
  public static void To(int x, int y) { mouse_event(Move | Absolute, (uint)(x * 65535L / GetSystemMetrics(0)), (uint)(y * 65535L / GetSystemMetrics(1)), 0, UIntPtr.Zero); }
}
"@

function Get-Handles {
  $w = Get-OuroWindow $ProcessName
  $group = Find-ByAutomationId $w "gallery.reorderable.three-rows"
  if (-not $group) { throw "No Reorderable on the lists page. $(Get-OuroWhy $ProcessName)" }
  $buttons = $group.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
  return @($buttons | Where-Object { $_.Current.Name -like "Reorder *" } | Sort-Object { $_.Current.BoundingRectangle.Y })
}
function Get-Order { @(Get-Handles | ForEach-Object { $_.Current.Name }) }
function Center($Element) { $r = $Element.Current.BoundingRectangle; return @{ x = [int]($r.X + $r.Width / 2); y = [int]($r.Y + $r.Height / 2) } }

[void][OuroDpi]::SetCursorPos(0, 0)
Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 800
Start-Process -FilePath $Command -ArgumentList "--page lists" | Out-Null
[void](Wait-OuroElement $ProcessName "gallery.reorderable.three-rows")
Start-Sleep -Milliseconds 2000

$before = Get-Order
$handles = Get-Handles
$from = Center $handles[0]
if ($Grab -eq "row") { $from.x -= 120 }
$to = Center $handles[$handles.Count - 1]
$to.y += 12
# A first click, on the empty end of the card, gives the window the foreground as a person's would, before the drag.
[OuroMouse]::To($from.x - 300, $from.y - 60)
Start-Sleep -Milliseconds 300
[OuroMouse]::mouse_event([OuroMouse]::Down, 0, 0, 0, [UIntPtr]::Zero)
[OuroMouse]::mouse_event([OuroMouse]::Up, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 500
[OuroMouse]::To($from.x, $from.y)
Start-Sleep -Milliseconds 300
[OuroMouse]::mouse_event([OuroMouse]::Down, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 300
$steps = 30
foreach ($i in 1..$steps) {
  [OuroMouse]::To($from.x, [int]($from.y + ($to.y - $from.y) * $i / $steps))
  Start-Sleep -Milliseconds 30
}
Start-Sleep -Milliseconds 500
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
$shot = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap $shot.Width, $shot.Height
[System.Drawing.Graphics]::FromImage($bmp).CopyFromScreen(0, 0, 0, 0, $bmp.Size)
$bmp.Save(($Out -replace '\.json$', '-mid-drag.png'))
[OuroMouse]::mouse_event([OuroMouse]::Up, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 1500
$after = Get-Order
$status = (Find-ByAutomationId (Get-OuroWindow $ProcessName) "status").Current.Name
$moved = ($before -join "|") -ne ($after -join "|") -and $after[0] -ne $before[0]
[ordered]@{ grab = $Grab; from = $from; to = $to; before = $before; after = $after; status = $status; moved = $moved } | ConvertTo-Json | Set-Content -Encoding utf8 $Out
Get-Content $Out
Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Stop-Process -Force
if (-not $moved) { Write-Host "::error::A drag from the $Grab did not move the row: before $($before -join ', '), after $($after -join ', ')"; exit 1 }
