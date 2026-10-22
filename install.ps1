[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Position = 0)]
    [string]$GameDirectory
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Get-GameDirectory {
    param([string]$RequestedPath)
    if ($RequestedPath) {
        return (Resolve-Path -LiteralPath $RequestedPath -ErrorAction Stop).ProviderPath
    }

    $steamRoots = New-Object 'System.Collections.Generic.List[string]'
    foreach ($registryPath in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam')) {
        $entry = Get-ItemProperty -LiteralPath $registryPath -ErrorAction SilentlyContinue
        foreach ($property in @('SteamPath', 'InstallPath')) {
            if ($entry -and $entry.PSObject.Properties[$property] -and $entry.$property) {
                $steamRoots.Add([string]$entry.$property)
            }
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
        if (Test-Path -LiteralPath (Join-Path $candidate 'Chill With You.exe') -PathType Leaf) {
            (Resolve-Path -LiteralPath $candidate).ProviderPath
        }
    } | Select-Object -Unique)
    if ($found.Count -eq 1) { return $found[0] }
    if ($found.Count -gt 1) { throw 'Multiple game installations found. Run again with -GameDirectory "full game folder".' }
    throw 'Game not found. In Steam, open Manage > Browse local files, then run install.ps1 -GameDirectory "full game folder".'
}

function Assert-GameStopped {
    param([string]$ExecutablePath)
    foreach ($gameProcess in @(Get-Process -Name 'Chill With You' -ErrorAction SilentlyContinue)) {
        $runningPath = $null
        try { $runningPath = $gameProcess.Path } catch { }
        if (-not $runningPath -or [string]::Equals([IO.Path]::GetFullPath($runningPath), $ExecutablePath, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The game is running. Exit it normally, wait for it to close, then run this script again.'
        }
    }
}

function Assert-BackupPath {
    param([string]$Path, [string]$BackupRoot)
    $fullPath = [IO.Path]::GetFullPath($Path)
    $prefix = [IO.Path]::GetFullPath($BackupRoot).TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Installation record contains a backup path outside the backup directory. No files were changed.'
    }
    return $fullPath
}

function Assert-NoDuplicatePlugin {
    param([string]$PluginRoot, [string]$TargetPath)
    if (-not (Test-Path -LiteralPath $PluginRoot -PathType Container)) { return }
    foreach ($file in @(Get-ChildItem -LiteralPath $PluginRoot -Filter '*.dll' -File -Recurse)) {
        if ([string]::Equals($file.FullName, $TargetPath, [StringComparison]::OrdinalIgnoreCase)) { continue }
        $related = $file.Name -like '*iGPU*Savior*' -or $file.Name -eq 'LoFi-Study-Window.dll'
        if (-not $related) {
            try { $related = [Reflection.AssemblyName]::GetAssemblyName($file.FullName).Name -eq 'iGPU Savior' } catch { }
        }
        if (-not $related) {
            $bytes = [IO.File]::ReadAllBytes($file.FullName)
            $related = [Text.Encoding]::Unicode.GetString($bytes).Contains('chillwithyou.potatomode')
            if (-not $related -and $bytes.Length -gt 1) {
                $related = [Text.Encoding]::Unicode.GetString($bytes, 1, $bytes.Length - 1).Contains('chillwithyou.potatomode')
            }
        }
        if ($related) { throw "Another copy of this plugin was found: $($file.FullName). Move it outside BepInEx\plugins, then retry." }
    }
}

$gameRoot = Get-GameDirectory $GameDirectory
$executable = Join-Path $gameRoot 'Chill With You.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'The selected folder does not contain Chill With You.exe.' }
if (-not (Test-Path -LiteralPath (Join-Path $gameRoot 'BepInEx\core\BepInEx.dll') -PathType Leaf)) {
    throw 'BepInEx 5 is required. Install the Windows x64 BepInEx 5 loader and start/exit the game once before installing this mod.'
}
Assert-GameStopped ([IO.Path]::GetFullPath($executable))

$sourceDll = Join-Path $PSScriptRoot 'BepInEx\plugins\iGPU Savior.dll'
$manifestPath = Join-Path $PSScriptRoot 'release.json'
if (-not (Test-Path -LiteralPath $sourceDll -PathType Leaf) -or -not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw 'Release files are missing. Extract the complete release ZIP first; do not run the installer from a source-code ZIP.'
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if (-not $manifest.PSObject.Properties['DllSha256'] -or [string]$manifest.DllSha256 -notmatch '^[a-fA-F0-9]{64}$') {
    throw 'release.json does not contain a valid DllSha256.'
}
$expectedHash = ([string]$manifest.DllSha256).ToUpperInvariant()
if ((Get-FileHash -LiteralPath $sourceDll -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Release DLL checksum mismatch. Download and extract a fresh release ZIP.' }

$pluginRoot = Join-Path $gameRoot 'BepInEx\plugins'
$targetDll = Join-Path $pluginRoot 'iGPU Savior.dll'
$metadataRoot = Join-Path $gameRoot 'BepInEx\LoFiStudyWindow'
$backupRoot = Join-Path $metadataRoot 'backups'
$statePath = Join-Path $metadataRoot 'install.json'
Assert-NoDuplicatePlugin $pluginRoot $targetDll

$currentHash = $null
if (Test-Path -LiteralPath $targetDll -PathType Leaf) { $currentHash = (Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash }
$previousState = $null
if (Test-Path -LiteralPath $statePath -PathType Leaf) {
    $previousState = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    if ($previousState.SchemaVersion -ne 1 -or $previousState.InstalledDllSha256 -ne $currentHash) {
        throw 'The installed DLL differs from the installation record. Back up your changes and resolve the mismatch before installing.'
    }
    if ($previousState.OriginalDllBackup) {
        $originalPath = Assert-BackupPath $previousState.OriginalDllBackup $backupRoot
        if (-not (Test-Path -LiteralPath $originalPath -PathType Leaf) -or (Get-FileHash -LiteralPath $originalPath -Algorithm SHA256).Hash -ne $previousState.OriginalDllSha256) {
            throw 'The original DLL backup is missing or changed. Resolve the backup before upgrading.'
        }
    }
    if ($currentHash -eq $expectedHash) { Write-Host 'This release is already installed. No files were changed.'; return }
}

if (-not $PSCmdlet.ShouldProcess($gameRoot, 'Back up the current mod and install LoFi Study Window (configuration and saves stay unchanged)')) { return }
$backupDirectory = Join-Path $backupRoot ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $pluginRoot -Force | Out-Null
$previousDllBackup = $null
if ($currentHash) {
    $previousDllBackup = Join-Path $backupDirectory 'previous-plugin.dll'
    Copy-Item -LiteralPath $targetDll -Destination $previousDllBackup
    if ((Get-FileHash -LiteralPath $previousDllBackup -Algorithm SHA256).Hash -ne $currentHash) { throw 'Could not verify the DLL backup. Installation stopped.' }
}
$configPath = Join-Path $gameRoot 'BepInEx\config\chillwithyou.potatomode.cfg'
if (Test-Path -LiteralPath $configPath -PathType Leaf) { Copy-Item -LiteralPath $configPath -Destination (Join-Path $backupDirectory 'config.cfg') }
if ($previousState) { Copy-Item -LiteralPath $statePath -Destination (Join-Path $backupDirectory 'previous-install.json') }
$stagedDll = Join-Path $backupDirectory 'new-plugin.dll'
Copy-Item -LiteralPath $sourceDll -Destination $stagedDll
if ((Get-FileHash -LiteralPath $stagedDll -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Could not verify the staged DLL. Installation stopped.' }
$originalBackup = $previousDllBackup
$originalHash = $currentHash
if ($previousState) {
    $originalBackup = $previousState.OriginalDllBackup
    $originalHash = $previousState.OriginalDllSha256
}
$state = [ordered]@{
    SchemaVersion = 1
    InstalledAtUtc = [DateTime]::UtcNow.ToString('o')
    InstalledDllSha256 = $expectedHash
    OriginalDllBackup = $originalBackup
    OriginalDllSha256 = $originalHash
    BackupDirectory = $backupDirectory
}
$stagedState = Join-Path $backupDirectory 'new-install.json'
$state | ConvertTo-Json | Set-Content -LiteralPath $stagedState -Encoding UTF8
try {
    Copy-Item -LiteralPath $stagedDll -Destination $targetDll -Force
    if ((Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash -ne $expectedHash) { throw 'Installed DLL checksum mismatch.' }
    Move-Item -LiteralPath $stagedState -Destination $statePath -Force
} catch {
    if ($previousDllBackup) {
        Copy-Item -LiteralPath $previousDllBackup -Destination $targetDll -Force
    } elseif (Test-Path -LiteralPath $targetDll -PathType Leaf) {
        Move-Item -LiteralPath $targetDll -Destination (Join-Path $backupDirectory 'failed-install.dll') -Force
    }
    throw
}
Write-Host 'LoFi Study Window installed successfully. Start the game through Steam.'
Write-Host "Backups: $backupDirectory"
Write-Host 'Existing configuration and game saves were not changed.'
