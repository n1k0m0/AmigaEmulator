# Machine models: implementation and validation

Status: 2026-10-11. Select a machine through **F10 -> SETTINGS -> HARDWARE**.
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

The 020/EC020 instruction cache has 64 independently tagged longwords; the
030/EC030 has 16 lines with four independently valid longwords. CACR enable,
freeze, clear and CAAR entry selection affect real cached instructions, with
logical address/FC2 tags and no DMA snooping. Aligned fills respect bus slots,
device inhibition, MMU page/transparent CI and resident ATC faults. The bus
does not acknowledge burst requests, so only the demanded longword is filled.

The 030/EC030 also has a separate 256-byte writethrough data cache, with 16
logical tags, all three FC bits and four independently valid longwords per
line. CACR enable/freeze, clear-all/entry and write allocation are implemented.
Partial/misaligned operands cross entries independently; disabled entries are
retained and frozen write hits update. Locked TAS/CAS/CAS2 reads use the
external bus. Chip/Slow RAM and device apertures are data-inhibited on the
Amiga motherboard; Fast RAM is cacheable without DMA snooping. MMU page/TT
CI, EC030 ACU CI, protection faults and pending bus retirement remain active.

Timing remains approximate. Pipeline timing, complete
restartable fault frames, CALLM/RTM and optional floating-point coprocessors
remain incomplete. Do not advertise these paths as CPU conformance.

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
The LOF field flop now follows VPOSW and toggles at field boundaries only
while BPLCON0 LACE is set. It no longer alternates in non-interlaced output.
This corrects field selection, not field lengths, weaving or programmable
beam-counter writes; those separate timing/output gates remain open.
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
version 2 records the exact motherboard. Core version 16 stores the full model
header, running sprite output shifters and the extended CPU instruction/data
caches (CPU extension version 5). Old CPU extension versions load with empty
cache entries where no payload exists. Core versions 1–15,
including earlier A500 files, remain readable. Older emulator builds reject the
new core version. Wrong CPU/clock/chipset/IDE/RAM/motherboard loads
are rejected before RAM mutation or disk reattachment. Development combinations
that cannot be reconstructed from public metadata must use the diagnostic
core-state API. Development state layouts are not a stable interchange format.

## Verification

- 320 standard regression tests pass, including CPU/MMU/caches, AGA, model, RTC,
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
| 1. Known defects | A2000 OCS CLI clipping fixed; cold/warm/restore reference assertions; Siedler wood/menu/pointer test passes; requested Intro Disk 1 -> Disk 2 crash fixed by physical media-removal timing; a separately repaired Siedler Disk 1 fixes the Kickstart-1.3 handoff; lost keyboard acknowledgement recovers; menu write protection no longer ejects media or discards partial writes (2026-10-11) | Current Siedler border report remains open; Pinball persistent-score/physical-track mismatch; fix defects discovered by the broader matrix; retain broader disk-validator/hardware coverage |
| 2. Native gameplay | Full local ADF sweep; Boxx 4 ECS window fix with failing-before test; Tinyus bounded movement/fire on all eight boards; verified live Pinball ball/flippers/pause/quit requester on all four tables using separate 32-bit-clean Disk 1 on native A3000/A4000; Desert Strike lift-off/flight/fire/map/EXIT/reset/restore on all eight models (2026-10-11) | Complete the 262-case visual/control review and title-specific disk/menu continuations; Pinball score persistence, Desert land combat/objectives, Mega/Cannon transitions, Sqrxz text/reference checks, native AGA software and longer sessions remain open |
| 3. Performance | Display/opcode caches and allocation reductions; vectorized framebuffer clear measured with identical complete CPU/frame states; timing lookup rejected after a regression (2026-10-11) | Continue profiling remaining allocations and repeat paired measurements after further changes |
| 4. Installed OS | A4000/030 and separate A3000/SCSI 3.1.4 installations; both caches exercised; five older boards with owned 46.143 ROM, Shell/Clock/ShowConfig and independently verified file persistence/reset/restore (2026-10-11) | Broader applications and complete controller coverage; optional hardware remains gated |
| 5. ECS/AGA modes | Extended AGA lores/hires scrolling and fractional compositor phases implemented and independently tested; LOF/VPOSW field selection corrected with a failing-before fixture | Native superhires, interlace field lengths/weaving, programmable timing, scan doubling, additional references |
| 6. 020/030 correctness | Integer/MMU and repair fixtures; locked-read protection and PTEST B/I fixes; actual 020/030 instruction caches and separate 030 data cache implemented and boot/application tested; overlapping TT windows combine cache inhibit correctly | Complete timing, ATC replacement and restartable format A/B faults |
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

Follow-up 2026-10-11: the earlier short transition conclusion is superseded
by longer cold/continue/start/control observations below. The blank phases
are finite in those runs and also occur on the unchanged HEAD control. This
does not establish a new renderer fix or complete long-session compatibility.
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
ARGB hash and complete serialized CPU-state SHA-256. CPU serialization runs
outside the timed/allocation region. Run the identical tool against both core builds. Keep guest media,
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

The full-library follow-up below supersedes the earlier claim that Tinyus never
recovers after a longer wait. Its blank transition is intermittent, rather than
proven to be a permanent hang. Next: review the longer deterministic transitions
and distinguish intended game phases from erroneous guest behavior before
changing CPU/DMA behavior; do not force BPLCON0. Continue
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

Follow-up: the exact requested Intro Disk 1 -> Disk 2 crash was reproduced and
fixed on 2026-10-10. See the later section on Siedler intro disk changes and
the separate, still-open game-handoff failures.

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

### Full local-library/model audit and ECS vertical-window fix — 2026-10-09

The preceding Speedball work was committed and pushed as `464ce54` at the
user's request. The subsequent all-game/all-board check covers 29 named games,
one unidentified three-disk set and all eight exposed model variants with native
ROMs. SELECTOR is a utility and is excluded from the 240-case game acceptance
matrix. Original media remain read-only and checksummed; fresh outputs are
outside the repository. Script completion never becomes a gameplay pass.

Found and fixed a real Boxx 4 ECS/native-A3000 missing-playfield defect: its
Copper changes DIWSTOP/DIWHIGH after the vertical start. Image data remains
present in Chip RAM, but the old instantaneous rectangle calculation blanked
the already-open window. Agnus now retains the vertical comparator latch until
a matching stop or fixed PAL vertical blank, including restore and legacy-state
handling. A failing-before ECS/AGA regression now passes, as do all 235 standard
plus local Siedler frame/menu/mouse checks. Targeted cold and control/restore
runs reached Boxx gameplay on all eight boards. The 240-case cold sweep is now
complete (the invalid A2000 OCS RAM case has its valid replacement). Its
reviewed per-title/per-board stage table is in the linked audit; execution
success is still not full gameplay acceptance.

The same matrix exposed Fred's Journey AGA noisy pixel rows on A1200 and
A4000/030. FMODE=0 now uses the legacy complete-group latches/pointer path;
wide AGA transfers retain their separate implementation. Two actual-game
golden replays fail before and pass after the correction, movement/restore
retains a clean level, all 60 AGA title states were replayed and reviewed,
and the 235 regressions remain passing.

Speedball 2 Practice runs on all five 68000 profiles. The local disk version's
Line-F loader at $8518 consumes a six-byte exception frame via ADDQ/RTS,
leaving an 020/030 format word on the stack. Paired traces establish this
disk-version restriction; Copylock checksum/register results match and the
already implemented format-two trace frame is not the missing feature.
Use a compatible 68000 profile for this image, or separately test a patched
version. Do not weaken CPU exception frames to accommodate it.

Tinyus's earlier permanent-blank diagnosis was too strong: an A600 replay
recovers after additional waiting without Fire or restore. A paired restore
control retains identical full registers, Chip RAM and pixels. Further input
can produce another blank phase, so the intermittent problem is still open.
Several older scripts stop in trainer/menu/loading phases or inadvertently pause
the game; their title-specific continuations remain acceptance work. Lotus
reaches the race on A500/A2000 OCS, but the same sequence leaves the other six
profiles in a guest-programmed planes-off phase. Lemmings has spawned figures,
an advancing timer and mouse/restore evidence on A500. Further native disk
transitions, control/restart acceptance and those unresolved phases are the
next concrete game work. All 50 ADF hashes still match the original inventory.

Detailed profiles, findings, test limits, preserved evidence and next steps:
[Game/model compatibility audit](GameModelCompatibility-2026-10-09.md).
No new commit/push for this follow-up. Neither this audit nor the full hardware
roadmap is complete.

### Full-library gameplay continuations and complete-state restore — 2026-10-10

The stronger runner restore gate compares serialized CPU and bus state,
including Chip/Slow/Fast RAM, ROM overlay and floppy controller, plus beam
timing and presented pixels. All 240 continuation cases pass; this does not
turn their unfinished intro/menu states into gameplay acceptance. Both Fred
AGA image goldens pass with this gate. Preflight rejects unsupported numeric
controller bits and accepts legitimate combined mouse buttons.

Oh No! More Lemmings now has actual spawned figures, advancing timer and mouse
input on all eight boards. Its corrected source sequence also passes eight
fresh native-ROM cold boots. The old Space input is removed from Maniac/Zak
smoke sequences because it pauses them.

Siedler's apparently empty larger-memory screen was a blinking Disk-2 request,
not missing menu rendering. Providing the requested disk and later left-clicking
through the animation reaches symbol input, the main menu and mission terrain
on all six larger-memory profiles. Together with the two smaller-memory paths,
all eight boards show the playfield and respond to mouse selection/restore.
The tested transition does not crash the host. The earlier user's exact
intro-disk swap report remains open without its pre-swap state. Model-specific
disk steps are retained in `Tools/model-gameplay-variants.json`. All eight
fresh source cold boots now reach the mission. Continuing those states builds
the first castle on every board, then verifies live frames and complete-state
restore. The common source sequence retains the construction menu actions.
A long populated-settlement stress test remains open.

Evidence: the existing full-library diagnostics root, particularly
`restore-matrix/`, `lemmings-models/`, `lemmings-cold-verified/`,
`siedler-required-disk/`, `siedler-left-transition/`, `siedler-finish-games/`
and `siedler-classic-gameplay/`, followed by eight `siedler-cold-verified/`
cold boots and eight `siedler-cold-construct/` first-castle runs. Maniac Mansion now has walking input on all eight boards, followed by eight fresh
native-ROM source cold boots into the controllable scene. Turrican III's corrected
DF1 Disk-2 and Fire sequence also passes eight fresh native source cold boots into
the level, input and complete-state restore. Hostages reaches its mission scene
on all eight native boards, with extra menu inputs on A600/A3000; eight fresh
source cold boots now confirm mission entry and restore. Fire and Ice reaches
the icy level on all eight boards. Sixteen matched-cycle input/control runs
show camera/coyote/projectile movement and pass full-state restore; its source
sequence is retained and all eight fresh cold replays now reach the level
with input/full-state restore.

The North & South A3000 map was stale: the guest had already taken a Line-F fault
at `$02030406`, which an older filter incorrectly missed by excluding addresses
above 24 bits. It is corrected to an open failure, not gameplay acceptance. The
three native-ROM 68000/ECS loaders also fault. Cold controls plus campaign inputs
work on A600/A2000 ECS with 2 MiB Chip + 2 MiB Fast and native Kickstart 3.1; A500+
works with the same memory and Kickstart 3.1. Its native 2.0/Fast-RAM control still
fails. Explicit additional profiles preserve the failing defaults and can be
included with `-IncludeCompatibilityProfiles` (North & South initially 244 cases
instead of 240; a subsequent Cannon Fodder A1200 Chip-only profile brings it to 245). Eleven
fresh source cold runs confirm working and failing configurations. A separate
A3000 Kickstart-1.3 cold run, retaining M68030, reaches campaign/input/restore
without guest instruction faults. This flat-ROM diagnostic does not implement
SuperKickstart or certify native installed OS/SCSI. Four explicit compatibility
profiles now provide a campaign path across all eight boards; native failures
remain visible. The 244-case manifest passes preflight, not gameplay acceptance. The runner now
records counts/first synchronous instruction exceptions outside ROM, including
32-bit addresses; positive A3000 and clean A500 controls pass, along with the two
Fred AGA pixel goldens and full-state restore. Protection exceptions still require
review, not automatic failure labels.

Cannon Fodder's Return/left-click intro/trainer flow and memory controls now reach
its cast intro on A500 1-MiB Chip and three ECS boards, also on an A500
512-KiB-Chip/Slow/2-MiB-Fast control. Five Disk-2 continuations clear the request
but remain black; alternative Disk-3 and three-drive controls are running.
Three-drive continuations now reach Mission 1 with squad/mouse input on six
boards (A500 with 1-MiB Chip, A500+, A600, A2000 ECS, A3000, A4000/030). A1200
with Fast and A2000 OCS/Fast remain black; fresh early-data and Chip-only
controls show fresh A1200 Chip-only Mission-1 gameplay; its Fast counterpart stays
black. A4000 Chip-only stays black; native motherboard Fast reaches the mission.
The source retains three data drives, preferred A500 one-MiB Chip and an explicit
A1200 Chip-only profile; nine fresh source cold replays are running. OCS/Fast
controls remain unresolved. Tiny Bobble also reaches Round 1 on all eight boards,
with all eight fresh source cold replays now passed. Lotus reaches a race on an A500+ Kickstart-1.3
control; its A1200 counterpart still fails. Its native keyboard IRQ storm is
retained for diagnosis.
The official Speedball 2 WHDLoad patch on three separate disposable HDFs also
fails before gameplay; an A600/68000 cold control fails at the same $07FFBC
Line-F as A1200, so this is not specific to the 020/030 CPU. It is not certified. Actual mission entry and remaining
native disk/menu/intro phases remain the next concrete game work. All 50 original ADF hashes were rechecked
unchanged. No new commit/push; neither the library audit nor the hardware roadmap
is complete.


2026-10-10 game-library continuation: all nine fresh Cannon Fodder source cold
runs are reviewed. Seven native models and the additional A1200 Chip-only
control reach Mission 1 and restore exactly; A2000 OCS remains black. Native
A1200 Fast is successful on cold boot, so the earlier warm-loader failure is
not classified as a hardware restriction. Pinball Fantasies reaches Partyland
on five classic boards plus native A1200. Its missing dot-matrix text is fixed:
DMA retains completed pointer/modulo progress across zero-plane scanline gaps,
without the erroneous zero-to-enabled raster-split reload. The independent
OCS/ECS/AGA 16/32/64-bit regression and real-game pixel golden both fail before
and pass after; paired full CPU/Chip-RAM hashes and beam remain identical.
All 235 standard regressions, Fred AGA goldens and the Siedler frame golden pass.
Native desktop-Fast Pinball configurations fault at $200002; fresh A3000/A4000
Chip-only cold controls are in progress. Moonstone reaches a controlled A500
Practice fight with debounced menu input and restore; native eight-model
Practice replays are in progress. Lotus K1.3/68000/ECS races additionally pass
on A600 and A2000 ECS, while native advanced CPU controls still fail. Remaining
loaders, native desktop faults, longer gameplay and reset acceptance are open.
See GameModelCompatibility-2026-10-09.md for exact evidence. No further commit
or push; the library audit and hardware roadmap remain unfinished.

2026-10-10 final-core library checks: all six Pinball Fantasies source cold
replays and all five 68000 Moonstone source cold replays are reviewed in actual
gameplay with inputs and complete restore. Moonstone's local CSL stub runs
user-mode MOVEC after installing only vector 4; the three 020/030 profiles
correctly take privilege vector 8 and fail this particular media version.
An independent five-CPU regression verifies the fault PC, saved SR, destination
and frame layout. The standard suite now passes 236/236; all 240 library/model
continuations also pass full CPU/bus/beam/pixel restore with the Pinball DMA fix.
These restore and 248-case preflight results are not gameplay certification.
All 50 original ADF hashes remain unchanged. Civilization reaches the main menu
on all eight boards and eventually requests Disk 4 after its lengthy world-birth
animation. Its subsequent DOS `Disk corrupt - task held` requester remains open,
despite valid independently unpacked source OFS files/checksums and an empty-drive
swap control. A guest DOS read fixture is the next diagnostic gate. Alien Breed
now reaches a controlled A500 level after its actual Disk-2 request and long data
load; the other native models are undergoing that continuation. Exact evidence
and remaining failures are recorded in GameModelCompatibility-2026-10-09.md.
No further commit/push; the library audit and hardware roadmap remain unfinished.

2026-10-10 further library evidence: Alien Breed's proper Disk-2/long-load flow
now reaches a controlled level on all eight native boards; fresh source cold
replays are underway. All five Speedball 2 source cold starts reach Practice
with input and exact restore on the fixed core. All 240 library/model states
also pass warm-reset and power-cycle restore with full CPU/bus/beam/pixel
equality. Independent guest DOS probes read the same Civilization Disk-4 file
under Kickstart 1.3 and 3.1 with the host-verified checksum 9EF884A5; the specific
A500 game's corrupt-disk requester persists even without duplicate data drives.
Native A600/A1200 world-creation controls and Speedball consecutive-field
scrolling checks are underway. No generic filesystem bypass has been added.
Original media remain unchanged; no further commit/push has been made.

2026-10-10 completed library controls: all eight source Alien Breed cold replays
now reach a controlled level and exact restore. Speedball 2 passes 32 consecutive
scrolling fields on each of the five 68000 boards, with fixed source geometry,
160 distinct frame hashes and no non-ROM exceptions. Native A600/A1200
Civilization world creation reaches Disk 4, but acknowledgment remains unresolved;
A500's requester occurs during Lock("CIV4:nuke1.pix", SHARED_LOCK).
Independent guest DOS reads of the complete adscreen.pix match checksum 8C6EB102
on Kickstart 1.3 and 3.1; host validation of all 307 files/5,064 OFS data blocks
finds no data-chain errors. Source cold-flow preflight again passes all 248 cases.
These are bounded tests, not certification of every title/model. Exact evidence
and the next diagnostic steps remain in GameModelCompatibility-2026-10-09.md.
No further commit/push; the library audit and hardware roadmap remain unfinished.

2026-10-10 Moonstone compatibility option: a separate Disk-1 copy using
ILLEGAL/NOP to enter the local CSL loader's existing vector-4 Supervisor handler
now passes fresh cold Practice/combat, inputs and full restore on native A1200,
A3000 and A4000Ec030. The exact original cracked set remains separately marked
incompatible on those CPUs. A hash/signature/OFS-validated copy tool preserves
all originals; the explicit matrix option adds three reviewed profiles and passes
251-case preflight. Existing-destination and unknown-source rejection controls
pass. The guest DOS control also successfully locks/reads CIV4:nuke1.pix under
Kickstart 1.3/3.1 (checksum 3EFE4AE7), so the game's specific cache/swap failure
requires further investigation. Next: single-drive A500 world creation and swap,
then unresolved Tinyus/loader/menu flows. See GameModelCompatibility-2026-10-09.md.
No further commit/push; the library audit and hardware roadmap remain unfinished.

2026-10-10 keyboard and swap progress: an independent modifier/down/up fixture
reproduces a lost queued key when the CIA ICR read precedes the host's KDAT
handshake. CIA now retains handshake completion, and keyboard delivery waits
for it; both states restore correctly, with safe legacy defaults. This removes
the real OS Retry shortcut's sustained V repeat. The suite passes 237/237.
A new external read-only HDF probe locks CIV1, swaps to original CIV4 and fully
locks/reads nuke1.pix with checksum 3EFE4AE7 after Retry. The actual game's A500
single-drive swap still corrupts; A600/A1200 remain at the request. Generic DOS
swap success is not game success. Full 240-case reset/restore revalidation is
running on the new keyboard core; 16 fresh Tinyus/Cannon cold controls follow.
Mega lo Mania reaches intro/title but its post-intro copy/privilege failures
remain unresolved. No title-specific hardware bypass is accepted. See the
compatibility report for exact evidence and remaining keyboard timing limits.
No further commit/push; the library audit and hardware roadmap remain unfinished.

2026-10-10 new-core validation: all 240 warm-reset/power-cycle restore cases pass
with the keyboard fix. All eight native Tinyus cold states are visually reviewed;
the three newer-ROM 68000/ECS transitions remain open. Separate Kickstart-1.3
profiles on the same native A500+/A600/A2000 ECS CPUs and 2-MiB Chip RAM pass cold
gameplay, five directional/shooting intervals, Continue/restart into gameplay and
full restore with zero non-ROM exceptions. Those three reviewed ROM alternatives
are retained explicitly, bringing the profile sweep to 251 cases, or 254 with
the separate Moonstone-disk option. No silent CPU substitution is introduced.
Fresh Civilization world/swap controls complete with the Disk-4 issue still open.
All eight native Cannon cold flows are reviewed on the keyboard-fixed core;
seven reach Mission 1, while A2000 OCS still recovers through the ROM.
No further commit/push; the library audit and hardware roadmap remain unfinished.

2026-10-10 floppy persistence: actual OS MFM writes decode correctly but were
not committed on replacement/disposal, and file-state reattachment discarded
the saved buffer and partial write. Three old-core-failing fixtures now pass:
flush the outgoing medium before replacement, flush all four drives on disposal
and menu persistence, save the pending-write flag, and retain the same-path
saved buffer/partial MFM sector. The standard suite passes 240/240. Actual DOS
writes on fresh external ADF copies pass all eight native models, exact
power-cycle restore and independent host payload readback (64 bytes 00..3F).
All 240 full reset/restore cases pass again and all 50 original ADF hashes are
unchanged. Writable Civ4 copies do not fix the game's Disk-4 request/cache issue.
A persistence-core 254-case cold sweep started in four disjoint workers under
all-254-floppy-fixed-cold/, with its remaining cases later continued in eight.
Review its captures before accepting gameplay; trace
Civilization's filesystem error before requester rendering next. Evidence and
worker metadata are in the compatibility report's diagnostics root. No further
commit/push; the library audit and hardware roadmap remain unfinished.

2026-10-10 floppy identification and state order: the motor now samples MOTOR
only on an inactive-to-active SELECT edge. External standard DD drives expose
their motor-off 32-bit ID even without media; ejection keeps them attached.
Independent old-core-failing motor/ID fixtures now pass. A separate failing
restore fixture also proved that bus loading applied the old CIA state to
the restored motor latch; line refresh is now deferred until both CIAs load.
The standard suite passes 243/243, all 240 full warm-reset/power-cycle restores
pass, and Siedler/Pinball image regressions pass on the final core. Actual native
Kickstart cold boots on the preceding motor/ID-fixed core read all 5974 bytes
of CIV4:nuke1.pix from preboot DF3 on all eight models (CRC 3EFE4AE7); the
old core leaves A500/A600/A1200 at the missing-volume requester. This proves DOS
drive registration, not game completion. HD/electrical timing/reset details
remain open. Primary signal specification:
[Commodore HRM Appendix H](https://www.theflatnet.de/pub/cbm/amiga/AmigaDevDocs/hard_h.html).

The old cold sweep's 47 + 126 completed cases are retained as baseline; its
unfinished workers were stopped. The final-core 254-case sweep now uses eight
disjoint workers under all-254-floppy-id-restore-fixed-cold/. No current full
library pass is claimed. Civilization's early trace found a Disk-1 directory
block number used against Disk-4 pixel data despite a correctly recognized
CIV4 volume; the precise cache/lookup cause is open. Three new preboot-four-drive
Earth Game controls preserve a visually verified menu selection and loading.
Next: inspect their actual map/input transitions, then review the 254 fresh
cases and retain/fix changed failures. Evidence/worker locations are recorded
in GameModelCompatibility-2026-10-09.md. No further commit/push; the broader
hardware roadmap and game acceptance remain unfinished.

2026-10-10 multi-drive gameplay and menu: Civilization now reaches its real
Earth map, accepts city naming and displays Rome on native A500/A600/A1200
cold/restore lineages. The merged source flow also has reviewed fresh A500+ and
A2000 OCS city screens; the remaining native cold confirmations are running in
civ-reviewed-four-drive-final-cold/. Its condition is all four original disks
attached before boot; the single-drive K13 stale-root lookup remains open and
is reproduced by a minimal DOS program. Zak reaches its bedroom and visibly
walks in response to mouse clicks on native A500/A1200 when Zak2 is in DF1
before boot and Zak1 remains in DF0. The source flow is now under fresh all-eight
cold confirmation in zak-reviewed-two-drive-final-cold/; other models are not
pre-accepted. Original RAM/CPU/ROM profiles are retained.

MEDIA now selects/inserts/ejects DF0..DF3 and requests reset when a new external
drive needs enumeration. Saving DF0 no longer clears the other configured
paths. Additional disks mount before startup and survive model changes/reset;
state loading keeps DF0 as the boot disk even if metadata names last data DF3.
A no-window/no-user-settings offscreen smoke test exercises the actual methods,
path persistence and drive selection; its menu rendering is reviewed. The
standard suite remains 243/243. The broad 254-case run's Civ/Zak entries are
superseded by these two dedicated eight-case flows; its other 238 cases retain
the same core/source. Finish/review all three runs and remaining game failures
next, using the compatibility report's worker metadata and evidence folders.
No further commit/push; complete library acceptance and the hardware roadmap
remain open.

2026-10-10 all-eight data-drive confirmation: Civilization and Zak's corrected
merged-source cold flows finish and reach actual gameplay on every native
board, with original CPU/ROM/RAM and zero observed non-ROM exceptions. City/map
screens and the bedroom's mouse-controlled walk are visually reviewed. Sixteen
gameplay checkpoints additionally pass warm-reset and power-cycle exact
CPU/bus/beam/pixel restore assertions. Evidence:
civ-zak-eight-native-gameplay-reviewed.json and
civ-zak-gameplay-reset-proof-run/. All fifty original ADF hashes remain
unchanged; updated complete 254-case scenario preflight passes. The library
sweep has 244/254 raw cases at this checkpoint, with its sixteen old Civ/Zak
flows superseded by the new gameplay evidence. Both preboot data-drive layouts
remain required; single-drive K13 stale-root handling remains open.
Low-Chip Cannon tests fail on both A500 and A2000 OCS even with added Fast RAM;
this is not yet a proven copy restriction or accepted profile workaround.
Finish/review the sweep and test preboot data drives for Desert/Mega next.
No further commit/push; library acceptance and hardware roadmap remain open.

2026-10-10 completed library sweep and follow-up: all 254 final floppy/ID/core
cases execute without harness errors; all 240 native captures are reviewed,
with sixteen Civ/Zak flows superseded by their actual gameplay cold runs.
Current capture coverage is not a universal gameplay/graphics pass. Pinball's
native A500+/K20 cold result is withdrawn after a new depacker failure exposed
by the keyboard handshake change. Identical older-core and same-board K31
controls reach Partyland. An actual-menu trace confirms proper F1 down/up
acknowledgements before the depacker leaves its output buffer; compare packed
data at the first depacker entry next. Keyboard backlogs after the hang are not
accepted as its cause.
An independent serial-clock correction now spaces keyboard bits by 43 E clocks
(approximately 60 us), with per-byte periods and partial/legacy restore tests.
The standard suite passes 245/245. Full electrical handshake pulse measurement
and 143 ms resync remain unimplemented; this clock fix alone does not fix
Pinball's K20 hang. Moonstone's two short K31 menu holds are corrected to about
seven frames and confirmed through native A600/A2000 ECS combat/input/restore.
A fresh 22-case Moonstone/Tinyus native and explicit-ROM/copy sweep is running
under moon-tinyus-bitclock-cold/. Desert now reaches story/loading after a
correctly timed late intro click, but mission entry remains open. Full evidence
and next steps are in GameModelCompatibility-2026-10-09.md. No further
commit/push; complete library acceptance and hardware work remain unfinished.

2026-10-10 physical floppy INDEX follow-up: paired native K20 Pinball menu
states load identical first packed/unpacked data, but the failing second
packet skips ten sectors at logical track 39. The synthesized MFM stream
wrapped after 691200 clocks independently of the 709379-clock physical INDEX;
startup timing exposed its drifting sector origin. FloppyDrive now pads gap
flux until INDEX and aligns its track origin at that edge, retaining modeled
data cadence and physical period. Three independent fixtures fail before/pass
after; full suite 248/248, Siedler alignment and exact Pinball DMD golden pass.
The formerly failing menu now reads the exact good second packet. Eight
native Pinball cold starts finish and are reviewed: all five 68000 boards and
A1200 reach Partyland with inputs; all six pass warm/power-cycle exact restores.
A500+/K20 is reinstated, while both native desktop cases remain loader failures.
All 22
previous-bitclock Moonstone/Tinyus cases are reviewed, with sixteen accepted
gameplay warm/power-cycle restores; newer-ROM Tinyus transitions remain open.
New full final-INDEX-core 254-case source validates and is running in eight
disjoint workers under diagnostics/AllGamesModels-20261009-bdfaa5f7/
all-254-index-fixed-cold/ (workers.txt records owned PIDs). Frozen binary
hashes are in index-fixed-runtime-provenance.json. Do not stop these workers,
duplicate their cases or substitute earlier-core results without a reason.
Complete/review that sweep, examine Pinball's K20 cold result and follow the
remaining game restrictions recorded in GameModelCompatibility-2026-10-09.md.
Final-core Desert A500/A1200 loading continuations reach the carrier cinematic
with coherent captures and zero new non-ROM exceptions; Fire/click/Return
controls finish under desert-carrier-input-run/ and retain that animated screen.
Mission entry is still open. All fifty original ADF hashes
remain unchanged. Do not promote this cinematic to mission gameplay.
No further commit/push; whole library and hardware roadmap remain unfinished.

### Requested upscale replacement (2026-10-10)

The presentation menu now exposes exactly `NONE`, `SCALE2X`, `SCALE3X`,
`HQ2X`, `HQ3X`, `HQ4X`, in that order. Scale2x/3x retain their published
neighbor rules. HQ2x/3x/4x now implement HQx pattern interpolation and YUV
thresholds through a managed adaptation of the permissively licensed FFmpeg
filter, rather than the earlier generic bilinear smooth options. Retired
`Xbrz`/`Clean4` string and numeric settings migrate to HQ4x without losing ROM,
disk, boot, stretch or other saved configuration. Unknown upscale values fall
back to NONE. The license notice is included in build outputs.

Independent FFmpeg 9.0.1 BGRA reference hashes verify all 256 binary neighbor
patterns and a deterministic colored/YUV-threshold fixture for each HQx
factor. Cropped copies match the same hashes without overscan contamination.
The complete 704x288 Siedler construction capture also matches FFmpeg exactly
at all three factors; the six-mode visual comparison is inspected. Win32 DIB
paint tests cover native crop/stride and reference edge colors. All modes retain
the original viewport and mouse geometry; FILL SCREEN still fills the client.
The Release build has zero warnings/errors and the full regression suite is
254/254, with no skipped tests.

HQx precomputes 4,096 compact interpolation rules per factor and normalizes
weights to a constant denominator. It reuses source/YUV/output buffers and
skips unchanged pictures. Three alternating baseline/optimized process pairs
on the same frozen Siedler frame use 60 warm-up renders, 100 changed renders
and disabled tiered compilation. Median filter times for the initial scalar
HQx port versus the final rule lookup are HQ2x 15.48 -> 13.05 ms, HQ3x
28.05 -> 16.40 ms, HQ4x 37.16 -> 22.45 ms. All three HQx outputs have identical
baseline/final hashes and zero allocations during the measured changed-frame
loops. These are presentation-filter timings under the current host load,
not complete emulation FPS or a comparison with the previous bilinear modes.

Evidence: `%APPDATA%/AmigaEmulator/diagnostics/Upscalers-20261010/`, particularly
`hqx-reference-results.json`, `siedler-reference-check.json`,
`siedler-six-upscalers.png`, `paired-performance-summary.json`, the six
`paired-*/` runs and `full-final-regression-tests.log`. No user settings, ROMs,
media, installed disks or game states are rewritten. Existing frozen game-sweep
snapshots are preserved. No new commit/push was requested for this change.
The broader game/model and hardware roadmap remains open.

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

### Shared setup and C64-compatible relay infrastructure (2026-10-10)

Amiga and C64 now share F10 main entries and six settings categories: hardware,
ROMs, RAM, graphics, sound and hard disk. The Amiga retains its GDI overlay style,
adds proper nested back navigation and selection scrolling, and saves volume/audio
preferences. C64 retains all existing settings; unsupported hard disks are explicitly
marked. No machine preview or unfinished hardware is promoted to full compatibility.

The C64 transport was ported with literal six-byte AMIGAR framing. LAN TLS pinning
and relay ECDH P-256/AES-GCM, full/Deflate/sparse/XOR-RLE delta codecs, local
upscaling, observers, keyboard/joystick grants and host/client roles are implemented.
Amiga extends the payload with exact ARGB colors, stereo ADPCM, crop metadata,
three joystick buttons, queued relative mouse transitions and full control handover.
Remote clients park and preserve their local Amiga and media; hardware/reset/media
actions stay with the host. Queue and allocation limits prevent accumulating
unbounded traffic. The new Python relay supports unchanged C64 clients on the
same TLS endpoint, with separate room namespaces.

Evidence: the complete 265-test Release suite passes with zero failures/skips,
including 11 network/frontend regressions; 4 independent Python relay tests also
pass. Real LAN and TLS/ECDH relay sessions transfer native full/delta frames,
stereo and input between two peers, and verify permission revoke, disconnect,
kick, wrong password, changed certificate pin, GCM tamper/replay and quick clicks.
Host input-layout changes synchronize without replacing the client profile and
release held input. An intermittent grant/UI race that swallowed the first key
press was fixed by atomically publishing input configuration and synchronizing
before input callbacks; the regression deliberately presses between UI updates.
The actual AmigaWindow test additionally passed with KICK13 booted for 80 frames
and HQ4x selected only on the client. An external C64 harness confirms six menu
categories and the unchanged C64 TLS/ECDH/AES-GCM video/input transport against
the new relay. Detailed protocol and commands: [NetworkPlay.md](NetworkPlay.md).
The build has zero warnings/errors; both service scripts pass Bash syntax checks.
Fresh external evidence is under
`%APPDATA%/AmigaEmulator/diagnostics/SetupNetwork-20261010-090319`, including
`all-regressions-final.log` and the C64 shared-relay smoke harness. No user ROM,
disk, save or hard disk image was changed by these network tests.

The existing public proxy has not been redeployed: replace its relay program
with the new dual-protocol server and preserve its certificate/key and port.
The previously listed machine/game/performance roadmap remains open; these
network tests do not prove gameplay compatibility for every model or game.

### Installer, user ROM startup and GitHub updates (2026-10-10)

The Amiga now uses the C64-style Inno Setup / self-contained Windows x64 release
and GitHub startup update flow. VERSION controls executable/setup versions.
The build audits ROM/media exclusion, emits Setup plus SHA-256, and the version-tag
workflow runs regressions and an actual install/update/uninstall test before
publishing assets. The stable Amiga installer ID is separate from C64's ID;
an extra closing brace inherited from the reference installer was found and
fixed during a real installation test. Silent upgrade handling and /NOICONS
were also verified. User-owned ROMs, media and AppData survive update/uninstall.

GUI startup asks for a user ROM file/folder when no usable ROM is available.
Selected external paths persist and appear in the ROM menu; no ROM is downloaded
or copied into the distribution. Cancel/invalid/absent ROMs exit before emulator
and updater creation. CLI diagnostics remain noninteractive. The actual entry
point was tested with a fresh empty profile and its dialog visually checked.

Nine distribution regressions cover startup validation/persistence, stable release
and repository selection, version comparison, streamed SHA-256 verification,
wrong hashes, truncated/empty/failed downloads, pre/mid-transfer cancellation,
and closing the completed download dialog without accepting installation.
All 274 .NET regressions pass. The actual self-contained Setup installs, upgrades,
runs ROM diagnostics and uninstalls in fresh external directories, with synthetic
user ROM/HDF fixtures unchanged and registration cleaned up. The GitHub workflow
passes actionlint; no public tag/release, commit or push was performed.

Evidence: `%TEMP%/AmigaInstaller-20261010-101520` and the external directory named
in `permanent-installer-smoke-final.log`. Reproducible commands and retained-user-data
behavior: [InstallationAndUpdates.md](InstallationAndUpdates.md). The hardware,
gameplay and performance work listed above remains open.

### Explicit mouse capture and release (2026-10-10)

GUI startup, focus and hover now leave the Windows mouse free. An explicit
image click enables the existing centered relative input and clips to the
actual presentation rectangle. The capture click and matching release are
consumed locally, preventing accidental guest actions. Ctrl+Alt+F8 and the
main menu's RELEASE MOUSE action restore the normal cursor, remove Windows
clipping and discard buffered local motion/deferred or held mouse buttons.
Menus retain capture, clip the visible menu pointer to the emulator client area,
and consume pointer movement/clicks as host UI input. RELEASE MOUSE is clickable;
closing a menu restores centered relative guest input without a pointer jump or
another capture click. Loss of focus, minimize and window/handle teardown release
capture. Returning to the window after focus loss requires another image click.
Keyboard F8 remains available outside the release chord; the shortcut releases
its guest modifier inputs. Host release preserves remote owners' input, while
client release sends its own mouse button release to the host.

Five regressions exercise actual shown WinForms windows and Win32 clipping,
capture-click suppression, guest CIA/POT/JOY registers, shortcut repeat/up,
clickable menu release/back navigation, scrolled disk/ROM click mapping, focus
and close, all six upscalers, stretch and
fullscreen. The existing real host/client transport regression additionally
checks client release and retention of remote input when the host releases.
All 279 regressions pass; the Release build has no warnings/errors. Fresh
evidence is in `%TEMP%/AmigaMouseCaptureVerification-20261010` and isolated
`%TEMP%/AmigaMouseCaptureTests` profiles, including a visually checked menu
capture. ROMs, saves, disk images and existing user profiles were not changed.

### Paula clarity and sample correctness (2026-10-10)

The user's muffled Siedler music exposed overly strong host filtering and a
Paula holding-word/output-word bug. Four independent OFF/A500/A1200/LED profiles
now use rate-correct circuit approximations; hardware profiles obey CIA-A /LED,
and remote audio carries that state per chunk. A prefetched word cannot overwrite
the playing low byte. AUDPER zero, PAL/NTSC output rate, fractional DAC-area
integration and old/new audio savestate continuation are covered.

All 294 regressions pass with no skips; Release builds have no warnings/errors.
Four paired Siedler intro runs preserve CPU/image hashes and show no meaningful
core slowdown (4.0371 s baseline vs 4.0086 s corrected median). Corrected A500
output preserves substantially more 2–8 kHz detail, with no clipping. Actual
mission-selection and older user gameplay saves also continue with unchanged final CPU/image state.
Evidence, reproducible WAV capture and remaining audio limitations are documented
in [Audio.md](Audio.md); fresh output is in
`%TEMP%/AmigaAudio-2dd393b5345b47a689fb8b2378dbd697`. User media and preferences
were preserved. This audio work does not complete the wider hardware/gameplay
roadmap. Next audio checks are subjective listening and long-session underruns,
followed by ADKCON modulation and fuller DMA/interrupt state-machine coverage.

### Siedler wood-frame report: reproduction still open (2026-10-10)

The user reports incorrect wood borders / black margins again. Current-core
continuations of the older user gameplay save and recent mission-selection
checkpoint were inspected in Fast and Compatible modes, including pointer
movement to opposite edges. The optional local Siedler wood-frame/menu/mouse
regression passes. Eight previously verified model gameplay checkpoints also
continue with coherent wood borders and bottom controls. This is continuation
coverage, not a new cold-boot or full-game compatibility claim.

An external STA probe exercised the actual AmigaWindow / SharpPixels OnPaint
path with NONE, SCALE2X, SCALE3X, HQ2X, HQ3X and HQ4X, with preserved aspect
(704x576) and stretch (1280x720). All modes keep the same presentation rectangle;
NONE matches independent pixel-center nearest-neighbor sampling exactly in both
sizes. The inspected NONE output shows a full frame and menu. No graphics fix
has been made: the reported current defect has not yet been reproduced.

Fresh read-only evidence is in
`%TEMP%/AmigaSiedlerPicture-9bf78e68c1b14c92a25b0fa67e52c48d`:
`before/`, `models-current/`, `golden-fast.log`, `ui-probe.log` and twelve actual
window-paint PNGs. The probe has its own isolated profile. User ROMs, disk
images, settings and original saves were preserved. Next concrete step: obtain
the user's current screenshot (and a pre-error save if needed), identify the
exact missing frame/margin, reproduce it before changing aperture logic, then
add a regression for that defect. This issue remains open.

### Siedler intro disk-change fix and separate game-handoff failures (2026-10-10)

The user confirmed starting with Disk 1 (Intro). A fresh A500 / Kickstart 1.3 /
2 MiB Chip + 8 MiB Fast cold boot reaches the actual Disk-2 request. Immediate
physical replacement leaves stale volume data: the RLE unpacker overruns its
input and overwrites its own code/pointers. The first non-ROM address error is
at $20E428, frame 10174; 116,407 faults accumulate after 85 million post-click
chip clocks, freezing the image and sustaining the sound note. Compatible
reproduces at frame 10175. An explicit five-million-clock empty interval removes
this failure; waiting after an immediate replacement does not.

Replacement now exposes physical absence for two emulated PAL seconds
(7,082,400 chip clocks), followed by insertion and motor-ready spin-up. Head,
side, selection and motor lines remain intact. Explicit eject time counts
toward the interval; initial boot insertion is immediate. Queued media provides
no flux, index or writes. Pending insertion survives file restore; legacy states
default to immediate presence and retain the physical drive after eject. This
uses the removal approach also documented in
[WinUAE's disk-change implementation](https://raw.githubusercontent.com/tonioni/WinUAE/master/disk.cpp);
no reference source code was copied.

Exact pre-swap replays now continue the second intro animation/music with zero
non-ROM faults in Fast and Compatible. The intro finishes and requests Disk 3.
The actual AmigaWindow MEDIA action and buffered mouse-button path also reach
the advancing movie. Tools/siedler-intro-swap-scenarios.json retains cold boot,
requested media, immediate confirmation and pending-insertion restore coverage.
Its scripts end at the later game-loader Disk-2 request, not completed gameplay.
GameRunner logs Paula audio and offers an explicit non-ROM fault limit. The
same guarded runner rejects the old 116,407-fault replay and continues with the
fixed core. This guard does not detect software Exec alerts.

All 299 regressions pass (zero failures/skips), including four new media-change/
restore/legacy tests and the local Siedler wood/menu/mouse golden. Persistence
tests still verify outgoing flush and incoming protection/readiness. Release,
test and runner builds have no warnings/errors. The local 0.1.0 Windows
installer was rebuilt; no installation, publication, commit or push occurred.

**Separate failures remain open.** The full Intro -> Disk 3 -> game-requested
Disk 2 route with 2 MiB Chip + 8 MiB Fast raises Exec AN_FreeTwice ($81000009)
before new disk DMA. DOS frees an already-released 224-byte block at $23B030;
the allocation and first free are traced. A cold run without any restore also
fails. Waiting for insertion before clicking or explicitly ejecting Disk 3
longer does not fix it; 1 MiB Chip + 4 MiB Fast also fails. No speculative
mouse delay, guest-memory patch or automatic RAM-profile change was added.

A 1 MiB Chip/no-Fast cold intro path reaches symbols, menu and mission terrain,
but construction later triggers address errors at $02D5DE. A 2 MiB Chip/no-Fast
control stalls in the intro ($FC0F94) and is not a pass. Direct Disk-3 boot with
the user's 2 MiB Chip + 8 MiB Fast layout reaches the playfield, responds to
input, builds the first castle and passes complete-state restore without
non-ROM exceptions. This is a verified temporary start route, not intro-handoff
acceptance. The current wood-border report and wider roadmap also remain open.

Fresh read-only evidence: %TEMP%/AmigaSiedlerSwap-2f0e4eda03734dfda99416766961afee
(swap-before/, swap-fixed/, guard-before/, guard-after/, window-menu-swap.png,
cold-no-restore/, cold-ram-controls/, mission-controls/ and heap traces).
Durable selected logs, reviewed captures and pre-error/first-castle states:
%APPDATA%/AmigaEmulator/diagnostics/SiedlerIntroSwap-20261010-10fd2990c1cf.
User ROMs, ADF/HDFs, settings and original saves were preserved.

Next: replay the durable large-RAM game-request state and trace DOS file-handle
owners from the first $23B030 free through the second close; compare direct-boot
and intro-loader process/segment lifetimes. Disk 3's startup runs SIEDLER.EXE
before DieSiedler; the intro loader uses LoadSeg/direct calls and UnLoadSeg.
These launch differences are evidence to investigate, not a proved cause or
justification for patching the game or suppressing Exec alerts.

### Siedler filesystem owner trace and cold controls (2026-10-10)

The exact large-RAM handoff was replayed again from the durable pre-free state
with an external instruction and memory-write probe. Both calls to Kickstart
Exec `FreeMem` for the 224-byte block at `$23B030` run in the **same** `File
System` task (`$2046D8`). The first occurs at frame 19503 through ROM
`$FFD038 -> $FF45CA -> $FC1740`; the second occurs after the game requests
Disk 2, at frame 20170 through `$FFD45A -> $FF45CA -> $FC1740`. The block's
BPTR `$0008EC0D` remains in the task-owned field at `$204588` between them.
That field was cleared at frame 19420, assigned `$0008EC0D` at frame 19484,
then not rewritten after the first free. The first ROM path branches around
its assignment at `$FFD056` because the tested table entry is zero; the
second ROM path attempts to free the stale pointer and clears the field only
after the failing call. This identifies an invalid guest filesystem state,
but not yet the emulated hardware event or earlier memory/CPU behavior that
causes it. No guest-memory/ROM patch was made.

At the first free, the handler follows a structure at `$2052AC` with value 4
at offset `$08` (the DOS packet number for `ACTION_SET_MAP`) and zero at
offset `$0C`. Its nearby pointers resemble a DOS packet, but the exact
structure/operation still needs confirmation. The first ROM branch tests the
zero field and bypasses replacement of the bitmap owner.
The block length `$E0` is consistent with a filesystem bitmap. A separate
read-only DMA audit of the immediately preceding Disk-3 track-40 read (frame
19482) decodes all 11 MFM sectors with valid checksums, and all 11 payloads
match the ADF byte-for-byte. This rules out corrupted sector delivery in that
read, but not earlier guest-state corruption or a different filesystem
operation.

Two further **cold** A500/Kickstart 1.3 controls rule out simple fixes:

- Normal A500 `Chip512KSlow512K` runs the Intro/Disk-2 exchange but remains
  black and ROM-idle after Disk 3 and the later Disk-2 request; no gameplay.
- With the original `Chip2MbFast8Mb` layout, explicitly ejecting Disk 2 and
  waiting 20,000,000 chip cycles before inserting Disk 3 avoids the immediate
  FreeTwice screen, but the guest still freezes with the green game panel.
  No new floppy DMA occurs after the game requests Disk 2 (487 reads before
  and after), and a further 120,000,000 cycles plus mouse input leave the
  framebuffer unchanged. This is not a gameplay pass.

A working alternative was verified with the user's existing Kickstart **2.04**
ROM (SHA-1 `C5839F5CB98A7A8947065C3ED2F14F5F42E334A1`, identified as the
A500+ 37.175 ROM by [FS-UAE](https://fs-uae.net/docs/kickstart-roms/)), A500 model,
2 MiB Chip + 8 MiB Fast, and the same three ADFs. The cold
Disk-1 Intro -> Disk-2 -> Disk-3 -> game-requested Disk-2 sequence makes 49
additional completed floppy DMA reads (527 to 576) after the final swap. It
reaches the animated game interface, accepts the symbol/menu mouse sequence,
starts a mission, responds to playfield input, builds a castle, and restores
both gameplay and construction states with identical CPU/bus/frame hashes.
There are zero observed non-ROM CPU exceptions through frame 27507. Reviewed
captures in the durable diagnostic directory at
`kick20-gameplay-evidence/40-mission-started.png` and
`48-construction-result.png` show terrain and the completed castle.
This A500 run was executed twice (once split at a saved state and once
continuously); both produced the same reviewed hashes.

The matching **A500+** model was then tested with the same ROM, RAM, disks
and complete input sequence. It also reaches the mission, builds the castle
and restores both gameplay and construction state at frame 27507 with zero
observed non-ROM CPU exceptions. The ECS model has different deterministic
framebuffer hashes, but its reviewed terrain and castle captures match the
expected scene. `Tools/siedler-kick20-gameplay-scenario.json` now targets
A500+ and asserts those two reviewed golden hashes. It was validated and
executed end-to-end from a cold boot twice, including a final run with both
golden assertions enabled. The corresponding durable evidence is
in `kick20-a500plus-gameplay-evidence/`. This is a practical configuration
workaround, not a fix for the Kickstart 1.3 path.

The Kickstart 1.3 ROM SHA-1 is
`891E9A547772FE0C6C19B610BAF8BC4EA7FCB785`, matching the recognized
34.5 image. A read-only state comparison locates the same file-system owner
field at `$204588` in both the direct-Disk-3 and Intro routes. At the direct
game Disk-2 request it contains BPTR `$0008E4BF`, whose `$2392F8` allocation
has an intact `$E0` header; that route subsequently enters gameplay. At the
Intro request it contains the already-freed `$0008EC0D`; waiting longer
before inserting Disk 3 leaves this exact stale value unchanged. Trace logs
and external read-only scenario outputs are under
`%TEMP%/AmigaSiedlerSwap-2f0e4eda03734dfda99416766961afee/` as
`dos-lifecycle-v3.log`, `dos-lifecycle-v4.log`, `dos-first-packet.log`,
`dma-audit.log`, `cold-default-ram-run/`,
`cold-long-eject-2-to-3-run/`, and `long-eject-continue-run/`. User media,
ROMs, settings, and saves remain untouched.
The key logs and reviewed Kickstart-2.04 captures/step log were also copied to
`%APPDATA%/AmigaEmulator/diagnostics/SiedlerIntroSwap-20261010-10fd2990c1cf/`
so the evidence does not depend on temporary files.

Next: identify the structure at `$2052AC` and why the Intro route takes the
zero branch despite a valid Disk-3 track-40 read; compare its arguments,
return path and prior bitmap state with the direct-Disk-3 boot. Correct any
underlying emulator behavior demonstrated by that comparison and verify
actual Disk-1-to-game gameplay with input and new Disk-2 DMA before marking
the handoff fixed.

### Siedler Kickstart-1.3 handoff: damaged Disk-1 bitmap repaired (2026-10-10)

The subsequent source-media and packet inspection resolves the preceding
unknown. The local PDX Intro Disk 1 has inconsistent OFS allocation metadata:
root block 880, word 78 is zero (not validated), while bitmap block 881,
word 28 is `$00002000`. That bit marks block 879 free even though block 879
is the valid, occupied header of the zero-length `.info` file in the root.
Root, bitmap and file-header checksums are otherwise valid. The independent
`amitools 0.8.1 xdfscan -v` scan reports exactly one root-validity warning and
one allocation error; Disk 2 and Disk 3 have no errors/warnings.

The old-state trace confirms that `$2052AC` is a DosPacket: type 4 is
`ACTION_SET_MAP`, its primary result at offset `$0C` is zero, and its reply
port points to the `Validator` task at `$205130`. This failed asynchronous
reply reaches the filesystem at frame 19503, after Disk 3 has loaded its
bitmap at frame 19484. ROM `$FFD026 -> $FFD038` frees that current 224-byte
bitmap allocation at `$23B030`, but the zero result branches around the
replacement/clear at `$FFD056`. The stale BPTR at `$204588` is subsequently
freed again through `$FFD45A` at the game-requested Disk-2 handoff, producing
`AN_FreeTwice`. The packet layout is consistent with the official
[Amiga ROM Kernel Reference Manual: DOS](https://developer.amigaos3.net/sites/default/files/downloads/2024-10/Amiga_ROM_Kernel_Reference_Manual_DOS.pdf).

`Tools/prepare_siedler_intro_disk.py` creates an exclusive new ADF and accepts
only the exact reviewed source SHA-256
`ce24f8af192683c6b143bb9c4d6d3e3da707dc79541da99398e62ad11fe38de0`.
It sets root word 78 to `$FFFFFFFF`, clears bitmap word 28 and recomputes
their two checksums. Exactly four 32-bit metadata words change; all boot,
directory entries and game code remain byte-identical. The new SHA-256 is
`5810ceafc46757a7d39b3693fb05afdaf041621e5f1463fa618aac0c21dce375`.
Independent filesystem validation is clean. Guard checks prove that an
existing destination/original cannot be overwritten and an unsupported disk
is rejected without producing output. The user's ready-to-use copy is
`%USERPROFILE%/Documents/AmigaEmulator/Die Siedler (disk 1).adf`
(renamed with the collection on 2026-10-11; see `GameMediaNames.md`).
Boot afresh from this copy; an old failed savestate still contains its stale
guest filesystem owner. Original ADFs, ROMs, HDFs, settings and saves remain
unchanged. No ROM/guest-memory patch, filesystem-read interception or alert
suppression was added to the emulator.

Fresh cold A500 / Kickstart 1.3 34.5 / 2 MiB Chip + 8 MiB Fast runs in both
Fast and Compatible complete Intro Disk 1 -> requested Disk 2 -> Disk 3 ->
game-requested Disk 2, with 58 new completed DMA reads after the final swap
(486 -> 544). It accepts symbol/menu input, starts the mission, responds to
playfield input, builds the first castle and restores both gameplay and
construction with identical serialized CPU/bus state, beam and framebuffer.
There are zero observed non-ROM CPU exceptions through frame 27507. The
mission and constructed-castle screenshots in both profiles were reviewed.
The deterministic profiles have slightly different rendered pixels but show
the same terrain, pointer response and completed castle:

| Profile | Mission frame 26306 SHA-256 | Castle frame 27507 SHA-256 |
| --- | --- | --- |
| Fast | `B43551D042CC26C6F563BCE1F9D33B25AEF57C6BA71BF44AF096D0A761CD2B72` | `6064FA30C427332946BF0185DB0AD1DC777B6DE42D0710049A87DBE9103E2A7D` |
| Compatible | `9C8E5449BD528265320F4CD8E2C40B98F9F25916B172ADE96FD96904091CB75D` | `BCA70BD58520752560CE2B5D55217ECFFBD37C0C831AE110941903E6E2F2D6D2` |

The permanent `Tools/siedler-kick13-gameplay-scenario.json` asserts these
reviewed frame hashes and documents the required private media and ROM in
`Tools/GameRunner/README.md`.
The Fast case was cold-booted a second time using the user's new Documents
copy, with both golden assertions enabled; it passed through frame 27507.
The Compatible case's saved game-loaded state was replayed with the same
input sequence and its two golden assertions enabled; both passed, including
the exact gameplay/construction restores. The two complete cold runs and
these assertion replays are distinct checks, not a full all-model matrix.

Selected validator traces, before/after filesystem scans, guard verification
and gameplay evidence are preserved in
`%APPDATA%/AmigaEmulator/diagnostics/SiedlerKick13BitmapRepair-20261010-0cc937b0564e/`.
This resolves the reviewed source image's Kickstart-1.3 handoff, not arbitrary
damaged-media handling or complete filesystem/hardware conformance. The full
model roadmap and unreproduced border report remain open.

Next: reproduce the remaining wood-border report in the frontend with the
active scaling/fullscreen configuration, using the repaired media for the
positive intro route; continue the separate native-model/CPU roadmap gates.

### Game-media filenames normalized (2026-10-11)

The user's collection now uses `Game Title (disk N).adf`. All 51 ADF hashes
are unchanged, including the repaired primary and archived original Siedler
Disk 1. Active source manifests/catalogs and local ADF goldens refer to the
new names; stable scenario IDs and historical evidence remain intact. Ten
save directories, 22 serialized savestates and startup drive references were
migrated, with identical restored CPU/bus/beam/framebuffer state verified for
all 22. `GameMediaNames.md` records scope, validation and the durable path-map
and original-save backup location. This migration does not close any open
hardware/gameplay roadmap gate.

### Selected work 1, 3, 4, 5, 6: caches, fields and validation (2026-10-11)

These numbers refer to the user's chat list (games, graphics, CPU/hardware,
OS/storage, performance), separately from the stage table above. The whole
selected scope remains active. SHRES, full interlace/programmed timing,
restartable MMU faults, FPU, 040 and Zorro III are not complete or advertised
as fully supported hardware.

Actual 020/030 instruction caches and the separate 030/EC030 data cache now
implement the geometry and semantics described above. Fixtures cover FC tags,
aligned fills, self-modifying code, CACR/CAAR, writethrough, freeze/disable,
write allocation, misaligned operands, MMU/ACU CI, locked RMW, faults and
pending bus retirement/restore. Chip/Slow RAM and volatile apertures inhibit
data caching; Fast RAM DMA still requires guest cache management. The bus
does not acknowledge bursts. Timing remains approximate. Core version 16 /
CPU extension 5 stores both caches and reads legacy missing payloads as empty.

LOF now follows VPOSW and BPLCON0 LACE rather than host frame parity. Guest
beam-read-register writes cannot corrupt it. This fixes a specific register
defect; field length is still 312 lines, and weaving/counter writes are pending.
Ten instruction-cache, eleven data-cache and one LOF fixtures extend the full
suite to **320 passing tests, zero failures/skips**. Cache and LOF fixtures have
failing-before proofs against frozen prior cores. Release application and both
runner builds have zero warnings/errors.

Native Kickstart 3.1 A1200/A3000/A4000 Initial CLI cold/warm/restore cases pass
with zero observed non-ROM exceptions and reviewed SHA-256
`E2DE8F5F9EDBB9843B7CB23C16B0CAD08F8998E92A17ACB596D7F0F7C1F1FB84`.
Unchanged HEAD produced the same image: their previous golden was already
stale. Only these three reviewed cases were updated.

Installed OS 3.1.4 runs on disposable A4000/IDE and A3000/SCSI HDFs with both
caches enabled by the guest CPU command. CACR `$00002101`, Shell status,
Version, ShowConfig, Clock and readback were inspected. Both HDFs independently
contain exactly `datacachetest\n`. The first Type after A3000 cold reset was
initially misread as empty in the small capture. Reinspection shows the content;
the pre-data-cache control has the same output. A separate A500 DOS Read probe
records exactly `legacyostest\n` on both first and second commands. There is
no demonstrated first-read storage/cache defect. An initial read-only guest continuation
showed a write-protected volume; the replacement fresh writable cold boot made
eight actual sector writes. The original installed A4000 HDF retains SHA-256
`09957ec42de994fcfa5e81e48c8a25545df0f7a5805c30dc0331e23883253ada`.
Additional native legacy OS tests use the user's purchased package's unsplit
46.143 ROM, checksum `$FFFFFFFF`, SHA-1
`D81CD6F131040895843D9DFA95A45BC95EDD9704`; the ROM/test disks stay external.

Seven native/model Tinyus cold runs and longer continue/start controls were
reviewed: A500, A500+, A600, A2000 ECS, A1200, A3000 and A4000/030. The blank
phases are finite: by frame 3772 the newer-ROM 68000 cases show gameplay or
Continue; controls through frame 4187 show enemies, movement/fire and exact
power-cycle restore, with zero observed non-ROM exceptions. Unchanged HEAD
A500+/A600 controls have the same transition images/CPU registers. The former
permanent-hang interpretation is unproven; later transitions are not certified.
The fresh native A2000 OCS case also shows enemies/movement/fire through frame
2691 with zero observed non-ROM exceptions and exact restore, completing the
eight-board bounded Tinyus coverage. Siedler's repaired K13 Compatible castle
state continues with mouse input and exact reset/restore, with reviewed borders.

A hidden STA probe uses the real AmigaWindow/SharpPixels paint and fullscreen
toggle methods with isolated preferences. All six upscalers retain the viewport
in window/fullscreen and aspect/fill combinations (24 checks). Fullscreen fill
covers 2560x1440; aspect uses x=400,width=1760,height=1440. NONE matches independent
pixel-center sampling exactly. Reviewed NONE-fill/HQ4X-aspect pictures retain
the complete Siedler frame/menu. This is actual automated frontend rendering,
not a visible manual input session or reproduction of the unknown failing setup.

GameRunner silently ignored unknown fields. Older private Desert controls used
`code` instead of `key`, sending zero instead of Return; Zak's source mouse
action used ignored `dx`/`dy`. It now uses `x`/`y`. Unknown fields, missing keys,
duplicate names and malformed/null scenario lists fail before output/disk
creation. Seven preflight regressions fail on the old runner and pass now.
All 80 direct source scenarios and a generated 24-case subset validate. Variant
templates are generator inputs, not direct cases. Correct Return/Space/Fire
Desert controls still show the carrier cinematic on A500/A1200; mission entry
remains open. Earlier wrongly spelled controls cannot certify those inputs.

Framebuffer clear uses Array.Fill. Five alternating pairs per game, 500 warmup
frames and two 300-frame samples retain identical complete serialized CPU,
register, PC and framebuffer hashes at all 20 paired endpoints. Final-core
paired median changes versus the pre-clear control are Siedler +0.87% and
Moonstone +0.52%; pair ranges are -2.03..+3.78% and -7.01..+3.75%. These are
modest noisy measurements, not a large speedup. Earlier clear-only pairs showed
+2.35%/+1.61% with greater variance. A cycle-estimate lookup regressed Moonstone
by 3.15% and was removed. The benchmark now hashes full CPU state outside timing.
Long unattended Alien Breed becomes black on both HEAD and the changed core,
including intentional ILLEGAL/trace vectors; it is not a gameplay FPS endpoint.

Durable evidence is under
`%APPDATA%/AmigaEmulator/diagnostics/RoadmapSelected-20261011-a7ac3b55b5e3/`:
`regression-both-caches-final.log`, cache/LOF failing-before logs,
`native-cli-data-cache-final/`, `os314-both-caches-final/`,
`a3000-both-caches-cold/`, `a3000-cache-read-control/`,
`a3000-cache-cold-comparison.json`, `cache-os-independent-verification.json`,
`tinyus-lof-*-final/`, `tinyus-live-controls/`, `siedler-core-final/`,
`frontend-probe/results.json`, `scenario-preflight-before-proof.log`,
`runner-manifest-preflight.json` and `paired-performance-final-summary.json`.
Frozen runtimes and private manifests remain beside the outputs.

Fresh native A3000/A4000 Pinball source flows still recover to Kickstart after
executing `$FFFF` at unmapped `$200002` (Line-F vector 11), preceded by the
known user-mode MOVE-to-SR privilege probe. The A3000 pre-cache control has the
same fault; the new caches change its frame timing, not the unmapped target.
This is not a gameplay pass and no RAM alias/CPU bypass was introduced. Evidence:
`selected-cold/`, with reviewed A3000 final output and the explicit exception logs.

The later `pinball-unmapped-watch/` trace identifies an earlier cause at frame
11219: the source disk reader obtains A3=`$80080000` from its tagged destination
field and writes 87,634 bytes to unpopulated high space. Partyland's actual Chip
RAM payload remains zero. Its relocation loop subsequently modifies the empty
module, calls `$80010` at frame 12606 and executes zeros until the Line-F fault
at `$200002`; it is not a direct jump to Zorro RAM. Removing motherboard Fast RAM
does not repair the source copy (`pinball-chip-only-control/`, reviewed black
output after the source flow, not a gameplay pass).

This is the documented 24-bit pointer-tag compatibility problem on full 32-bit
processors, described by the [WHDLoad author](https://www.whdload.de/docs/en/howto.html).
The 68000/68EC020 discard the upper eight address bits; 68020/68EC030/68030 do
not. The address-width fixture now includes full 68030 and bit-31 device/RAM
probes. Original media is retained. A separately guarded, actually tested
32-bit-clean loader copy would be required for this version; the core must not
map `$80080000` onto Chip RAM or globally mask native desktop CPU addresses.

For causality only, the external `PinballFlagProof` probe clears A3 bit 31 at
two reader points (`$2C50`, `$2D1E`) after loading the before-table checkpoint.
That modified diagnostic state reaches an actual Partyland table at frame
13704 with zero non-ROM synchronous faults. A subsequent ordinary core/input
continuation shows the lower table, launch/flipper activity and matching
power-cycle restore at frame 13926, still zero observed faults. Screenshots
were reviewed in `pinball-diagnostic-pointer-review/`; source SHA-256 remains
`b186b41c3ffdc245743995fac86f4aa3e7476658607be6946c7b5c1a3e2a1476`.
This confirms the pointer diagnosis, but host register edits are not a released
fix or an accepted source-media compatibility case. Evidence also includes
`pinball-pointer-cause-verification.json`. No production game-specific branch
or hardware-address alias was added.

The first five owned-ROM legacy OS scripts reach coherent desktops, but their
Execute Command dialog drops the first letter of `newshell` and reports
`ewshell: Unknown command`. Their later text/Clock steps are therefore invalid
application evidence. These retained outputs are superseded by fresh
`os-legacy-ready/` scripts waiting 12 million chip clocks for the dialog and
Shell before typing. The permanent `Tools/run-purchased-os-validation.ps1`
flow was then executed on five fresh HDF copies under `source-os-preflight/`.
A500, A500+, A600, A2000 OCS and A2000 ECS show coherent Shell/Version/ShowConfig,
Clock and file output after cold/warm restarts. All five HDFs independently contain
exactly `legacyostest\n`; recorded CPU/frame/image restore endpoints match and
no non-ROM synchronous exceptions were observed. Source test HDF and original
installed user HDF hashes are unchanged. Evidence: `os-legacy-source-contact.png`,
`legacy-os-independent-verification.json` and `dos-read-first-type-copy/dos-read.jsonl`.
The initial claim of an empty first output was an incorrect visual assessment,
not a fixed emulation defect; it is explicitly superseded here.

Next: develop and verify a separate 32-bit-clean Pinball loader copy, and trace
Desert/Mega mission transitions using validated inputs. Then implement missing native
output and CPU/MMU hardware gates with independent fixtures. This checkpoint is
committed and pushed as `1bbc12e` at the user's request on 2026-10-11; the
recurring roadmap remains active.

### Post-commit Pinball loader and overlapping TT fix — 2026-10-11

The preceding cache/LOF/OS checkpoint is committed and pushed as `1bbc12e`.
The following work is a separate local continuation, not another commit/push.
Evidence remains under
`%APPDATA%/AmigaEmulator/diagnostics/RoadmapSelected-20261011-a7ac3b55b5e3/`.

**Pinball's separate media repair is now real guest code.** The original block
setup at guest `$2A5A` copies a tagged descriptor into the transfer pointer and
only then clears bit 31 in the descriptor. It subsequently adds the relative
base to the still-tagged transfer pointer. The 38-byte replacement loads D1,
clears the descriptor flag, conditionally removes bit 31 from D1 and adds the
base, then publishes the clean pointer. Length/type setup and reader entry stay
at their original addresses; an absolute-word JMP saves the two bytes needed.
No trampolines, data tables, CPU address masks or game-specific core branches
are added. Original full-address behavior is retained.

`Tools/prepare_pinball_32bit_disk.py` accepts only source Disk 1 SHA-256
`b186b41c3ffdc245743995fac86f4aa3e7476658607be6946c7b5c1a3e2a1476`, checks
the exact instruction window at ADF offset `$5914`, creates exclusively, and
verifies output `f4e0eacdd4908efc71dbd26625388c503134529b0adc5d6be86319e6cbccba28`.
Only those 38 payload bytes change; raw ADF/MFM sector checksums are generated
by the existing encoder. Source-as-output, existing-output, already-patched and
altered-source negative controls pass. Disk 2/3 retain their original bytes.
The CPU fixture executes both setups on all five CPU families, exercises
absolute and relative descriptor branches, checks preserved state and a real
transfer write, and reproduces the original tagged write on full-address CPUs.

Fresh cold starts with the new copy reach Partyland on native A3000/A4000 at
frame 16332, with reviewed launch/flipper/lower-table activity and exact restore.
A500/A1200 cold controls also pass; their final power-cycle restores match.
These four boot paths retain only the expected initial vector-8 privilege probe
from the game's CPU-detection code. Separate final-core desktop continuations
pass power-cycle restore and input through frame 16424 with zero newly observed
non-ROM synchronous exceptions. Unlike the prior host-register proof, these
are actual cold boots from the repaired disk.

A fresh patched-disk A3000 boot supplies a reviewed course-menu checkpoint at
frame 9166. From that checkpoint, Speed Devils (frame 12712), Billion Dollar
Gameshow and Stones 'n' Bones (frame 15095 each) show their own table artwork,
one-player launch/flipper activity and exact power-cycle restore, with zero
new non-ROM synchronous exceptions. Course Disk 3 is requested via eject/HELP
and visibly reaches its distinct course menu. These are bounded continuations
from a real cold boot, not complete campaigns or separate cold starts for
every table. A4000's other three tables remain to be executed/reviewed.

The initial `pinball-additional-tables/`, `pinball-course2/` and
`pinball-quit-chord/` executions are explicitly **not accepted as additional
tables**: pause/quit did not enter the menu and their captures stayed on
Partyland. The earlier progress-message claim of Speed Devils was corrected
after image review. Their retained outputs do not replace the valid fresh-menu
tests. Pause/quit behavior is still open and needs explicit held-key observation.

Evidence: `pinball-native-copy-cold/`, `pinball-copy-legacy-controls/`,
`pinball-final-core-restore/`, `pinball-copy-before-table/`,
`pinball-remaining-tables-valid-menu/`, `pinball-loader-final-reviewed.json`,
`pinball-reproducible-tool-validation.json`. A user-selectable three-disk copy
is prepared under `%USERPROFILE%/Documents/AmigaEmulator/Kompatibel/Pinball Fantasies/`;
no disk is silently substituted and user emulator preferences are preserved.
The original-media matrix classification still applies to the original set.

The matrix generator's explicit `-PinballCompatibleBoot` adds separate native
desktop cases; `-PinballTables` chooses all four with correct F1/F2 and course
disks. Original cases remain. The default combined Moonstone/Pinball full matrix
has 256 validated cases; all Pinball tables yield 262 validated cases. Wrong
hash/model controls reject before creating an output directory. Preflight is
not gameplay acceptance. Evidence: `all-256-compatible-preflight/`,
`all-262-four-tables-preflight/`, `pinball-matrix-tool-verification.json`,
`pinball-matrix-negative-controls.json`.

**68030 transparent overlap:** translation previously returned at the first
matching TT register. A cacheable TT0 therefore suppressed CI from overlapping
TT1. Motorola MC68030UM section 9.3 requires ORing CI from all matching windows.
The fix combines both matches while ignoring CI from nonmatching address, FC
or read/write windows. FC7 still bypasses them and internal CI stays absent
from MMUSR. The independent truth-table/save/restore fixture fails before the
fix (`tt-overlap-before.log`); instruction/data cache fixtures verify that
neither register order allocates an inhibited miss. This changes no state format.
Source: [MC68030UM](https://www.nxp.com/docs/en/reference-manual/MC68030UM.pdf).

Final Release build has zero warnings/errors; **322 core tests** and seven
strict-runner preflight tests pass. A3000 native-ROM HDF boot, warm/cold reset,
golden framebuffer and restore assertions pass with the new core under
`native-cli-tt-followup/`. Original installed OS HDF SHA-256 remains
`09957ec42de994fcfa5e81e48c8a25545df0f7a5805c30dc0331e23883253ada`;
all fifty original ADF hashes match the preserved inventory.

**Desert remains open:** fresh correctly spelled left/right click, Return,
Space, Fire, F10, Up and Shift/Escape controls on A500/A1200 still retain the
carrier screen. The A500 probe verifies physical CIA fire input (`$FE->$7E`)
and the game's own current/previous button bytes (`$00->$40->$00`) over multiple
frames. The game receives Fire; input delivery is not the established cause of
the stalled transition. No forced guest flag or fabricated gameplay pass is
used. Evidence: `desert-carrier-mouse/`, `desert-carrier-map/`,
`desert-input-probe.log`. Continue by tracing the active script/menu state and
by correcting/validating held-key pause/quit, then execute the three remaining
A4000 Pinball table cases. Mega/Cannon, native superhires/full interlace,
ATC/timing/restartable MMU, FPU/040 and Zorro III acceptance are still open.

### Keyboard recovery, Desert flight and remaining Pinball tables — 2026-10-11

This is another local continuation after `1bbc12e`, with no new commit/push.
Evidence uses the same `RoadmapSelected-20261011-a7ac3b55b5e3` diagnostics root.

**All four Pinball tables now have bounded native desktop loading evidence.** A fresh
A4000 repaired-disk boot supplies its own frame-9166 course-menu checkpoint.
From there, Speed Devils reaches frame 12712; Billion Dollar Gameshow and
Stones 'n' Bones reach frame 15095 each, with distinct reviewed artwork,
one-player/key input, exact power-cycle restore and zero new non-ROM
synchronous exceptions. Together with the preceding A3000 evidence, all four
tables are covered on both desktops using the separate compatibility copy.
After the keyboard changes below, fresh A3000/A4000 Partyland boots again pass
table loading/input/reset/restore at frame 16332. The later continuation below
corrects the insufficient plunger hold: these earlier checkpoints alone did
not prove an actual launched ball. Evidence:
`pinball-a4000-before-table/`, `pinball-a4000-remaining-tables/`,
`pinball-a4000-tables-reviewed.json`, `pinball-native-keyboard-cold/`.

**A real keyboard deadlock is fixed.** Desert's old airborne checkpoints contain
seven queued events and an outstanding byte whose CRA acknowledgement was lost
during initialization. New F10/0 events accumulate indefinitely: clearing CIA
ICR does not complete the keyboard handshake. The old empty-queue early return
also prevented recovery of a final outstanding byte.

[Amiga HRM Appendix G](https://www.theflatnet.de/pub/cbm/amiga/AmigaDevDocs/hard_g.html)
specifies a logical-1 clock every 143 ms until synchronization, then F9 and
retransmission of the garbled code. The implementation supplies one physical
KDAT-low bit per timeout, using a CIA receiver that counts individual external
CNT edges. F9 and the replay each wait for their own acknowledgement. Startup
synchronizes before FD/FE without F9. Partial receiver state, deadline and replay
stage are saved; legacy snapshots reconstruct known partial bytes and recover
queued events without inventing their unavailable outstanding byte. Full
electrical handshake pulse-width and clock-edge timing coverage remains open.
This adds no title-specific core branch or forced guest register/flag.

Six independent fixtures cover timeout ordering even with an empty queue,
startup, partial-edge/F9 restore, legacy snapshots, and losing F9's own
acknowledgement. `keyboard-recovery-before.log` fails on the old implementation;
the final suite passes **328 tests, zero failures/skips**, with a Release build
containing zero warnings/errors. GameRunner now supports explicit `key-down`
and `key-up` edges and logs queue depth; all nine strict-JSON preflight checks
pass. The regenerated 262-case full manifest also validates; this remains
preflight rather than complete-library gameplay acceptance.

**Desert Strike mission entry and input now have real evidence.** The carrier
was already a playable starting position, awaiting joystick Down to lift off;
the earlier Up/Fire/Return attempts did not establish a stalled cinematic.
Normal Down/turn/fire inputs visibly move the helicopter. With recovery enabled,
F10 opens the mission map and a left click on EXIT returns to flight on A500
and A1200. Continued flight changes the map position and fuel from 97 to 87;
these checkpoints pass exact power-cycle restore with zero new non-ROM faults.
Evidence: `desert-takeoff/`, `desert-keyboard-recovery/`,
`desert-map-exit-flight/`, `desert-keyboard-probe.log`.

Fresh native-ROM cold starts also reach the campaign menu at frame 24971 with
all three original ADFs mounted read-only. From each exact menu checkpoint, a
left click starts the campaign; normal Down/fire/F10 input reaches flight and
the map at frame 32737, then map EXIT/flight/restore at frame 32833. Reviewed
A500 startup retains only its intentional MOVEC and DIVS-by-zero probes;
A1200 retains only the DIVS probe. No new faults occur in either mission
continuation. The source `DESERTS1` flow now contains these reviewed cold-prefix,
mission and map-exit actions. The evidence is segmented at exact checkpoints,
not a newly executed uninterrupted source-manifest run. Remaining six native
models, land combat, objectives and longer missions still require execution.
Evidence: `desert-keyboard-cold/`, `desert-cold-menu-flight/`,
`desert-cold-menu-flight-a1200/`, `desert-cold-map-exit/`,
`desert-cold-map-exit-a1200/`, `all-262-keyboard-followup-preflight/`.

**Pinball pause/quit is still open.** Held P/Esc edges reach the guest's serial
ISR and mode flags with an empty input queue, but a pause freeze/menu return
is not accepted. Esc followed by Y reaches the course-disk write-enable prompt.
Tests with separate writable copies still retain that prompt; its cause is
unresolved, rather than an established write-protect hardware bug. Original
media is untouched. Evidence: `pinball-explicit-pause/`,
`pinball-held-key-probe.log`, `pinball-escape-yes/`,
`pinball-private-return-menu/`, `pinball-confirm-writable/`.

**Idle-path performance control:** an A-B-B-A comparison runs the same 30 million
chip clocks from one reviewed A3000 Pinball checkpoint, without other test jobs.
Old/new measured means are 3.6275/3.6119 seconds (about 0.4% difference).
All four finish at frame 16847 with identical complete serialized CPU and bus
states, beam and pixel hash. Keyboard bookkeeping differs because the new path
clears an already acknowledged final byte instead of leaving its stale wait;
it introduces no guest-state difference here. This is a no-observed-regression
control, not evidence of an FPS improvement. Evidence:
`keyboard-paired-state-observed-with-input.json`.

Native A3000 OS HDF boot, warm/cold resets, golden frame and exact restore pass
again under `native-cli-keyboard-followup/`. All fifty original ADF hashes and
the installed OS HDF still match their preserved hashes
(`keyboard-recovery-originals-verified.json`).

Next: run the validated Desert flow on the other six native profiles; resolve
Pinball's pause/quit/write-enable path using private media; continue Mega lo
Mania/Cannon transitions. Native superhires/full interlace, ATC replacement,
timing/restartable MMU faults, FPU/040 and Zorro III remain open. The full roadmap
is not complete and unsupported hardware is not advertised as compatible.

### Live Pinball, write protection and six native Desert runs — 2026-10-11

Local continuation after `1bbc12e`; no commit/push. Fresh evidence root:
`%APPDATA%/AmigaEmulator/diagnostics/RoadmapContinuation-20261011-074437-f843c655/`.
Existing user changes, original media and OS images are retained.

**DISK WRITE now changes the tab without remounting DF0.** The old remount
triggered media absence/CHNG and discarded partial MFM, rotation and the pending
startup insertion. The drive/controller API now changes only protection;
the frontend refreshes PROT and updates mounted/pending metadata while retaining
the pending deadline, motor, head, dirty sectors, write history and CHNG.
This follows the independent lines in [HRM disk sensing](https://www.theflatnet.de/pub/cbm/amiga/AmigaDevDocs/hard_8.html).
No title-specific core branch or savestate field is added.

Three independent fixtures exercise the actual hidden menu in an isolated
user-data profile, resume/persist a partial sector, and cover pending insertion,
PROT/CHNG and restore on DF0-DF3. The old menu fails both live-medium and pending-
deadline tests (`disk-protection-before.log`). The final focused fixtures and
complete **331-test suite pass, zero failures/skips**; Release builds have zero
warnings/errors. GameRunner's explicit `write-protect` changes the same tab
without replacing media; all ten strict-JSON guards pass. Writable scenarios
use private copies. The full regenerated **262-case** manifest validates;
that remains preflight rather than library gameplay acceptance.

**Correction to earlier Pinball launch claims:** the two-million-clock Down
hold left the guest in launch-chute mode 5. Artwork and injected flipper keys
did not prove a live ball. A 15-million-clock hold and release actually launches
it. Late pauses can miss live play after a ball drains into bonus/next-launch
processing; those retained timing controls are not pause passes.

The corrected suffix now passes on all four tables on native A3000/A4000,
from their own real cold-loaded checkpoints: full spring, one-million-clock
wait, separate short Shift inputs, live capture, P press/release, held pause,
cold reset/exact restore, then Esc/Y. All eight `live.amigasav` probes report
active mode 1. GAME PAUSED captures keep identical pixels across press, release,
five-million-clock hold and restore. Quit confirmation and the write-enable
requester are visible, with zero new non-ROM faults. Per-table pixels match
between desktops. The source contains this same suffix. These are segmented
cold-prefix/continuation checks, not a new uninterrupted eight-case cold run
or long-session/highscore acceptance. Evidence:
`pinball-eight-early-live-pause/`, `pinball-eight-reviewed.json`;
rejected late timing controls: `pinball-eight-live-pause/`,
`pinball-eight-full-spring-pause/`.

A separate A3000 Partyland exit cancels saving, supplies the requested main
disk and then course Disk 2, and visibly returns to the actual selection menu
(frame 21423). Both media replacements and exact cold restores pass. The first
requester after cancellation is not the menu. Evidence: `pinball-long-spring/`,
`pinball-return-main-disk/`, `pinball-return-course-disk/`.

**Persistent scores remain unaccepted.** Clean media is cloned to a fresh
external path while preserving physical checkpoint state; no guest flags or
registers are changed. Changing the tab clears the write-enable wait. Disk DMA
accepts 6658 words with none rejected, but the private ADF remains unchanged.
A buffer dump at DSKLEN arming contains eleven valid standard AmigaDOS sectors
with header track 0 while the drive is at cylinder 1, side 1 (physical track 3).
Canonical ADF decoding refuses that mismatch. A later RAM dump contains track-1
read data, which is not the actual write source. Do not remove the track guard
to manufacture persistence. Evidence: `pinball-private-tab-run/`,
`pinball-write-dma-mode.log`, `write-dma-probe/dma-source-3.bin`,
`pinball-actual-write-decoded.json`. Next trace the loader's cached head and
STEP/SIDE around `$2F8A/$301A` through a writable exit on another private copy.
Raw/custom track preservation remains incomplete.

**Desert Strike now has native cold evidence on the other six boards.**
Uninterrupted source runs on A500+, A600, A2000 OCS/ECS, A3000 and A4000/030
reach campaign selection, lift-off, flight/fire, F10 map, mouse EXIT and
continued flight, exact cold-reset/core restore and further input at frame
32881. Four 68000 runs retain only intentional MOVEC and DIVS-by-zero probes;
the two desktops retain only DIVS. No new mission faults occur. Native ROMs and
three original ADFs are used read-only. No forced guest flags are used.
All six checkpoints also pass movement/cold-reset/restore/continued-flight
on the final tab-fix runtime at frame 32963 with zero new faults.
Evidence: `desert-ecs/`, `desert-ocs-desktops/`,
`desert-six-reviewed.json`, `desert-six-final-tab-core/`.
Together with preceding segmented A500/A1200 checks, bounded flight/map coverage
spans all eight selectable models. Land combat, objectives and campaigns remain
open.

All **50 original ADFs** and the installed A4000 AmigaOS HDF match preserved
SHA-256 hashes (`originals-final-verified.json`). No FPS gain is claimed for
a menu-only fix; further performance work still requires paired identical
CPU/bus/pixel states.

Next: resolve the measured Pinball score-write/head mismatch on private media,
then Mega lo Mania and Cannon Fodder's remaining map/input transitions. Continue
the game/model review and native superhires/interlace, ATC/timing/restartable
faults, FPU/040 and Zorro III gates. The roadmap is active and incomplete;
unsupported hardware remains unavailable.
