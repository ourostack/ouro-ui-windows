# Waits for a window with this title (the MCP server's confirmation), saves a screenshot of it, and presses one of its
# buttons through UI Automation, as the person would. Writes what it saw as JSON. Run it beside the call that asks.
param([Parameter(Mandatory)][string]$Title, [ValidateSet("Yes", "No")][string]$Press = "No", [Parameter(Mandatory)][string]$Out, [string]$Screenshot, [int]$TimeoutMs = 60000)
. "$PSScriptRoot\uia.ps1"
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
# A message box's parts by their dialog ids, for when UI Automation lists none: its default button (DM_GETDEFID), its
# text (the static control 0xFFFF) and a press (BM_CLICK), as Windows' own dialog manager knows them.
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text;
public static class OuroDialog {
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
  [DllImport("user32.dll")] static extern IntPtr GetDlgItem(IntPtr dlg, int id);
  [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  public static IntPtr Find(string title) { return FindWindow("#32770", title); }
  public static string Text(IntPtr dlg, int id) { var h = GetDlgItem(dlg, id); if (h == IntPtr.Zero) return null; var s = new StringBuilder(1024); GetWindowText(h, s, s.Capacity); return s.ToString(); }
  public static int DefaultId(IntPtr dlg) { long r = SendMessage(dlg, 0x0400, IntPtr.Zero, IntPtr.Zero).ToInt64(); return ((r >> 16) & 0xFFFF) == 0x534B ? (int)(r & 0xFFFF) : 0; }
  public static void Press(IntPtr dlg, int id) { SendMessage(GetDlgItem(dlg, id), 0x00F5, IntPtr.Zero, IntPtr.Zero); }
}
"@
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
$ids = @{ Yes = 6; No = 7 }
$dlg = [OuroDialog]::Find($Title)
$via = "UI Automation"
if (-not $target -and $dlg -ne [IntPtr]::Zero) {
  $via = "dialog manager"
  $texts = @([OuroDialog]::Text($dlg, 0xFFFF))
  $buttons = @($ids.Keys | Where-Object { [OuroDialog]::Text($dlg, $ids[$_]) -ne $null } | Sort-Object -Descending)
  $def = [OuroDialog]::DefaultId($dlg)
  $focusedName = ($ids.Keys | Where-Object { $ids[$_] -eq $def } | Select-Object -First 1)
} else {
  $buttons = @($buttons | ForEach-Object { $_.Current.Name })
  $focusedName = if ($focused) { $focused.Current.Name } else { $null }
}
[ordered]@{ seen = $true; title = $Title; text = $texts; buttons = $buttons; defaultFocus = $focusedName; pressed = $Press; via = $via; afterMs = $sw.ElapsedMilliseconds } | ConvertTo-Json | Set-Content -Encoding utf8 $Out
if ($target) { Invoke-OuroButton $target }
elseif ($dlg -ne [IntPtr]::Zero) { [OuroDialog]::Press($dlg, $ids[$Press]) }
else { throw "'$Title' has no $Press button that UI Automation or the dialog manager can press" }
Write-Host "answer-dialog: '$Title' answered $Press through $via (default: $focusedName)"
