param(
    [Parameter(Mandatory = $true)][string]$GameDirectory,
    [string]$ReleaseId = 'learning-window-2026-10-21',
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if ($ReleaseId -notmatch '^[a-zA-Z0-9][a-zA-Z0-9.-]+$') { throw 'Invalid release identifier.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo 'dist' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$stage = Join-Path $OutputDirectory ('LoFi-Study-Window-' + $ReleaseId)
$zip = $stage + '.zip'
if ((Test-Path -LiteralPath $stage) -or (Test-Path -LiteralPath $zip)) {
    throw 'Output already exists. Choose a new output directory; existing packages are not overwritten.'
}
if (-not (Test-Path -LiteralPath (Join-Path $GameDirectory 'Chill With You_Data/Managed/Assembly-CSharp.dll'))) {
    throw 'GameDirectory must contain the installed game assemblies and BepInEx/core.'
}

$msbuildGameDirectory = ([IO.Path]::GetFullPath($GameDirectory)).Replace('%', '%25').Replace(';', '%3B').Replace(',', '%2C')
& dotnet build (Join-Path $repo 'iGPU Savior/iGPU Savior.csproj') -c Release "-p:GameDir=$msbuildGameDirectory" -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Production build failed.' }
& pwsh -NoProfile -File (Join-Path $repo 'scripts/sync-version.ps1') -CheckOnly
if ($LASTEXITCODE -ne 0) { throw 'Version check failed.' }

$dll = Join-Path $repo 'iGPU Savior/bin/Release/iGPU Savior.dll'
$dllHash = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
$version = (Get-Content -LiteralPath (Join-Path $repo 'version.json') -Raw | ConvertFrom-Json).version
$null = New-Item -ItemType Directory -Path (Join-Path $stage 'BepInEx/plugins') -Force
$null = New-Item -ItemType Directory -Path (Join-Path $stage 'docs') -Force
Copy-Item -LiteralPath $dll -Destination (Join-Path $stage 'BepInEx/plugins/iGPU Savior.dll')
foreach ($file in @('README.md', 'CREDITS.md', 'LICENSE.txt', 'install.ps1', 'uninstall.ps1', '安装.cmd', '卸载.cmd')) {
    Copy-Item -LiteralPath (Join-Path $repo $file) -Destination (Join-Path $stage $file)
}
foreach ($file in @('INSTALL.md', 'LEARNING_WINDOW.md', 'VALIDATION.md', 'RELEASE_NOTES.md', 'validation-results.json')) {
    Copy-Item -LiteralPath (Join-Path $repo ('docs/' + $file)) -Destination (Join-Path $stage ('docs/' + $file))
}
$null = New-Item -ItemType Directory -Path (Join-Path $stage 'docs/images') -Force
Copy-Item -LiteralPath (Join-Path $repo 'docs/images/learning-window-preview.png') -Destination (Join-Path $stage 'docs/images/learning-window-preview.png')
$null = New-Item -ItemType Directory -Path (Join-Path $stage 'scripts') -Force
foreach ($file in @('Measure-LearningPower.ps1', 'Compare-LearningPower.ps1', 'Test-LearningPower.ps1')) {
    Copy-Item -LiteralPath (Join-Path $repo ('scripts/' + $file)) -Destination (Join-Path $stage ('scripts/' + $file))
}
[ordered]@{
    Project = 'LoFi-Study-Window'
    ReleaseId = $ReleaseId
    PluginVersion = $version
    DllSha256 = $dllHash
    Repository = 'https://github.com/Nasida-is-the-cutest/LoFi-Study-Window'
    BasedOn = 'iGPU Savior by Small-tailqwq (MIT)'
    Upstream = 'https://github.com/Small-tailqwq/iGPUSaviorMod'
    TestedGameVersion = '1.17.3'
    BepInEx = 'Windows x64 5.x (tested 5.4.23.5); installed separately'
    PowerMeasurement = 'Not measured; no battery-life percentage claim'
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'release.json') -Encoding utf8

Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
$zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
($zipHash + '  ' + [IO.Path]::GetFileName($zip)) | Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding ascii
Write-Output $zip
Write-Output ('SHA256: ' + $zipHash)
