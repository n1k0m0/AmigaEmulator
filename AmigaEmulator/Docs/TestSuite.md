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

## Useful Filters

```powershell
dotnet .\AmigaEmulator\AmigaEmulator.Tests\bin\x64\Release\AmigaEmulator.Tests.dll --list
dotnet .\AmigaEmulator\AmigaEmulator.Tests\bin\x64\Release\AmigaEmulator.Tests.dll --filter CPU
dotnet .\AmigaEmulator\AmigaEmulator.Tests\bin\x64\Release\AmigaEmulator.Tests.dll --filter Denise
dotnet .\AmigaEmulator\AmigaEmulator.Tests\bin\x64\Release\AmigaEmulator.Tests.dll --filter GoldenADF --include-adf
dotnet .\AmigaEmulator\AmigaEmulator.Tests\bin\x64\Release\AmigaEmulator.Tests.dll --filter vAmigaTS --include-vamigats
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
- Production Agnus bitplane DMA uses one plane-word fetch per chip-cycle slot
  and applies the tested prefetch-to-shifter delay.
- Dual-playfield color-bank mapping.
- Direct sprite register rendering.
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
