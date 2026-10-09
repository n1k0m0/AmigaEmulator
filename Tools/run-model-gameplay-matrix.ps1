param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$SourceManifest = (Join-Path $PSScriptRoot 'ten-new-games-scenarios.json'),
    [string[]]$Games = @('Tinyus', 'Sqrxz2'),
    [string[]]$Models = @('A500', 'A500Plus', 'A600', 'A2000Ocs', 'A2000Ecs', 'A1200', 'A3000', 'A4000Ec030'),
    [string]$RunnerAssembly = '',
    [string]$RomDirectory = (Join-Path $env:APPDATA 'AmigaEmulator/roms'),
    [switch]$ManifestOnly
)

$ErrorActionPreference = 'Stop'
if (-not $ManifestOnly -and [string]::IsNullOrWhiteSpace($RunnerAssembly)) {
    $project = Join-Path $PSScriptRoot 'GameRunner/GameRunner.csproj'
    & dotnet build $project -c Release -p:Platform=x64 --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'GameRunner build failed.' }
    $RunnerAssembly = Join-Path $PSScriptRoot 'GameRunner/bin/x64/Release/net10.0-windows7.0/GameRunner.dll'
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
$templates = @(Get-Content -LiteralPath $SourceManifest -Raw | ConvertFrom-Json)
$cases = @()
foreach ($model in $Models) {
    if (-not $profiles.ContainsKey($model)) { throw "Unknown model: $model" }
    $rom = Join-Path $RomDirectory $profiles[$model][1]
    if (-not (Test-Path -LiteralPath $rom -PathType Leaf)) { throw "ROM missing for ${model}: $rom" }
    foreach ($game in $Games) {
        $selected = @($templates | Where-Object name -eq $game)
        if ($selected.Count -ne 1) { throw "Expected one template for $game in $SourceManifest" }
        $case = $selected[0] | ConvertTo-Json -Depth 20 | ConvertFrom-Json
        $case.name = "$model-$game"
        $case.ram = $profiles[$model][0]
        $case | Add-Member model $model -Force
        $case | Add-Member kickstartRom $rom -Force
        $case | Add-Member strict $true -Force
        $case | Add-Member traceExceptions $true -Force
        $case.actions = @($case.actions) + @(
            @{ type = 'restore'; label = 'gameplay-restore' },
            @{ type = 'joystick'; button = 'Left'; cycles = 1000000; label = 'move-after-restore' }
        )
        $cases += $case
    }
}
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a fresh output directory to preserve prior evidence.' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$manifest = Join-Path $OutputDirectory 'scenarios.json'
ConvertTo-Json -InputObject @($cases) -Depth 20 | Set-Content -LiteralPath $manifest -Encoding utf8
Write-Output "Generated $($cases.Count) model/game cases: $manifest"
if (-not $ManifestOnly) {
    & dotnet $RunnerAssembly $manifest $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'One or more model/game scenarios failed. Inspect the captures and logs.' }
    Write-Output 'Execution completed; gameplay and visual compatibility still require capture review.'
}
