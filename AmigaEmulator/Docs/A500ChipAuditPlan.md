# A500 implementation audit and remaining work

Updated: 2026-10-07. This document describes the current source, including the
uncommitted compatibility and presentation changes. Implementation does not
imply complete cycle accuracy or compatibility with every game.

## Verified baseline

- Windows x64 Release build on .NET SDK 10.0.401 succeeds.
- All 110 standard tests pass, covering CPU, DMA, Copper, Blitter, CIA,
  bitplanes, sprites, collision, disk, input, memory profiles, and savestates.
- Kickstart 1.3 passes `--assert-kickstart-screen-path 120000000 --model a500`
  with strict chip-bus arbitration through ROM, Copper, Blitter, Chip RAM,
  bitplane DMA, and Denise.
- All nine focused vAmigaTS smoke cases pass with both relaxed and strict
  arbitration. These checks verify boot/output properties and selected uniform
  reference colors, not full byte-exact reference screenshots.
- The local Die Siedler savestate geometry test passes with both arbitration
  settings. It uses local game assets and broad frame/panel metrics.

- All four optional GoldenADF checks pass: Turrican III, North & South intro,
  North & South mouse selection/main-menu loading, and Amiga Test Kit boot.
- A 123-second Siedler GUI run records 49-51 FPS (average 50.09), average
  host speed 100%, and zero GDI/USER handle growth. Memory decreases after
  warmup; this bounded observation is not a proof that all workloads are leak-free.

The sprite-border update passes all 110 standard tests and the local Siedler
check with strict arbitration, plus all nine vAmigaTS smoke checks. See
`TestSuite.md` for commands and optional-check scope.

## Agnus and DMA

Implemented: a fixed 227-slot PAL scanline table for refresh, audio, disk and
sprites; one bitplane word per plane slot; sprite priority; Copper bus demand;
BLTPRI behavior; and region-aware CPU bus arbitration. Regression checks cover
these features. The previous claim that all planes are fetched in one abstract
DMA slot is obsolete.

Remaining: differential slot/timing traces under mid-line register changes,
complete interlace/long-short-frame behavior, and validation of timing across
all CPU/DMA combinations. NTSC/ECS are separate milestones.

## Copper

Implemented: MOVE, WAIT, SKIP, two-phase instruction fetch, COPJMP, Blitter
waiting, and OCS protected ranges (`$00..$3E` blocked; `$40..$7E` gated by
COPCON/CDANG; `$80+` writable). Protected ranges have self-test coverage.

Remaining: broader mask, jump, high vertical position, and exact wake-up timing
comparisons against hardware/reference traces. Preserve the working ROM boot
path while extending these cases.

## Denise, bitplanes, sprites and host presentation

Implemented: per-plane DMA, lowres/hires, odd/even fine scrolling, dual
playfields, HAM/EHB, sprite DMA/latches, BPLCON2 priority, and CLXCON/CLXDAT.
Collision matching follows the [Hardware Reference Manual register summary](https://www.theflatnet.de/pub/cbm/amiga/AmigaDevDocs/hard_a.html#clxcon),
evaluating enabled odd and even planes independently;
disabled planes cannot suppress a collision, including on zero-valued pixels.

DIW/DDF and per-line display metadata select the host source geometry. Host
presentation no longer scans image brightness to shift/crop that geometry or
erases sparse top-edge artwork. Regression cases cover black scene edges,
brightness changes, sparse artwork, wide DDF, and panel alignment.

Sprite borders extending beyond the regular beam raster now render through the
line-tail path instead of being erased by background fill. Wide framed
playfields include wide attached sprite pairs in per-line presentation geometry;
full-width sprite frames provide the vertical start of split-screen layouts.
Independent moving sprites do not change this aperture. The presentation
compositor samples sprites in one horizontal coordinate system across the
playfield and panel, retaining the covered background to avoid ghost pointers.
The local Siedler regression checks the outer left/right and top wooden edges.

Remaining: hardware/reference comparisons for mid-line pointer/register writes,
attached sprites, collision timing, HAM/sprite composition, and interlace.
Register-based overscan normalization and line-specific scaling are host
presentation policies and should be checked separately from raw chip output.

## Paula disk and audio

Implemented and tested for disk: ADF/MFM reads, WORDSYNC following-word behavior,
read/write DMA, sector decoding, explicit write flush, drive selection, motor,
step direction, rotational phase, media swaps, and external drives. Claims that
disk writes are a no-op or DF1-DF3 are absent are obsolete.

Implemented for audio: four channel registers/state, DMA sample fetching,
period/volume output, channel interrupts, stereo host output and filters.
Implementation is not a proof of hardware-exact analog or modulation behavior.

Remaining: loader-specific disk edge cases, precompensation/bitcell accuracy,
and focused audio reload, manual-mode, interrupt, and ADKCON modulation tests.

## CIA and 68000

Implemented: CIA timers/TOD/interrupt basics, keyboard/mouse/joystick integration,
68000 opcode families and exceptions, prefetch persistence, and queued bus
operations that can wait for Chip/Slow RAM or custom-register access.
ROM/Fast RAM accesses are not globally stalled like Chip RAM.

The 65,536-opcode smoke matrix checks isolated execution for host exceptions;
representative vectors and self-tests check behavior. It does not exhaust all
operand/flag/exception/timing combinations.

Remaining: differential CPU vectors and bus traces, complete peripheral pin and
serial timing, precise interrupt sampling, and full exception/bus timing.

## Memory and accuracy profiles

Implemented: Kickstart overlay, selectable Chip/Slow/Fast RAM maps and
savestate restoration. Menu RAM profiles have regression coverage. The
frontend default is 1 MiB Chip RAM; the base A500 model remains 512 KiB.

`Fast` deliberately relaxes chip-bus contention and disables tracing.
`Compatible` and `Strict` enable contention and tracing. This profile choice
is explicit and is not evidence of complete cycle accuracy. The Fast frontend
default remains in place until broader gameplay and timing validation supports
a change; local savestate and vAmigaTS checks can be repeated with `--strict-bus`.

Remaining: Gary/open-bus details, expansion Autoconfig, A501 RTC, and stronger
validation across ROM and memory profiles.

## Next implementation priorities

1. Add hardware/reference traces for CPU bus retirement and mid-line display
   updates; assess strict arbitration using actual gameplay, not only boot.
2. Extend raw screenshot/reference comparison independently of host scaling.
3. Add focused Paula audio and CIA serial/pin tests.
4. Expand loader/game input scenarios and long-running GUI resource checks.
5. Complete interlace/long-short frames, followed by expansion peripherals.

Each timing change should retain the standard tests, strict Kickstart check,
and local game regressions. Fix raw chip output at its source; do not hide
incorrect pixels with content-dependent presentation cleanup.


### Gameplay review follow-up (2026-10-07)

A per-plane eight-slot lowres experiment was rejected after comparison with the
same Siedler gameplay savestate: the lower four-plane command panel became
corrupt in both Fast and Compatible, and the vAmigaTS VPOS smoke regressed.
The existing compact path for <=4 lowres planes remains. Correcting this requires
handling mid-line plane enable/pointer rewrites and partially fetched groups,
with clean split-screen gameplay evidence before replacing the compatibility
path. Do not label the compact path hardware-exact. BPLCON1 nibble assignment
and the hires shifter lead-in were corrected independently and tested.
