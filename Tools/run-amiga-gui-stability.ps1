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
    [int]$Seconds = 120,
    [int]$SampleSeconds = 5,
    [string]$OutputDirectory = "",
    [string]$SavestatePath = "",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

if ($Seconds -lt 1) {
    $Seconds = 120
}

if ($SampleSeconds -lt 1) {
    $SampleSeconds = 5
}

$toolsDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionDirectory = Resolve-Path (Join-Path $toolsDirectory "..")
$solutionPath = Join-Path $solutionDirectory "AmigaEmulator.sln"
$emulatorExe = Join-Path $solutionDirectory "AmigaEmulator\bin\x64\Release\AmigaEmulator.exe"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $OutputDirectory = Join-Path (Join-Path $solutionDirectory "..\tmp") ("amiga_gui_stability_" + $stamp)
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$csvPath = Join-Path $OutputDirectory "gui_stability.csv"
$summaryPath = Join-Path $OutputDirectory "gui_stability_summary.txt"

if (-not $NoBuild) {
    dotnet build $solutionPath -c Release -p:Platform=x64
    if ($LASTEXITCODE -ne 0) {
        throw "Release build failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path $emulatorExe)) {
    throw "Release emulator not found: $emulatorExe"
}

if (-not ("GuiResourceReader" -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class GuiResourceReader
{
    [DllImport("user32.dll")]
    public static extern int GetGuiResources(IntPtr hProcess, int uiFlags);

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private static IntPtr FindEmulatorWindow(int processId)
    {
        IntPtr result = IntPtr.Zero;
        EnumWindows((window, parameter) => {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner != (uint)processId) return true;
            var title = new StringBuilder(1024);
            GetWindowText(window, title, title.Capacity);
            if (!title.ToString().StartsWith("Amiga Emulator ", StringComparison.Ordinal)) return true;
            result = window;
            return false;
        }, IntPtr.Zero);
        return result;
    }

    public static string GetEmulatorTitle(int processId)
    {
        var title = new StringBuilder(1024);
        IntPtr window = FindEmulatorWindow(processId);
        if (window != IntPtr.Zero) GetWindowText(window, title, title.Capacity);
        return title.ToString();
    }

    public static bool CloseEmulatorWindow(int processId)
    {
        IntPtr window = FindEmulatorWindow(processId);
        return window != IntPtr.Zero && PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
    }

}
"@
}

function Get-TitleNumber {
    param(
        [string]$Title,
        [string]$Pattern
    )

    if ([string]::IsNullOrWhiteSpace($Title)) {
        return $null
    }

    $match = [regex]::Match($Title, $Pattern)
    if (-not $match.Success) {
        return $null
    }

    return [double]::Parse($match.Groups[1].Value, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Write-Summary {
    param(
        $Rows,
        [string]$Path
    )

    if ($Rows.Count -eq 0) {
        "No samples collected." | Set-Content -Path $Path -Encoding UTF8
        return
    }

    $warmRows = if ($Rows.Count -gt 2) { $Rows | Select-Object -Skip 1 } else { $Rows }
    $fpsRows = @($warmRows | Where-Object { $_.Fps -ne $null })
    $hostRows = @($warmRows | Where-Object { $_.Host -ne $null })
    $first = $warmRows | Select-Object -First 1
    $last = $warmRows | Select-Object -Last 1
    $segment = [Math]::Max(1, [int][Math]::Floor($fpsRows.Count / 3))
    $firstSegment = @($fpsRows | Select-Object -First $segment)
    $lastSegment = @($fpsRows | Select-Object -Last $segment)

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("AMIGA GUI STABILITY SUMMARY")
    $lines.Add("Samples=$($Rows.Count)")
    $lines.Add("WarmupSamplesSkipped=$([int]($Rows.Count -gt 2))")
    if ($fpsRows.Count -gt 0) {
        $lines.Add(("MinFPS={0:F2}" -f (($fpsRows | Measure-Object -Property Fps -Minimum).Minimum)))
        $lines.Add(("AvgFPS={0:F2}" -f (($fpsRows | Measure-Object -Property Fps -Average).Average)))
        $lines.Add(("MaxFPS={0:F2}" -f (($fpsRows | Measure-Object -Property Fps -Maximum).Maximum)))
        $lines.Add(("FirstSegmentAvgFPS={0:F2}" -f (($firstSegment | Measure-Object -Property Fps -Average).Average)))
        $lines.Add(("LastSegmentAvgFPS={0:F2}" -f (($lastSegment | Measure-Object -Property Fps -Average).Average)))
    }

    if ($hostRows.Count -gt 0) {
        $lines.Add(("AvgHostPercent={0:F2}" -f (($hostRows | Measure-Object -Property Host -Average).Average)))
    }

    if ($first -ne $null -and $last -ne $null) {
        $lines.Add(("PrivateBytesGrowthMB={0:F2}" -f (($last.PrivateBytes - $first.PrivateBytes) / 1MB)))
        $lines.Add(("WorkingSetGrowthMB={0:F2}" -f (($last.WorkingSetBytes - $first.WorkingSetBytes) / 1MB)))
        $lines.Add("GdiHandleGrowth=$($last.GdiHandles - $first.GdiHandles)")
        $lines.Add("UserHandleGrowth=$($last.UserHandles - $first.UserHandles)")
    }

    $lines.Add("Csv=$csvPath")
    $lines | Set-Content -Path $Path -Encoding UTF8
}

$header = "sample,elapsed_seconds,timestamp,title,fps,host_percent,cpu_percent,working_set_bytes,private_bytes,gdi_handles,user_handles"
$header | Set-Content -Path $csvPath -Encoding UTF8
$rows = New-Object System.Collections.Generic.List[object]
$invariantCulture = [System.Globalization.CultureInfo]::InvariantCulture

Write-Host "Starting GUI stability run. CSV: $csvPath"
Write-Host "DurationSeconds=$Seconds SampleSeconds=$SampleSeconds"
$argumentList = @()
if (-not [string]::IsNullOrWhiteSpace($SavestatePath)) {
    if (-not (Test-Path -LiteralPath $SavestatePath)) {
        throw "Savestate not found: $SavestatePath"
    }

    $argumentList += "--gui-savestate"
    $argumentList += ('"' + ((Resolve-Path -LiteralPath $SavestatePath).Path -replace '"', '\"') + '"')
}

$process = if ($argumentList.Count -gt 0) {
    Start-Process -FilePath $emulatorExe -ArgumentList $argumentList -WindowStyle Hidden -PassThru
} else {
    Start-Process -FilePath $emulatorExe -WindowStyle Hidden -PassThru
}
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$previousCpu = $process.TotalProcessorTime
$previousElapsed = 0.0
$sample = 0
$endedEarly = $false
$exitCode = $null

try {
    Start-Sleep -Seconds ([Math]::Min(3, $SampleSeconds))

    while ($stopwatch.Elapsed.TotalSeconds -lt $Seconds) {
        Start-Sleep -Seconds $SampleSeconds
        $process.Refresh()
        if ($process.HasExited) {
            $endedEarly = $true
            $exitCode = $process.ExitCode
            Write-Warning ("Emulator exited before requested duration at {0:F1}s. ExitCode={1}" -f $stopwatch.Elapsed.TotalSeconds, $exitCode)
            break
        }

        $elapsed = $stopwatch.Elapsed.TotalSeconds
        $title = $process.MainWindowTitle
        if ([string]::IsNullOrWhiteSpace($title)) {
            $title = [GuiResourceReader]::GetEmulatorTitle($process.Id)
        }
        $fps = Get-TitleNumber -Title $title -Pattern "FPS:(\d+(?:\.\d+)?)"
        $hostPercent = Get-TitleNumber -Title $title -Pattern "HOST:(\d+(?:\.\d+)?)%"
        $cpu = $process.TotalProcessorTime
        $deltaCpu = ($cpu - $previousCpu).TotalSeconds
        $deltaElapsed = [Math]::Max(0.000001, $elapsed - $previousElapsed)
        $cpuPercent = ($deltaCpu / $deltaElapsed / [Environment]::ProcessorCount) * 100.0
        $previousCpu = $cpu
        $previousElapsed = $elapsed
        $gdiHandles = [GuiResourceReader]::GetGuiResources($process.Handle, 0)
        $userHandles = [GuiResourceReader]::GetGuiResources($process.Handle, 1)
        $safeTitle = '"' + ($title -replace '"', '""') + '"'

        $row = [pscustomobject]@{
            Sample = ++$sample
            Elapsed = $elapsed
            Timestamp = (Get-Date).ToString("o")
            Title = $title
            Fps = $fps
            Host = $hostPercent
            CpuPercent = $cpuPercent
            WorkingSetBytes = $process.WorkingSet64
            PrivateBytes = $process.PrivateMemorySize64
            GdiHandles = $gdiHandles
            UserHandles = $userHandles
        }
        $rows.Add($row) | Out-Null

        $line = [string]::Format($invariantCulture, "{0},{1:F3},{2},{3},{4},{5},{6:F3},{7},{8},{9},{10}", `
            $row.Sample,
            $row.Elapsed,
            $row.Timestamp,
            $safeTitle,
            $(if ($null -eq $fps) { "" } else { $fps.ToString([System.Globalization.CultureInfo]::InvariantCulture) }),
            $(if ($null -eq $hostPercent) { "" } else { $hostPercent.ToString([System.Globalization.CultureInfo]::InvariantCulture) }),
            $row.CpuPercent,
            $row.WorkingSetBytes,
            $row.PrivateBytes,
            $row.GdiHandles,
            $row.UserHandles)
        Add-Content -Path $csvPath -Value $line -Encoding UTF8

        Write-Host ("sample={0} elapsed={1:F1}s fps={2} host={3} privateMB={4:F1} gdi={5} user={6}" -f `
            $row.Sample,
            $row.Elapsed,
            $(if ($null -eq $fps) { "n/a" } else { $fps }),
            $(if ($null -eq $hostPercent) { "n/a" } else { "$hostPercent%" }),
            ($row.PrivateBytes / 1MB),
            $row.GdiHandles,
            $row.UserHandles)
    }
}
finally {
    if ($process -ne $null -and -not $process.HasExited) {
        if (-not $process.CloseMainWindow()) {
            [void][GuiResourceReader]::CloseEmulatorWindow($process.Id)
        }
        if (-not $process.WaitForExit(5000)) {
            $process.Kill()
            $process.WaitForExit()
        }
    }

    Write-Summary -Rows @($rows.ToArray()) -Path $summaryPath
    if ($endedEarly) {
        Add-Content -Path $summaryPath -Value ("EndedEarly=True ExitCode={0}" -f $exitCode) -Encoding UTF8
    } else {
        Add-Content -Path $summaryPath -Value "EndedEarly=False" -Encoding UTF8
    }

    Get-Content -Path $summaryPath
}

if ($endedEarly -or $rows.Count -eq 0) {
    Add-Content -Path $summaryPath -Value "Result=FAIL" -Encoding UTF8
    exit 1
}

$measuredFrames = @($rows | Where-Object { $null -ne $_.Fps -and $_.Fps -gt 0 })
if ($measuredFrames.Count -eq 0) {
    Add-Content -Path $summaryPath -Value "Result=FAIL No positive presentation FPS samples collected." -Encoding UTF8
    exit 1
}

Add-Content -Path $summaryPath -Value "Result=PASS" -Encoding UTF8
exit 0
