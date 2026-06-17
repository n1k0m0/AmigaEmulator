# A500 chip audit and implementation plan

Goal: turn the current boot-capable A500 core into a complete, hardware-faithful Amiga 500 simulation.

Sources checked:

- `technical documentations/Amiga_Hardware_Reference_Manual_3rd_edition.pdf`
- `technical documentations/Commodore_Amiga_500_Technical_Manual.pdf`
- `technical documentations/Amiga_ROM_Kernel_Reference_Manual_v1.3.pdf`
- `technical documentations/amiga_intern.pdf`
- Current source under `AmigaEmulator/AmigaEmulator`

Current verified baseline:

- Kickstart 1.3 reaches the real insert-disk screen.
- The screen is produced through ROM, Blitter, Copper, bitplane DMA, palette registers, Chip RAM bitplanes, and Denise output.
- Release checks pass for CPU, CIA, Blitter, input, disk self-tests and `--assert-kickstart-screen-path 120000000 --model a500`.

Priority key:

- `P0`: directly blocks game/app compatibility or core A500 correctness.
- `P1`: needed for broad compatibility and cycle accuracy.
- `P2`: maturity, polish, edge cases, UI consistency.

## Executive summary

The emulator has a strong boot-oriented slice: Kickstart overlay, CIA basics, PAL timing constants, real Copper/Blitter/bitplane boot screen, ADF loading, and a growing set of focused self-tests. The main gap is that several chips are still implemented as pragmatic approximations rather than full state machines.

Most urgent work:

1. Fix Copper protection exactly to OCS COPCON/CDANG rules and fix WAIT/SKIP mask semantics.
2. Replace heuristic DMA scheduling with a real OCS PAL slot model.
3. Build a real Denise pixel mixer with sprite/playfield priority and collision.
4. Complete Paula disk details needed by loaders and implement Paula audio.
5. Move CPU/bus timing toward transaction-level arbitration instead of global stalls.

## Agnus and DMA

Files:

- `CustomChips/Agnus.cs`
- `CustomChips/DmaScheduler.cs`
- `Core/AmigaModel.cs`
- `Core/AmigaSystem.cs`

Good:

- PAL constants are close to A500 PAL: 7.093790 MHz CPU, half-rate chip clock, 227 chip cycles per line, 312 lines per frame.
- Agnus owns beam progression, VPOS/VHPOS update, Copper, Blitter, bitplane fetch dispatch, DMA slot trace.
- DMA master and individual DMA bits are recognized.
- VBlank interrupt path is present and stable enough for Kickstart.

Faulty or risky:

- `DmaScheduler` is heuristic, not a fixed OCS slot table.
- Bitplane DMA consumes one abstract slot and fetches all active planes at once.
- Sprite/Copper/Blitter priority is approximate; Copper can occupy slots real hardware would reserve for sprites or other DMA.
- CPU stalls are currently global per chip slot, not based on whether the current CPU bus access actually needs Chip RAM.
- `VPOSR`/`VHPOSR` are simplified: no full LOF/interlace/long-short-frame/lightpen/Agnus-ID behavior.

Missing:

- Real PAL OCS DMA slot table: refresh, disk, audio, sprite, bitplane, Copper, Blitter, CPU.
- Plane-by-plane bitplane fetch slots for 1-6 planes.
- BLTPRI/nasty behavior.
- Region-aware CPU bus arbitration.
- Long/short frame and interlace details.

Plan:

- `P0`: Introduce an OCS PAL slot table and make `DmaScheduler` choose owners from that table.
- `P0`: Split bitplane fetch into per-plane DMA operations.
- `P0`: Route CPU stalls through a bus-access transaction type so ROM/Fast/IO cycles are not blocked like Chip RAM cycles.
- `P1`: Add BLTPRI/nasty and more faithful Blitter/CPU interleaving.
- `P1`: Implement accurate VPOS/VHPOS/LOF/interlace readback.
- `P2`: Add NTSC and ECS model variants only after OCS PAL is stable.

## Copper

Files:

- `CustomChips/Copper.cs`
- `Memory/CustomRegisterMap.cs`

Good:

- Copper has MOVE, WAIT, SKIP, COPJMP strobes, COP1/COP2 pointer reads, and trace events.
- Copper instruction fetch is two-phase.
- Copper can wait for Blitter finished.
- Copper-origin writes are routed to side effects for Paula/Blitter.

Faulty or risky:

- Copper protected register range is wrong. HRM OCS rules are: `$00..$3E` are never writable by Copper, `$40..$7E` are writable only when COPCON/CDANG is set, and `$80..$FF` are always writable. Current code blocks only `< $20`, gates `$20..$3E`, and allows `$40+`, so it is both too permissive for Blitter registers and too permissive for `$20..$3E` when CDANG is set.
- WAIT/SKIP mask handling still uses the current 8-bit beam approximation. A strict first pass that made vertical bit 7 unmaskable matched the HRM text but regressed the Kickstart insert-disk path with the current simplified beam model, so this must be fixed together with VPOS/LOF/long-short-frame work instead of as an isolated mask change.
- `COPJMP2` has a hardware-foreign invalid-target heuristic that can block legal low Chip RAM Copper lists.
- WAIT/SKIP behavior across high vertical ranges is not fully reliable.

Missing:

- Exact OCS protected register tests for `$00..$3E`, `$40..$7E` with CDANG clear/set, and `$80..$FF`.
- Correct BFD handling independent of vertical compare, including the vertical bit 7 rule once the beam counter model is complete enough.
- Exhaustive WAIT/SKIP tests.
- Removal or optionalization of non-hardware jump heuristics.

Plan:

- `P0`: Correct protected register logic to the OCS three-range rule: never `$00..$3E`, CDANG-gated `$40..$7E`, always `$80..$FF`.
- `P0`: Keep BFD blitter-finished behavior tested now; implement the vertical bit 7 unmaskable rule together with accurate VPOS/LOF/long-short-frame behavior so Kickstart timing remains stable.
- `P0`: Remove `COPJMP2` invalid-target heuristic or put it behind a debug-only compatibility guard.
- `P1`: Add Copper self-tests for WAIT, SKIP, BFD, high vertical lines, frame-end wait, protected moves.
- `P1`: Integrate Copper execution timing with the real DMA slot table.

## Blitter

Files:

- `CustomChips/Blitter.cs`
- `CustomChips/BlitterSelfTest.cs`
- `CustomChips/BlitterWordTraceEntry.cs`

Good:

- Block blits, minterms, masks, source constants, descending mode, fill modes, and line mode are implemented at useful logical fidelity.
- Recent fixes cover disabled-source latch behavior and line-mode D/C destination movement.
- Blitter execution is slot-driven rather than fully synchronous.
- Self-tests cover many Kickstart-relevant regressions.

Faulty or risky:

- Blitter word execution is still too atomic: channel reads/writes are not modeled as individual bus micro-operations.
- Channel order, early read behavior, destination timing, and busy/zero timing are approximated.
- BLTPRI/nasty coupling is missing.
- Full line-mode edge cases are not exhaustively tested.
- ECS-only features must not leak into A500 OCS mode.

Missing:

- Channel micro-op pipeline.
- BLTDDAT readback behavior.
- More exact line/fill behavior for demos and games.
- Timing relationship between Blitter, CPU, Copper waits, and DMA slots.

Plan:

- `P0`: Keep current logical Blitter as compatibility baseline, then refactor execution into channel micro-ops behind tests.
- `P0`: Add golden tests for Kickstart insert-disk blits and known line/fill edge cases.
- `P1`: Implement BLTPRI/nasty and CPU interleaving.
- `P1`: Implement BLTDDAT, better busy/zero timing, and exact interrupt timing.
- `P2`: Gate ECS-only big blits or DOFF-style behavior by model.

## Denise, bitplanes, sprites, collision

Files:

- `CustomChips/Denise.cs`
- `CustomChips/BitplaneUnit.cs`
- `CustomChips/SpriteUnit.cs`
- `CustomChips/CollisionUnit.cs`
- `Video/AmigaFrameBuffer.cs`
- `Video/VideoFilters.cs`

Good:

- Bitplane registers, DIW/DDF, BPLCON0/1/2, pointers, modulos, colors and DMA fetch path are wired.
- Odd/even modulo use matches the HRM: BPL1/3/5 and BPL2/4/6.
- Dual playfield basic separation exists.
- EHB-like color halving exists.
- Sprite priority by lower sprite number is roughly represented.
- Kickstart boot screen now renders through real bitplane data.

Faulty or risky:

- `Denise.Tick` is fixed lowres and ignores HIRES.
- Horizontal DIW is effectively ignored; rendering uses a fixed display start.
- HAM is not implemented.
- Sprite-vs-playfield priority is wrong: sprites always draw over bitplanes.
- Sprite rendering scans Chip RAM directly per pixel instead of using sprite DMA/latches.
- Manual sprite mode via SPRxPOS/SPRxCTL/SPRxDATA/SPRxDATB is missing.
- Collision is a stub; CLXCON/CLXDAT are not implemented as read-clear state.
- Mid-screen bitplane pointer changes are risky because line starts can be recomputed as `newPointer + row * stride`.

Missing:

- Real Denise pixel mixer with playfield classes, sprite groups, priority, transparency and collisions.
- Sprite DMA state machine and latches.
- HIRES, HAM, horizontal DIW, overscan, interlace field offsets.
- Correct EHB activation only for the valid mode.
- Real filters connected to SharpPixels presentation.

Plan:

- `P0`: Build a Denise pixel mixer that receives bitplane and sprite pixel classes, applies BPLCON2 priorities, and emits final color.
- `P0`: Implement CLXCON/CLXDAT and set collision flags from the same mixer.
- `P0`: Implement HIRES and horizontal DIW handling.
- `P1`: Replace direct sprite scanning with sprite DMA/latch model.
- `P1`: Fix mid-screen bitplane pointer write semantics.
- `P1`: Implement HAM and tighten EHB mode conditions.
- `P2`: Implement interlace/overscan and real presentation filters.

## Paula disk

Files:

- `CustomChips/Paula.cs`
- `Media/AdfImage.cs`
- `Media/AdfMfmEncoder.cs`
- `Media/AdfMfmTrack.cs`
- `Media/AdfMfmWordStream.cs`
- `Media/FloppyController.cs`
- `Media/FloppyDrive.cs`

Good:

- Standard 901120-byte ADF geometry is recognized.
- DF0 media path exists from UI to drive.
- DSKPTH/DSKPTL/DSKLEN/DSKSYNC/DSKBYTR are partially modeled.
- DSKLEN double-write arming exists.
- DSKBLK and DSKSYNC interrupt requests exist.
- Basic CIAB drive select, motor, side and step integration exists.
- ADF MFM track stream with sync, odd/even data, and checksums exists.

Faulty or risky:

- WORDSYNC likely transfers the sync word itself instead of starting with the following word.
- Resync during an active disk read is not modeled.
- DSKBYTR is word-oriented, but hardware is byte-ready oriented.
- DSKDAT is treated as readable even though it is the write-data register.
- Disk write path completes without actually writing.
- Step direction appears inverted against the A500 technical manual.
- Track/side changes reset stream position to track start; real rotation continues.

Missing:

- Byte/bitcell-level rotation model.
- Continuous disk rotational phase and index position.
- Correct DSKBYTR/DSKDATR behavior.
- Correct read DMA with WORDSYNC and resync.
- Write DMA and writable ADF support.
- DF1-DF3.

Plan:

- `P0`: Fix WORDSYNC so DMA starts after the matching sync word.
- `P0`: Make DSKBYTR byte-ready based and remove or correct DSKDAT read behavior.
- `P0`: Verify and fix CIAB step direction.
- `P1`: Keep rotation phase continuous across side/track changes.
- `P1`: Couple disk word/byte production to the real DMA slot model.
- `P1`: Add focused disk tests for sync, byte-ready, block IRQ, step direction, index pulse.
- `P2`: Implement write DMA, writable ADFs, DF1-DF3, and loader compatibility matrix.

## Paula audio

Files:

- `CustomChips/Paula.cs`
- `Audio/AudioResampler.cs`
- `Audio/PaulaAudioOutput.cs`
- `AmigaWindow.cs`

Good:

- Audio UI concepts exist: audio enable, filter, volume.
- DMA scheduler already reserves audio slots when audio DMA bits are set.
- Interrupt sources for audio channels exist in the custom interrupt enum.

Faulty or risky:

- Audio itself is almost absent. `Paula` only counts ticks.
- AUDxLC/AUDxLEN/AUDxPER/AUDxVOL/AUDxDAT are not modeled.
- Audio DMA slots can steal bus time without producing audio data.
- Audio UI settings are not meaningfully wired to Paula output.

Missing:

- Four-channel Paula state machine.
- DMA reload and manual mode.
- Period counters, volume, sample output, channel IRQs.
- ADKCON modulation.
- Host mixer, stereo routing, resampler, buffering, lowpass/LED filter.

Plan:

- `P0`: Implement AUD0-AUD3 registers and basic DMA playback state machines.
- `P0`: Consume audio DMA slots only when Paula really fetches audio data.
- `P1`: Add 8-bit sample output, volume, period timing, channel interrupts.
- `P1`: Add host mixer/resampler and wire UI volume/filter/audio enable.
- `P2`: Add ADKCON modulation, LED filter behavior, improved analog mixing.

## CIA 8520 A/B

Files:

- `Cia/Cia8520.cs`
- `Cia/CiaSelfTest.cs`
- `Input/AmigaInputDevice.cs`
- `Media/FloppyController.cs`

Good:

- CIA byte lane mapping and register index decode are present.
- CIA-A OVL handling is correctly coupled to the bus overlay.
- Timers, ICR, TOD latch/alarm and one-shot high-byte behavior are tested.
- CIA-A keyboard/input and CIA-B floppy control basics exist.

Faulty or risky:

- Serial/CNT/SP behavior is incomplete for full keyboard timing.
- FLAG/CNT edge timing is simplified.
- CIA pin configuration is generic rather than fully split into CIA-A and CIA-B board wiring.
- TOD source phase and exact 50/60 Hz details are simplified.
- Some drive-control polarity details still need verification.

Missing:

- Complete keyboard serial protocol timing.
- Full CNT/SP shift register behavior.
- Exact FLAG and TOD source handling.
- Model-specific pin configuration and board wiring.

Plan:

- `P0`: Verify CIA-B floppy line polarity and step direction against the A500 technical manual.
- `P1`: Split CIA pin wiring into CIA-A and CIA-B configuration classes.
- `P1`: Implement serial/CNT/SP timing for keyboard and possible peripheral use.
- `P1`: Tighten TOD source/phase behavior.
- `P2`: Add more CIA edge-case self-tests and golden traces.

## 68000 CPU

Files:

- `Cpu/M68kCpu.cs`
- `Cpu/M68kSelfTest.cs`
- `Cpu/IM68kBus.cs`
- `Cpu/M68kDecoder.cs`
- `Cpu/M68kState.cs`

Good:

- Important boot opcodes and supervisor/user state are implemented.
- Address registers are internally 32-bit with external 24-bit masking.
- Interrupts, address error frame, MOVEM/MOVEP, DIV/MUL and many Kickstart-oriented tests exist.
- Decoder and trace support are useful for debugging.

Faulty or risky:

- `CLR` does not perform 68000 read-before-write.
- CPU bus model is too coarse: no FC/UDS/LDS/VPA/DTACK/BERR transaction layer.
- DMA arbitration is not tied to actual operand bus accesses.
- Reset path contains a Kickstart-specific stack hack rather than pure reset-vector semantics.
- Opcode/flag/timing matrix is not complete enough for broad game/demo compatibility.

Missing:

- Full opcode matrix including BCD, CHK, TRAPV, RTR, TAS and edge cases.
- Prefetch and bus-cycle timing.
- Read-modify-write behavior.
- Accurate exception frames for all relevant exceptions.
- Differential CPU tests against known-good 68000 behavior.

Plan:

- `P0`: Fix `CLR` read-before-write and audit all RMW instructions.
- `P0`: Introduce bus transaction API with access type, size, region, and cycle intent.
- `P1`: Add prefetch-aware timing and per-operand bus-cycle traces.
- `P1`: Fill missing opcodes and exception frames.
- `P2`: Add differential 68000 tests and golden traces.

## Gary, memory and expansion

Files:

- `Memory/AmigaBus.cs`
- `Memory/MemoryMap.cs`
- `Memory/ChipRam.cs`
- `Memory/SlowRam.cs`
- `Memory/FastRam.cs`
- `Memory/KickstartRom.cs`
- `Core/KickstartRomResolver.cs`

Good:

- Kickstart overlay and normal ROM mapping work well enough for boot.
- Chip RAM and optional Slow RAM storage exist.
- CIA/custom register routing is clear.
- Kickstart ROM probing and AppData ROM import exist.

Faulty or risky:

- Gary is not represented as a bus decode chip.
- Memory map is static and incomplete for mirrors, expansion, RTC/A501, Autoconfig.
- Fast RAM UI exists but core Fast RAM is size zero and not routed.
- Chip/Slow RAM silently wrap on direct storage accesses.
- ROM selection UI exposes more versions than loader behavior reliably supports.

Missing:

- Gary decode model.
- Model-dependent memory region table.
- A501 slow RAM and RTC behavior.
- Fast RAM expansion and Autoconfig.
- Open bus and odd-address/bus error behavior integrated with CPU transactions.
- Strict Kickstart ROM database.

Plan:

- `P0`: Either disable unavailable Fast RAM UI modes or implement core Fast RAM mapping.
- `P1`: Replace static memory constants with model-dependent region table.
- `P1`: Add Gary-like decode layer for overlay, Chip, Slow, CIA, custom, ROM and expansion.
- `P1`: Remove silent wrapping from DMA-facing helpers or assert invalid accesses.
- `P2`: Add A501 RTC, Autoconfig and strict ROM database.

## Cross-cutting verification plan

Immediate test assets:

- Keep `--assert-kickstart-screen-path`.
- Keep `--dump-frame` and `--dump-bitplane-memory-image`.
- Extend `--trace-blits`, `--trace-copper`, `--trace-custom-regs`, `--diagnose-adf-boot`.

New tests to add:

- Copper: protected moves across `$00..$3E`, `$40..$7E`, `$80..$FF`, BFD, high vertical waits after VPOS/LOF work, SKIP behavior.
- DMA: slot table owner sequences for known PAL lines.
- Bitplane: 1-6 plane fetch pressure and mid-screen pointer changes.
- Denise: sprite/playfield priority, CLXCON/CLXDAT, HIRES, HAM.
- Paula disk: WORDSYNC following-word behavior, DSKBYTR byte-ready, step direction.
- Paula audio: AUDx DMA fetch, period timing, channel IRQ.
- CPU: CLR read-before-write, RMW family, BERR/odd address, missing opcodes.
- Memory: overlay transitions, Slow/Fast RAM mapping, no unintended wrapping.

Recommended implementation order:

1. Copper correctness fixes: exact OCS protected ranges, BFD blitter wait, remove jump heuristic; defer V7 until the beam counter model is upgraded.
2. Disk correctness fixes: WORDSYNC, DSKBYTR, step direction.
3. DMA slot table foundation.
4. Denise mixer plus collision.
5. Bitplane and sprite DMA/latch rework.
6. Paula audio baseline.
7. CPU transaction bus and RMW fixes.
8. Gary/memory expansion model.

This order keeps the current Kickstart boot screen stable while attacking the highest-risk compatibility blockers first.
