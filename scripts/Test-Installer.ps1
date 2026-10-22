[CmdletBinding()]
param()

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$fixtureRoot = Join-Path $repositoryRoot ('.installer-tests-' + [Guid]::NewGuid().ToString('N'))
$packageRoot = Join-Path $fixtureRoot 'release package'
$gameRoot = Join-Path $fixtureRoot 'fake game'
$checks = 0

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw "CHECK FAILED: $Message" }
    $script:checks++
}

function Assert-Throws {
    param([scriptblock]$Action, [string]$ExpectedText)
    $failedAsExpected = $false
    try { & $Action } catch {
        if ($_.Exception.Message -notlike "*$ExpectedText*") { throw "Unexpected failure: $($_.Exception.Message)" }
        $failedAsExpected = $true
    }
    Assert-True $failedAsExpected "Expected refusal containing '$ExpectedText'"
}

function Write-Release {
    param([string]$Content)
    [IO.File]::WriteAllText((Join-Path $packageRoot 'BepInEx\plugins\iGPU Savior.dll'), $Content)
    $hash = (Get-FileHash -LiteralPath (Join-Path $packageRoot 'BepInEx\plugins\iGPU Savior.dll') -Algorithm SHA256).Hash
    @{ DllSha256 = $hash } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $packageRoot 'release.json') -Encoding UTF8
    return $hash
}

function Reset-Game {
    if (Test-Path -LiteralPath $gameRoot) {
        $resolved = (Resolve-Path -LiteralPath $gameRoot).ProviderPath
        if (-not $resolved.StartsWith($fixtureRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup path.' }
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    New-Item -ItemType Directory -Path (Join-Path $gameRoot 'BepInEx\core') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $gameRoot 'BepInEx\plugins') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $gameRoot 'BepInEx\config') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $gameRoot 'Chill With You.exe'), 'fake exe: never executed')
    [IO.File]::WriteAllText((Join-Path $gameRoot 'BepInEx\core\BepInEx.dll'), 'fake BepInEx: never executed')
    [IO.File]::WriteAllText((Join-Path $gameRoot 'BepInEx\config\chillwithyou.potatomode.cfg'), 'UserConfiguration=Keep')
}

try {
    New-Item -ItemType Directory -Path (Join-Path $packageRoot 'BepInEx\plugins') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'install.ps1') -Destination $packageRoot
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'uninstall.ps1') -Destination $packageRoot
    $install = Join-Path $packageRoot 'install.ps1'
    $uninstall = Join-Path $packageRoot 'uninstall.ps1'
    $targetDll = Join-Path $gameRoot 'BepInEx\plugins\iGPU Savior.dll'
    $statePath = Join-Path $gameRoot 'BepInEx\LoFiStudyWindow\install.json'
    $configPath = Join-Path $gameRoot 'BepInEx\config\chillwithyou.potatomode.cfg'
    $firstHash = Write-Release 'First release fixture'
    Reset-Game

    & $install -GameDirectory $gameRoot -WhatIf
    Assert-True (-not (Test-Path -LiteralPath $targetDll)) 'WhatIf must not install DLL'
    Assert-True (-not (Test-Path -LiteralPath (Split-Path -Parent $statePath))) 'WhatIf must not create metadata directories'

    & $install -GameDirectory $gameRoot
    Assert-True ((Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash -eq $firstHash) 'Clean installation installs release DLL'
    Assert-True ((Get-Content -LiteralPath $configPath -Raw) -eq 'UserConfiguration=Keep') 'Install preserves configuration'
    $firstState = Get-Content -LiteralPath $statePath -Raw
    & $install -GameDirectory $gameRoot
    Assert-True ((Get-Content -LiteralPath $statePath -Raw) -eq $firstState) 'Same release installation is idempotent'
    & $uninstall -GameDirectory $gameRoot -WhatIf
    Assert-True (Test-Path -LiteralPath $targetDll) 'Uninstall WhatIf must keep DLL'
    & $uninstall -GameDirectory $gameRoot
    Assert-True (-not (Test-Path -LiteralPath $targetDll)) 'Clean installation uninstall moves DLL outside plugins'
    Assert-True (-not (Test-Path -LiteralPath $statePath)) 'Uninstall retires active installation record'
    Assert-True (Test-Path -LiteralPath (Join-Path $gameRoot 'BepInEx\core\BepInEx.dll')) 'Uninstall preserves loader'
    Assert-True ((Get-Content -LiteralPath $configPath -Raw) -eq 'UserConfiguration=Keep') 'Uninstall preserves configuration'
    & $uninstall -GameDirectory $gameRoot

    Reset-Game
    [IO.File]::WriteAllText($targetDll, 'Existing upstream plugin fixture')
    $upstreamHash = (Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash
    & $install -GameDirectory $gameRoot
    $initialState = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    Assert-True ((Get-FileHash -LiteralPath $initialState.OriginalDllBackup -Algorithm SHA256).Hash -eq $upstreamHash) 'Existing DLL is backed up'
    Assert-True (Test-Path -LiteralPath (Join-Path $initialState.BackupDirectory 'config.cfg')) 'Existing configuration has a backup'
    [IO.File]::WriteAllText($configPath, 'UserConfiguration=ChangedAfterInstall')
    $secondHash = Write-Release 'Second release fixture'
    & $install -GameDirectory $gameRoot
    $upgradedState = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    Assert-True ($upgradedState.OriginalDllBackup -eq $initialState.OriginalDllBackup) 'Upgrade retains original restoration target'
    Assert-True ((Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash -eq $secondHash) 'Upgrade replaces DLL'
    & $uninstall -GameDirectory $gameRoot
    Assert-True ((Get-FileHash -LiteralPath $targetDll -Algorithm SHA256).Hash -eq $upstreamHash) 'Uninstall after upgrade restores original upstream DLL'
    Assert-True ((Get-Content -LiteralPath $configPath -Raw) -eq 'UserConfiguration=ChangedAfterInstall') 'Uninstall never overwrites later configuration'

    Reset-Game
    [IO.File]::WriteAllText((Join-Path $packageRoot 'BepInEx\plugins\iGPU Savior.dll'), 'Tampered release fixture')
    Assert-Throws { & $install -GameDirectory $gameRoot } 'checksum mismatch'
    Assert-True (-not (Test-Path -LiteralPath $statePath)) 'Tampered release does not create an installation record'
    $null = Write-Release 'Verified release fixture'
    $nestedPlugins = Join-Path $gameRoot 'BepInEx\plugins\old-version'
    New-Item -ItemType Directory -Path $nestedPlugins -Force | Out-Null
    $duplicateDll = Join-Path $nestedPlugins 'renamed.dll'
    [IO.File]::WriteAllBytes($duplicateDll, [Text.Encoding]::Unicode.GetBytes('binary fixture chillwithyou.potatomode metadata'))
    Assert-Throws { & $install -GameDirectory $gameRoot } 'Another copy'
    Assert-True (-not (Test-Path -LiteralPath $targetDll)) 'Renamed duplicate plugin refusal does not install DLL'
    Move-Item -LiteralPath $duplicateDll -Destination (Join-Path $gameRoot 'duplicate.dll.disabled')
    & $install -GameDirectory $gameRoot
    [IO.File]::WriteAllText($targetDll, 'User changed installed DLL')
    Assert-Throws { & $uninstall -GameDirectory $gameRoot } 'differs from the installation record'
    Assert-Throws { & $install -GameDirectory $gameRoot } 'differs from the installation record'
    Assert-True ((Get-Content -LiteralPath $targetDll -Raw) -eq 'User changed installed DLL') 'Mismatched user DLL remains untouched'

    Reset-Game
    [IO.File]::WriteAllText($targetDll, 'Original plugin for backup tamper test')
    & $install -GameDirectory $gameRoot
    $backupState = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    [IO.File]::WriteAllText($backupState.OriginalDllBackup, 'Tampered backup')
    Assert-Throws { & $uninstall -GameDirectory $gameRoot } 'backup is missing or changed'
    $backupState.OriginalDllBackup = Join-Path $fixtureRoot 'outside-backups.dll'
    $backupState | ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding UTF8
    Assert-Throws { & $uninstall -GameDirectory $gameRoot } 'outside the backup directory'

    Reset-Game
    function Get-Process {
        param($Name, $ErrorAction)
        [pscustomobject]@{ Path = Join-Path $gameRoot 'Chill With You.exe' }
    }
    try {
        Assert-Throws { & $install -GameDirectory $gameRoot } 'game is running'
        Assert-True (-not (Test-Path -LiteralPath $targetDll)) 'Running game refusal makes no changes'
    } finally { Remove-Item -LiteralPath Function:\Get-Process }

    Reset-Game
    $steamRoot = Join-Path $fixtureRoot 'fake Steam'
    $libraryRoot = Join-Path $fixtureRoot 'second library'
    $discoveredGame = Join-Path $libraryRoot 'steamapps\common\Chill with You Lo-Fi Story'
    New-Item -ItemType Directory -Path (Join-Path $steamRoot 'steamapps') -Force | Out-Null
    New-Item -ItemType Directory -Path (Split-Path -Parent $discoveredGame) -Force | Out-Null
    Copy-Item -LiteralPath $gameRoot -Destination $discoveredGame -Recurse
    ('"libraryfolders" { "1" { "path" "' + $libraryRoot.Replace('\', '\\') + '" } }') | Set-Content -LiteralPath (Join-Path $steamRoot 'steamapps\libraryfolders.vdf') -Encoding UTF8
    function Get-ItemProperty {
        param($LiteralPath, $ErrorAction)
        if ($LiteralPath -eq 'HKCU:\Software\Valve\Steam') { [pscustomobject]@{ SteamPath = $steamRoot } }
    }
    # Hide every path outside the fixture, including real Steam installations.
    function Test-Path {
        [CmdletBinding()]
        param([string]$LiteralPath, [Microsoft.PowerShell.Commands.TestPathType]$PathType = 'Any')
        if ([IO.Path]::GetFullPath($LiteralPath).StartsWith($fixtureRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
            Microsoft.PowerShell.Management\Test-Path @PSBoundParameters
        } else { $false }
    }
    try {
        & $install
        Assert-True (Test-Path -LiteralPath (Join-Path $discoveredGame 'BepInEx\plugins\iGPU Savior.dll')) 'Steam secondary library autodetection works'
        & $uninstall
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $discoveredGame 'BepInEx\plugins\iGPU Savior.dll'))) 'Uninstaller autodetects same Steam library'
    } finally {
        Remove-Item -LiteralPath Function:\Test-Path
        Remove-Item -LiteralPath Function:\Get-ItemProperty
    }

    Write-Host "Installer tests passed: $checks checks. Only disposable workspace fixtures were used."
} finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        $resolvedFixture = (Resolve-Path -LiteralPath $fixtureRoot).ProviderPath
        $expectedPrefix = [IO.Path]::GetFullPath($repositoryRoot).TrimEnd('\') + '\.installer-tests-'
        if (-not $resolvedFixture.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup path.' }
        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
    }
}
