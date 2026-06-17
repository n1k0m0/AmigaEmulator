# A500 graphics path

Goal: render the Kickstart 1.3 boot display only through the emulated A500 hardware path.

## Hardware path

The intended flow is:

1. Kickstart and graphics.library build View, ViewPort, intermediate CopList, and real CprList structures in Chip RAM.
2. CPU writes COP1LC/COP2LC, bitplane registers, sprite pointers, color registers, DMA/interrupt control, and blitter registers through the custom register map.
3. Agnus arbitrates DMA slots and performs Copper, blitter, sprite, audio, disk, and bitplane bus activity.
4. Bitplane DMA fetches Chip RAM words into the Agnus/Denise display path.
5. Denise shifts fetched bitplane data, applies BPLCON/DIW/DDF/color/sprite state, and writes final pixels into the frame buffer.

No boot image helper or vector painter is part of this path.

## Implementation concept

The emulator must keep the same ownership boundaries as the A500 hardware:

1. CPU/ROM side: Kickstart, Exec, Intuition, and graphics.library own View, ViewPort, CopList, CprList, bitmap, sprite, and color data in RAM.
2. Register side: CPU and Copper only communicate with the chipset through the custom registers and Chip RAM pointers.
3. Agnus side: DMA arbitration chooses which unit owns each chip bus slot; Copper and bitplane DMA fetch only when their DMA channels and beam windows allow it.
4. Denise side: Denise never invents boot graphics. It shifts the bitplane words fetched by Agnus, applies BPLCON/DIW/DDF/COLOR state, mixes sprites, and writes final ARGB pixels to the host framebuffer.
5. Verification side: boot screenshots are only valid when BPL pointers, COLOR registers, DMACON, and Copper trace all explain the visible pixels.

## Current implementation state

- The old Kickstart vector boot renderer has been removed.
- Copper execution now traces real instruction fetches and no longer falls back from COPJMP2 to COP1LC.
- CPU or Copper writes to COPJMP1/COPJMP2 are now treated as hardware strobes and reload the Copper PC from COP1LC/COP2LC.
- Copper instruction fetch now uses a two-phase word latch: one DMA slot fetches IR1, a later Copper slot fetches IR2 and executes the instruction.
- Copper blocks protected register writes below $40 unless COPCON/CDANG allows them.
- Bitplane DMA has priority over Copper in the simplified DMA scheduler, matching the important hardware ordering for display fetch slots.
- Bitplanes now have an explicit DMA fetch path: Agnus fetches words during bitplane DMA slots, Denise renders from those fetched words.
- Bitplane DMA now advances internal per-plane line pointers and applies odd/even modulo at end of each fetched display line instead of reading arbitrary pixels directly from Chip RAM.
- CPU self-tests include the graphics.library LoadView nested CprList pointer pattern.
- CPU self-tests now also cover graphics.library-style indexed addressing, memory BTST, and STOP wakeup through a level 3 interrupt.
- Custom register set/clear byte writes no longer fold current readback bits into the write value. `DMACON` now stores only writable DMA control bits; blitter busy/zero remain dynamic `DMACONR` status.
- Blitter startup now respects write size: `BLTSIZE` starts on word writes or low-byte writes only, avoiding half-old size starts.
- Copper self-jumps no longer route through the CPU register write callback; Copper-side `COPJMPx` moves load the target list directly.
- CPU self-tests now cover illegal effective-address side effects for `MOVE`, `LEA`, and bit operations, and invalid internal CPU exceptions now vector through 68000 vector 4.
- The boot assertion now rejects nonzero but non-executable Copper targets such as `$0676`/OS structures instead of treating them as valid raw lists.
- Diagnostic CLI now has `--diagnose-kickstart-display [cycles] [--model a500|slow512]` and `--dump-words <address> <words> [cycles]`.
- CPU effective-address validation is stricter for 68000 data-alterable and memory-alterable destinations: immediate ALU/CMPI, `OR Dn,<ea>`, `CLR`, `TST`, `NEGX`, `NEG`, and `NOT` no longer allow illegal PC-relative/immediate/address-register destinations.

## Current boot result

Kickstart now reaches the real insert-disk display path:

- `--run-until-pc FE861A 300000000 1` reaches the ROM write that enables bitplane DMA with overlay disabled.
- The real Copper list at `$010450` sets the final palette, DIW/DDF, BPL pointers, and two-plane display enable.
- At 90M cycles the stable display state is `BPLCON0=$2302`, `BPL1PT=$0000A572`, `BPL2PT=$0000C4B2`, and colors `0=$0FFF`, `1=$0000`, `2=$077C`, `3=$0BBB`.

The insert-disk bitmap now renders from the real Kickstart/graphics.library path. The visible `--dump-frame 120000000` result is the genuine Kickstart 1.3 hand/disk image with `V 1.3`, produced through COP2, bitplane DMA, palette registers, and the bitmap that Kickstart builds in Chip RAM.

The blocker was the Blitter source-data latch semantics for disabled source channels:

- Disabled source channels correctly use the preloaded `BLTxDAT` constant for the minterm input.
- The emulator incorrectly wrote the masked A input back into `BLTADAT` even when channel A DMA was disabled.
- Kickstart uses small mask blits with A disabled and `BLTADAT=$FFFF`; corrupting that latch made later masks decay into broken full-span/fragmented graphics.
- `BlitterSelfTest` now covers this with `disabled source mask does not corrupt constant latch`.

## Latest release verification

- `dotnet build .\AmigaEmulator\AmigaEmulator.sln -p:Configuration=Release -p:Platform=x64 -p:UseAppHost=false` passes.
- `dotnet .\AmigaEmulator\AmigaEmulator\bin\x64\Release\AmigaEmulator.dll --self-test-cpu` passes.
- `dotnet .\AmigaEmulator\AmigaEmulator\bin\x64\Release\AmigaEmulator.dll --self-test-blitter` passes, including the disabled-source latch regression.
- CIA TOD writes no longer raise false TOD-alarm interrupts while Kickstart is programming the clock/alarm registers.
- CIA 8520 one-shot timers now start on timer high-byte latch writes when one-shot mode is armed; this restores the CIA-A Timer B interrupt Kickstart waits for before the boot display path.
- Blitter line mode now keeps the D write destination in step with the C source pointer for line drawing, which fixes the corrupt/inverted Kickstart disk bitmap built in Chip RAM.
- `--run-until-pc FE861A 300000000 1` passes and reaches the ROM bitplane-DMA enable path.
- `--assert-kickstart-screen-path 120000000 --model a500` passes by checking the real hardware path: raw Copper list, bitplane pointers, bitplane DMA, lowres two-plane mode, and boot palette.
- `--dump-frame 120000000 .\tmp\kickstart_release_final_120m.png --model a500` renders the real Kickstart 1.3 insert-disk screen.
- `--dump-bitplane-memory-image 120000000 .\tmp\bitplane_memory_release_final_120m.png --model a500` confirms the bitmap in Chip RAM matches the visible frame.
- `Program.cs` now includes `--trace-blits cycles maxBlits startCycle [filterStart filterEnd]` for focused Blitter-start diagnostics.
- The CPU now keeps address registers internally 32-bit while still masking external A500 bus addresses to 24-bit; a self-test covers that distinction.
- Blitter execution is now DMA-slot driven instead of being completed synchronously at `BLTSIZE`. Machine trace confirms real `DmaOwner=Blitter` slots around the first Kickstart blit. This did not by itself populate `$49A6/$49C0`, so the next blocker is still upstream View/ViewPort construction or a missing hardware semantic that affects that construction.
- `--trace-until-fault 5600000` finds no suspicious CPU instruction before the failing screen/list setup.
- `--trace-custom-regs 5600000 180` shows `DMACONR` blitter busy toggling around the early Kickstart blit and no obvious blitter hang.
