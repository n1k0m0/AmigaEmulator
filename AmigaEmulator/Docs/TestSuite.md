# AmigaEmulator Test Suite

The emulator now has a dedicated test runner project:

```text
AmigaEmulator/AmigaEmulator.Tests
```

It intentionally has no external test-framework dependency. This keeps the
suite usable from normal release builds, local scripts, and future CI without
NuGet/tooling friction.

## Quick Run

```powershell
dotnet build .\AmigaEmulator\AmigaEmulator.sln -p:Configuration=Release -p:Platform=x64
dotnet .\AmigaEmulator\AmigaEmulator.Tests\bin\x64\Release\AmigaEmulator.Tests.dll
```

## Include Golden ADF Checks

Golden ADF checks require the local Kickstart ROM and ADF corpus:

```powershell
dotnet .\AmigaEmulator\AmigaEmulator.Tests\bin\x64\Release\AmigaEmulator.Tests.dll --include-adf
```

## Include vAmigaTS Checks

The external vAmigaTS repository is stored under `external-tests/vAmigaTS`.
The default vAmigaTS mode runs a focused smoke set across Agnus, Blitter,
Copper, Denise, Paula, CIA, memory, and input-facing mainboard tests:

```powershell
dotnet .\AmigaEmulator\AmigaEmulator.Tests\bin\x64\Release\AmigaEmulator.Tests.dll --include-vamigats --filter vAmigaTS
```

For broader sweeps, discover all vAmigaTS ADFs that have a reference RAW or
image artifact:

```powershell
dotnet .\AmigaEmulator\AmigaEmulator.Tests\bin\x64\Release\AmigaEmulator.Tests.dll --vamigats-all --vamigats-limit 50 --filter vAmigaTS
```

The current integration validates that the ADF boots, advances frames, produces
non-uniform visible output, and has a matching vAmigaTS reference artifact.
Exact RAW screenshot comparison is intentionally deferred until the emulator
exports the full vAmigaTS 716x285 border/overscan capture area.

## Include Amiga Devtest Checks

The external `cdhooper/amiga_devtest` repository is stored under
`external-tests/amiga_devtest`. These checks are optional because they require a
bootable AmigaDOS ADF that runs `devtest` from `S:startup-sequence`.

```powershell
.\Tools\run-amiga-devtest.ps1
```

or:

```powershell
dotnet .\AmigaEmulator\AmigaEmulator.Tests\bin\x64\Release\AmigaEmulator.Tests.dll --include-amiga-devtest
```

Pass a prepared ADF explicitly with:

```powershell
.\Tools\run-amiga-devtest.ps1 -AdfPath C:\path\to\amiga_devtest_trackdisk.adf
```

When the prepared ADF or Kickstart ROM is missing, the test reports `SKIP`.

## Useful Filters

```powershell
dotnet .\AmigaEmulator\AmigaEmulator.Tests\bin\x64\Release\AmigaEmulator.Tests.dll --list
dotnet .\AmigaEmulator\AmigaEmulator.Tests\bin\x64\Release\AmigaEmulator.Tests.dll --filter CPU
dotnet .\AmigaEmulator\AmigaEmulator.Tests\bin\x64\Release\AmigaEmulator.Tests.dll --filter Denise
dotnet .\AmigaEmulator\AmigaEmulator.Tests\bin\x64\Release\AmigaEmulator.Tests.dll --filter GoldenADF --include-adf
dotnet .\AmigaEmulator\AmigaEmulator.Tests\bin\x64\Release\AmigaEmulator.Tests.dll --filter vAmigaTS --include-vamigats
dotnet .\AmigaEmulator\AmigaEmulator.Tests\bin\x64\Release\AmigaEmulator.Tests.dll --filter AmigaDevtest --include-amiga-devtest
```

## Current Coverage

- Existing CPU, Blitter, Copper, CIA, and Input regression self-tests.
- CPU opcode matrix smoke coverage for all 65,536 opcode words.
- CPU implemented-family decode guard plus representative semantic vectors for
  flags, branches, shifts, bit operations, and exceptions.
- Agnus PAL beam line advance.
- Denise/bitplane BPLxDAT latch rendering.
- Bitplane DMA fetch to visible color index.
- Split bitplane DMA plane-word fetch grouping: half-fetched groups stay hidden
  until all enabled planes have arrived.
- Production fetch grouping and prefetch-to-shifter delay regressions. Lowres
  displays with up to four planes currently use a compact compatibility path;
  this does not certify hardware-accurate per-slot reads.
- Dual-playfield color-bank mapping.
- Direct sprite register rendering.
- Copper word-fetch bandwidth and release of waiting fetch slots to the Blitter.
- Sprite POS reuse, CTL disarming, DATA arming independently of the DMA vertical
  interval, DATB non-arming, parked attached palette selection, and idle DMA
  activation state through save/restore.
- Standard ADF MFM stream sync/gap sanity.
- CPU bus write side effect into Denise bitplane latch.
- Optional Turrican III nonblank Golden ADF checkpoint.
- Optional North & South Golden ADF checkpoint with a vertical fetch-stripe
  regression guard.
- Optional Amiga Test Kit 1.21 Golden ADF boot checkpoint. The release asset is
  stored under `external-tests/amiga-test-kit-1.21`.
- Optional vAmigaTS smoke coverage for representative Agnus, Blitter, Copper,
  Denise, Paula, CIA, memory, and mainboard/input ADFs.
- Optional vAmigaTS recursive discovery mode for broader local sweeps.
- Optional Amiga Devtest boot-ADF smoke coverage for the AmigaOS
  `trackdisk.device` block-device path.

## Next Test Targets

1. Full 716x285 border/overscan capture so vAmigaTS RAW screenshots can be
   compared byte-exactly or with controlled tolerances.
2. Sprite DMA list timing, vertical reuse, and collision registers.
3. Copper mid-frame register writes with exact beam-position expectations.
4. Paula audio period/volume/modulation golden samples.
5. Kickstart 1.3 no-disk animated disk screen golden trace.
6. More ADF-specific visual heuristics for known graphics regressions.
7. Reference-generated 68000 per-opcode expected-state vectors, including
   cycle counts, for instructions beyond the current semantic sample set.

## Presentation and collision regressions

The standard suite checks that changing scene brightness does not change the
DIW/DDF source geometry, that asymmetric black scene edges retain their width
and position, and that sparse and dense top-edge artwork survive end-of-frame
presentation. CLXCON checks cover independent odd/even groups, zero matches,
disabled planes, saturation and read-clear behavior.

## Strict arbitration for external checks

From the solution directory, use `--strict-bus` for vAmigaTS or the local
Siedler savestate check (other test categories retain their own profiles):

```powershell
dotnet .\AmigaEmulator.Tests\bin\x64\Release\AmigaEmulator.Tests.dll --include-vamigats --filter vAmigaTS --strict-bus
dotnet .\AmigaEmulator.Tests\bin\x64\Release\AmigaEmulator.Tests.dll --include-local-siedler-golden --filter LocalSiedlerGolden --strict-bus
```

These runs enable contention without collecting traces. The Siedler test picks
the newest local Siedler/Settlers savestate; use `--siedler-save <path>` to pin a
specific asset. vAmigaTS remains a smoke/reference-property check, not a full
pixel-by-pixel comparison.

## GUI stability

```powershell
.\Tools\run-amiga-gui-stability.ps1 -Seconds 120 -SampleSeconds 10 -SavestatePath <path>
```

The script builds Release, launches its own emulator process, records FPS,
host speed, memory and GDI/USER resources, then closes that process. Hidden
window titles are collected directly when `MainWindowTitle` is empty. Build
failure, early exit, no samples or no positive FPS measurements return failure.
Resource growth and FPS changes are reported for inspection; a successful run
alone does not certify a leak-free renderer or sustained real-time speed.

## Sprite-framed split-screen geometry

The sprite regression suite checks the full latched word aperture and a sprite
whose final lowres pixel lies beyond both the ordinary beam raster and the
bitplane DDF window. The pixel must survive line-tail completion. The local
Siedler check now requires the wooden playfield border at both monitor edges
and top-frame content on the first presented row, in addition to panel metrics.
Split-screen presentation includes full-width sprite frame geometry rather
than treating those sprite pixels as black monitor border.

The moving-pointer regression sweeps the local Siedler pointer to both horizontal
edges, down into the command panel, and back to the top. Completed frame line
apertures must remain fixed throughout. A synthetic split-screen fixture also
checks that identical sprite X positions map to the same presented X above and
below the panel seam, with the background restored at the former shifted pointer
location. Attached border-pair geometry is tested independently of moving sprites.
The transient sprite/background compositor buffers do not change savestate format.


## Scripted gameplay review

`Tools/GameRunner` replays input and disk-swap scenarios, including continuation
from savestates. See [GameRunner README](../../Tools/GameRunner/README.md).
Each action captures a screenshot, CPU/disk state, presentation bounds and a
frame hash. Execution success and nonblank output do not prove gameplay or
absence of glitches. The matrix marks visible output as `visible-unverified`;
GameRunner records `requires-visual-review` until a person inspects the actual
playfield. The 18 supplied scenarios are local-media smoke examples, not
certified all-game walkthroughs.

New regressions cover asymmetric BPLCON1 nibble assignment, beam-ordered hires
DMA without periodic missing font pixels, HAM color hold beneath sprites both
within and across rendering spans, manual AUDxDAT playback without DMA, and
profiler equivalence with ordinary frame/presentation output. Compatible keeps
bus arbitration enabled without collecting continuous CPU/DMA traces; Strict
remains the explicit tracing profile.

CIA serial regressions cover completion after 16 Timer-A underflows, buffered
SDR output, input/output direction changes, stopped clocks, reset, and transfer
phase restoration from new and legacy savestates.

CPU tests also cover zero-count shift/rotation flags (including modulo-64
counts) and instruction-emulation handlers for illegal/Line-A/Line-F opcodes.
Both direct Step and deferred Tick execution must stack the original opcode
address, allow the handler to inspect/skip it, and resume with RTE.

Storage/Expansion tests cover file-backed ATA PIO, RDB creation, read-only and
range failures, IRQ acknowledgement, CPU deferred FIFO reads, partial-transfer
savestates and Zorro II memory registration/bounds. The independent ROM boot
evidence and compatibility limits are in [StorageExpansion.md](StorageExpansion.md).

The score-writer CPU regression executes the original and repaired six-byte
header setup plus its following assembly instructions on all five supported
CPU types. It covers 160 requested tracks, eleven sectors and stale/matching
scratch registers against the independent FF/track/sector/remaining format.
It reproduces the original wrong header without game/ROM media and checks that
the repair retains caller state and code size. Real private-ADF write/readback
evidence and its limits are recorded in [MachineModels.md](MachineModels.md).
