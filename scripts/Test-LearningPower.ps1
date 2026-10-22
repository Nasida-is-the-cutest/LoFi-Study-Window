param([string]$OutputDirectory = (Join-Path ([IO.Path]::GetTempPath()) ('igpu-power-test-'+[guid]::NewGuid().ToString('N'))))
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$compare = Join-Path $PSScriptRoot 'Compare-LearningPower.ps1'
$culture = [Globalization.CultureInfo]::InvariantCulture
$checks = 0
function Check([bool]$condition, [string]$message) {
  if (!$condition) { throw $message }
  $script:checks++
}
function Fixture([string]$name, [bool]$capacity) {
  $folder=Join-Path $OutputDirectory $name
  New-Item -ItemType Directory -Path $folder -Force | Out-Null
  for($pair=1; $pair -le 3; $pair++) {
    foreach($mode in @('BaselinePiP','LearningStatic')) {
      $prefix=Join-Path $folder "pair$pair-$mode"
      $watts=if($mode -eq 'BaselinePiP'){30.0}else{22.5}
      [ordered]@{ Mode=$mode; Pair=$pair; Status='Complete'; WarmupSeconds=120; SampleSeconds=600;
        PluginSha256=$mode; ComputerModel='Synthetic'; PowerScheme='Synthetic' } |
        ConvertTo-Json | Set-Content -LiteralPath ($prefix+'.json') -Encoding utf8
      $rows=0..120 | ForEach-Object {
        [pscustomobject]@{ Mode=$mode; Pair=$pair; ElapsedSeconds=$_*5;
          RemainingCapacityMWh=if($capacity){(60000-$watts*1000*($_*5)/3600).ToString('R',$culture)}else{''};
          DischargeWatts=$watts.ToString('R',$culture); PowerOnline='False' }
      }
      $rows | Export-Csv -LiteralPath ($prefix+'.csv') -NoTypeInformation -Encoding utf8
    }
  }
  $folder
}
function MustReject([string]$folder) {
  $rejected=$false
  try { & $compare -ResultsDirectory $folder | Out-Null } catch { $rejected=$true }
  Check $rejected "Invalid fixture was accepted: $folder"
}
foreach($capacity in @($true,$false)) {
  $folder=Fixture "valid-$capacity" $capacity
  & $compare -ResultsDirectory $folder | Out-Null
  $summary=Get-Content -LiteralPath (Join-Path $folder 'summary.json') -Raw | ConvertFrom-Json
  Check ([math]::Abs($summary.ReductionPercent-25) -lt 0.0001) 'Incorrect reduction'
  Check $summary.MeetsTarget 'Expected target to pass'
  Check ($summary.Pairs.Count -eq 3) 'Incomplete pairs'
  $expected=if($capacity){'CapacityDelta'}else{'IntegratedRate'}
  Check ($summary.Pairs[0].Method -eq $expected) 'Wrong computation method'
}
foreach($fault in @('warmup','same-plugin','wrong-mode','ac','timestamp','negative-rate','incomplete')) {
  $folder=Fixture $fault ($fault -ne 'negative-rate')
  $prefix=Join-Path $folder 'pair1-LearningStatic'
  $meta=Get-Content -LiteralPath ($prefix+'.json') -Raw | ConvertFrom-Json
  $rows=@(Import-Csv -LiteralPath ($prefix+'.csv'))
  switch($fault) {
    'warmup' {$meta.WarmupSeconds=0}
    'same-plugin' {$meta.PluginSha256='BaselinePiP'}
    'wrong-mode' {$rows[10].Mode='BaselinePiP'}
    'ac' {$rows[10].PowerOnline='True'}
    'timestamp' {$rows[10].ElapsedSeconds=$rows[9].ElapsedSeconds}
    'negative-rate' {$rows | ForEach-Object {$_.DischargeWatts='-1'}}
    'incomplete' {$meta.Status='Running'}
  }
  $meta | ConvertTo-Json | Set-Content -LiteralPath ($prefix+'.json') -Encoding utf8
  $rows | Export-Csv -LiteralPath ($prefix+'.csv') -NoTypeInformation -Encoding utf8
  MustReject $folder
}
Write-Host "Power calculation checks passed: $checks (synthetic data only)."
