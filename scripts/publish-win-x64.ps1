param([string]$Configuration = 'Release', [string]$Version = '')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'distribution-common.ps1')
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$Version = Get-AmigaVersion $repoRoot $Version
$artifactsDir = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$publishDir = [IO.Path]::GetFullPath((Join-Path $artifactsDir 'publish/win-x64'))
$boundary = $artifactsDir.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
if (-not $publishDir.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe publish directory: $publishDir" }
if (Test-Path -LiteralPath $publishDir) {
    # Never clear a generated folder if someone has placed their ROMs/media there.
    Assert-AmigaDistribution $publishDir
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
dotnet publish (Join-Path $repoRoot 'AmigaEmulator/AmigaEmulator.csproj') `
    -c $Configuration -r win-x64 --self-contained true -p:Platform=x64 `
    -p:PublishSingleFile=false -p:Version=$Version -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }
Assert-AmigaDistribution $publishDir
Assert-AmigaPublishedVersion $publishDir $Version
Write-Host "Published Amiga Emulator $Version without ROMs/media: $publishDir"
