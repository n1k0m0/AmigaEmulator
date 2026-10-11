#
# Copyright 2026 Nils Kopal <Nils.Kopal<at>kopaldev.de
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#     http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.
#
param(
    [string]$AdfDirectory = "",
    [int]$Cycles = 120000000,
    [string]$OutputDirectory = "",
    [string]$SummaryCsv = "",
    [int]$Limit = 0,
    [string]$Model = "",
    [int]$ExtendedRetryCycles = 420000000,
    [int]$ExtendedRetryPriority = 1,
    [int]$InteractiveRetryCycles = 420000000,
    [int]$InteractiveRetryPriority = 2,
    [int]$InteractiveRetryInputCycle = 180000000,
    [int]$InteractiveRetryInputHoldCycles = 400000,
    [string]$InteractiveRetryInputProfile = "fire",
    [switch]$NoExtendedRetry,
    [switch]$NoInteractiveRetry,
    [switch]$NoSummary
)

$ErrorActionPreference = "Stop"

$toolsDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionDirectory = Resolve-Path (Join-Path $toolsDirectory "..")
$solutionPath = Join-Path $solutionDirectory "AmigaEmulator.sln"
$emulatorAssembly = Join-Path $solutionDirectory "AmigaEmulator\bin\x64\Release\AmigaEmulator.dll"

if ([string]::IsNullOrWhiteSpace($AdfDirectory)) {
    $AdfDirectory = Join-Path ([Environment]::GetFolderPath("MyDocuments")) "AmigaEmulator"
}

if (-not [string]::IsNullOrWhiteSpace($SummaryCsv) -and [string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Split-Path -Parent (Resolve-Path -LiteralPath $SummaryCsv)
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $OutputDirectory = Join-Path (Join-Path $solutionDirectory "..\tmp") ("game_matrix_" + $stamp)
}

if (-not (Test-Path $AdfDirectory)) {
    New-Item -ItemType Directory -Force -Path $AdfDirectory | Out-Null
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$AdfDirectory = (Resolve-Path -LiteralPath $AdfDirectory).Path
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path

function Get-MatrixScore {
    param([object]$Row)

    $score = 0
    switch ($Row.image_activity) {
        "visible" { $score += 400; break }
        "mono" { $score += 200; break }
        "tiny" { $score += 100; break }
        "blank" { $score += 0; break }
        default { $score += 50; break }
    }

    switch ($Row.category) {
        "bootcode-visible" { $score += 120; break }
        "kickstart-visible" { $score += 80; break }
        "kickstart-disk-wait" { $score += 30; break }
        "bootcode-corrupt-video" { $score -= 80; break }
        "bootcode-no-video" { $score -= 120; break }
        default { $score += 0; break }
    }

    if ($Row.glitch_flags -and $Row.glitch_flags -ne "none") {
        $score -= 40
        if ($Row.glitch_flags.Contains("blank")) { $score -= 100 }
        if ($Row.glitch_flags.Contains("tiny-output")) { $score -= 80 }
        if ($Row.glitch_flags.Contains("low-entropy")) { $score -= 30 }
        if ($Row.glitch_flags.Contains("edge-noise")) { $score -= 20 }
    }

    $unique = 0
    [void][int]::TryParse($Row.unique_colors, [ref]$unique)
    $score += [Math]::Min(40, $unique)
    return $score
}

function Get-MatrixPriority {
    param([string]$AdfName)

    $priority1 = @(
        "Zak McKracken",
        "Fire and Ice (disk 2)",
        "Civilization",
        "Desert Strike",
        "LOTUS",
        "Maniac Mansion",
        "Oh No! More Lemmings"
    )
    $priority2 = @(
        "Cannon Fodder",
        "Moonstone",
        "North & South",
        "Turrican 3",
        "Turrican III",
        "Speedball 2"
    )

    foreach ($name in $priority1) {
        if ($AdfName.IndexOf($name, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return 1
        }
    }

    foreach ($name in $priority2) {
        if ($AdfName.IndexOf($name, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return 2
        }
    }

    return 3
}

function Get-MatrixInputProfile {
    param([object]$Row)

    if ($Row.PSObject.Properties.Name -contains "input_profile" -and
        -not [string]::IsNullOrWhiteSpace($Row.input_profile)) {
        return $Row.input_profile
    }

    return "none"
}

function Test-MatrixSecondaryDiskNoStandalone {
    param([object]$Row)

    if ($Row.image_activity -ne "blank") {
        return $false
    }

    if ($Row.category -ne "bootcode-no-video" -and $Row.category -ne "kickstart-disk-wait") {
        return $false
    }

    if ($Row.adf -match "(?i)\(Disk\s+([2-9])\s+of\s+([2-9])\)" -or
        $Row.adf -match "(?i)Disk\s+([2-9])\s+of\s+([2-9])") {
        return $true
    }

    return $false
}

function Test-MatrixCriticalGlitch {
    param([string]$Flags)

    if ([string]::IsNullOrWhiteSpace($Flags) -or $Flags -eq "none") {
        return $false
    }

    foreach ($flag in @("blank", "tiny-output", "low-entropy", "dominant-fill")) {
        if ($Flags.IndexOf($flag, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $true
        }
    }

    return $false
}

function Get-MatrixStatus {
    param([object]$Row)

    if (Test-MatrixSecondaryDiskNoStandalone $Row) {
        return "secondary-disk-no-standalone-video"
    }

    if ($Row.image_activity -ne "visible") {
        return "needs-attention"
    }

    if ($Row.category -eq "bootcode-no-video" -or
        $Row.category -eq "bootcode-corrupt-video" -or
        $Row.category -eq "kickstart-disk-wait") {
        return "needs-attention"
    }

    if (Test-MatrixCriticalGlitch $Row.glitch_flags) {
        return "needs-attention"
    }

    if ($Row.glitch_flags -and $Row.glitch_flags -ne "none") {
        return "visual-warning"
    }

    if ((Get-MatrixInputProfile $Row) -ne "none") {
        return "input-assisted-visible"
    }

    return "visible-unverified"
}

function Test-MatrixRowNeedsAttention {
    param([object]$Row)

    return (Get-MatrixStatus $Row) -eq "needs-attention"
}

function Get-MatrixBestRows {
    param([string]$CsvPath)

    if (-not (Test-Path $CsvPath)) {
        Write-Warning "Matrix CSV not found for summary: $CsvPath"
        return @()
    }

    $rows = @(Import-Csv -Path $CsvPath)
    if ($rows.Count -eq 0) {
        Write-Warning "Matrix CSV contains no rows: $CsvPath"
        return @()
    }

    return @(foreach ($group in ($rows | Group-Object -Property adf)) {
        $best = $group.Group | Sort-Object @{ Expression = { Get-MatrixScore $_ }; Descending = $true }, model | Select-Object -First 1
        [pscustomobject]@{
            adf = $best.adf
            priority = Get-MatrixPriority $best.adf
            status = Get-MatrixStatus $best
            gameplay = "requires-visual-review"
            image_activity = $best.image_activity
            category = $best.category
            model = $best.model
            cycles = $best.cycles
            input_profile = Get-MatrixInputProfile $best
            glitch_flags = $best.glitch_flags
            pc = $best.pc
            frame_hash = $best.frame_hash
            score = Get-MatrixScore $best
            screenshot = $best.screenshot
        }
    })
}

function Export-MatrixCsvRows {
    param([object[]]$Rows, [string]$Path)

    $columns = @(
        "adf",
        "path",
        "model",
        "cycles",
        "input_profile",
        "input_cycle",
        "input_hold_cycles",
        "frame",
        "pc",
        "opcode",
        "sr",
        "category",
        "image_activity",
        "unique_colors",
        "non_background_pixels",
        "frame_hash",
        "dominant_color",
        "dominant_percent",
        "edge_non_background_pixels",
        "edge_non_background_percent",
        "glitch_flags",
        "df0",
        "paula_disk",
        "paula_audio",
        "dmacon",
        "intena",
        "intreq",
        "bplcon0",
        "bplcon1",
        "cop1lc",
        "bpl1pt",
        "bpl2pt",
        "screenshot"
    )

    $Rows | Select-Object $columns | Export-Csv -Path $Path -NoTypeInformation -Encoding UTF8
}

function Write-MatrixSummary {
    param([string]$CsvPath, [string]$Directory)

    $bestRows = @(Get-MatrixBestRows -CsvPath $CsvPath)
    if ($bestRows.Count -eq 0) {
        return
    }

    $bestPath = Join-Path $Directory "game_matrix_best_by_adf.csv"
    $failPath = Join-Path $Directory "game_matrix_priority_failures.csv"
    $bestRows |
        Sort-Object priority, adf |
        Export-Csv -Path $bestPath -NoTypeInformation -Encoding UTF8

    $bestRows |
        Where-Object { Test-MatrixRowNeedsAttention $_ } |
        Sort-Object priority, score, adf |
        Export-Csv -Path $failPath -NoTypeInformation -Encoding UTF8

    $failCount = @($bestRows | Where-Object { Test-MatrixRowNeedsAttention $_ }).Count
    Write-Host "Matrix best-by-ADF summary: $bestPath"
    Write-Host "Matrix priority failures: $failPath ($failCount rows)"
}

function Test-MatrixRowNeedsExtendedRetry {
    param([object]$Row, [int]$PriorityLimit)

    $priority = Get-MatrixPriority $Row.adf
    if ($priority -gt $PriorityLimit) {
        return $false
    }

    if (Test-MatrixSecondaryDiskNoStandalone $Row) {
        return $false
    }

    if ($Row.image_activity -eq "blank" -or $Row.image_activity -eq "tiny" -or $Row.image_activity -eq "mono") {
        return $true
    }

    if ($Row.category -eq "bootcode-no-video" -or $Row.category -eq "kickstart-disk-wait") {
        return $true
    }

    return $false
}

function Test-MatrixRowNeedsInteractiveRetry {
    param([object]$Row, [int]$PriorityLimit)

    $priority = Get-MatrixPriority $Row.adf
    if ($priority -gt $PriorityLimit) {
        return $false
    }

    if ((Get-MatrixInputProfile $Row) -ne "none") {
        return $false
    }

    if (Test-MatrixSecondaryDiskNoStandalone $Row) {
        return $false
    }

    if ($Row.image_activity -eq "visible" -and
        $Row.category -eq "bootcode-visible" -and
        $Row.glitch_flags -and
        $Row.glitch_flags.IndexOf("dominant-fill", [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
        return $true
    }

    return $false
}

function Resolve-MatrixAdfPath {
    param([string]$AdfDirectory, [string]$AdfName)

    $direct = Join-Path $AdfDirectory $AdfName
    if (Test-Path -LiteralPath $direct) {
        return (Resolve-Path -LiteralPath $direct).Path
    }

    $match = Get-ChildItem -LiteralPath $AdfDirectory -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { [string]::Equals($_.Name, $AdfName, [System.StringComparison]::OrdinalIgnoreCase) } |
        Select-Object -First 1
    if ($match) {
        return $match.FullName
    }

    return ""
}

function Get-MatrixModelArgument {
    param([string]$ModelName)

    if ($ModelName -match "512K SLOW") {
        return "slow512"
    }

    if ($ModelName -match "1MB CHIP") {
        return "chip1mb"
    }

    if ($ModelName -match "2MB CHIP") {
        return "2mbchip"
    }

    return "a500"
}

function Invoke-MatrixExtendedRetries {
    param(
        [string]$CsvPath,
        [string]$AdfDirectory,
        [string]$OutputDirectory,
        [string]$EmulatorAssembly,
        [int]$RetryCycles,
        [int]$PriorityLimit
    )

    $bestRows = @(Get-MatrixBestRows -CsvPath $CsvPath)
    $retryCandidates = @($bestRows | Where-Object { Test-MatrixRowNeedsExtendedRetry $_ $PriorityLimit })
    if ($retryCandidates.Count -eq 0) {
        Write-Host "Matrix extended retry: no candidates"
        return $CsvPath
    }

    $retryRoot = Join-Path $OutputDirectory "extended_retries"
    New-Item -ItemType Directory -Force -Path $retryRoot | Out-Null
    $retryRows = @()
    $index = 0

    foreach ($candidate in $retryCandidates) {
        $index++
        $sourceAdf = Resolve-MatrixAdfPath -AdfDirectory $AdfDirectory -AdfName $candidate.adf
        if ([string]::IsNullOrWhiteSpace($sourceAdf)) {
            Write-Warning "Matrix extended retry skipped; ADF not found: $($candidate.adf)"
            continue
        }

        $retryInput = Join-Path $retryRoot ("input_" + $index.ToString("000", [System.Globalization.CultureInfo]::InvariantCulture))
        $retryOutput = Join-Path $retryRoot ("output_" + $index.ToString("000", [System.Globalization.CultureInfo]::InvariantCulture))
        New-Item -ItemType Directory -Force -Path $retryInput | Out-Null
        New-Item -ItemType Directory -Force -Path $retryOutput | Out-Null
        Copy-Item -LiteralPath $sourceAdf -Destination (Join-Path $retryInput ([System.IO.Path]::GetFileName($sourceAdf))) -Force

        $modelArgument = Get-MatrixModelArgument $candidate.model
        $retryArguments = @(
            $EmulatorAssembly,
            "--adf-test-matrix",
            $retryInput,
            $RetryCycles.ToString([System.Globalization.CultureInfo]::InvariantCulture),
            $retryOutput,
            "--model",
            $modelArgument
        )

        Write-Host "Matrix extended retry: $($candidate.adf) model=$modelArgument cycles=$RetryCycles"
        $retryLogPath = Join-Path $retryOutput "game_matrix_retry.log"
        $retryOutputLines = @(& dotnet @retryArguments 2>&1)
        $retryExitCode = $LASTEXITCODE
        $retryOutputLines | Set-Content -Path $retryLogPath -Encoding UTF8
        $retryOutputLines | ForEach-Object { Write-Host $_ }
        if ($retryExitCode -ne 0) {
            Write-Warning "Matrix extended retry failed for $($candidate.adf) with exit code $retryExitCode"
            continue
        }

        $retryCsv = Join-Path $retryOutput "adf_matrix.csv"
        if (Test-Path -LiteralPath $retryCsv) {
            $retryRows += @(Import-Csv -LiteralPath $retryCsv)
        }
    }

    if ($retryRows.Count -eq 0) {
        Write-Host "Matrix extended retry: no retry rows written"
        return $CsvPath
    }

    $retryPath = Join-Path $OutputDirectory "adf_matrix_extended_retries.csv"
    $combinedPath = Join-Path $OutputDirectory "adf_matrix_with_retries.csv"
    $originalRows = @(Import-Csv -LiteralPath $CsvPath)

    $retryRows | Export-Csv -Path $retryPath -NoTypeInformation -Encoding UTF8
    Export-MatrixCsvRows -Rows @($originalRows + $retryRows) -Path $combinedPath

    Write-Host "Matrix extended retry rows: $retryPath ($($retryRows.Count) rows)"
    Write-Host "Matrix combined CSV: $combinedPath"
    return $combinedPath
}

function Invoke-MatrixInteractiveRetries {
    param(
        [string]$CsvPath,
        [string]$AdfDirectory,
        [string]$OutputDirectory,
        [string]$EmulatorAssembly,
        [int]$RetryCycles,
        [int]$PriorityLimit,
        [string]$InputProfile,
        [int]$InputCycle,
        [int]$InputHoldCycles
    )

    $bestRows = @(Get-MatrixBestRows -CsvPath $CsvPath)
    $retryCandidates = @($bestRows | Where-Object { Test-MatrixRowNeedsInteractiveRetry $_ $PriorityLimit })
    if ($retryCandidates.Count -eq 0) {
        Write-Host "Matrix interactive retry: no candidates"
        return $CsvPath
    }

    $retryRoot = Join-Path $OutputDirectory "interactive_retries"
    New-Item -ItemType Directory -Force -Path $retryRoot | Out-Null
    $retryRows = @()
    $index = 0

    foreach ($candidate in $retryCandidates) {
        $index++
        $sourceAdf = Resolve-MatrixAdfPath -AdfDirectory $AdfDirectory -AdfName $candidate.adf
        if ([string]::IsNullOrWhiteSpace($sourceAdf)) {
            Write-Warning "Matrix interactive retry skipped; ADF not found: $($candidate.adf)"
            continue
        }

        $retryInput = Join-Path $retryRoot ("input_" + $index.ToString("000", [System.Globalization.CultureInfo]::InvariantCulture))
        $retryOutput = Join-Path $retryRoot ("output_" + $index.ToString("000", [System.Globalization.CultureInfo]::InvariantCulture))
        New-Item -ItemType Directory -Force -Path $retryInput | Out-Null
        New-Item -ItemType Directory -Force -Path $retryOutput | Out-Null
        Copy-Item -LiteralPath $sourceAdf -Destination (Join-Path $retryInput ([System.IO.Path]::GetFileName($sourceAdf))) -Force

        $modelArgument = Get-MatrixModelArgument $candidate.model
        $retryArguments = @(
            $EmulatorAssembly,
            "--adf-test-matrix",
            $retryInput,
            $RetryCycles.ToString([System.Globalization.CultureInfo]::InvariantCulture),
            $retryOutput,
            "--model",
            $modelArgument,
            "--input-profile",
            $InputProfile,
            "--input-cycle",
            $InputCycle.ToString([System.Globalization.CultureInfo]::InvariantCulture),
            "--input-hold-cycles",
            $InputHoldCycles.ToString([System.Globalization.CultureInfo]::InvariantCulture)
        )

        Write-Host "Matrix interactive retry: $($candidate.adf) model=$modelArgument profile=$InputProfile cycle=$InputCycle hold=$InputHoldCycles total=$RetryCycles"
        $retryLogPath = Join-Path $retryOutput "game_matrix_interactive_retry.log"
        $retryOutputLines = @(& dotnet @retryArguments 2>&1)
        $retryExitCode = $LASTEXITCODE
        $retryOutputLines | Set-Content -Path $retryLogPath -Encoding UTF8
        $retryOutputLines | ForEach-Object { Write-Host $_ }
        if ($retryExitCode -ne 0) {
            Write-Warning "Matrix interactive retry failed for $($candidate.adf) with exit code $retryExitCode"
            continue
        }

        $retryCsv = Join-Path $retryOutput "adf_matrix.csv"
        if (Test-Path -LiteralPath $retryCsv) {
            $retryRows += @(Import-Csv -LiteralPath $retryCsv)
        }
    }

    if ($retryRows.Count -eq 0) {
        Write-Host "Matrix interactive retry: no retry rows written"
        return $CsvPath
    }

    $retryPath = Join-Path $OutputDirectory "adf_matrix_interactive_retries.csv"
    $combinedPath = Join-Path $OutputDirectory "adf_matrix_with_interactive_retries.csv"
    $originalRows = @(Import-Csv -LiteralPath $CsvPath)

    $retryRows | Export-Csv -Path $retryPath -NoTypeInformation -Encoding UTF8
    Export-MatrixCsvRows -Rows @($originalRows + $retryRows) -Path $combinedPath

    Write-Host "Matrix interactive retry rows: $retryPath ($($retryRows.Count) rows)"
    Write-Host "Matrix combined interactive CSV: $combinedPath"
    return $combinedPath
}

if (-not [string]::IsNullOrWhiteSpace($SummaryCsv)) {
    $summaryCsvPath = (Resolve-Path -LiteralPath $SummaryCsv).Path
    if (-not $NoExtendedRetry -and
        [string]::IsNullOrWhiteSpace($Model) -and
        $ExtendedRetryCycles -gt $Cycles -and
        $ExtendedRetryPriority -gt 0) {
        $summaryCsvPath = Invoke-MatrixExtendedRetries `
            -CsvPath $summaryCsvPath `
            -AdfDirectory $AdfDirectory `
            -OutputDirectory $OutputDirectory `
            -EmulatorAssembly $emulatorAssembly `
            -RetryCycles $ExtendedRetryCycles `
            -PriorityLimit $ExtendedRetryPriority
    }

    if (-not $NoInteractiveRetry -and
        [string]::IsNullOrWhiteSpace($Model) -and
        $InteractiveRetryCycles -gt 0 -and
        $InteractiveRetryPriority -gt 0) {
        $summaryCsvPath = Invoke-MatrixInteractiveRetries `
            -CsvPath $summaryCsvPath `
            -AdfDirectory $AdfDirectory `
            -OutputDirectory $OutputDirectory `
            -EmulatorAssembly $emulatorAssembly `
            -RetryCycles $InteractiveRetryCycles `
            -PriorityLimit $InteractiveRetryPriority `
            -InputProfile $InteractiveRetryInputProfile `
            -InputCycle $InteractiveRetryInputCycle `
            -InputHoldCycles $InteractiveRetryInputHoldCycles
    }

    Write-MatrixSummary -CsvPath $summaryCsvPath -Directory $OutputDirectory
    exit 0
}

dotnet build $solutionPath -c Release -p:Platform=x64
if ($LASTEXITCODE -ne 0) {
    throw "Release build failed with exit code $LASTEXITCODE."
}

$arguments = @(
    $emulatorAssembly,
    "--adf-test-matrix",
    $AdfDirectory,
    $Cycles.ToString([System.Globalization.CultureInfo]::InvariantCulture),
    $OutputDirectory
)

if ($Limit -gt 0) {
    $arguments += @("--limit", $Limit.ToString([System.Globalization.CultureInfo]::InvariantCulture))
}

if (-not [string]::IsNullOrWhiteSpace($Model)) {
    $arguments += @("--model", $Model)
}

$logPath = Join-Path $OutputDirectory "game_matrix.log"
Write-Host "Running ADF game matrix. CSV: $(Join-Path $OutputDirectory 'adf_matrix.csv')"
& dotnet @arguments 2>&1 | Tee-Object -FilePath $logPath
$exitCode = $LASTEXITCODE

if ($exitCode -eq 0 -and -not $NoSummary) {
    $summaryCsvPath = Join-Path $OutputDirectory "adf_matrix.csv"
    if (-not $NoExtendedRetry -and
        $Limit -eq 0 -and
        [string]::IsNullOrWhiteSpace($Model) -and
        $ExtendedRetryCycles -gt $Cycles -and
        $ExtendedRetryPriority -gt 0) {
        $summaryCsvPath = Invoke-MatrixExtendedRetries `
            -CsvPath $summaryCsvPath `
            -AdfDirectory $AdfDirectory `
            -OutputDirectory $OutputDirectory `
            -EmulatorAssembly $emulatorAssembly `
            -RetryCycles $ExtendedRetryCycles `
            -PriorityLimit $ExtendedRetryPriority
    }

    if (-not $NoInteractiveRetry -and
        $Limit -eq 0 -and
        [string]::IsNullOrWhiteSpace($Model) -and
        $InteractiveRetryCycles -gt 0 -and
        $InteractiveRetryPriority -gt 0) {
        $summaryCsvPath = Invoke-MatrixInteractiveRetries `
            -CsvPath $summaryCsvPath `
            -AdfDirectory $AdfDirectory `
            -OutputDirectory $OutputDirectory `
            -EmulatorAssembly $emulatorAssembly `
            -RetryCycles $InteractiveRetryCycles `
            -PriorityLimit $InteractiveRetryPriority `
            -InputProfile $InteractiveRetryInputProfile `
            -InputCycle $InteractiveRetryInputCycle `
            -InputHoldCycles $InteractiveRetryInputHoldCycles
    }

    Write-MatrixSummary -CsvPath $summaryCsvPath -Directory $OutputDirectory
}

exit $exitCode
