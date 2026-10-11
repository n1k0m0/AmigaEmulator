# Copyright 2026 Nils Kopal
# SPDX-License-Identifier: Apache-2.0
param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$RunnerAssembly = '',
    [string]$MediaDirectory = (Join-Path $env:USERPROFILE 'Documents/AmigaEmulator'),
    [string]$RomDirectory = (Join-Path $env:APPDATA 'AmigaEmulator/roms'),
    [string[]]$Models = @('A500', 'A500Plus', 'A600', 'A2000Ocs', 'A2000Ecs', 'A1200', 'A3000', 'A4000Ec030'),
    [string[]]$Games = @(),
    [switch]$ManifestOnly,
    [switch]$IncludeUtilities,
    [switch]$IncludeCompatibilityProfiles,
    [string]$MoonstoneCompatibleBoot = '',
    [string]$PinballCompatibleBoot = '',
    [string]$MegaLoManiaCompatibleBoot = '',
    [ValidateSet('Partyland', 'SpeedDevils', 'BillionDollarGameshow', 'StonesNBones')]
    [string[]]$PinballTables = @('Partyland')
)

$ErrorActionPreference = 'Stop'
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a fresh output directory; existing evidence is never overwritten.' }
if (($MoonstoneCompatibleBoot -or $PinballCompatibleBoot -or $MegaLoManiaCompatibleBoot) -and -not $IncludeCompatibilityProfiles) {
    throw 'Separate compatibility disks require -IncludeCompatibilityProfiles.'
}
if (-not $PinballTables.Count -or @($PinballTables | Sort-Object -Unique).Count -ne $PinballTables.Count) {
    throw 'Select distinct Pinball table names.'
}
if (-not $PinballCompatibleBoot -and (@($PinballTables).Count -ne 1 -or $PinballTables[0] -ne 'Partyland')) {
    throw 'Additional Pinball tables require -PinballCompatibleBoot.'
}
$profiles = @{
    A500 = @('Chip512KSlow512K', 'KICK13.ROM')
    A500Plus = @('Chip2Mb', 'KICK20.ROM')
    A600 = @('Chip2Mb', 'KICK31.ROM')
    A2000Ocs = @('Chip512KSlow512K', 'KICK13.ROM')
    A2000Ecs = @('Chip2Mb', 'KICK31.ROM')
    A1200 = @('Chip2MbFast4Mb', 'KICK31-A1200.ROM')
    A3000 = @('Chip2MbFast4Mb', 'KICK31-A3000.ROM')
    A4000Ec030 = @('Chip2MbFast4Mb', 'KICK31-A4000.ROM')
}
$templates = @('gameplay-scenarios.json', 'freeware-gameplay-scenarios.json', 'ten-new-games-scenarios.json') |
    ForEach-Object { Get-Content -LiteralPath (Join-Path $PSScriptRoot $_) -Raw | ConvertFrom-Json }
if (@($templates.name | Sort-Object -Unique).Count -ne @($templates).Count) { throw 'Duplicate game names in source manifests.' }
$catalog = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'game-media-catalog.json') -Raw | ConvertFrom-Json)
if (@($catalog.name | Sort-Object -Unique).Count -ne $catalog.Count) { throw 'Duplicate catalog names.' }
$catalogMedia = @($catalog | ForEach-Object { $_.media })
if (@($catalogMedia | Sort-Object -Unique).Count -ne $catalogMedia.Count) { throw 'An ADF belongs to more than one catalog entry.' }
foreach ($template in $templates) {
    $entry = @($catalog | Where-Object name -eq $template.name)
    if ($entry.Count -ne 1 -or $template.boot -notin $entry[0].media) { throw "Missing catalog entry/boot disk: $($template.name)" }
}
$variants = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'model-gameplay-variants.json') -Raw | ConvertFrom-Json)
$variantMap = @{}
foreach ($variant in $variants) {
    $template = @($templates | Where-Object name -eq $variant.game)
    if ($template.Count -ne 1) { throw "Unknown variant game: $($variant.game)" }
    if (@($template[0].actions | Where-Object label -eq $variant.beforeLabel).Count -ne 1) {
        throw "Variant insertion label must be unique: $($variant.game)/$($variant.beforeLabel)"
    }
    if (-not $variant.models.Count -or -not $variant.actions.Count) { throw 'Empty gameplay variant.' }
    foreach ($variantModel in $variant.models) {
        if (-not $profiles.ContainsKey($variantModel)) { throw "Unknown variant model: $variantModel" }
        $key = "$variantModel/$($variant.game)"
        if ($variantMap.ContainsKey($key)) { throw "Duplicate gameplay variant: $key" }
        $variantMap[$key] = $variant
    }
}
if ($Games.Count) {
    foreach ($game in $Games) { if ($game -notin $templates.name) { throw "Unknown game: $game" } }
    $templates = @($templates | Where-Object name -in $Games)
}
if (-not $IncludeUtilities) { $templates = @($templates | Where-Object name -notin @($catalog | Where-Object kind -eq 'utility').name) }
if (-not $templates.Count) { throw 'No games selected; utility cases require -IncludeUtilities.' }
if (@($Models | Sort-Object -Unique).Count -ne $Models.Count) { throw 'Duplicate models.' }
# Prioritize known transition/scrolling reports, then cover every remaining title.
$templates = @($templates | Sort-Object @{Expression={if ($_.name -in @('Tinyus','Sqrxz2','Speedball-2')) {0} else {1}}}, name)
$cases = @()
$inventory = @(Get-ChildItem -LiteralPath $MediaDirectory -Recurse -File -Filter '*.adf' | ForEach-Object {
    $file = $_
    $relative = [IO.Path]::GetRelativePath($MediaDirectory, $file.FullName).Replace('\', '/')
    $entry = @($catalog | Where-Object { $relative -in $_.media -or $relative -in $_.archivedMedia })
    $archived = [bool](@($entry | Where-Object { $relative -in $_.archivedMedia }).Count)
    [ordered]@{path=$file.FullName; bytes=$file.Length; sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash;
        title=@($entry.name); kind=@($entry.kind); archivedOriginal=$archived;
        coveredByManifest=(-not $archived -and [bool](@($entry.name | Where-Object { $_ -in $templates.name }).Count))}
})
foreach ($model in $Models) {
    if (-not $profiles.ContainsKey($model)) { throw "Unknown model: $model" }
    $rom = Join-Path $RomDirectory $profiles[$model][1]
    if (-not (Test-Path -LiteralPath $rom -PathType Leaf)) { throw "ROM missing: $rom" }
    foreach ($template in $templates) {
        $disk = Join-Path $MediaDirectory $template.boot
        if (-not (Test-Path -LiteralPath $disk -PathType Leaf)) { throw "Boot disk missing: $disk" }
        foreach ($medium in @($catalog | Where-Object name -eq $template.name).media) {
            $mediumPath = Join-Path $MediaDirectory $medium
            if (-not (Test-Path -LiteralPath $mediumPath -PathType Leaf)) { throw "Data disk missing: $mediumPath" }
        }
        $case = $template | ConvertTo-Json -Depth 30 | ConvertFrom-Json
        $case.name = "$model-$($template.name)"
        $variantKey = "$model/$($template.name)"
        if ($variantMap.ContainsKey($variantKey)) {
            $variant = $variantMap[$variantKey]
            $case.actions = @($case.actions | ForEach-Object {
                if ($_.label -eq $variant.beforeLabel) { $variant.actions }
                $_
            })
        }
        # Retain the source's preferred A500 Chip layout. A2000 OCS has a
        # distinct 512-KiB Chip board and cannot accept the A500 one-MiB layout.
        $case.ram = if ($model -eq 'A500' -and $template.ram -eq 'Chip1Mb') { 'Chip1Mb' } else { $profiles[$model][0] }
        $case | Add-Member model $model -Force
        $case | Add-Member mediaRoot $MediaDirectory -Force
        $case | Add-Member kickstartRom $rom -Force
        $case | Add-Member strict $true -Force
        $case | Add-Member traceExceptions $true -Force
        $case.actions = @($case.actions) + @(
            @{type='checkpoint';label='before-restore'},
            @{type='restore';label='restore'},
            @{type='joystick';button='Right';cycles=1000000;label='post-script-right'},
            @{type='wait';cycles=2000000;label='post-script-continued'}
        )
        $cases += $case
    }
}
if ($IncludeCompatibilityProfiles) {
    $compatibilityProfiles = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'game-compatibility-profiles.json') -Raw | ConvertFrom-Json)
    foreach ($compatibility in $compatibilityProfiles) {
        if (-not $profiles.ContainsKey($compatibility.model)) { throw "Unknown compatibility model: $($compatibility.model)" }
        if ($compatibility.game -notin $catalog.name) { throw "Unknown compatibility game: $($compatibility.game)" }
        if ($compatibility.model -notin $Models -or $compatibility.game -notin $templates.name) { continue }
        $native = @($cases | Where-Object name -eq "$($compatibility.model)-$($compatibility.game)")
        if ($native.Count -ne 1) { throw 'A compatibility profile must extend exactly one native case.' }
        $case = $native[0] | ConvertTo-Json -Depth 30 | ConvertFrom-Json
        $case.name += "-$($compatibility.name)"
        if ($case.name -in $cases.name) { throw "Duplicate compatibility case: $($case.name)" }
        $case.ram = $compatibility.ram
        $case.kickstartRom = Join-Path $RomDirectory $compatibility.rom
        if (-not (Test-Path -LiteralPath $case.kickstartRom -PathType Leaf)) { throw "ROM missing: $($case.kickstartRom)" }
        $cases += $case
    }
}
if ($MoonstoneCompatibleBoot) {
    $compatibleDisk = (Resolve-Path -LiteralPath $MoonstoneCompatibleBoot -ErrorAction Stop).Path
    $patchedHash = (Get-FileHash -LiteralPath $compatibleDisk -Algorithm SHA256).Hash
    if ($patchedHash -ne 'FBA2AA85A63922B4549E0F7D2883E2DF6571A9F3EBAB21448D5DE2767D056665') {
        throw 'Moonstone compatibility disk does not match the reviewed patch; use prepare_moonstone_020_disk.py.'
    }
    $nativeMoonstone = @($cases | Where-Object { $_.name -in @('A1200-Moonstone', 'A3000-Moonstone', 'A4000Ec030-Moonstone') })
    if (-not $nativeMoonstone.Count) { throw 'Select Moonstone and at least one 020/030 model for its compatibility disk.' }
    foreach ($nativeCase in $nativeMoonstone) {
        $case = $nativeCase | ConvertTo-Json -Depth 30 | ConvertFrom-Json
        $case.name += '-illegal-entry-copy'
        $case.boot = $compatibleDisk
        foreach ($action in $case.actions) {
            if ($action.type -eq 'disk' -and $action.disk -eq $nativeCase.boot) {
                $action.disk = $compatibleDisk
            }
        }
        $cases += $case
    }
    # A selected copy inside MediaDirectory already has an unclassified row.
    # Replace it so the same file is not also reported as uncovered.
    $inventory = @($inventory | Where-Object { $_.path -ne $compatibleDisk })
    $inventory += [ordered]@{path=$compatibleDisk; bytes=(Get-Item -LiteralPath $compatibleDisk).Length;
        sha256=$patchedHash; title=@('Moonstone'); kind=@('game-compatibility'); coveredByManifest=$true}
}
if ($MegaLoManiaCompatibleBoot) {
    $compatibleDisk = (Resolve-Path -LiteralPath $MegaLoManiaCompatibleBoot -ErrorAction Stop).Path
    $patchedHash = (Get-FileHash -LiteralPath $compatibleDisk -Algorithm SHA256).Hash
    if ($patchedHash -ne 'C296C827B34A8861489EC096B424956BFA2BE3FAE67EB94E0BC8A0B3CCCA6814') {
        throw 'Mega lo Mania compatibility disk does not match the reviewed patch; use prepare_megalomania_020_disk.py.'
    }
    $nativeMega = @($cases | Where-Object { $_.name -in @('A1200-Mega-lo-Mania', 'A3000-Mega-lo-Mania', 'A4000Ec030-Mega-lo-Mania') })
    if (-not $nativeMega.Count) { throw 'Select Mega-lo-Mania and at least one 020/030 model for its compatibility disk.' }
    foreach ($nativeCase in $nativeMega) {
        $case = $nativeCase | ConvertTo-Json -Depth 30 | ConvertFrom-Json
        $case.name += '-user-ccr-copy'
        $case.boot = $compatibleDisk
        foreach ($action in $case.actions) {
            if ($action.type -eq 'disk' -and $action.disk -eq $nativeCase.boot) { $action.disk = $compatibleDisk }
        }
        $cases += $case
    }
    $inventory = @($inventory | Where-Object { $_.path -ne $compatibleDisk })
    $inventory += [ordered]@{path=$compatibleDisk; bytes=(Get-Item -LiteralPath $compatibleDisk).Length;
        sha256=$patchedHash; title=@('Mega-lo-Mania'); kind=@('game-compatibility'); coveredByManifest=$true}
}
if ($PinballCompatibleBoot) {
    $compatibleDisk = (Resolve-Path -LiteralPath $PinballCompatibleBoot -ErrorAction Stop).Path
    $patchedHash = (Get-FileHash -LiteralPath $compatibleDisk -Algorithm SHA256).Hash
    if ($patchedHash -notin @('F4E0EACDD4908EFC71DBD26625388C503134529B0ADC5D6BE86319E6CBCCBA28',
        '74307ADC5F44748E078AB7782628DE0482C1E45DDA1781E5A7327A53779E23B2')) {
        throw 'Pinball compatibility disk does not match the reviewed patch; use prepare_pinball_32bit_disk.py.'
    }
    $nativePinball = @($cases | Where-Object { $_.name -in @('A3000-PDREMS21', 'A4000Ec030-PDREMS21') })
    if (-not $nativePinball.Count) { throw 'Select PDREMS21 and at least one native desktop model for its compatibility disk.' }
    foreach ($nativeCase in $nativePinball) {
        foreach ($table in $PinballTables) {
            $case = $nativeCase | ConvertTo-Json -Depth 30 | ConvertFrom-Json
            $case.name += "-32bit-loader-copy-$table"
            $case.boot = $compatibleDisk
            $courseDisk = if ($table -in @('BillionDollarGameshow', 'StonesNBones')) { 3 } else { 2 }
            $tableKey = if ($table -in @('SpeedDevils', 'StonesNBones')) { 81 } else { 80 }
            $selection = @($case.actions | Where-Object label -eq 'pinball-14-partyland-f1')
            $course = @($case.actions | Where-Object label -eq 'pinball-11-course-disk2')
            if ($selection.Count -ne 1 -or $course.Count -ne 1) { throw 'Pinball table/course actions must be unique.' }
            $selection[0].key = $tableKey
            $selection[0].label = "pinball-14-select-$table"
            $course[0].disk = "Pinball Fantasies (disk $courseDisk).adf"
            $course[0].label = "pinball-11-course-disk$courseDisk"
            foreach ($action in $case.actions) {
                if ($action.type -eq 'disk' -and $action.disk -eq $nativeCase.boot) {
                    $action.disk = $compatibleDisk
                }
            }
            $cases += $case
        }
    }
    # Keep one classified row when the explicitly selected copy lives under
    # MediaDirectory; otherwise its earlier row would still warn as uncovered.
    $inventory = @($inventory | Where-Object { $_.path -ne $compatibleDisk })
    $inventory += [ordered]@{path=$compatibleDisk; bytes=(Get-Item -LiteralPath $compatibleDisk).Length;
        sha256=$patchedHash; title=@('PDREMS21'); kind=@('game-compatibility'); coveredByManifest=$true}
}
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$manifest = Join-Path $OutputDirectory 'scenarios.json'
ConvertTo-Json -InputObject @($cases) -Depth 30 | Set-Content -LiteralPath $manifest -Encoding utf8
ConvertTo-Json -InputObject @($inventory) -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'media-inventory.json') -Encoding utf8
$uncovered = @($inventory | Where-Object { -not $_.coveredByManifest -and $_.kind -notcontains 'utility' })
if ($uncovered.Count) { Write-Warning "$($uncovered.Count) ADF files are not covered by this selection; inspect media-inventory.json." }
Write-Output "Generated $($cases.Count) cases for $(@($templates).Count) titles and $($Models.Count) models. Captures must be reviewed for actual gameplay."
if (-not $ManifestOnly) {
    if ([string]::IsNullOrWhiteSpace($RunnerAssembly)) {
        $buildDirectory = Join-Path $OutputDirectory 'runner'
        & dotnet build (Join-Path $PSScriptRoot 'GameRunner/GameRunner.csproj') -c Release -p:Platform=x64 -o $buildDirectory --nologo -v q
        if ($LASTEXITCODE -ne 0) { throw 'GameRunner build failed.' }
        $RunnerAssembly = Join-Path $buildDirectory 'GameRunner.dll'
    }
    & dotnet $RunnerAssembly $manifest $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'A scenario failed; inspect result.json, exception/step logs and captures.' }
}
