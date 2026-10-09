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
on the formatted test HDF. A4000/030 installed AmigaOS 3.1.4 now passes basic
desktop, application, file-persistence and cold/warm/restore checks below;
installed-OS coverage on every model remains open.
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
protection and 22 cached translations. Level-zero PTEST converts cached table
faults to the hardware ATC B/I status and distinguishes them from a cache miss;
table-search PTEST remains separate. Its ATC replacement is currently FIFO.
Faults enter a short format-A frame containing logical address, function code
and transfer status. TAS/CAS/CAS2 locked reads check write permission before
changing condition codes or compare registers; their faults identify the RMW
read in the SSW. Full format-B pipeline/partial-operand restart is pending;
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

AGA BPLCON1 now decodes the full eight-bit, 35 ns delay for independent odd/even
planes in lores and hires. Fractional phases also invalidate the compositor's
lores pixel-pair optimization. All 256 delay codes are checked against an
independent register/bitstream fixture, including restore and final composition.
Native superhires output, interlace,
programmable monitor timing, scan doubling and genlock remain incomplete.
Sprite output retains its bitmap/position separately from subsequent Copper
register writes. VSTOP uses an exact beam comparison; a zero stop cannot create
a fictitious line-256 fetch. Early DATA writes now initialize the new scanline
before arming the sprite, preventing DMA bookkeeping from erasing the output.
These fixes remove the Celtic Heart background gaps and lower-screen corruption
in the tested gameplay. The A2000 OCS/Kickstart 3.1 duplicate CLI column is
fixed: legacy horizontal DIW comparators clip DDF fetches on OCS as well as ECS.
The corrected Initial CLI image survives cold/warm restart and core restore. A successful script run is not a claim of universally glitch-free gameplay.

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

- 219 standard regression tests pass, including CPU/MMU, AGA, model, RTC,
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
  That original controller test volume had no Workbench files installed;
  the separately prepared user OS volume below is not an A4000 OS pass yet.
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
dotnet AmigaEmulator.Tests/bin/x64/Release/AmigaEmulator.Tests.dll
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

## Approved roadmap progress (2026-10-09)

The roadmap remains active; the work below does not complete the full hardware
expansion. The user supplied the purchased AmigaOS 3.1.4 A500/A600/A2000
package on 2026-10-09 and subsequently supplied a source for matching 3.1.4
ROMs. The requested A4000/030 installation now passes the basic OS checks;
broader application and all-model storage coverage remain open.

| Stage | Evidence and current status | Remaining acceptance work |
| --- | --- | --- |
| 1. Known defects | A2000 OCS CLI clipping fixed; cold/warm/restore reference assertions; Siedler wood/menu/pointer test passes | Fix defects discovered by the broader matrix |
| 2. Native gameplay | Tinyus and Sqrxz 2 swept on all eight boards with model-specific ROMs, movement/fire and restore | Tinyus/new-Kickstart/68000 blank output remains open; investigate Sqrxz 2 text output; native AGA-specific software, longer sessions and disk swaps |
| 3. Performance | Cache unchanged display controls and opcode-derived descriptions; remove bitfield-name arrays; restore respects host trace settings; unobserved blitter tracing no longer allocates; paired CPU/frame hashes match | Continue profiling remaining allocations and repeat measurements after further changes |
| 4. Installed OS | Purchased AmigaOS 3.1.4 installed on a new A4000/030 HDF; separate A3000/SCSI copy passes native 46.143 boot, Shell, Clock, ShowConfig, persistence and cold/warm/restore | Broader applications, install/boot on remaining models and complete storage-controller coverage |
| 5. ECS/AGA modes | Extended AGA lores/hires scrolling and fractional compositor phases implemented and independently tested | Native superhires, interlace fields/weaving, programmable timing, scan doubling, additional references |
| 6. 020/030 correctness | Integer/MMU tests pass; locked-read protection faults and cached PTEST B/I status fixed; guest PMMU instruction and repair fixtures pass | Actual instruction/data caches, timing, ATC replacement and restartable format A/B faults |
| 7. Further hardware | Dependencies and test gates remain explicit; unsupported models stay unavailable | Optional 68881/68882, distinct 68040 CPU/MMU/FPU, then Zorro III enumeration |

### Gameplay evidence

`Tools/run-model-gameplay-matrix.ps1` builds a manifest with ROMs from external
user data, deterministic existing input sequences and post-restore movement.
Default cases are Tinyus and Sqrxz 2. Captures were reviewed for all 16 cases:
Sqrxz 2 reaches gameplay on all eight boards, but its text region still needs a
reference comparison. Tinyus initially reaches its space stage on all eight
boards. On A500/A2000 OCS with 1.3 and A1200/A3000/A4000 with native 3.1,
the full movement/fire/restore sequence keeps visible gameplay. On the three
68000 ECS boards with 2.0/3.1, an additional Fire input is followed by blank
output. The guest CPU explicitly changes Copper BPLCON0 from $5200 to $0200
(zero planes); the renderer follows that request. This also reproduces with
the pre-optimization core. Continued play without the second Fire retains
visible enemies through another 80 million chip clocks. Longer waits after
the blank transition and added Fast RAM do not resolve it. An
A600/1.3 control reaches gameplay; an A500/3.1 control reproduces the blank
output. This isolates the failure to the newer-Kickstart/68000 path rather than
ECS rendering. These are diagnostic controls, not recommended motherboard ROMs.
The exact guest-side transition failure is still unresolved; this is not a
clean compatibility pass on the affected configurations.
A600 with only 1 MiB Chip reports the game's own insufficient-Fast-memory error.
Menu images and black frames do not count as successful gameplay.

`Tools/a2000-ocs-cli-scenarios.json` reproduces the corrected, reviewed OCS CLI
on the external formatted test HDF. It asserts cold/warm boot, core restore and
continued output. The volume and ROM remain external; replace the reference
hash only after reviewing a different test volume.

### Paired performance measurements

`Tools/BenchmarkRunner` restores the model/RAM from metadata, explicitly applies
Compatible without CPU/DMA tracing, warms the guest and measures consecutive
frames using Stopwatch timestamps. JSON lines include FPS, median/p95 frame
time, thread allocations, all CPU registers/last instruction and the presented
ARGB hash. Run the identical tool against both core builds. Keep guest media,
profile, warmup and frame counts identical; alternate the build order and avoid
concurrent emulation jobs for final comparisons. The old CLI `--profile-savestate`
now also obtains the model/RAM from the file rather than defaulting to A500.

Local results: three alternating runs per build, each with 100 warmup frames and
three 100-frame samples (nine samples per row). The control already includes the
intentional OCS DIW fix; it excludes the subsequent performance changes.

| Checkpoint | Control median FPS | Optimized median FPS | Change | Allocated bytes/frame, control -> optimized |
| --- | ---: | ---: | ---: | ---: |
| Siedler, A500 | 107.30 | 121.74 | +13.5% | 1,168,947 -> 535,138 |
| Moonstone, A500 | 151.37 | 162.61 | +7.4% | 564,574 -> 4,497 |
| Alien Breed, A500 | 127.48 | 138.01 | +8.3% | 714,416 -> 76,800 |
| Celtic Heart, native A3000 | 48.57 | 55.07 | +13.4% | 5,356,859 -> 59,360 |

Every one of the 36 paired sample endpoints retains identical framebuffer hashes
and full CPU debug states. These are headless core throughput measurements on
this host, not guaranteed GUI FPS or proof of cycle-exact hardware. The static
opcode description cache is bounded to 65,536 references and is absent from
savestates; execution, runtime exceptions and trace suffixes remain live.

```powershell
dotnet build Tools/BenchmarkRunner/BenchmarkRunner.csproj -c Release -p:Platform=x64
dotnet Tools/BenchmarkRunner/bin/x64/Release/net10.0-windows7.0/BenchmarkRunner.dll <save.amigasav> 100 3 100
```

### Heartbeat validation: unobserved blitter diagnostics (2026-10-09)

Block and line blits previously allocated a `BlitterWordTraceEntry` for every
word/pixel, even with no `WordExecuted` subscriber. Both paths now construct
the snapshot only when the event has an observer. Enabled diagnostics retain
independent snapshots and the existing event timing; guest DMA, registers,
memory writes and savestate layout are unchanged.

Four new tests cover allocation bounds after warmup and retained diagnostic
results for block copies and horizontal lines, including unsubscribing.
Running the same new tests against the saved pre-change core fails both
allocation checks (1,638,400 bytes for twenty 1,024-word copies; 4,800 bytes
for twenty three-pixel lines), while the snapshot checks pass. With the fix,
all four tests and all 219 standard regressions pass. The external Siedler
wooden-frame/menu/mouse regression also passes.

This comparison uses the immediately preceding optimized core as control,
including the OCS, CPU trace and locked-MMU fixes above. Both builds use the
same BenchmarkRunner, Compatible profile, checkpoints, 100 warmup frames and
three 100-frame samples in each of three alternating runs. No other emulation
jobs ran concurrently during this comparison. Medians across nine samples:

| Checkpoint | Control FPS | New FPS | Allocated bytes/frame, control -> new |
| --- | ---: | ---: | ---: |
| Siedler, A500 | 141.53 | 142.94 | 535,137.68 -> 35.76 |
| Moonstone, A500 | 188.87 | 187.89 | 4,497.36 -> 4,497.36 |
| Alien Breed, A500 | 159.07 | 159.63 | 76,800 -> 0 |
| Celtic Heart, native A3000 | 88.56 | 88.20 | 59,360 -> 0 |

All 36 paired endpoints match the presented ARGB SHA-256 and full CPU debug
state exactly. FPS changes range from -0.52% to +0.99%; these small differences
do not establish a speed improvement. The measured improvement is eliminating
the remaining blitter diagnostic allocation and its garbage-collection load.
Moonstone's remaining allocations require separate profiling.

Fresh external evidence is under
`%TEMP%/amiga-heartbeat-20261009-1542/`: paired JSON lines and
`paired-summary.json`, the full `regression.txt`, and scripted Siedler mouse /
Celtic Heart movement, jump/fire and restore captures in `gameplay/`.
The continued captures were reviewed and retain visible gameplay. The native
A3000 HDF cold/warm/restore case was rerun in `native-boot/`; reviewed CLI hash
`D23BC28DE051DF36F0BD88FF006CF202B9A13B75BA3B0E847146735EC4BD9913`
and minimum sector-read assertions pass throughout. These tests use existing
read-only user media and fresh output savestates; they do not install an OS
or complete the all-model gameplay acceptance gate.

Next concrete gameplay step: capture the Tinyus object/animation-pointer list
immediately before the second Fire on the working 1.3/68000 control and the
failing 3.1/68000 configuration. Trace the first unexpected null animation
pointer backwards through its guest writes before changing CPU/DMA behavior;
do not force BPLCON0 to hide the guest-side transition failure. Continue
restartable 030 fault and display-mode implementation independently of the
user's pending OS media. The roadmap and recurring continuation remain active.

### Purchased OS media and A4000 installation preparation (2026-10-09)

The user requested installation for "Amiga 400", interpreted as A4000/030,
from `\\freenas.local\software\Retro\Amiga\Meine gekauften Amiga Produkte\AmigaOS-3.1.4-A500_A600_A2000`.
The supplied `Documentation/Installation.readme` explicitly requires a matching
model-specific 3.1.4 ROM, or the appropriate modules disk when retaining an
older 3.1 ROM. This package contains only A500/A600/A2000 modules and the
46.143 ROM for those models. `ModulesA4000D_3.1.4.adf` and a matching A4000
3.1.4 ROM were absent from that package; the nearby `kickstart_roms` directory
only has the previous legacy ROM files. This initial missing-media condition
was resolved using the user's subsequently supplied source, detailed below.

At the user's request, the linked
[firmware archive](https://archive.org/download/commodore-amiga-firmware)
was checked again on 2026-10-09 through its directory and complete metadata
file list (129 entries). It contains A4000 Kickstart 3.0/39.106 and several
3.1 revisions, including the native 40.68 already installed and used above;
no 3.1.4/46.143 ROM or modules disk is listed. The complete checked filename
list is saved in the installation record's
`firmware-archive-check-20261009.json`. This source therefore does not supply
the missing A4000 3.1.4 component.

A new writable nominal 256 MiB RDB/DH0/FFS image was created at
`%APPDATA%/AmigaEmulator/harddisks/AmigaOS-3.1.4-A4000-20261009.hdf`.
The original images and existing HDFs were preserved. Shared Workbench, Extras,
Fonts, Locale and Storage files, the install disk's Workbench/icon libraries,
hard-drive startup sequence and PAL monitor were prepared there, with complete
original source trees in `Installation/` for the real installer. All 1,355
resulting files were reopened and verified byte-for-byte and for Amiga
protection flags, including pure bits. Source image hashes and installed-file
hashes are recorded externally; no proprietary files were added to Git.

Actual boot evidence:

- A4000/EC030, 2 MiB Chip + 8 MiB Fast, native 40.68: the supplied Install
  disk cold-boots to its reviewed Workbench desktop and sees the new DH0.
- The prepared HDF with that old A4000 ROM reads 486 sectors and reaches an
  empty Workbench Screen; it does **not** reach a usable desktop. Core restore
  retains that image. This is incomplete installation evidence, not success
  and not proof that missing modules are its only unresolved problem.
- An independent copy of the prepared HDF was tested on A600, 2 MiB Chip,
  with the supplied model-appropriate 46.143 ROM
  (SHA-1 `D81CD6F131040895843D9DFA95A45BC95EDD9704`). Cold boot, warm boot
  and core restore show the reviewed desktop with RAM Disk and System icons,
  with 898 sector reads and identical presented ARGB SHA-256
  `E00EC859BEBED3B1BD505E9E36748A1CD137D1A850913773DA9009AC5D8D201E`.
  No guest write/persistence/application acceptance has been completed yet.

Durable preparation manifest, input scenarios, captures and logs are in
`%APPDATA%/AmigaEmulator/installations/AmigaOS-3.1.4-A4000-20261009/`.
Fresh diagnostic runners, savestates and the separate A600 test HDF remain in
`%TEMP%/amiga-os314-20261009/`. During preparation, saved model/ROM/HDF
settings were not changed. The following checks subsequently completed the
requested basic A4000 installation and enabled its startup profile.

### A4000/030 AmigaOS 3.1.4 installation validation (2026-10-09)

The user's new
[Kickstart 3.1.4 source](https://archive.decromancer.ca/fsck.technology/software/Commodore/Amiga/Kickstart%20ROMs/Kickstart%203.1.4/)
contains the unsplit desktop `kick.a4000.46.143` image. It was saved externally
as `%APPDATA%/AmigaEmulator/roms/KICK314-A4000.ROM`: 524,288 bytes, header
46.143, SHA-1 `BAD0AE388442DB02EAEB6A5363EF43EB4E308AE6`, valid internal
end-around-carry checksum `$FFFFFFFF`. This is the desktop A4000 ROM, not the
A4000T image or programmer-oriented hi/lo files. The previous ROMs remain.

The previously prepared 256 MiB DH0/System HDF now boots to a usable Workbench
desktop on A4000/EC030, 2 MiB Chip + 8 MiB Fast, Compatible. Its files were
installed by file-level copying from the purchased ADFs, preserving metadata
and following the distribution's hard-drive file layout; the interactive
Installer was not executed. The original installation source trees are retained
on the volume. No wrong-model RAM modules or FPU/MMU support package was used.

Validated evidence with no boot floppy attached:

- Cold boot, warm reset and a second cold boot read 898 sectors each and
  reproduce reviewed desktop SHA-256
  `CB2B10B163DC56156AC00685AAC95006CF0B1318A3A4AF8F0EBFD108ADBF1480`.
  Core restore preserves the presented image and CPU PC.
- Right Amiga + E opens Execute Command; NewShell opens an interactive Shell.
  Guest `Version FULL` reports Kickstart 46.143 and Workbench 45.194;
  `Version workbench.library FULL` reports 45.194 (09/23/2018).
- Guest Echo creates `SYS:Storage-Test.txt`, with seven observed HDF sector
  writes. Type displays its exact text after warm/cold reset. Reopening the HDF
  independently also verifies the bytes, rather than relying on a RAM cache.
- `SYS:Tools/ShowConfig` reports the 68030, AA chipset, 2 MiB Chip and 8 MiB
  Fast; `SYS:Utilities/Clock` opens its functioning graphical window. The first
  ShowConfig attempt used the wrong System drawer and returned Unknown command;
  the correct Tools executable was subsequently run and reviewed.
- After these guest writes, all 1,355 original copied files were reopened and
  still match their preparation hashes and Amiga protection flags.

`Tools/GameRunner` now supports raw modifier chords for reproducible Workbench
shortcuts. The new behavior was verified through the actual OS Execute Command
dialog and subsequent keyboard input; existing single-key scenarios remain
supported. `installation-validated.json`, native-ROM boot/persistence scenarios,
captures, logs and the verification script are saved alongside the durable
installation record above. Its HDF SHA-256 after validation is
`09957EC42DE994FCFA5E81E48C8A25545DF0F7A5805C30DC0331E23883253ADA`;
normal later guest writes can change it.

The previous frontend settings were backed up to `settings-before-os.json` in
that record. The current startup profile selects A4000/EC030, 2+8 MiB RAM,
the explicit 46.143 ROM, this writable HDF and disk boot. The latest GUI build
was started with that profile. This completes the user's basic installed-OS
request, not full A4000 hardware conformance or the entire roadmap. Broader
applications, other models/controllers, interlace/superhires and CPU work remain.

### Installed OS display and window input correction (2026-10-09)

The user's report of the right edge wrapping onto the left exposed a defect
in the earlier reviewed OS captures: the old desktop hashes above are boot
evidence, not acceptance of correct horizontal graphics or mouse alignment.
ECS/AGA hires erroneously added a 16-pixel word displacement plus an OCS
four-pixel host-span delay. Workbench fetches a preceding word at DDFSTRT
`$38` with negative modulo; those pixels must precede DIW rather than display
the previous row's right edge on the left.

The extra displacement and hires host-span delay are removed. Denise now
renders ECS/AGA hires spans one chip tick later at their original coordinates,
after DMA completion, instead of dropping the first four bits of every word.
Line-tail geometry uses the same phase. OCS rendering remains unchanged.
An independent beam-ordered ECS/AGA fixture checks every pixel of both a
normal `$3C..$D4` fetch and a `$38..$D8` preload with distinct preceding,
visible and following words. All 220 standard tests and the additional local
Siedler wood-frame/menu/mouse-edge golden regression pass.

Actual installed A4000/EC030 Workbench replay, using a separate read-only HDF
copy, verifies that the pointer over the visible close gadget closes Clock,
then Shell. Double-clicking System opens its drawer; moving to its visible
close gadget closes that drawer. Captures were reviewed before and after each
close; neither the HDF nor keyboard shortcuts substitute for guest mouse
button input. An initial drawer click missed its gadget and only activated
Workbench; the corrected move and subsequent close are recorded separately.
Cold and warm boot each read 898 sectors, with zero test-disk writes and
identical corrected desktop ARGB SHA-256
`1183E4BC62AB3E3F5959B490A514CBE942F63A21EB7004C8923E20ECBE32EB89`.
Restore preserves that image and CPU PC.

Scenarios, screenshots, step logs and the regression log are retained under
`%APPDATA%/AmigaEmulator/installations/AmigaOS-3.1.4-A4000-20261009/display-input-fix/`.
Fresh runners, savestates and the isolated HDF are under
`%TEMP%/amiga-os-render-input-20261009/`. GameRunner accepts an explicit HDF
override on savestate replay to isolate these tests from the user's live disk.
The frontend is restarted with the user's saved CRT/Clean4/fullscreen profile;
these checks validate core output and guest input, not a manual fullscreen
presentation review or complete ECS/AGA timing conformance. Wider AGA modes,
interlace and the remaining hardware roadmap still require their own tests.

### Heartbeat validation: cached MMU faults and A3000 installed OS (2026-10-09)

Level-zero PTEST previously exposed the table walk's invalid flag without
setting the resident ATC bus-error flag for invalid descriptors, limit and
supervisor violations. Motorola MC68030UM section 9.4 and table 9-3 require
B/I for those resident faults, with L/S/N cleared. A missing entry instead
reports I without B. The cached entry retains its diagnostic table information
internally; normalization occurs on PTEST, keeping the existing savestate
layout and correctly interpreting older cached-fault states.

Three new regression cases cover five fault sources (invalid descriptor,
limit, supervisor restriction, physical table read and descriptor update),
restore, FC separation, ordinary reads of write-protected pages and explicit
repair. PTEST table searches neither update U/M nor replace a cached fault;
PLOAD or PFLUSH can repair/invalidate it. An independently encoded guest
PLOADR/PTESTR/PMOVE sequence observes `$8400` through MMUSR, sees repaired
tables through level-seven PTEST and still sees the resident fault at level
zero. All three cases fail against the preceding core and pass with the fix.
All 223 standard regressions and the local Siedler golden test pass.

A separate fresh copy of the prepared OS HDF was tested on A3000, 2 MiB Chip
+ 8 MiB Fast, Compatible. The native unsplit `kick.a3000.46.143` came from
the user's supplied [3.1.4 ROM directory](https://archive.decromancer.ca/fsck.technology/software/Commodore/Amiga/Kickstart%20ROMs/Kickstart%203.1.4/):
524,288 bytes, header 46.143, SHA-1
`1A34B25A25E260E38E879172850143C80B461A64`, checksum `$FFFFFFFF`.
Cold/warm/second-cold desktop boots read 898 SCSI sectors each with zero writes
and reproduce corrected desktop hash
`1183E4BC62AB3E3F5959B490A514CBE942F63A21EB7004C8923E20ECBE32EB89`.
Restore retains CPU/image. A paired 100-million-chip-clock cold boot against
the preceding core has identical full CPU debug state and framebuffer hash;
this is a correctness comparison, not a performance measurement.

Real OS input opens Execute Command and Shell. ShowConfig identifies 68030
and ECS; Version reports Kickstart 46.143 and Workbench 45.194. Clock opens
and its visible gadget closes with mouse movement/button input. Guest Echo
writes `SYS:A3000-SCSI-Test.txt` through the emulated SCSI disk; after cold
reset, guest Type reads the exact text. Independent host reopening verifies
`A3000 SCSI persistence OK 2026-10-09\n` and all 1,355 original file hashes
and protection flags. The host checker initially used the Windows default
text encoding for the UTF-8 manifest; specifying UTF-8 corrected that checker
error without altering the filesystem. The test HDF hash after verification is
`54327E5D752960826075A582C4D6CA89257FCBED70DCBD266A9D7D4AF876AFDA`.

Durable scenarios, captures, control/fixed test logs, ROM validation and host
verification are under
`%APPDATA%/AmigaEmulator/installations/A3000-3.1.4-validation-20261009/`.
Fresh builds, continuation states, the downloaded diagnostic ROM and isolated
HDF remain under `%TEMP%/amiga-roadmap-mmu-status-20261009/`. The interactive
Installer was not run on A3000; this exercises the shared prepared files with
the model-specific ROM and SCSI hardware. It does not prove arbitrary MMU
demand paging, FPU or full SCSI timing. The user's running emulator, selected
game/profile and original disk images were left in place. No commit/push.

Next: add independent fault/retry fixtures for postincrement and page-crossing
operands before implementing restartable format A/B state; expand A3000 SCSI
coverage beyond these basic installed-OS accesses. Hardware ATC replacement
remains explicitly FIFO until its history policy has suitable reference tests.

### Frontend scaling and Siedler disk-change investigation (2026-10-09)

The Win32 fast presentation path previously ignored the persisted upscale mode.
It now performs source upscaling before its final DIB stretch: independent
Scale2x/Scale3x, two-pass sharp 4x, bilinear smooth 2x/3x/4x and mixed soft 4x.
Legacy serialized HQx/xBRZ names are retained, without algorithm conformance
claims. The displayed bounds remain independent of the source upscale factor.
Neighbors clamp at the crop boundary and changed frames reuse output buffers.
IMAGE SIZE / KEEP ASPECT / FILL SCREEN is available in both Display and Settings;
fill ignores the letterbox margins and fullscreen uses the complete monitor
bounds, restoring the previous window bounds/state on exit. The Settings menu's
previously unreachable PCMCIA row is also corrected.

Nine new presentation/diagnostic regressions check diagonal reconstruction,
crop/stride/native framebuffer preservation, exact center interpolation,
allocation-free warmed rendering, identical viewport geometry for every mode,
actual offscreen Win32 DIB output, failure-hook propagation and fresh crash
reports. The full suite plus local Siedler wood/menu/mouse golden regression
passes (233 tests, zero failures/skips). Manual live fullscreen acceptance remains
pending: the user stopped Computer Use with Escape during the preceding turn.

The subsequent reported Siedler intro/disk-2/click failure remains OPEN. Read-only
replays used the three existing PDX ADFs, A500 / 512 KiB Chip + 512 KiB Slow +
2 MiB Fast / Kickstart 1.3 / Compatible. Cold disk-3 boot, title, symbol input,
main menu, mission start and disk replacement/click reach the playfield without
a managed host exception. The intro disk also reaches its animated scene; an
early abort returns to the OS. These related paths do not reproduce the exact
reported requested-disk transition and are not acceptance of its correctness.
Additional Chip1Mb and alternate-click runs are investigation artifacts only.
The user's crash type and a new F12 save immediately before the failing swap
were requested; neither is available yet. Do not claim this game defect fixed.

GUI frame-update/paint failures now record the original managed exception and
CPU, beam, drive/DMA, input, model/RAM/ROM and display context in a fresh
`%APPDATA%/AmigaEmulator/logs/host-error-*.txt`. Logging does not swallow the
error or mutate disks/saves. GameRunner step logs include DMA and input details.
Fresh scenarios, captures and replay states are under
`%TEMP%/amiga-siedler-diskswap-20261009/`; regression log is `regression.log`.
Durable copies of scenarios, step/exception logs, reviewed captures and three
continuation states are under
`%APPDATA%/AmigaEmulator/diagnostics/SiedlerDiskSwap-20261009-11ea0fa205914526aec82a7e00479d11/`.
Original ADFs and existing user checkpoints remain unchanged. No commit/push.

Next for this report: replay the user's exact pre-swap save, distinguish host
failure from Guru/reset/hang, and add a failing regression before a targeted fix.

### Speedball 2 scrolling viewport correction — 2026-10-09

Reproduced the reported jumping picture in actual PRACTICE gameplay on A500,
Kickstart 1.3, Chip512KSlow512KFast2Mb, Compatible arbitration. The game changes
DDFSTRT from $38 to $30 and varies BPLCON1 while DIW remains $2C81/$F4C1.
The presenter incorrectly treated the additional scrolling prefetch as visible
overscan: source geometry jumped from `132,44+640x256` to `96,44+704x256`.
Per-line normalization also gave the scrolling pitch and stationary HUD different
horizontal scales.

Bounded displays beginning at/after the PAL left edge now use the horizontal
DIW comparators for presentation. DDF/BPLCON1 still determine actual fetches and
shifter output, but cannot move/resize this visible window. Existing negative-left
overscan and sprite-framed panel handling are retained. This follows the HRM's
[DIW comparator description](https://amigadev.elowar.com/read/ADCD_2.1/Hardware_Manual_guide/node002E.html)
and [scrolling prefetch description](https://amigadev.elowar.com/read/ADCD_2.1/Hardware_Manual_guide/node0086.html).
There is no game-name workaround or hiding of guest pixels.

Evidence and scope:

- A new synthetic regression failed before the fix. It exercises all 16 fine
  scroll values, early/standard DDF, changing scroll between adjacent rows and
  the HUD split; it checks both source geometry and complete horizontal pixel
  mapping against the fixed DIW window.
- Paired 1,500-frame gameplay replays use the same save and Up/Right/Fire input
  schedule. All 150 sampled raw frames are byte-identical before/after, while
  corrected presentation stays `128,44+640x256` throughout. Reviewed captures
  show players, ball, scrolling pitch and the stationary HUD.
- In-memory restore preserves CPU PC and presented pixels; continuation accepts
  Right input, and cold reset reaches the disk's trainer again. Release builds
  have no warnings/errors. Standard plus local Siedler golden: **234 passed**,
  no failures/skips. Turrican III and both North & South GoldenADF checks also
  pass with strict arbitration. The optional Amiga Test Kit check could not run
  because its external ADF fixture is absent; this is not a compatibility pass.
- The reviewed thin dither band at native rows 88/89 includes regularly repeated
  `$5555` words already present in fetched bitplanes. It has not been established
  as a second renderer defect. Further arbitrary pixel-row corruption in the
  user's exact match remains unverified; a pre-error F12 save was requested.
  Knockout fixture/gym menus were reached, but a manually started knockout match
  is still not claimed as tested. PRACTICE gameplay is the demonstrated scope.

Durable scenarios, before/after captures, frame logs, Copper trace, regression
logs, replay probe source and continuation saves:
`%APPDATA%/AmigaEmulator/diagnostics/SpeedballRaster-20261009-e8ecdac5/`.
Fresh full outputs remain under
`%TEMP%/amiga-speedball-raster-20261009-e8ecdac5/`.
User ADFs were mounted read-only; no user settings/saves/HDFs were changed.
No commit/push for this follow-up. Next: replay any remaining pixel-row defect
from the user's exact checkpoint and isolate DMA/blitter/shifter data from host
presentation before changing hardware behavior.

## Remaining implementation order

1. Complete restartable 68030 format A/B bus faults, cache semantics and ATC
   replacement using independent hardware fixtures.
2. Expand A3000 installed-OS/SCSI coverage, then add negotiated SCSI timing,
   disconnect/reselection and complete target command coverage.
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
