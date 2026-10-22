param(
  [Parameter(Mandatory)][ValidateSet('BaselinePiP', 'LearningStatic')][string]$Mode,
  [Parameter(Mandatory)][ValidateRange(1, 3)][int]$Pair,
  [Parameter(Mandatory)][string]$GameDirectory,
  [ValidateRange(0, 600)][int]$WarmupSeconds = 120,
  [ValidateRange(10, 3600)][int]$SampleSeconds = 600,
  [string]$OutputDirectory = (Join-Path $PSScriptRoot '../power-results')
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-Battery {
  $battery = Get-CimInstance -Namespace root/WMI -ClassName BatteryStatus |
    Where-Object Active | Select-Object -First 1
  if ($null -eq $battery) { throw '无法读取活动电池。请在本机 Windows PowerShell 中运行。' }
  if ($battery.PowerOnline -or -not $battery.Discharging) { throw '当前未使用电池放电；请拔掉充电器后重试。此次不能计入功耗验收。' }
  $battery
}

Read-Battery | Out-Null
$resolvedGame = (Resolve-Path -LiteralPath $GameDirectory).Path
$plugin = Join-Path $resolvedGame 'BepInEx/plugins/iGPU Savior.dll'
if (-not (Test-Path -LiteralPath $plugin)) { throw '此游戏目录未找到 iGPU Savior.dll。' }
$allGameProcesses = @(Get-Process 'Chill With You' -ErrorAction SilentlyContinue)
$gameProcess = @($allGameProcesses |
  Where-Object { $_.Path -eq (Join-Path $resolvedGame 'Chill With You.exe') })
if ($allGameProcesses.Count -ne 1 -or $gameProcess.Count -ne 1) { throw '指定目录的游戏必须正在运行，且同一时间只测试一份游戏。' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$output = (Resolve-Path -LiteralPath $OutputDirectory).Path
$prefix = Join-Path $output ("pair{0}-{1}" -f $Pair, $Mode)
if ((Test-Path -LiteralPath ($prefix + '.csv')) -or (Test-Path -LiteralPath ($prefix + '.json'))) { throw '该组数据已存在，请另选输出目录以保留原始记录。' }
$hash = (Get-FileHash -LiteralPath $plugin -Algorithm SHA256).Hash
$metadata = [ordered]@{
  Mode=$Mode; Pair=$Pair; PluginSha256=$hash; GameDirectory=$resolvedGame;
  ComputerModel=(Get-CimInstance Win32_ComputerSystem).Model;
  Graphics=@(Get-CimInstance Win32_VideoController | Select-Object Name, DriverVersion);
  PowerScheme=(& powercfg /getactivescheme | Out-String).Trim();
  WarmupSeconds=$WarmupSeconds; SampleSeconds=$SampleSeconds; IntervalSeconds=5;
  StartedUtc=[DateTime]::UtcNow.ToString('O'); Status='Running';
  Conditions='人工核对：相同亮度、GPU、场景、音乐、音量、电源模式和后台程序；新模式为 Static，旧模式为原版 PiP。'
}
$metadata | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath ($prefix+'.json') -Encoding utf8
Write-Host "预热 $WarmupSeconds 秒，再记录 $SampleSeconds 秒。请保持测试条件不变。"
try {
  $warmup = [Diagnostics.Stopwatch]::StartNew()
  while ($warmup.Elapsed.TotalSeconds -lt $WarmupSeconds) { Read-Battery | Out-Null; Start-Sleep -Seconds 5 }
  $sampling = [Diagnostics.Stopwatch]::StartNew()
  $rows = [Collections.Generic.List[object]]::new()
  do {
    $battery = Read-Battery
    if ($gameProcess[0].HasExited) { throw '测试游戏已退出，该组数据不完整。' }
    $watts = $null
    if ($battery.DischargeRate -gt 0 -and $battery.DischargeRate -lt 4294967295) { $watts = $battery.DischargeRate / 1000.0 }
    $rows.Add([pscustomobject]@{
      Utc=[DateTime]::UtcNow.ToString('O'); ElapsedSeconds=$sampling.Elapsed.TotalSeconds;
      Mode=$Mode; Pair=$Pair; DischargeWatts=$watts;
      RemainingCapacityMWh=$battery.RemainingCapacity; PowerOnline=$battery.PowerOnline;
    })
    $rows | Export-Csv -LiteralPath ($prefix+'.csv') -NoTypeInformation -Encoding utf8
    if ($rows[-1].ElapsedSeconds - $rows[0].ElapsedSeconds -ge $SampleSeconds) { break }
    Start-Sleep -Seconds 5
  } while ($true)
  if ((Get-FileHash -LiteralPath $plugin -Algorithm SHA256).Hash -ne $hash) { throw '测量期间插件发生变化，该组无效。' }
  if ((& powercfg /getactivescheme | Out-String).Trim() -ne $metadata.PowerScheme) { throw '测量期间电源模式发生变化，该组无效。' }
  $metadata.Status='Complete'; $metadata.CompletedUtc=[DateTime]::UtcNow.ToString('O')
  Write-Host "已保存原始测量：$prefix.csv"
} catch {
  $metadata.Status='Invalid'; $metadata.Error=$_.Exception.Message
  throw
} finally {
  $metadata | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath ($prefix+'.json') -Encoding utf8
}
