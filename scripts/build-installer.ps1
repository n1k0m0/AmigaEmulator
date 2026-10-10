# Copyright 2026 Nils Kopal
# SPDX-License-Identifier: Apache-2.0
param([string]$Configuration = 'Release', [string]$Version = '', [switch]$SkipPublish)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'distribution-common.ps1')
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$Version = Get-AmigaVersion $repoRoot $Version
$candidates = @()
foreach ($base in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
    if ($base) { $candidates += Join-Path $base 'Inno Setup 6/ISCC.exe' }
}
if ($env:LOCALAPPDATA) { $candidates += Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe' }
$command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if ($command) { $candidates += $command.Source }
$iscc = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 missing. Install with: winget install --id JRSoftware.InnoSetup -e' }
$publishDir = Join-Path $repoRoot 'artifacts/publish/win-x64'
$outputDir = Join-Path $repoRoot 'artifacts/installer'
if (-not $SkipPublish) { & (Join-Path $PSScriptRoot 'publish-win-x64.ps1') -Configuration $Configuration -Version $Version }
Assert-AmigaDistribution $publishDir
Assert-AmigaPublishedVersion $publishDir $Version
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
& $iscc "/DAppVersion=$Version" "/DAppVersionInfo=$Version.0" "/DSourceDir=$publishDir" "/DOutputDir=$outputDir" (Join-Path $repoRoot 'installer/AmigaEmulator.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed: $LASTEXITCODE" }
$setupPath = Join-Path $outputDir "AmigaEmulator-$Version-win-x64-setup.exe"
$hash = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$setupPath.sha256", "$hash  $([IO.Path]::GetFileName($setupPath))`n", [Text.UTF8Encoding]::new($false))
Write-Host "Installer: $setupPath"
