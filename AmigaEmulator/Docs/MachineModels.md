# Machine models: implementation and validation

Status: 2026-10-09. Select a machine through **Esc -> MACHINE -> MODEL**.
Changing the model restarts the guest. Every model except A500 is currently
marked **PREVIEW**. Preview means incomplete hardware/software coverage.

| Menu model | CPU and PAL clock | Chipset | Motherboard devices |
| --- | --- | --- | --- |
| A500 | 68000, 7.093790 MHz | OCS | Optional synthetic Gayle IDE expansion |
| A500+ | 68000, 7.093790 MHz | ECS subset | MSM6242 battery clock |
| A600 | 68000, 7.093790 MHz | ECS subset | Gayle IDE; PCMCIA SRAM |
| A2000 OCS | 68000, 7.093790 MHz | OCS | MSM6242 clock; independent 50 Hz CIA-A TOD |
| A2000 ECS | 68000, 7.093790 MHz | ECS subset | MSM6242 clock; independent 50 Hz CIA-A TOD |
| A1200 | 68EC020, 14.187580 MHz | AGA subset | Gayle IDE; PCMCIA SRAM |
| A4000/030 | 68EC030, 25 MHz | AGA subset | DD2020 IDE; Gary/Ramsey registers; motherboard Fast RAM; RP5C01 clock |
| A3000/030 | 68030, 25 MHz | ECS subset | WD33C93A/Super DMAC SCSI target 0; Gary/Ramsey; motherboard Fast RAM; RP5C01 clock; independent 50 Hz CIA-A TOD |

**A4000/040 is not implemented and is not exposed in the selector.** It needs
a distinct 68040 CPU implementation. A3000 and the AGA models are previews;
Native A1200, A3000 and A4000/030 Kickstart 3.1 boot to Initial CLI is verified
on the formatted test HDF. An installed Workbench/OS remains untested.
The 68EC030 variant advertised for A4000/030 has no PMMU or integrated FPU.

## Memory and CPU

AGA boards and A3000 require 2 MiB Chip RAM. A1200 accepts optional 2/4/8 MiB Fast RAM
through the existing emulated Zorro II-style expansion; this is not an exact
trapdoor accelerator board implementation. A3000/A4000/030 Fast RAM uses 32-bit
motherboard addresses ending at $08000000, without advertising a Zorro II card.
The selectable 2 MiB Fast configuration is a synthetic reduced RAM layout;
physical Ramsey SIMM bank geometry is not fully modeled. Zorro III is absent.
A2000 OCS supports 512 KiB Chip RAM and optional 512 KiB Slow RAM; ECS models
use 1/2 MiB Chip RAM. Legacy enum and RAM-profile IDs retain their values.

The extended integer CPU implements full/scaled indexed addressing, odd data
accesses, long branches, bitfields, CAS/CAS2, long multiply/divide, MOVES,
CMP2/CHK2, PACK/UNPK, MOVEC and extended exception/stack formats. Instruction
fetch alignment remains strict. EC030 adds ACU PMOVE/PTEST and cache-control
register semantics. Physical addresses remain 32-bit on 68020/EC030 and wrap
at 24 bits on 68000/EC020. CPU clocks are scheduled independently of the PAL
chip clock; a Chip RAM transfer still requires its DMA slot.

Timing is approximate: instruction/data caches, pipeline timing, complete
restartable fault frames, CALLM/RTM and optional floating-point coprocessors
are not fully implemented. Do not advertise these paths as CPU conformance.

`Cpu/M68030Mmu.cs` is connected to full-68030 instruction and data accesses.
PMOVE/PLOAD/PTEST/PFLUSH and MOVES function codes are implemented. It supports
short/long and indirect translation descriptors, early/root mappings,
root limits, function-code tables, transparent windows, U/M updates, access
protection and 22 cached translations. Its ATC replacement is currently FIFO.
Faults enter a short format-A frame containing logical address, function code
and transfer status. Full format-B pipeline/partial-operand restart is pending;
this is not sufficient for arbitrary demand-paged operating systems.

## Graphics

ECS adds chipset identification, DIWHIGH high bits and explicit write history,
finer DDF alignment, BLTCON0L/BLTSIZV/H, border blank, KILLEHB and sprite/window
clipping. Legacy DIW writes release the DIWHIGH interlock. OCS retains its own
pixel-composition path.

AGA adds 256 24-bit colors, bank/LOCT writes, RDRAM readback, eight bitplanes,
HAM8, deeper dual playfields, BPLAM XOR, extended collision masks, sprite color
banks and 16/32/64-bit DMA groups. Wide sprites retain complete header and
pending data groups across restore; position/resolution controls are sampled
at the existing hires framebuffer resolution. Playfield XOR leaves COLOR00
borders and HAM seeds unchanged.

Native superhires output, complete extended fine scrolling, interlace,
programmable monitor timing, scan doubling and genlock remain incomplete.
Sprite output retains its bitmap/position separately from subsequent Copper
register writes. VSTOP uses an exact beam comparison; a zero stop cannot create
a fictitious line-256 fetch. Early DATA writes now initialize the new scanline
before arming the sprite, preventing DMA bookkeeping from erasing the output.
These fixes remove the Celtic Heart background gaps and lower-screen corruption
in the tested gameplay. The A2000 OCS/Kickstart 3.1 CLI column defect remains
open. A successful script run is not a claim of universally glitch-free gameplay.

## Storage, clocks and PCMCIA

A600/A1200 map Gayle ATA PIO. A4000/030 maps its task file at $DD2020, alternate
status/control at $DD303A and IRQ status at $DD3020. ATA status acknowledges
the interrupt; peeks and alternate status retain it. Read-only errors,
multi-sector CHS/LBA transfers, partial FIFO save/restore and soft reset are
covered. HDF files contain the actual persistent sectors. A same-image
read-only/writable mode change releases the controller's own exclusive handle
and attempts to recover the previous mount if reopening fails.

A3000 maps Super DMAC at $DD0000 and WD address/status and data ports at
$DD0041/$DD0043 and their four-longword byte mirrors, including the native-ROM
SASR access at $DD0049. Selection completion (11) precedes the first target
phase request (8A/8E); combined completion precedes bus disconnect (85) with
EDI clear. Pending interrupts survive controller state version 3; versions 1/2
remain readable. Message-in pauses with ACK asserted (20) until Negate ACK.
Target 0 supports INQUIRY, REQUEST SENSE, READ CAPACITY,
MODE SENSE, READ/WRITE(6)/(10), basic positioning and cache synchronization.
PIO and DMA preserve sector contents, partial transfers and read-only sense
errors. DMA uses physical Chip/motherboard Fast RAM; Chip transfers wait for
available shared slots. Missing targets return selection timeout. Synchronous
SCSI timing, intermediate disconnect/reselection, multiple targets, target mode, detailed
FIFO/terminal-count behavior and all MODE SENSE pages remain incomplete.
The historical `AmigaIdeModel` enum retains its name for saved controller IDs;
its A3000Scsi entry selects SCSI and exposes no IDE aperture.

Gary/Ramsey expose register latches and the Ramsey-07 revision. DRAM refresh,
page/burst timing and Gary timeout-induced bus errors are still approximate.

MSM6242 clocks use the legacy odd-byte mapping. The desktop RP5C01 uses the
fourth byte lane of each 32-bit register at $DC0003 + register*4, with calendar,
alarm-control, 12/24-hour mode and two independent 13-nibble battery RAM banks.
Both clocks advance lazily from monotonic host time. Calendar/control/RAM and
divider phase survive save/restore; battery files also account for elapsed UTC
time between application sessions. They live outside Git in the user's
`clocks` directory. Machine reset preserves battery data. Clock output pins,
BUSY/test-frequency timing and subsecond HOLD/stop details are not cycle exact.

PCMCIA supports 1/2/4 MiB SRAM, CIS/attribute byte lanes, card reset, detection,
independent Gayle interrupt latches and save/restore. SRAM conflicts with the
8 MiB expansion aperture; the frontend prevents that combination. PCMCIA ATA,
network adapters, full hot-plug timing and separate persistent card images are
not implemented.

## ROM selection and save compatibility

Use ROMs for the actual motherboard. Automatic AGA/A3000 ROM selection uses known
SHA-1 fingerprints rather than filenames; explicit custom selection remains
available. The local `KICK30.ROM` and `KICK31.ROM` are identical A500/A600/A2000
40.63 images and are not verified A1200 ROMs. The available `KICK4070.ROM`
contains an A4000 IDE driver and boots, but its hash differs from the standard
40.70 image. Keep that distinction in test reports.

The user's [firmware archive](https://archive.org/download/commodore-amiga-firmware)
provided these complete, unmodified 512 KiB Kickstart 3.1/40.68 images. ZIP
contents, SHA-1 identities and the internal end-around-carry checksum ($FFFFFFFF)
were checked before installation in `%APPDATA%/AmigaEmulator/roms`:

| Model | Local filename | SHA-1 |
| --- | --- | --- |
| A1200 | KICK31-A1200.ROM | E21545723FE8374E91342617604F1B3D703094F1 |
| A3000 | KICK31-A3000.ROM | F8E210D72B4C4853E0C9B85D223BA20E3D1B36EE |
| A4000/030 | KICK31-A4000.ROM | 5FE04842D04A489720F0F4BB0E46948199406F49 |

The existing automatic model selector recognizes all three identities. ROMs
remain external user data; existing ROMs were preserved.

Settings version 6 stores model and RAM separately. Public savestate file
version 2 records the exact motherboard. Core version 14 stores the full model
header and running sprite output shifters for every board. Core versions 1–13,
including earlier A500 files, remain readable. Older emulator builds reject the
new core version. Wrong CPU/clock/chipset/IDE/RAM/motherboard loads
are rejected before RAM mutation or disk reattachment. Development combinations
that cannot be reconstructed from public metadata must use the diagnostic
core-state API. Development state layouts are not a stable interchange format.

## Verification

- 210 standard regression tests pass, including CPU/MMU, AGA, model, RTC,
  PCMCIA, physical address, descriptor, A4000 PIO and A3000 SCSI/DMA cases.
  New fixtures cover running sprite tails/restores, early Copper DATA, exact
  stop comparisons, independent AGA border composition and packed bitplane output.
- The local strict-bus Siedler golden test checks wooden frame/menu alignment.
- Nine legacy Siedler/Moonstone/Alien Breed benchmark checkpoints retain the
  exact pre-model framebuffer hashes and CPU program counters.
- A4000/030 boots a read-only, independently formatted FFS/RDB test HDF to
  AmigaDOS Initial CLI with 0/4/8 MiB Fast RAM. Cold/warm reset and core
  save/restore retain the console image. The 0/4/8 MiB sweep used the existing
  modified 40.70 ROM; native 40.68 was also tested with 4 MiB Fast RAM.
  No Workbench files were installed.
- The independently encoded `RTCCheck` guest executable reports `RTC PASS`
  through Kickstart's battclock.resource on A500+, A2000 and A4000/030.
- A1200 reaches Initial CLI with its matching 40.68 ROM and 4 MiB Fast RAM.
  Cold/warm restart, core restore and continued output reproduce the same
  console image and 14-sector boot reads. Previous generic 40.63 compatibility
  tests remain separate. Solid Gold reaches gameplay, responds
  to joystick input and continues after restore on this compatibility setup
  and on A4000/030. Celtic Heart's sprite defects are corrected. Both games
  were retested from gameplay checkpoints on A1200, A3000 and A4000/030 with
  directional/jump input, restore and continued movement. Screenshots were
  inspected; the Celtic Heart hills are continuous and the bottom is clean.
- ECS model boot/expansion sweeps and Solid Gold/Celtic Heart scenarios are
  recorded separately. Review PNGs: menu/credits/disk prompts are not gameplay.
- A3000 reaches Solid Gold gameplay with the generic 40.63 compatibility ROM.
  The original SCSICheck boot block reads an RDB sector from target 0 via DMA
  and verifies RDSK in physical Chip RAM. Its green result proves this guest
  hardware path, not native A3000-ROM or installed-OS boot.
- Native A3000 40.68 reaches Initial CLI through SCSI target 0. The original
  black-screen failure came from ignored $DD0049 selector writes and missing
  phase-service/disconnect interrupts, leaving scsi.device waiting for an event.
  Cold/warm restarts and core restore reproduce the reviewed CLI image with
  14 HDF sector reads for 2 MiB Chip alone and each 2/4/8 MiB Fast configuration.
  Keyboard input in the restored console is functional.
  With no HDF mounted, 2 MiB Chip alone and 4 MiB Fast configurations show the
  real Kickstart 3.1 insert-disk animation instead of the original black output.
- Solid Gold and Celtic Heart also cold-boot to gameplay with native A3000
  40.68, respond to directional/jump input, restore and continue moving.
  Reviewed screenshots retain clean hills/ground in Celtic Heart. Neither game
  raises further synchronous exceptions during the recorded input/restore
  steps; their boot traces include Kickstart's normal CPU/privilege probes.

All ROMs, ADFs, HDFs, test binaries, captures and diagnostic dependencies remain
outside the repository. No proprietary ROM/OS content was copied into Git.
Throughput measurements are host-sensitive; there is no confirmed
performance-gain claim for this model expansion. Repeat the paired benchmark
when optimizing further, and investigate reproducible regressions over 5%.
Completed DMA words now cache packed color indices, and internal bitplane/sprite
register reads bypass CPU bus read decoding. CPU/model capabilities and clocks
are cached at construction; 68000 ticks omit extended-CPU bus-use bookkeeping.
Alternating 25-frame measurements vary by run and game: Moonstone improved,
while Siedler and Alien Breed do not establish a consistent gain. Remaining
throughput regressions need further work. Identical framebuffer/CPU checkpoints
establish functional consistency, not equal FPS.

Reproduce the automated suite from the solution directory:

```powershell
dotnet build AmigaEmulator.Tests/AmigaEmulator.Tests.csproj -c Release -p:Platform=x64
dotnet AmigaEmulator.Tests/bin/x64/Release/net10.0-windows7.0/AmigaEmulator.Tests.dll
```

`Tools/model-validation-scenarios.json` and `Tools/model-gameplay-scenarios.json`
cover the legacy/ECS matrix. `Tools/aga-validation-scenarios.json` contains the
AGA boot/reset/RTC cases; `Tools/aga-gameplay-scenarios.json` covers gameplay.
`Tools/a3000-scsi-scenarios.json` and `Tools/a3000-gameplay-scenarios.json`
cover the SCSI guest diagnostic and generic/native-ROM gameplay. Local diagnostic HDF
paths must be adapted. Generate the SCSI ADF using
`Tools/GuestDiagnostics/build_scsi_check_adf.py`; it contains no ROM/OS content.
Use GameRunner with a fresh output directory and inspect screenshots manually.
`Tools/native-rom-validation-scenarios.json` reproduces the native 40.68
HDF/reset/restore checks with reviewed framebuffer and sector-read assertions,
all four A3000 RAM layouts and the no-HDF boot checks.

## Remaining implementation order

1. Complete restartable 68030 format A/B bus faults, cache semantics and ATC
   replacement using independent hardware fixtures.
2. Verify installed OS coverage on A3000, then add negotiated
   SCSI timing, disconnect/reselection and complete target command coverage.
3. Expand native A1200/AGA software and installed-OS coverage. Optional
   68881/68882 support must be explicit; no FPU is currently emulated.
4. Complete ECS/AGA missing output modes and known shared graphics defects;
   add independent raster/reference fixtures and native AGA software tests.
5. Implement A4000/040 CPU/MMU/FPU separately, then Zorro III and larger RAM
   configurations with actual guest enumeration.

## Hardware references

Implementation is independent. Manufacturer documents establish register and
instruction behavior; Linux drivers identify Amiga IDE wiring.

- [Motorola M68000 PRM](https://www.nxp.com/docs/en/reference-manual/M68000PRM.pdf)
- [Motorola MC68020 manual](https://www.nxp.com/docs/en/data-sheet/MC68020UM.pdf)
- [Motorola MC68030 manual](https://www.nxp.com/docs/en/reference-manual/MC68030UM.pdf)
- [Commodore AGA functional specification](https://shanson.com/spencer/Amiga-AA-Chipset.pdf)
- [Commodore Hardware Reference Manual, third edition](https://www.ikod.se/wp-content/uploads/2020/08/Amiga_Hardware_Reference_Manual_3rd_Edition.pdf)
- [Commodore A4000 service specification](https://support.retrorewind.ca/media/commodore/amiga/a4000_service_manual.pdf)
- [Commodore ECS Denise specification](https://www.amigawiki.org/lib/exe/fetch.php?media=de%3Aparts%3Adenise_specs.pdf)
- [MSM6242 datasheet](https://www.syntax.com.tw/upload/pdf/IC-6242.pdf)
- [Ricoh RP5C01 datasheet](https://map.grauw.nl/resources/rtc/ricoh_rp5c01.pdf)
- [Linux pata_gayle driver](https://github.com/torvalds/linux/blob/master/drivers/ata/pata_gayle.c)
- [Western Digital WD33C93A datasheet](https://www.msxarchive.nl/pub/msx/docs/datasheets/wd33c93a.pdf)
- [Linux A3000 hardware register mapping](https://github.com/torvalds/linux/blob/master/drivers/scsi/a3000.h)
- [Commodore A3000+ system specification, backward-compatible SDMAC register table](https://www.theflatnet.de/pub/cbm/amiga/hayniefiles/research/a3000p/docs/a3000p.pdf)
