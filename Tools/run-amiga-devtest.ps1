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
    [string]$AdfPath = "",
    [int]$Cycles = 180000000
)

$ErrorActionPreference = "Stop"

$toolsDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionDirectory = Resolve-Path (Join-Path $toolsDirectory "..")
$solutionPath = Join-Path $solutionDirectory "AmigaEmulator.sln"
$testAssembly = Join-Path $solutionDirectory "AmigaEmulator.Tests\bin\x64\Release\AmigaEmulator.Tests.dll"

dotnet build $solutionPath -c Release -p:Platform=x64

$arguments = @(
    $testAssembly,
    "--include-amiga-devtest",
    "--amiga-devtest-cycles",
    $Cycles.ToString([System.Globalization.CultureInfo]::InvariantCulture)
)

if (-not [string]::IsNullOrWhiteSpace($AdfPath)) {
    $arguments += @("--amiga-devtest-adf", $AdfPath)
}

& dotnet @arguments
exit $LASTEXITCODE
