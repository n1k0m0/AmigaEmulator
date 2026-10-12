# Copyright 2026 Nils Kopal
# SPDX-License-Identifier: Apache-2.0
param(
    [Parameter(Mandatory = $true)][string]$SourceHardDisk,
    [Parameter(Mandatory = $true)][string]$KickstartRom,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [ValidateSet('A500','A500Plus','A600','A2000Ocs','A2000Ecs','A1200','A3000','A4000')]
    [string[]]$Models = @('A500','A500Plus','A600','A2000Ocs','A2000Ecs'),
    [string]$RunnerAssembly = '',
    [switch]$ManifestOnly
)

# Uses a matching unsplit 3.1.4 board ROM and an already installed OS disk.
# All guest writes go to exclusive new copies; execution is not visual acceptance.
$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a fresh output directory.' }
$sourceDisk = (Get-Item -LiteralPath $SourceHardDisk).FullName
$sourceRom = (Get-Item -LiteralPath $KickstartRom).FullName
$romHash = (Get-FileHash -LiteralPath $sourceRom -Algorithm SHA1).Hash
if (-not $Models.Count -or @($Models | Select-Object -Unique).Count -ne $Models.Count) { throw 'Choose distinct models.' }
$matchingRom = @{
    A500='D81CD6F131040895843D9DFA95A45BC95EDD9704'
    A500Plus='D81CD6F131040895843D9DFA95A45BC95EDD9704'
    A600='D81CD6F131040895843D9DFA95A45BC95EDD9704'
    A2000Ocs='D81CD6F131040895843D9DFA95A45BC95EDD9704'
    A2000Ecs='D81CD6F131040895843D9DFA95A45BC95EDD9704'
    A1200='6355A9ED5DC840422F9B73308A91BE0D0BB506BD'
    A3000='1A34B25A25E260E38E879172850143C80B461A64'
    A4000='BAD0AE388442DB02EAEB6A5363EF43EB4E308AE6'
}
foreach ($model in $Models) {
    if ($romHash -ne $matchingRom[$model]) {
        throw "$model requires its matching unsplit Kickstart 46.143 ROM. Select one ROM family per invocation."
    }
}

$keys = @{}
for ($i=0; $i -lt 10; $i++) { $keys['1234567890'.Substring($i,1)] = $i+1 }
foreach ($row in @(@(0x10,'qwertyuiop'),@(0x20,'asdfghjkl'),@(0x31,'zxcvbnm'))) {
    for ($i=0; $i -lt $row[1].Length; $i++) { $keys[$row[1].Substring($i,1)] = $row[0]+$i }
}
$keys[' ']=0x40; $keys['-']=0x0b; $keys['.']=0x39; $keys['/']=0x3a; $keys[':']=0x29; $keys['>']=0x39
$actions = [System.Collections.Generic.List[object]]::new()
function Add-Wait([string]$label, [int]$cycles=8000000) {
    $actions.Add([ordered]@{ type='wait'; cycles=$cycles; label=$label })
}
function Add-Text([string]$value, [string]$label) {
    for ($i=0; $i -lt $value.Length; $i++) {
        $char=$value.Substring($i,1)
        if (-not $keys.ContainsKey($char)) { throw "Unsupported typed character: $char" }
        $action=[ordered]@{ type='key'; key=$keys[$char]; cycles=120000; label=('{0}-{1:d2}' -f $label,$i) }
        if ($char -in @(':','>')) { $action.modifiers=@(0x60) }
        $actions.Add($action)
    }
    $actions.Add([ordered]@{ type='key'; key=0x44; cycles=120000; label="$label-enter" })
    Add-Wait "$label-result"
}
function Add-Shell([string]$phase) {
    $actions.Add([ordered]@{ type='key'; key=0x12; modifiers=@(0x67); label="$phase-execute-command" })
    # The 68000 requester can discard text before it has acquired focus.
    Add-Wait "$phase-dialog-ready" 12000000
    Add-Text 'newshell' "$phase-new-shell"
    Add-Wait "$phase-shell-ready" 12000000
}

Add-Wait 'cold-desktop' 100000000
Add-Shell 'cold'
Add-Text 'version full' 'version'
Add-Text 'echo legacyostest >sys:legacy-os-test.txt' 'write-file'
Add-Text 'type sys:legacy-os-test.txt' 'read-file'
Add-Text 'sys:tools/showconfig' 'showconfig'
Add-Text 'run sys:utilities/clock' 'clock'
$actions.Add([ordered]@{ type='restore'; resetBeforeRestore=$true; cold=$true; label='power-cycle-restore' })
foreach ($cold in @($false,$true)) {
    $phase = if ($cold) { 'cold-again' } else { 'warm' }
    $actions.Add([ordered]@{ type='reset'; cold=$cold; label="$phase-reset" })
    Add-Wait "$phase-desktop" 100000000
    Add-Shell $phase
    # Capture both commands separately so persistence is reviewed on the first read.
    Add-Text 'type sys:legacy-os-test.txt' "$phase-first-read"
    Add-Text 'type sys:legacy-os-test.txt' "$phase-second-read"
}
$actions.Add([ordered]@{ type='restore'; resetBeforeRestore=$true; cold=$true; label='cold-persistence-restore' })

$root=(New-Item -ItemType Directory -Path $OutputDirectory).FullName
$sourceDiskHash=(Get-FileHash -LiteralPath $sourceDisk -Algorithm SHA256).Hash
$cases=@()
foreach ($model in $Models) {
    $testDisk=Join-Path $root "$model-test.hdf"
    [System.IO.File]::Copy($sourceDisk,$testDisk,$false)
    if ((Get-FileHash -LiteralPath $testDisk -Algorithm SHA256).Hash -ne $sourceDiskHash) {
        throw 'Source disk changed while copying; no guest has been started.'
    }
    $ram = switch ($model) {
        'A2000Ocs' { 'Chip512KSlow512KFast8Mb' }
        'A1200' { 'Chip2MbFast4Mb' }
        default { 'Chip2MbFast8Mb' }
    }
    $cases += [ordered]@{ name="$model-native314-OS"; model=$model; ram=$ram; kickstartRom=$sourceRom;
        hardDisk=$testDisk; hardDiskReadOnly=$false; strict=$true; traceExceptions=$true; maximumNonRomExceptions=0; actions=@($actions.ToArray()) }
}
$manifest=Join-Path $root 'scenarios.json'
ConvertTo-Json -InputObject @($cases) -Depth 20 | Set-Content -LiteralPath $manifest -Encoding utf8
[ordered]@{ sourceDisk=$sourceDisk; sha256=$sourceDiskHash;
    sourceRom=$sourceRom; romSha1=$romHash; review='Screenshots and independent file readback required; execution is not acceptance.' } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'provenance.json') -Encoding utf8
if ($ManifestOnly) { Write-Output "Generated $($cases.Count) private test disk copies and cases in $root"; return }
if ([string]::IsNullOrWhiteSpace($RunnerAssembly)) {
    $build=Join-Path $root 'runner'
    & dotnet build (Join-Path $PSScriptRoot 'GameRunner/GameRunner.csproj') -c Release -p:Platform=x64 -o $build --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'GameRunner build failed.' }
    $RunnerAssembly=Join-Path $build 'GameRunner.dll'
}
& dotnet $RunnerAssembly $manifest --validate
if ($LASTEXITCODE -ne 0) { throw 'OS preflight failed.' }
& dotnet $RunnerAssembly $manifest $root
if ($LASTEXITCODE -ne 0) { throw 'OS script failed; inspect output.' }
if ((Get-FileHash -LiteralPath $sourceDisk -Algorithm SHA256).Hash -ne (Get-Content -LiteralPath (Join-Path $root 'provenance.json') -Raw | ConvertFrom-Json).sha256) {
    throw 'Source disk changed during validation.'
}
Write-Output 'Execution completed; review desktop/applications, both first/second file reads and independently verify test HDF contents.'
