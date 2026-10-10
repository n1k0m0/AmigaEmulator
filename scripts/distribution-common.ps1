# Copyright 2026 Nils Kopal
# SPDX-License-Identifier: Apache-2.0
$ErrorActionPreference = 'Stop'

function Get-AmigaVersion([string]$RepoRoot, [string]$RequestedVersion) {
    if ([string]::IsNullOrWhiteSpace($RequestedVersion)) {
        $RequestedVersion = (Get-Content -LiteralPath (Join-Path $RepoRoot 'VERSION') -Raw).Trim()
    }
    if ($RequestedVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must contain major.minor.patch.' }
    $parts = $RequestedVersion.Split('.')
    foreach ($part in $parts) { if ([long]$part -gt 65535) { throw 'Version component exceeds the Windows version limit.' } }
    return $RequestedVersion
}

function Assert-AmigaDistribution([string]$Directory) {
    $root = [IO.Path]::GetFullPath($Directory).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $forbidden = @('.rom', '.bin', '.key', '.adf', '.adz', '.dms', '.ipf', '.hdf', '.hdd', '.img', '.iso', '.vhd', '.vhdx', '.amigasav', '.sav', '.lha', '.lzh', '.zip', '.7z', '.rar')
    foreach ($file in Get-ChildItem -LiteralPath $Directory -File -Recurse) {
        $relative = $file.FullName.Substring($root.Length)
        if ($file.Extension.ToLowerInvariant() -in $forbidden -or $relative -match '(^|[\\/])(roms?|media|games|saves|harddisks|relay_server)([\\/]|$)') {
            throw "Distribution contains user-owned media or ROM files: $relative"
        }
        # Detect renamed raw/encrypted Kickstart files as well as ordinary ROM extensions.
        $stream = [IO.File]::OpenRead($file.FullName)
        try {
            $header = New-Object byte[] 11
            $read = $stream.Read($header, 0, $header.Length)
            if ($read -eq 11 -and [Text.Encoding]::ASCII.GetString($header) -eq 'AMIROMTYPE1') {
                throw "Distribution contains an encrypted Kickstart ROM: $relative"
            }
            if ($read -ge 8 -and $file.Length -in @(262144, 524288, 1048576)) {
                $stack = ([long]$header[0] -shl 24) -bor ([long]$header[1] -shl 16) -bor ([long]$header[2] -shl 8) -bor $header[3]
                $pc = ([long]$header[4] -shl 24) -bor ([long]$header[5] -shl 16) -bor ([long]$header[6] -shl 8) -bor $header[7]
                if ($pc -ge 0xF80000 -and $pc -le 0xFFFFFF -and
                    (($stack -le 0x200000) -or ($stack -ge 0xC00000 -and $stack -le 0xC80000) -or ($header[2] -eq 0x4E -and $header[3] -eq 0xF9))) {
                    throw "Distribution contains a renamed Kickstart ROM: $relative"
                }
            }
        } finally { $stream.Dispose() }
    }
}

function Assert-AmigaPublishedVersion([string]$Directory, [string]$Version) {
    $exe = Join-Path $Directory 'AmigaEmulator.exe'
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Published executable missing: $exe" }
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
    $expected = "$Version.0"
    if ($info.FileVersion -ne $expected -or $info.ProductVersion -ne $Version) {
        throw "Published version mismatch: file=$($info.FileVersion), product=$($info.ProductVersion), expected=$expected/$Version"
    }
}
