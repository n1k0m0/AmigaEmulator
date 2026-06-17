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
    [int]$Limit = 0,
    [string]$Model = ""
)

$ErrorActionPreference = "Stop"

$toolsDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionDirectory = Resolve-Path (Join-Path $toolsDirectory "..")
$solutionPath = Join-Path $solutionDirectory "AmigaEmulator.sln"
$emulatorAssembly = Join-Path $solutionDirectory "AmigaEmulator\bin\x64\Release\AmigaEmulator.dll"

if ([string]::IsNullOrWhiteSpace($AdfDirectory)) {
    $AdfDirectory = Join-Path ([Environment]::GetFolderPath("MyDocuments")) "AmigaEmulator"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $OutputDirectory = Join-Path (Join-Path $solutionDirectory "..\tmp") ("game_matrix_" + $stamp)
}

if (-not (Test-Path $AdfDirectory)) {
    New-Item -ItemType Directory -Force -Path $AdfDirectory | Out-Null
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

dotnet build $solutionPath -c Release -p:Platform=x64

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
exit $LASTEXITCODE
