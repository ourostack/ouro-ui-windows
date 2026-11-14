# Waits for a window with this title (the MCP server's confirmation), saves a screenshot of it, and presses one of its
# buttons through UI Automation, as the person would. Writes what it saw as JSON. Run it beside the call that asks.
param([Parameter(Mandatory)][string]$Title, [ValidateSet("Yes", "No")][string]$Press = "No", [Parameter(Mandatory)][string]$Out, [string]$Screenshot, [int]$TimeoutMs = 60000)
. "$PSScriptRoot\uia.ps1"
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
$cond = New-Object System.Windows.Automation.PropertyCondition($script:Uia::NameProperty, $Title)
$sw = [Diagnostics.Stopwatch]::StartNew()
$w = $null
while (-not $w -and $sw.ElapsedMilliseconds -lt $TimeoutMs) { $w = $script:Uia::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond); if (-not $w) { Start-Sleep -Milliseconds 100 } }
if (-not $w) { [ordered]@{ seen = $false; title = $Title } | ConvertTo-Json | Set-Content -Encoding utf8 $Out; throw "No '$Title' window in $TimeoutMs ms" }
Start-Sleep -Milliseconds 500
$texts = @($w.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($script:Uia::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text))) | ForEach-Object { $_.Current.Name })
$buttons = @($w.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition($script:Uia::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button))))
if ($Screenshot) {
  $r = $w.Current.BoundingRectangle
  $bmp = New-Object System.Drawing.Bitmap ([int]$r.Width), ([int]$r.Height)
  $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
  $bmp.Save($Screenshot, [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
}
$target = $buttons | Where-Object { $_.Current.Name -eq $Press } | Select-Object -First 1
$focused = ($buttons | Where-Object { $_.Current.HasKeyboardFocus } | Select-Object -First 1)
[ordered]@{ seen = $true; title = $Title; text = $texts; buttons = @($buttons | ForEach-Object { $_.Current.Name }); defaultFocus = if ($focused) { $focused.Current.Name } else { $null }; pressed = $Press; afterMs = $sw.ElapsedMilliseconds } | ConvertTo-Json | Set-Content -Encoding utf8 $Out
Invoke-OuroButton $target
Write-Host "answer-dialog: '$Title' answered $Press"
