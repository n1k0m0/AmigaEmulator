param([string]$TestRoot = '')
$ErrorActionPreference = 'Stop'
$RepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($TestRoot)) { $TestRoot = Join-Path ([IO.Path]::GetTempPath()) ('AmigaInstallerTests-' + [Guid]::NewGuid().ToString('N')) }
$testRoot = [IO.Path]::GetFullPath($TestRoot)
if (Test-Path -LiteralPath $testRoot) { throw 'Installer tests require a fresh, nonexistent output directory.' }
New-Item -ItemType Directory -Path $testRoot | Out-Null
$Version = (Get-Content -LiteralPath (Join-Path $RepoRoot 'VERSION') -Raw).Trim()
$installDir = [IO.Path]::GetFullPath((Join-Path $testRoot 'installed'))
if (-not $installDir.StartsWith($testRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test install location' }
$key = 'HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/{7B9B8822-5AF7-4E06-B72B-72D658B6C5D9}_is1'
$machineKey = $key.Replace('HKCU:', 'HKLM:')
if ((Test-Path -LiteralPath $key) -or (Test-Path -LiteralPath $machineKey)) { throw 'Existing user installation; test will not change it.' }
$setup = Join-Path $RepoRoot "artifacts/installer/AmigaEmulator-$Version-win-x64-setup.exe"
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
if (-not (Get-Content -LiteralPath "$setup.sha256" -Raw).StartsWith($hash)) { throw 'Installer checksum mismatch' }
$setupArgs = @('/CURRENTUSER', '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOICONS', '/TASKS=', "/DIR=`"$installDir`"", "/LOG=`"$(Join-Path $testRoot 'install.log')`"")
try {
    $install = Start-Process -FilePath $setup -ArgumentList $setupArgs -PassThru -Wait -WindowStyle Hidden
    if ($install.ExitCode -ne 0) { throw "Installer failed: $($install.ExitCode)" }
    $registered = (Get-ItemProperty -LiteralPath $key).InstallLocation.TrimEnd('\')
    if ($registered -ne $installDir) { throw "Unexpected registered installation: $registered" }
    . (Join-Path $RepoRoot 'scripts/distribution-common.ps1')
    Assert-AmigaDistribution $installDir
    Assert-AmigaPublishedVersion $installDir $Version
    Write-Output 'OK real setup: isolated current-user installation, version, checksum, no shipped ROM/media'
    $rom = [byte[]]::new(262144); $rom[1] = 8; $rom[5] = 248; $rom[7] = 8
    $userRom = Join-Path $installDir 'user-owned.rom'; [IO.File]::WriteAllBytes($userRom, $rom)
    $userHdf = Join-Path $installDir 'user-owned.hdf'; [IO.File]::WriteAllText($userHdf, 'user hard disk marker')
    $romHash = (Get-FileHash -LiteralPath $userRom).Hash
    $hdfHash = (Get-FileHash -LiteralPath $userHdf).Hash
    $previousProfile = $env:AMIGAEMULATOR_USER_DATA_DIRECTORY
    try {
        $env:AMIGAEMULATOR_USER_DATA_DIRECTORY = Join-Path $testRoot 'installed-profile'
        $check = Start-Process -FilePath (Join-Path $installDir 'AmigaEmulator.exe') -ArgumentList @('--check-roms', "`"$userRom`"") -PassThru -Wait -WindowStyle Hidden
        if ($check.ExitCode -ne 0) { throw "Installed emulator could not validate own ROM: $($check.ExitCode)" }
        $missing = Start-Process -FilePath (Join-Path $installDir 'AmigaEmulator.exe') -ArgumentList @('--check-roms', "`"$(Join-Path $testRoot 'does-not-exist.rom')`"") -PassThru -Wait -WindowStyle Hidden
        if ($missing.ExitCode -ne 1) { throw 'Installed headless ROM diagnostic did not fail cleanly' }
    } finally { $env:AMIGAEMULATOR_USER_DATA_DIRECTORY = $previousProfile }
    Write-Output 'OK installed self-contained emulator runs ROM diagnostics without SDK or ROM download'
    $setupArgs[-1] = "/LOG=`"$(Join-Path $testRoot 'upgrade.log')`""
    $upgrade = Start-Process -FilePath $setup -ArgumentList $setupArgs -PassThru -Wait -WindowStyle Hidden
    if ($upgrade.ExitCode -ne 0 -or (Get-FileHash -LiteralPath $userRom).Hash -ne $romHash -or (Get-FileHash -LiteralPath $userHdf).Hash -ne $hdfHash) { throw 'Update changed user media' }
    Write-Output 'OK real setup update preserves user ROM and HDF, no silent-mode confirmation hang'
    $uninstall = Join-Path $installDir 'unins000.exe'
    if (-not $uninstall.StartsWith($testRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or $registered -ne $installDir) { throw 'Unsafe uninstall target' }
    $remove = Start-Process -FilePath $uninstall -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$(Join-Path $testRoot 'uninstall.log')`"") -PassThru -Wait -WindowStyle Hidden
    if ($remove.ExitCode -ne 0 -or (Test-Path -LiteralPath (Join-Path $installDir 'AmigaEmulator.exe')) -or (Test-Path -LiteralPath $key)) { throw 'Uninstall did not remove application/registration' }
    if ((Get-FileHash -LiteralPath $userRom).Hash -ne $romHash -or (Get-FileHash -LiteralPath $userHdf).Hash -ne $hdfHash) { throw 'Uninstall changed user media' }
    Write-Output 'OK real uninstall removes only installed application and preserves user-owned ROM/HDF'

    Write-Output "Test evidence: $testRoot"
} finally {
    # This directory was created fresh for this test and checked above. Never
    # unregister or uninstall another directory belonging to a real installation.
    $testUninstaller = Join-Path $installDir 'unins000.exe'
    if (Test-Path -LiteralPath $testUninstaller) {
        if (-not $installDir.StartsWith($testRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe failure cleanup target' }
        $cleanup = Start-Process -FilePath $testUninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -Wait -PassThru -WindowStyle Hidden
        if ($cleanup.ExitCode -ne 0) { Write-Warning 'Test cleanup failed; evidence and test installation are retained.' }
    }
}
