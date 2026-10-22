param([Parameter(Mandatory)][string]$ResultsDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$culture = [Globalization.CultureInfo]::InvariantCulture

function Finite-Number([string]$value) {
  $number = [double]::Parse($value,$culture)
  if ([double]::IsNaN($number) -or [double]::IsInfinity($number)) { throw '记录含有无效数值。' }
  $number
}

function Mean-Watts([string]$prefix, [string]$mode, [int]$pair) {
  $meta = Get-Content -LiteralPath ($prefix+'.json') -Raw | ConvertFrom-Json
  if ($meta.Status -ne 'Complete' -or $meta.WarmupSeconds -lt 120 -or $meta.SampleSeconds -lt 600) { throw "预热或测量不足：$prefix" }
  if ($meta.Mode -ne $mode -or $meta.Pair -ne $pair) { throw "测量标记与配对文件不符：$prefix" }
  $rows = @(Import-Csv -LiteralPath ($prefix+'.csv'))
  if ($rows.Count -lt 2) { throw "记录不完整：$prefix" }
  if (@($rows | Where-Object { $_.Mode -ne $mode -or $_.Pair -ne $pair }).Count) { throw "采样标记错误：$prefix" }
  $times = @($rows | ForEach-Object { Finite-Number $_.ElapsedSeconds })
  for ($i=1; $i -lt $times.Count; $i++) { if ($times[$i] -le $times[$i-1]) { throw "采样时间未递增：$prefix" } }
  $duration = $times[-1] - $times[0]
  if ($duration -lt 600 -or @($rows | Where-Object PowerOnline -ne 'False').Count) { throw "未完整测量 10 分钟电池放电：$prefix" }
  # Prefer energy lost over the full measurement window. Firmware rate samples can
  # lag or be unsupported; capacity is also reported by the same battery gauge.
  $firstCapacity = if ([string]::IsNullOrWhiteSpace($rows[0].RemainingCapacityMWh)) { 0 } else { Finite-Number $rows[0].RemainingCapacityMWh }
  $lastCapacity = if ([string]::IsNullOrWhiteSpace($rows[-1].RemainingCapacityMWh)) { 0 } else { Finite-Number $rows[-1].RemainingCapacityMWh }
  if ($firstCapacity -gt 0 -and $firstCapacity -lt 4294967295 -and $lastCapacity -gt 0 -and $firstCapacity -gt $lastCapacity) {
    return [pscustomobject]@{ Watts=($firstCapacity-$lastCapacity)/1000*3600/$duration; Method='CapacityDelta'; Metadata=$meta }
  }
  $valid = @($rows | Where-Object { -not [string]::IsNullOrWhiteSpace($_.DischargeWatts) })
  if ($valid.Count -lt $rows.Count*0.8) { throw "电池仪表不能提供有效功率：$prefix" }
  $energy = 0.0; $covered = 0.0
  for ($i=1; $i -lt $rows.Count; $i++) {
    if ([string]::IsNullOrWhiteSpace($rows[$i-1].DischargeWatts) -or [string]::IsNullOrWhiteSpace($rows[$i].DischargeWatts)) { continue }
    $dt=$times[$i]-$times[$i-1]
    if ($dt -gt 10) { continue }
    $before = Finite-Number $rows[$i-1].DischargeWatts
    $after = Finite-Number $rows[$i].DischargeWatts
    if ($before -le 0 -or $after -le 0) { throw "放电功率必须为正：$prefix" }
    $energy+=($before+$after)*0.5*$dt
    $covered+=$dt
  }
  if ($covered -lt $duration*0.8) { throw "有效采样时长不足：$prefix" }
  [pscustomobject]@{ Watts=$energy/$covered; Method='IntegratedRate'; Metadata=$meta }
}

$results = for ($pair=1; $pair -le 3; $pair++) {
  $baseline = Mean-Watts (Join-Path $ResultsDirectory "pair$pair-BaselinePiP") 'BaselinePiP' $pair
  $learning = Mean-Watts (Join-Path $ResultsDirectory "pair$pair-LearningStatic") 'LearningStatic' $pair
  if ($baseline.Metadata.PluginSha256 -eq $learning.Metadata.PluginSha256) { throw '两种模式使用了同一插件，不能证明新旧版本差异。' }
  if ($baseline.Metadata.ComputerModel -ne $learning.Metadata.ComputerModel -or $baseline.Metadata.PowerScheme -ne $learning.Metadata.PowerScheme) { throw '配对测试的电脑或电源模式不同。' }
  if ($baseline.Method -ne $learning.Method) { throw '配对测试的功率计算方法不同，请重新采样。' }
  [pscustomobject]@{ Pair=$pair; BaselineWatts=$baseline.Watts; LearningWatts=$learning.Watts;
    ReductionPercent=(1-$learning.Watts/$baseline.Watts)*100; Method=$baseline.Method }
}
$baselineMean=($results | Measure-Object BaselineWatts -Average).Average
$learningMean=($results | Measure-Object LearningWatts -Average).Average
$reduction=(1-$learningMean/$baselineMean)*100
[ordered]@{ Pairs=$results; BaselineMeanWatts=$baselineMean; LearningMeanWatts=$learningMean;
  ReductionPercent=$reduction; TargetPercent=20; MeetsTarget=($reduction -ge 20);
  Note='需人工确认每对的亮度、实际使用的 GPU、场景、音乐、音量、后台程序及小窗大小一致；电池仪表精度有限。'
} | ConvertTo-Json -Depth 6 | Tee-Object -FilePath (Join-Path $ResultsDirectory 'summary.json')
