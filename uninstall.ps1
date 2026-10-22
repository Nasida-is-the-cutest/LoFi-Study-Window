[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Position = 0)]
    [string]$GameDirectory
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Get-GameDirectory {
    param([string]$RequestedPath)
    if ($RequestedPath) { return (Resolve-Path -LiteralPath $RequestedPath -ErrorAction Stop).ProviderPath }
    $steamRoots = New-Object 'System.Collections.Generic.List[string]'
    foreach ($registryPath in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam')) {
        $entry = Get-ItemProperty -LiteralPath $registryPath -ErrorAction SilentlyContinue
        foreach ($property in @('SteamPath', 'InstallPath')) {
            if ($entry -and $entry.PSObject.Properties[$property] -and $entry.$property) { $steamRoots.Add([string]$entry.$property) }
        }
    }
    foreach ($programRoot in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
        if ($programRoot) { $steamRoots.Add((Join-Path $programRoot 'Steam')) }
    }
    $libraries = New-Object 'System.Collections.Generic.List[string]'
    foreach ($steamRoot in ($steamRoots | Select-Object -Unique)) {
        $libraries.Add($steamRoot)
        $vdfPath = Join-Path $steamRoot 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $vdfPath -PathType Leaf) {
            foreach ($match in [regex]::Matches([IO.File]::ReadAllText($vdfPath), '"(?:path|\d+)"\s+"([^"\r\n]+)"')) {
                $candidate = $match.Groups[1].Value.Replace('\\', '\')
                if ([IO.Path]::IsPathRooted($candidate)) { $libraries.Add($candidate) }
            }
        }
    }
    $found = @($libraries | Select-Object -Unique | ForEach-Object {
        $candidate = Join-Path $_ 'steamapps\common\Chill with You Lo-Fi Story'
        if (Test-Path -LiteralPath (Join-Path $candidate 'Chill With You.exe') -PathType Leaf) { (Resolve-Path -LiteralPath $candidate).ProviderPath }
    } | Select-Object -Unique)
    if ($found.Count -eq 1) { return $found[0] }
    if ($found.Count -gt 1) { throw 'Multiple game installations found. Run again with -GameDirectory "full game folder".' }
    throw 'Game not found. Run uninstall.ps1 -GameDirectory "full game folder".'
}

$gameRoot = Get-GameDirectory $GameDirectory
$executable = Join-Path $gameRoot 'Chill With You.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'The selected folder does not contain Chill With You.exe.' }
if (-not (Test-Path -LiteralPath (Join-Path $gameRoot 'BepInEx\core\BepInEx.dll') -PathType Leaf)) { throw 'The selected folder does not contain the BepInEx 5 loader.' }
foreach ($gameProcess in @(Get-Process -Name 'Chill With You' -ErrorAction SilentlyContinue)) {
    $runningPath = $null
    try { $runningPath = $gameProcess.Path } catch { }
    if (-not $runningPath -or [string]::Equals([IO.Path]::GetFullPath($runningPath), [IO.Path]::GetFullPath($executable), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The game is running. Exit it normally, wait for it to close, then run this script again.'
    }
}
$metadataRoot = Join-Path $gameRoot 'BepInEx\LoFiStudyWindow'
$backupRoot = Join-Path $metadataRoot 'backups'
$statePath = Join-Path $metadataRoot 'install.json'
$targetDll = Join-Path $gameRoot 'BepInEx\plugins\iGPU Savior.dll'
if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) { Write-Host 'No managed installation was found. No files were changed.'; return }
$state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
if ($state.SchemaVersion -ne 1 -or -not (Test-Path -LiteralPath $targetDll -PathType Leaf) -or (Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash -ne $state.InstalledDllSha256) {
    throw 'The installed DLL differs from the installation record. No files were changed; back up your changes before resolving the mismatch.'
}
$originalPath = $null
if ($state.OriginalDllBackup) {
    $originalPath = [IO.Path]::GetFullPath([string]$state.OriginalDllBackup)
    $prefix = [IO.Path]::GetFullPath($backupRoot).TrimEnd('\') + '\'
    if (-not $originalPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'The backup path is outside the backup directory. No files were changed.' }
    if (-not (Test-Path -LiteralPath $originalPath -PathType Leaf) -or (Get-FileHash -LiteralPath $originalPath -Algorithm SHA256).Hash -ne $state.OriginalDllSha256) {
        throw 'The original DLL backup is missing or changed. No files were changed.'
    }
}
if (-not $PSCmdlet.ShouldProcess($gameRoot, 'Remove LoFi Study Window and restore the previous plugin DLL, if one existed')) { return }
$backupDirectory = Join-Path $backupRoot ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-uninstall-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
$removedDll = Join-Path $backupDirectory 'removed-plugin.dll'
Move-Item -LiteralPath $targetDll -Destination $removedDll
try {
    if ($originalPath) {
        Copy-Item -LiteralPath $originalPath -Destination $targetDll
        if ((Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash -ne $state.OriginalDllSha256) { throw 'Restored DLL checksum mismatch.' }
    }
    Move-Item -LiteralPath $statePath -Destination (Join-Path $backupDirectory 'removed-install.json')
} catch {
    if (Test-Path -LiteralPath $targetDll -PathType Leaf) { Move-Item -LiteralPath $targetDll -Destination (Join-Path $backupDirectory 'failed-restore.dll') }
    Copy-Item -LiteralPath $removedDll -Destination $targetDll
    throw
}
Write-Host 'LoFi Study Window uninstalled. The previous plugin DLL was restored if one existed.'
Write-Host "Backups: $backupDirectory"
Write-Host 'Your current configuration, game saves, and the BepInEx loader were kept.'
