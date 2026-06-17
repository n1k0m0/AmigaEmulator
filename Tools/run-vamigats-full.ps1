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
    [int]$Cycles = 40000000,
    [string]$OutputDirectory = "",
    [string]$Filter = ""
)

$ErrorActionPreference = "Stop"

$toolsDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionDirectory = Resolve-Path (Join-Path $toolsDirectory "..")
$solutionPath = Join-Path $solutionDirectory "AmigaEmulator.sln"
$testAssembly = Join-Path $solutionDirectory "AmigaEmulator.Tests\bin\x64\Release\AmigaEmulator.Tests.dll"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $OutputDirectory = Join-Path (Join-Path $solutionDirectory "..\tmp") ("vamigats_full_" + $stamp)
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

dotnet build $solutionPath -c Release -p:Platform=x64

$arguments = @(
    $testAssembly,
    "--include-vamigats",
    "--vamigats-all",
    "--vamigats-cycles",
    $Cycles.ToString([System.Globalization.CultureInfo]::InvariantCulture)
)

if (-not [string]::IsNullOrWhiteSpace($Filter)) {
    $arguments += @("--filter", $Filter)
}

$logPath = Join-Path $OutputDirectory "vamigats_full.log"
Write-Host "Running full vAmigaTS suite. Log: $logPath"
& dotnet @arguments 2>&1 | Tee-Object -FilePath $logPath
exit $LASTEXITCODE
