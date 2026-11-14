# Measures one build of the spike window: launch to first window, launch to the first action button in the
# UI Automation tree, memory three seconds after it is ready, and press-to-status latency through UI Automation.
# Run 1 is cold (first launch after the build); the summary takes medians over the warm runs.
param(
  [Parameter(Mandatory)][string]$Label,
  [Parameter(Mandatory)][string]$Command,
  [Parameter(Mandatory)][string]$ProcessName,
  [int]$Runs = 8,
  [int]$Presses = 20,
  [Parameter(Mandatory)][string]$Out
)
. "$PSScriptRoot\uia.ps1"
$ready = "action.add_shopping_list_item"
function Median([double[]]$xs) { $s = $xs | Sort-Object; $n = $s.Count; if ($n -eq 0) { return $null }; if ($n % 2) { return $s[[int][math]::Floor($n / 2)] } else { return ($s[$n / 2 - 1] + $s[$n / 2]) / 2 } }
function Pct([double[]]$xs, [double]$p) { $s = $xs | Sort-Object; return $s[[int][math]::Min($s.Count - 1, [math]::Ceiling($p * $s.Count) - 1)] }

$results = @()
$latencies = @()
for ($i = 1; $i -le $Runs; $i++) {
  Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Sleep -Milliseconds 1500
  $sw = [Diagnostics.Stopwatch]::StartNew()
  Start-Process -FilePath $Command | Out-Null
  $windowMs = $null; $readyMs = $null
  while ($sw.ElapsedMilliseconds -lt 60000) {
    $w = Get-OuroWindow $ProcessName
    if ($w) {
      if (-not $windowMs) { $windowMs = $sw.ElapsedMilliseconds }
      if (Find-ByAutomationId $w $ready) { $readyMs = $sw.ElapsedMilliseconds; break }
    }
    Start-Sleep -Milliseconds 5
  }
  if (-not $readyMs) { throw "$Label run ${i}: the button never appeared" }
  Start-Sleep -Seconds 3
  $p = Get-Process -Name $ProcessName | Select-Object -First 1
  $p.Refresh()
  $run = [ordered]@{ run = $i; windowMs = $windowMs; readyMs = $readyMs; workingSetMB = [math]::Round($p.WorkingSet64 / 1MB, 1); privateMB = [math]::Round($p.PrivateMemorySize64 / 1MB, 1) }
  if ($i -eq $Runs) {
    $w = Get-OuroWindow $ProcessName
    $button = Find-ByAutomationId $w $ready
    $status = Find-ByAutomationId $w "status"
    for ($k = 0; $k -lt $Presses; $k++) {
      $before = $status.Current.Name
      $t = [Diagnostics.Stopwatch]::StartNew()
      Invoke-OuroButton $button
      while ($status.Current.Name -eq $before -and $t.ElapsedMilliseconds -lt 5000) { }
      $latencies += $t.Elapsed.TotalMilliseconds
      Start-Sleep -Milliseconds 100
    }
  }
  $results += $run
}
Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Stop-Process -Force
$warm = $results | Select-Object -Skip 1
$summary = [ordered]@{
  label = $Label
  runs = $Runs
  coldReadyMs = $results[0].readyMs
  warmWindowMsMedian = Median ($warm | ForEach-Object { [double]$_.windowMs })
  warmReadyMsMedian = Median ($warm | ForEach-Object { [double]$_.readyMs })
  workingSetMBMedian = Median ($results | ForEach-Object { [double]$_.workingSetMB })
  privateMBMedian = Median ($results | ForEach-Object { [double]$_.privateMB })
  pressToStatusMsMedian = [math]::Round((Median $latencies), 1)
  pressToStatusMsP95 = [math]::Round((Pct $latencies 0.95), 1)
  os = [Environment]::OSVersion.VersionString
  cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name
  logicalProcessors = [Environment]::ProcessorCount
}
$doc = [ordered]@{ summary = $summary; runs = $results; pressToStatusMs = $latencies }
New-Item -ItemType Directory -Force -Path (Split-Path $Out) | Out-Null
$doc | ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 $Out
$summary | ConvertTo-Json | Write-Host
