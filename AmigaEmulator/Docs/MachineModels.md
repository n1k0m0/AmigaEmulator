# Machine models: implementation and validation

Status: 2026-10-12. Select a machine through **F10 -> SETTINGS -> HARDWARE**.
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
table-search PTEST remains separate. The ATC has 22 fixed slots with a history
bit per slot. Real translation hits and fills set history; exhausting zero
bits clears the other entries' history. The lowest invalid slot is preferred,
otherwise the lowest zero-history slot is replaced. PTEST and observational
queries do not promote entries. Selective flush retains slot order/history.
Nested MMU state version 2 stores slots/history and reads version 1 with neutral
history; core state version 17 lets older applications reject this new layout
before restoring RAM. Independent fixtures cover eviction, epoch reset, holes,
PLOAD, exact restore, old CPU/MMU payloads and transactional rejection of corrupt
slots/history. Timing and complete fault restart remain separate gates.
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
Native PAL superhires output and two-field interlace weaving now pass the
specific guest and AmigaOS routes documented below. Programmable monitor
timing, scan doubling, genlock and generalized mixed-mode transitions remain
incomplete.
The LOF field flop now follows VPOSW and toggles at field boundaries only
while BPLCON0 LACE is set. It no longer alternates in non-interlaced output.
PAL fields now last 312 or 313 scanlines according to LOF, including a programmed
long field with LACE cleared. The machine presents only after the full field;
normal/profiling paths and an extra-line restore are checked independently.
Field weaving beyond the tested PAL modes and programmable beam-counter
writes remain open.
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
| 1. Known defects | A2000 OCS CLI clipping fixed; cold/warm/restore reference assertions; Siedler wood/menu/pointer test passes; requested Intro Disk 1 -> Disk 2 crash fixed by physical media-removal timing; a separately repaired Siedler Disk 1 fixes the Kickstart-1.3 handoff; lost keyboard acknowledgement recovers; menu write protection retains media/partial writes; separate Pinball writer repair persists all eleven score sectors on the correct track on both native desktops; late Cannon sprite no longer collapses the host presentation to its final 80–88 source rows (2026-10-12) | Current Siedler border report remains open; raw/custom-track preservation remains incomplete; fix defects discovered by the broader matrix; retain broader disk-validator/hardware coverage |
| 2. Native gameplay | Full local ADF sweep; Boxx 4 ECS window fix with failing-before test; Tinyus bounded movement/fire on all eight boards; verified live Pinball ball/flippers/pause/quit and actual score-sector writes on all four tables using an explicit repaired Disk 1 on native A3000/A4000; Desert Strike lift-off/flight/fire/map/EXIT/reset/restore on all eight models; Mega lo Mania live islands with explicit RAM/media requirements; Cannon Fodder's first mission on all eight native boards with data drives (2026-10-11) | Complete the 268-case visual/control review and title-specific disk/menu continuations; naturally earned new Pinball highscore entry and broader score reload/model coverage, Desert land combat/objectives, Cannon single-drive requests and longer campaigns, Mega later islands, Sqrxz text/reference checks, native AGA software and longer sessions remain open |
| 3. Performance | Display/opcode caches and allocation reductions; vectorized framebuffer clear measured with identical complete CPU/frame states; profile-driven floppy selection/index/tick optimization with paired identical CPU/frame states (2026-10-12); timing lookup and duplicate bus-classification candidates rejected | Continue profiling remaining allocations and repeat paired measurements after further changes |
| 4. Installed OS | A4000/030 and separate A3000/SCSI 3.1.4 installations; both caches exercised; all eight boards have matching 46.143 desktop/application evidence; A1200 IDE/A3000 SCSI Shell/Clock/ShowConfig and independently verified file persistence/reset/restore repeated on the current core (2026-10-12) | Broader applications and complete controller coverage; A1200 native 3.1 comparison remains unresolved; optional hardware remains gated |
| 5. ECS/AGA modes | Extended AGA lores/hires scrolling and fractional compositor phases implemented and independently tested; LOF/VPOSW field selection and 312/313-line PAL field timing/presentation corrected with failing-before fixtures (2026-10-12) | Native superhires, full interlace weaving, programmable timing, scan doubling, additional references |
| 6. 020/030 correctness | Integer/MMU and repair fixtures; locked-read protection and PTEST B/I fixes; actual 020/030 instruction caches and separate 030 data cache implemented and boot/application tested; overlapping TT windows combine cache inhibit correctly; fixed-slot ATC history replacement and legacy restore checked; bounded 030 MOVE.B/W postincrement retry, split-long source read, prechecked register-source split-long write, MOVEM read/write transfer probes including odd byte/word page crossings, and MOVEP word/long byte-transfer probes after MMU faults (2026-10-13) | Complete timing and full restartable format A/B faults, including real partial-transfer continuation, device reads after successful translation, indexed-EA side effects and multi-transfer writes |
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

1. Complete restartable 68030 format A/B bus faults and cache/timing semantics
   using independent hardware fixtures; retain the verified ATC history replacement.
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

### Pinball standard-ADF score writer — 2026-10-11

The preceding approved changes were committed and pushed as **19c95ce** on
`origin/main`. This subsequent continuation is local work. Fresh evidence:
`%APPDATA%/AmigaEmulator/diagnostics/PinballScore-20261011-081849-963ca87a/`.

**The physical head was correct; the disk set's writer encoded the wrong track.**
A private-copy CIAB/DSKLEN trace shows requested track 3, the loader's cache
advancing from 0 to 3, and physical cylinder 1/side 1 at DMA arming. `$2F8A`
clears scratch D2 during write-protection calibration; `$2D92` nevertheless
uses it for every sector header. The original reader ignores the track byte,
but standard ADF cannot preserve a raw header for track 0 on physical track 3.
The canonical decoder's physical-track check stays in place. See
[ADFlib's format description](https://adflib.github.io/FAQ/adf_info.html) and
[WinUAE's reference disk handling](https://raw.githubusercontent.com/tonioni/WinUAE/master/disk.cpp).
Evidence: `trace-current-menu.log`, `trace-current-menu/dma-2.bin`; no forced
guest registers, cached head or RAM flags were used for acceptance.

`Tools/prepare_pinball_32bit_disk.py --repair-scores` now creates an explicit
second compatibility variant. Besides the existing 38-byte block-setup window,
it changes six bytes at disk offset `$5C4C` / guest `$2D92`: the encoder takes
the requested track from the IO descriptor instead of scratch D2. Header/data
checksums are generated by the guest as before. Source hash/signatures and the
entire resulting image are checked; existing output and original media are
preserved. Default invocation still produces the preceding loader-only variant.
The score-repaired output SHA-256 is
`74307adc5f44748e078ab7782628de0482c1e45dda1781e5a7327a53779e23b2`.
The matrix accepts both explicitly selected variants, without replacing original
cases or advertising general raw-track support. No game/ROM/OS media is included
in Git or distributions.

An independent CPU fixture executes both original and replacement header
assembly on 68000, EC020, 020, EC030 and 030, covering all 160 tracks, eleven
sectors and stale/matching scratch values. It reproduces the original wrong
header and checks the repaired format, caller registers, descriptor and unchanged
instruction-window size. The complete **332-test suite passes, zero failures or
skips**, as do ten runner guards and exact-output/preservation tool checks.
Evidence: `header-test.log`, `full-suite.log`, `runner-guards.log`, `tool-guards.json`.

**Eight real native write continuations pass.** The new loader is cold-loaded
from repaired media on A3000 and A4000/030; its six bytes are checked when each
clean external media set is cloned. Both course disks/all four tables reach
actual live mode 1, separate flippers, frozen GAME PAUSED, exact cold-reset
restore and Esc/Y. Normal tab changes then produce eleven accepted sectors.
An independent decoder verifies the DMA-start track-3 headers and both checksums,
and compares every payload byte with the flushed private ADF. All eight have
zero new non-ROM faults; this is actual file persistence, beyond a DMA-completion
or changed-file-hash observation. Evidence: `verified-all-eight-exits/`,
`eight-native-writes/`, `eight-native-writes-reviewed.json`.

**A further timing correction:** three million clocks after F1 can still be in
the player's start animation. Six initial captures remained in chute mode 5
after the first spring pull; they are rejected as live-ball passes. A subsequent
normal pull from those ready checkpoints launches correctly; Stones 'n' Bones
was already live. The source now waits 30 million clocks after selecting one
player. Fresh selection continuations on both desktops directly reach mode 1,
frozen pause/restore and the write requester with that wait. Evidence:
`initial-live-modes.json`, `spring-timing-live-control.log`,
`settled-first-launch/`; an incorrect external Disk-3 continuation slice was
stopped, retained and excluded before the reviewed eight-case rerun.

A3000 Partyland persists its current score table, replaces the requested main
and course disks, returns to the real selection menu at frame 21766, and passes
exact cold restore. A completely fresh native cold boot from the persisted
private course disk reaches the menu and reloads Partyland at frame 16104;
the 96-byte score/name record sequence agrees with the actual written payload
at guest `$200`. Both desktops also return to the actual Disk-3 selection menu
after Stones 'n' Bones writes (frame 23898); A4000 Partyland returns at 21766.
All four menu continuations pass exact cold restore and have zero new faults.
This establishes round-trip preservation of existing records,
not a newly earned named highscore. Evidence: `persisted-exit-menu/`,
`other-persisted-menus/`, `readback-cold/`, `readback-table/`,
`readback-table-ram/`, `final-pinball-review.json`.

The separate three-disk user set is prepared under
`%USERPROFILE%/Documents/AmigaEmulator/Kompatibel/Pinball Fantasies (32bit und Scores)/`;
its course disks start as untouched copies. The preceding loader-only set is
also unchanged. All **50 original ADFs and the installed OS HDF** retain their
preserved hashes (`originals-final-verified.json`). Select this new Disk 1 and
its corresponding course copies explicitly; original cases stay separate.

The final full **262-case manifest validates**, rather than establishing full
execution/visual acceptance. A matrix inventory defect is also corrected:
selected compatibility media inside MediaDirectory now replaces its unclassified
row, avoiding a duplicate uncovered warning. Old/new Pinball and a private
Moonstone copy exercise this; unsupported hashes still reject before any output
is created. Evidence: `all-262-score-variant-final/`, `matrix-variant-guards.json`.
Final Release solution build has zero warnings/errors and `git diff --check` passes.

Next: finish broader menu/score reload and naturally earned entry coverage,
then trace Mega lo Mania's post-intro copy/privilege failure on native A500/A1200
with read-only data drives, and Cannon Fodder's remaining mission/map inputs. Native graphics,
timing/MMU, FPU/040 and Zorro III gates remain open. The roadmap is not complete;
no performance gain is claimed for a media repair.

### Mega lo Mania and Cannon Fodder memory/loader review — 2026-10-11

The preceding Pinball score-writer work is committed and pushed as `b4c0d2b`.
These changes were subsequently committed/pushed as `58faa31`. Evidence is in
`%APPDATA%/AmigaEmulator/diagnostics/MegaCannon-20261011-085650-892a6697/`.
Original games, ROMs and the installed OS disk are preserved.

**A real Chip-RAM decoding error affected both games on low-memory OCS.** The
CPU previously treated addresses beyond the fitted RAM as unmapped. The OCS
CPU Chip-RAM aperture extends to `$1FFFFF`; unconnected address lines mirror
the fitted cells. The CPU-region and optimized peek paths now agree, while
physical RAM size, storage DMA bounds, Slow RAM and Zorro II remain separate.
The 512 KiB ECS development-board case retains its unpopulated-bank behavior.
This is consistent with the overlay/Chip-RAM mapping in
[WinUAE's memory implementation](https://raw.githubusercontent.com/tonioni/WinUAE/master/memory.cpp).
The new independent fixture fails before the change and checks reads, writes,
aliases, bus contention, observer addresses and both physical/aperture boundaries.

Mega's probe had falsely selected `$90000` for its file cache on a 512 KiB
Chip/512 KiB Slow A500. A missing `mod.music` lookup subsequently left a zero
length/pointer; its byte-copy loop at `$7870` wrapped the count and never ended.
With correct aliases the cache starts in fitted Slow RAM at `$C10000`.
Cannon's low-Chip loader also stops treating the mirrored banks as extra RAM.
Evidence: `mega-baseline/`, `mega-copy-entry/`, `mega-menu-ram/`,
`cannon-baseline/`, `mirrors-test-before.log`, `mirrors-test.log`, `mirrors-cold/`.

**Mega's native-ROM configurations now reach a real first island.** A500 and
A2000 OCS use original media, Kickstart 1.3 and 512 KiB Chip + 512 KiB Slow.
A500+ (2.04), A600 and A2000 ECS (3.1) additionally need 2 MiB Fast RAM: the
game's fixed program load at `$4000` otherwise overlaps live Exec structures.
Bare-Chip native cases are retained as failures, not silently changed to an
older ROM. All three Fast configurations were rerun cold on the final core;
seven allocated men, a placed tower, live year/population display, P and exact
cold restore are reviewed at frame 14151. Evidence: `mega-native/`,
`mega-fast2-stable/`, `mega-fast2-stable-review.json`.

**The reviewed Mega disk needs a separate instruction repair on 020/030.**
Its user-mode `MOVE SR,$5E2C` at `$5E84` correctly faults on these CPUs.
`Tools/prepare_megalomania_020_disk.py` changes one unique IMP packed literal to
`MOVE CCR` and recalculates the packed checksum. Independently decoding both
payloads confirms that only the opcode byte at `$5E84` changes. The tested
user status has no upper bits, so the saved CCR preserves its needed value.
The CPU still rejects user SR reads; the regression fixture also proves that
CCR reads remain illegal on a 68000. Use this copy only on 020/030.

The exact supported source SHA-256 is
`96b851ffcd62710d53ccb356407f813dbe0458dd58a9f57e19533c39b67ceaa5`;
the new result is
`c296c827b34a8861489ec096b424956bfa2be3fae67eb94e0bc8a0b3ccca6814`.
Six media-tool guards prove exact output, original/existing-output preservation
and rejection of unsupported, truncated and already repaired disks. The new
user set is in `Documents/AmigaEmulator/Kompatibel/Mega lo Mania (020 und 030)/`.
Fresh native A1200 and A3000 cold flows reach the island. A4000/030's initial
script selected water; selecting a valid cell 44 mouse units lower reaches
its live island, with continued play after exact cold restore. The source
variant records this selection. Evidence: `mega-020-cold/`, `mega-030-cold/`,
`mega-040-icon/`, `mega-tool-guards.json`, `user-status-test.log`.

Mega pause has additional guest-state evidence on A500, A600/Fast and
A4000/030: mode `$17ED0=4`, pause `$17EE0=1`, and game tick `$4676` unchanged
across two twenty-million-clock pause intervals. Unpausing clears the flag and
advances ticks, including after exact cold restore. Evidence:
`mega-pause-probe/`, `mega-pause-ram/`, `mega-pause-registers.json`.
The same three continuations pass on the final accepted core with tick sequences
216/216/274/500, 216/216/275/501 and 218/218/276/502 respectively:
`mega-pause-stable/`, `mega-pause-stable-registers.json`.

**Cannon's original media reaches Mission 1 on all eight native boards.**
Fresh cold runs use native ROMs, 512 KiB Chip + 512 KiB Slow on OCS, native
2 MiB Chip profiles on ECS, and native 2 MiB Chip + 4 MiB Fast on 020/030.
Disk 1/2/3 are in DF0/DF1/DF2. Actual Jools/Jops jungle scenes at frame 19469
and subsequent input are reviewed, not merely an intro or a successful script.
The loader intentionally uses CPU-detection and trace exceptions; these are
counted separately from new faults in live continuations.
The old long fire/advance suffix killed the squad on seven boards. Its frozen
retry screens are explicitly rejected as pause evidence. The source now pauses
while alive and uses shorter movement/fire orders. A final-core live continuation
confirms both soldiers moving, fire, frozen pause pixels, exact cold reset/
restore, and a different movement order afterwards, with zero new faults.
Evidence: `cannon-native/`, `cannon-native-controls-review.json`,
`cannon-safe-continue/`. Longer campaigns and later disks remain unaccepted.

The shortened source flow subsequently passes **eight fresh native cold runs**
through the final movement order at frame 19852: all squads remain alive,
movement before/after exact cold restore is visible, and all eight pause hashes
match. Only reviewed loader vectors 4/5/9 occur, with no new live-suffix faults.
An independent A500 pause probe verifies `$C01DEE=$FFFF`, game tick `$C0061A`
561/561 while held, then flag zero and tick 588 after unpausing. Evidence:
`cannon-safe-cold/A500-Cannon-native-safe-controls/`, `cannon-safe-parallel/`,
`cannon-eight-safe-review.json`, `cannon-pause-guest/`, `cannon-pause-registers.json`.

**68000 synchronous exceptions now preserve IPL.** An independently failing-before
CPU test confirms that they previously cleared the interrupt priority mask;
[NXP's MC68000 manual](https://www.nxp.com/docs/en/reference-manual/MC68000UM.pdf),
section 6.2.5, specifies updating it only for reset/accepted interrupts.
The fixture covers all five modeled CPU variants, masks 0..7, real synchronous
opcodes through direct/arbitrated execution, pending masked IRQs, RTE and later
accepted interrupts. Only accepted interrupts replace IPL in the 68000 path;
the separate 020/030 path is unchanged.

The initial black-frame rejection of this candidate was incorrect: its earlier
capture already showed Mission 1. The old unsafe suffix killed the squad, then
clicked the next briefing and stopped while the next mission was still loading.
Five fresh native 68000 cold runs with the revised controls pass live movement,
fire, pause and continued orders after exact cold restore. A further 280-million-
clock continuation of the disputed black checkpoint reaches a new live squad
(Stoo/RJ), with zero new faults and exact cold restore. The change and independent
fixture are therefore retained. The DF0-only address error occurs at the same
frame/PC with both cores and remains separate. Evidence: `ipl-rechecked-before.log`,
`ipl-recheck/`, `ipl-recheck-two-more/`, `ipl-five-native-review.json`,
`cannon-ipl-after-retry/`. A500 Mega's complete original-media cold flow also
passes on this CPU core through frame 14151. The three Siedler reference flows
(Kickstart 1.3 Fast/Compatible and 2.04 Fast) pass their actual disk changes,
gameplay/construction image hashes and exact restores at frame 27507:
`ipl-siedler-rom-goldens/`. All fifty root ADFs and the installed OS HDF still
match their preserved hashes (`final-preservation/`).

**Single-drive Cannon loading remains a separate gate.** The final-core
DF0-only replay gets the correct Disk 2 requester, then keeps loading behind
a black palette. Its guest/Copper ticks still advance; this is not itself a
CPU halt. Making Disk 1 available again resumes the army menu. A later
DF0-only mission attempt raises an address error at `$C24496` followed by
line-A recovery into ROM and is rejected. Use the reviewed data-drive setup
until the exact request/file sequence and late failure are resolved.
Evidence: `cannon-single-final/`, `cannon-single-stable-ram-v2/`,
`cannon-single-next/`, `cannon-single-add/`, `cannon-single-cycle/`,
`cannon-single-mission/`. Black frames or normal game-over screens are not passes.

The final accepted core passes **335/335** tests and the solution build has zero
warnings/errors. Twelve runner preflight guards and three new matrix rejection
guards pass. `-MegaLoManiaCompatibleBoot` adds explicit native 020/030 cases,
requires compatibility profiles and the reviewed result hash, and preserves
original-media cases. With the three Fast profiles and all previously reviewed
Moonstone/Pinball variants, the full **268-case** manifest validates; this is
preflight rather than complete gameplay acceptance. No ROM/game/OS data is
distributed. No speed improvement is claimed without paired measurements.

The runner also supports `allowedNonRomExceptionVectors`, validated before
creating outputs. Cannon allows only its reviewed 4/5/9 probes: the actual
DF0-only vector-3 failure now returns exit 1 with its exact PC even though its
total count remains below 600. A paired successful continuation retains
identical CPU, frame, pixels, disk state and exception observations at all 14
captures with/without this guard. Evidence: `vector-guard-before.log`,
`vector-guard-after.log`, `cannon-single-mission-guarded/`,
`vector-guard-paired-proof.json`, `all-268-guarded-final/`.
The final CPU/runner build validates the same full manifest again in
`all-268-accepted-final/`; `full-suite-ipl-accepted.log`,
`runner-accepted-guards.log` and `solution-build-ipl-accepted.log` retain the
final 335-test, twelve-guard and clean-build results.
A final-build Cannon continuation also matches all 14 previous CPU/frame/pixel/
disk captures with zero new faults (`cannon-safe-accepted/`,
`accepted-paired-control-proof.json`).

Next: isolate the DF0-only request/late address error with paired checkpoints,
then test later missions/islands and actual later-disk transitions. Retain
the existing graphics/timing/MMU/FPU/040/Zorro III gates. The
roadmap remains active.

### Execution of the seven requested points — 2026-10-12

This execution follows the seven-point chat list: game defects, broader gameplay,
graphics, CPU/MMU, performance, OS/additional hardware, then release/relay.
The overall scope remains active. Evidence is in
`%APPDATA%/AmigaEmulator/diagnostics/RoadmapSeven-20261012-4fcc1fe9/`.
The baseline hashes cover 63 existing user files: fifty root ADFs, both permanent
HDFs and eleven ROMs. New diagnostics and OS writes use external test outputs
and fresh HDF copies. None of this media is distributed.

**Cannon DF0-only timing:** paired replay of the same army checkpoint reproduces
the previous address error at frame 45280, `$C24496` (`CLR.W (A4)`). The player
reads sample base `$64C4` plus offset `$149`, producing odd address `$660D`.
Adding data drives reaches the actual mission. Waiting four million clocks
before confirming the same briefing also reaches Mission 1 with only DF0;
twenty/160-million-clock controls and exact cold restores pass. Changing the
inserted disk before confirmation is not necessary for these continuations.
The PAL field correction below now removes this exact immediate-click failure.
An isolated negative control changing only the field length back to constant
312 reproduces vector 3 at the original frame/PC; the corrected core reaches
the actual jungle mission with Jools/Jops. A fresh cold DF0 attempt still ends
at black output after the first Disk-2 handoff; it is explicitly rejected despite
runner exit zero. Full cold single-drive and later disk transitions remain open.
Evidence: `df0-baseline/`, `data-control/`, `df0-fields-integrated/`,
`df0-fields-negative-control/`, `cannon-df0-cold-integrated/`,
`cannon-cold-black-registers.log`, `cannon-fields-live-registers.log`,
`df0-fault-registers.log`, `mission-waits/`, `mission-disk1/`, `mission-disk3/`.
The cold black checkpoint has active DMA `$03FF`, four planes (`BPLCON0=$4000`)
and a running Copper at `$003254`, but all 32 stored COLORxx values are zero.
That differs from the working mission's loaded palette; do not mask this guest
transition with a host renderer/palette workaround.

**68030 ATC:** the FIFO implementation fails both new independent fixtures.
Fixed slots/history now pass the eight MMU tests, including malformed histories,
out-of-range/duplicate slots, truncated payloads and unchanged live state after
rejection. Historical instruction/data-cache fixtures retain real version-1
MMU payloads. The complete intermediate suite passes **337/337**;
`atc-before.log`, `atc-guards.log`, `cache-legacy-after.log`,
`full-atc-final.log` retain the evidence. The nested format change is tagged as
core version 17. The final integrated suite passes **339/339**, including the
additional PAL field and normal/profiled presentation fixtures (`full-final.log`).
Behavior is derived independently from MC68030UM 9.4 and cross-checked with
[WinUAE's ATC behavior](https://raw.githubusercontent.com/tonioni/WinUAE/master/cpummu30.cpp).

**OS checks:** a fresh copy of the previously installed A3000 SCSI image passes
matching 46.143 ROM cold/warm/cold boot and exact restore, including the real
Execute Command dialog (`a3000-atc-os/`). A separate A1200 copy with native
Kickstart 3.1 reaches an empty Workbench screen; script exit zero does not
establish desktop/application acceptance. The matching A1200 3.1.4 comparison
passes with the matching native 46.143 image obtained from the user's archive;
the initial timeout was followed by a successful size/checksum-verified download
into the external diagnostics directory. SHA-1 is
`6355A9ED5DC840422F9B73308A91BE0D0BB506BD`. The reusable purchased-OS script now
validates all eight boards' individual ROM families before creating outputs.
Current-core A1200 IDE and A3000 SCSI runs show real Shell, Version, ShowConfig
and Clock, exact restore, warm/cold boot and the content on the first Type
command after each reboot. Independent read-only amitools extraction confirms
`legacyostest\n` from both private HDFs (`os-independent-readback.json`).
Evidence: `A1200-applications-final/`, `A3000-applications-final/`.
No source OS disk or existing installation was altered.

**Performance:** a duplicate bus-classification candidate retained identical
complete CPU and framebuffer hashes at all thirty endpoints, but the paired
medians were -1.46% on A4000/Mega and -1.91% on A1200/Moonstone. The 68000
control had excessive variance (-9.11..+54.00%). The candidate was removed;
no speed improvement is claimed. Evidence: `bus-benchmark-pairs/`,
`bench-control/`, `bench-candidate/`. A sampled .NET stack profile of real
A4000/Mega highlights the chip-cycle loop, AGA composition and repeated floppy
selection/index queries (`profile-mega.nettrace`, `profile-mega-top.txt`).
Direct live DF0-DF3 selection/index checks and ticks remove the per-call array
construction/iteration while retaining bus priority and legacy drive repair.
The first five alternating pairs per scene give +11.04% Mega map, +8.56%
Moonstone battle and +15.02% Siedler construction medians, with all thirty
complete CPU/frame endpoints identical (`floppy-benchmark-pairs/`). Those
measurements isolate the floppy change before the PAL timing correction;
a second pair of builds retains correct field timing on both sides. Its five
alternating pairs per scene retain all thirty CPU/frame hashes and allocation
counts, with medians +7.15% Mega map (-12.93..+14.12%), +7.79% Moonstone battle
(+1.18..+21.92%) and +4.63% Siedler construction (+0.60..+16.52%). These are
local measurements of the named scenes, not universal FPS promises.
Evidence: `bench-field-floppy-control/`, `bench-field-floppy-candidate/`,
`field-floppy-benchmark-pairs/` and `field-floppy-benchmark.log`.

**Graphics/game controls:** PAL alternates 312/313 lines and respects forced
LOF without LACE; the extra scanline survives exact restore. The compositor
waits until that line completes in both ordinary and profiled execution.
Both Siedler cold flows retain complete wood/menu, pointer interaction and
actual castle construction with zero guest faults. Their four time-dependent
goldens are re-reviewed after the corrected timing, preserving old/new hashes
in `siedler-field-reference-review.json`; initial old-reference failures remain
in the evidence. Eight Cannon mission continuations pass exact pause/restore
and the fault guard. Combat causes normal squad deaths on several profiles;
the changed-pixel test is not alone evidence of living post-restore movement.
Keep the later-gameplay gate, and record these observations without treating
game-over as complete compatibility. Evidence: `cannon-eight-current-controls/`,
`cannon-eight-retreat/`, `cannon-eight-*-review.png`,
`siedler-native13-timing-review/`, `siedler-native20-timing-review/`.

**Release preparation:** the first actual GitHub Actions installer workflow
passes all checks on committed `871b76f`: 335 source tests, five relay tests,
distribution audit and real isolated install/update/uninstall with retained
synthetic ROM/HDF data. The workflow was manually dispatched, so it did not
publish a release. Evidence: `github-installer-baseline.log` and
[workflow 38203773101](https://github.com/n1k0m0/AmigaEmulator/actions/runs/38203773101).
The first Windows release is now published as
[v0.1.1](https://github.com/n1k0m0/AmigaEmulator/releases/tag/v0.1.1), built from
`ad1233ad3f375ba437bac9a5dad37724637c4e2b` by
[workflow 38210764572](https://github.com/n1k0m0/AmigaEmulator/actions/runs/38210764572).
That tag run passes 339/339 source tests with zero skips, five relay tests,
the source-header/distribution audits and actual isolated installation,
update and uninstall, retaining synthetic user ROM/HDF files. The installer
download is independently SHA-256 verified:
`65d727cf61370c9e361ef6c07ef7a0a1640a2509a883f22a4e94503f5ed0ab02`
(36,887,478 bytes). Evidence: `github-release-0.1.1.log` and
`released-0.1.1/checksum-proof.json`. All 63 original user ADF/HDF/ROM files
remain byte-identical (`user-media-final-proof.json`). The twelve runner
preflight tests pass, and the solution build has zero warnings/errors.
Relay deployment still awaits the user's SSH target and must preserve the
existing certificate/key/port; no existing public proxy has been changed.

The cold Cannon black checkpoint also remains black after independently
remounting Disk 1, 2 or 3 and confirming; all three retain hash
`C9926CBFA109FB0E876776ECF213DE72260B7EA4E7EB1AA4B0C90BFE164E2345`
with no new CPU faults (`cannon-cold-black-recovery/`). These are failed recovery
controls. Trace the initial palette/fade loading before the first Disk-2
handoff instead of treating repeated disk insertion as progress.

Next: trace the cold Cannon palette/fade handoff and complete a visually verified
DF0 boot/mission sequence, then continue
full graphics/fault restart/FPU/040/Zorro III and the broad gameplay
acceptance work. No remaining hardware gate is removed by these results.

**Local tooling follow-up after the release:** GameRunner can now reject black
output at selected gameplay checkpoints with `minimumNonBlackPixels` and logs
the observed count. Thirteen preflight guards pass, including invalid limits
rejected before outputs/HDF creation. The actual cold Cannon black save fails
with exit 1/status `failed`/zero visible pixels; the identical no-guard control
still returns `executed-unreviewed`, not compatibility acceptance. Reviewed
Cannon, Siedler and Moonstone gameplay checkpoints satisfy the new guard.
Evidence: `runner-visible-guard-tests.log`, `visible-scene-guard-proof.json`,
`visible-scene-guards/`. This follow-up is local and uncommitted; it does not
change the already published v0.1.1 installer. Visible menu/error/game-over
pixels still require semantic review.

### Cold DF0 Cannon and Blitter modulo follow-up — 2026-10-12

Evidence root: `%APPDATA%/AmigaEmulator/diagnostics/CannonCold-20261012-085342-07568c8a/`.
This follow-up remains local; the published v0.1.1 installer is unchanged.

**Cold Cannon:** the Disk-2-only handoff and original longer input sequence
both remain black. A fixed-312-line control is also black; this is separate
from the fixed PAL-dependent address exception. Bus/Copper and software-TRAP
traces show an intentional fade followed by repeated lookup of `pstuff.lbm`.
The asset is present in the reviewed Disk 1 and absent from Disks 2/3.
Reintroducing Disk 1 advances that lookup; the following assets need Disk 2
again. No host palette substitution or guest patch is justified. Keeping
boot/data disks available in separate drives avoids this missing-media path.
The visibility of further single-drive requests remains a gate.

`Tools/cannon-df0-cold-gameplay-scenario.json` executes an uninterrupted,
read-only native A500/1.3 cold boot with 512 KiB Chip + 512 KiB Slow and only
DF0, using Disk 1 -> 2 -> 1 -> 2 -> 1 -> 2 round trips. It reaches recruitment
and real Mission 1 with both Jools and Jops, uses P, holds 70 further fields,
power-cycles/restores exactly, resumes and orders movement in the playfield.
The final independent cold run (`full-cold-world-final/`) has 146,203 nonblack
pixels at Mission 1. Mission/pause/held/restore hashes are identical:
`67924E6A2AC167743764DCE214E70E00379EB5C863B7795B933EDF07DE686F39`.
Final live hash is
`85D56A927D9A2708175CCFA5F41B8B47C7EAF8A96F176943D6CD9D4CA5B7417E`.
The reviewed references are now runner assertions. All 366 observed non-ROM
exceptions are the reviewed loader's vectors 5/4/9; there is no bus/address
exception or new fault in the live control suffix.

A continuation replaces only the click interval with an identically timed
no-click wait (`order-negative-control/`). Cursor/input position and living
squad names remain the same; sidebar pixels are identical. 388 pixels differ
in the lower playfield, with squad movement visibly distinct from the
no-order control. This corrects the earlier test's left-menu target.
This accepts bounded native A500 initial gameplay. Later missions, visible
single-drive requesters and other boards' full cold DF0 routes remain open.
Intermediate probe saves inherited mounted-media metadata; acceptance relies
on fresh GameRunner cold runs that track each actual disk, rather than treating
those diagnostic saves as cold gameplay proof.

**Blitter:** BLTxMOD bit zero must be ignored before signed descending strides
and line-slope arithmetic. Masking just the resulting pointer rounded an odd
descending offset down by another word; odd BLTAMOD/BLTBMOD bits also entered
the line-error accumulator. All four decoded modulo latches are now even.
Old in-flight states normalize subsequent modulo steps while retaining their
pipeline and addresses; this does not undo prior raster damage. No state
layout change is needed. Three failing-before fixtures cover all four DMA
channels, positive/negative/zero strides in both directions on OCS/ECS/AGA,
byte programming, actual writes/final pointers, legacy partial source fetches
and upward line slopes with mid-pixel restore. Twelve Blitter-related tests
pass; the full suite passes **342/342**, zero failures/skips
(`modulo-before.log`, `modulo-after.log`, `full-tests.log`). The register
behavior is cross-checked against the hardware
[BLTxMOD description](https://www.devili.iki.fi/mirrors/haynie/chips/aa/regbits.pdf)
and the mask in the reference
[custom-chip implementation](https://github.com/tonioni/WinUAE/blob/master/custom.cpp);
no implementation code is imported.

Paired before/after cold Cannon runs retain all 46 capture hashes, CPU debug
states and fault observations. Siedler/A500+ construction, Moonstone/A1200
battle and Mega/A4000Ec030 map continuations retain twelve more capture hashes,
exact cold restore and zero new faults. Complete serialized CPU, bus and Agnus
plus framebuffer signatures match at all five paired checkpoints
(`modulo-game-controls-proof.json`). These are regression controls, not new
FPS measurements or later-campaign acceptance. The world-target correction
is separately executed and reviewed above. Thirteen runner preflight guards
pass, source headers all have Apache-2.0, and all 63 original ADF/HDF/ROM files
are byte-identical (`runner-tests.log`, `header-audit.log`, `user-media-proof.json`).

The existing hourly continuation is active again at the user's request.
Next: later Cannon mission/data changes and visible requesters using the
established cold DF0 prefix; continue Mega's later islands and hardware/OS/matrix
gates. SHRES, weaving/programmed timing, restartable MMU faults, optional FPU,
complete 040 and Zorro III remain incomplete. Relay deployment still needs
the user's SSH target. No new commit/push/release was performed.

### Cannon Mission 1 retry and long DF0 continuation — 2026-10-12

Fresh evidence: `%APPDATA%/AmigaEmulator/diagnostics/CannonNext-20261012-100514/`.
The source `Tools/cannon-df0-cold-gameplay-scenario.json` now continues the
previously accepted native A500/1.3 DF0-only cold path through a longer Mission 1
session. Its 56 captures include a later live jungle (145,984 nonblack pixels),
a move order to the right, a short 31-pixel fade, the *Mission 1 / Phase 1 of 1*
briefing with 11 recruits remaining, and a click that reloads the playable
jungle (145,928 nonblack pixels). The exact reviewed hashes are asserted for
the live, fade, briefing and retry scenes. A fresh uninterrupted run of the
source manifest completed (`source-retry-acceptance/`); all 366 observed
non-ROM exceptions remain the existing loader's vectors 4/5/9. There are no
new guest faults after the first mission starts. The black fade is explicitly
recorded as a loss transition, not accepted as gameplay or a graphics pass.

Paired continuations from the same saved live mission replace a short right
click with an equally long no-button wait. Both have identical complete CPU
debug text and frame hashes before and after the input, through the fade and
back to the same briefing (`pair-run/`). A separate cold-suffix continuation
matches all ten corresponding frames and CPU texts in the uninterrupted run
(`pair-retry-run/`, `followup-proof.json`). Briefing confirmation reloads the
jungle with Disk 2 still in DF0; repeating from the same briefing with Disk 1
in DF0 produces the same retry frames (`retry-run/`). This bounded retry needs
no new disk swap. A held right-button control and equal-time no-button control
show different frame output at equal frame counts after the retry, with no new
guest faults (`fire-pair-run/`); this establishes an input response, not an
enemy kill or objective completion. The mouse mapping follows the
[Cannon Fodder controls](https://manuals.plus/m/b85cfd177f1b3ec673f29689f3852d5869169d6fa95e8ffbf0e6d99326171d1c).
An additional pair starts from the original live Mission 1 checkpoint and aims
at the visible enemy to the right. Holding the right mouse button for five
million chip cycles produces the game's red crosshair; the equal-time
no-button control retains the arrow cursor (`first-enemy-run/`). The pixel
difference is primarily in the pointer region, with 53 other changed pixels
in the playfield; this does not independently establish a shot or a hit.
Both continue with living Jools/Jops and zero new guest faults. Confirmed
gunfire and an enemy kill remain to be demonstrated in a longer held-input
sequence. A 20-million-cycle held-right comparison (`long-fire-run/`) also
keeps the squad alive but does not prove a kill; this fixed cursor target is
insufficient for Mission 1 completion.
All 63 original ADF/HDF/ROM files still match their earlier SHA-256 inventory
(`media-proof.json`). Runner preflight accepts the source scenario and
`git diff --check` is clean. These changes remain local and uncommitted.

Next concrete Cannon step: use the live retry checkpoint to earn the Mission 1
kill-all-enemy objective and verify the completed-mission score, transition,
next visible disk request and Mission 2 live play. If DF0-only loading stalls,
trace the next requested file against the correct source disk before changing
emulator code. The other six roadmap gates remain open as stated above.

### Cannon west combat and explicit loss-screen rejection — 2026-10-12

Fresh evidence: `%APPDATA%/AmigaEmulator/diagnostics/CannonMission-20261012-105548/`.
This run begins from the correctly mounted, reviewed native A500 Mission 1
checkpoint. A westward left-click moves Jools/Jops across the jungle for 20
million clocks (`run/`). At the nearby enemy, two continuations have the same
mouse position, chip-cycle duration and final frame (23671): one holds the
right mouse button for ten million clocks, the other waits. In the held-button
capture Jops remains listed and active. The no-button capture visibly says
*TRY AGAIN*, despite containing 145,303 nonblack pixels (`engage-run/`).
Both have zero new non-ROM guest faults. This is a bounded, paired real-input
survival difference, not a mission objective or enemy-kill pass. Continuing the
survivor westward for another 30 million clocks leaves Jops active but does not
complete Mission 1 (`survivor-west-run/`). A later post-retry west/engagement
pair also reaches live jungle but neither branch completes the objective
(`retry-west-run/`, `retry-engage-run/`).

The visible *TRY AGAIN* frame exposed a runner guard gap: its many pixels pass
`minimumNonBlackPixels`. `GameRunner` now supports optional action-level
`forbiddenFrameHashes` with complete SHA-256 values. Preflight rejects invalid
hashes before output/HDF creation. At the same frame 23671, the new guard
accepts the living-soldier branch and rejects the reviewed game-over branch
after retaining its diagnostic capture (`guarded-run/`, `guard-proof.json`).
The guard is exact-scene evidence only; it does not classify every possible
failure screen. Fourteen runner preflight tests pass; the rebuilt runner has
zero warnings/errors. All 63 original ROM/ADF/HDF files remain byte-identical
(`media-proof.json`). Source edits are local and uncommitted.

Next concrete step: use Jops's surviving west position to locate and eliminate
the remaining enemies, then inspect an actual completed Mission 1 score and
Mission 2 disk request. Continue independent graphics, CPU/MMU, performance,
OS/application, FPU/040/Zorro III and relay gates; none is closed here.

### Cannon left-edge continuation and runner capture reuse — 2026-10-12

Fresh evidence: `%APPDATA%/AmigaEmulator/diagnostics/RoadmapFollow-20261012-115447/`.
The surviving native A500 Jops checkpoint from the preceding controlled
firefight accepts another west/northward movement order and remains in the
live jungle for 20 million clocks with no new CPU fault (`cannon-run/`). The
pointer reaches the left edge, but the captured scene does not show a mission
objective, score, disk request or Mission 2; none is accepted on this evidence.

The new GameRunner exact-loss guard originally recomputed the full framebuffer
SHA-256 after writing each capture, and the visibility guard counted pixels
again. `Capture` now returns the hash and nonblack count that it already logs;
both guards consume the same captured values. This changes no emulated machine
state or output format. The rebuilt runner has zero warnings/errors, all 14
preflight tests pass, and the paired living-Jops / *TRY AGAIN* runtime check
still accepts the former and rejects the latter with the same recorded hash
(`guarded-final/`). The source cold DF0 manifest passes preflight. All 63
original ROM/ADF/HDF files still match the baseline SHA-256 inventory
(`media-proof.json`). These are tooling results, not measured core-FPS gains.

Next: test routes and aiming that can actually eliminate the remaining Cannon
enemies, then verify score/Mission 2 and its disk transition. Continue the
other open roadmap stages in parallel over subsequent runs. No commit, push
or new release was made.

### AGA hotspot controls and bounded Cannon aiming — 2026-10-12

Fresh evidence: `%APPDATA%/AmigaEmulator/diagnostics/VideoPerf-20261012-125531/`.
Two plausible AGA-renderer speed changes were measured and rejected. A direct
non-HAM palette return retained identical complete CPU and framebuffer hashes
at six paired A1200/Moonstone endpoints, but its median FPS change was -4.68%
(-7.84..+0.78%). Computing the DIW bounds and border color once per chip-tick
span retained identical states in ten A1200/Moonstone and two A4000/Mega
endpoints. Its combined median was -2.61%, with high variance and no stable
benefit; the first Mega pair was -5.08% and -4.48%. Both candidates were
removed; no core-FPS gain is claimed. Alternating baselines/candidates,
individual timings and exact-state comparisons are in `proof.json`,
`window-proof.json` and the per-run JSONL files. The benchmarks use the same
saved scenes, 500 warmup frames and two 300-frame samples per pair. A500+
Siedler was not reached before stopping the unpromising second candidate.

The native A500/1.3 single-drive Cannon survivor checkpoint was continued
with two additional right-button aim points. Moderate movement (120/30 and
220/50 mouse-counter units) leaves Jops visibly alive after twelve million
held-fire and five million subsequent chip clocks, with no new non-ROM
exception (`cannon-center-run/`, `cannon-right-run/`). Neither reviewed image
shows a kill, score or Mission 2. A larger 430/80 movement scrolls the game
view toward a mostly black map edge even before firing (`cannon-east-run/`);
this is not accepted as gameplay or classified as an emulator fault without
a reference comparison. The next Cannon check should move/aim in smaller
counter steps while inspecting the enemy sprites and mission counter, then
confirm a completed Mission 1 and its next disk request.

The complete local source suite passes **342/342** (`full-tests.log`). The
GameRunner preflight initially picked up an old default Release binary; after
rebuilding that binary from the current source, all **14/14** checks pass
(`runner-preflight.log`). All 63 original ROM/ADF/HDF files remain byte-identical
(`media-proof.json`). Native SHRES, full interlace/programmed timing,
restartable MMU faults, optional FPU, complete 040, Zorro III, longer game
campaigns and relay deployment remain open. No commit, push or release was made.

### Bounded 68030 MMU MOVE retry — 2026-10-12

Fresh evidence: `%APPDATA%/AmigaEmulator/diagnostics/MmuRetry-20261012-115650/`.
An independently encoded `MOVE.W (A0)+,(A1)+` fixture exposes a guest-visible
MMU recovery error. Its source read succeeds, its destination write faults on
a protected page, but the pre-fix source register has already advanced by two
bytes. A later mapping repair and `RTE` would retry from the wrong source.
The failing-before suite reports 342 passes and this one failure (`before.log`).
For 68030 with active MMU, byte/word `MOVE.B/W` now restores the source
and destination effective-address register side effects if a translation
throws, so the existing instruction retry after `RTE` uses the original
operands. The fixture checks direct and deferred chip-clock execution, the
logical fault, no protected write, descriptor repair/ATC flush, one increment
per register and the original word written only once. This is deliberately a
bounded repair; split longwords, `MOVEM`, full format-A/B bus-cycle resumption
and architectural frame contents are still open. The
[MC68030 user manual](https://www.nxp.com/docs/en/reference-manual/MC68030UM-P2.pdf),
sections 8.2.3 and 8.4, describes recovery by completing faulted bus cycles
and the need to retain internal fault state. The implementation does not claim
to provide that full mechanism.

The current source suite passes **343/343**, no skips (`full-tests.log`), and
the GameRunner build has zero warnings/errors. Its **14/14** preflight checks
also pass (`runner-preflight.log`). On fresh continuation outputs, A4000/030
Mega lo Mania and A500+ Siedler each match four pre-fix CPU text, framebuffer,
disk-DMA and exception captures across cold restore (`game-proof.json`);
their final game images were visually inspected. An A3000 AmigaOS 3.1.4 Shell
checkpoint continues and cold-restores with four corresponding captures
identical to the pre-fix runner (`baseline-os/`, `current-os/`). Those OS
controls use separate new copies of the 256-MiB test HDF, and the source HDF
hash is unchanged (`hdf-initial-hashes.json`, `hdf-final-hashes.json`). They do
not establish that the guest OS actively enabled the MMU.

Five alternating Mega/A4000 benchmark pairs retain all ten complete CPU-state
and framebuffer hashes. The FPS median is +0.03% (-4.97..+6.00%), within
measurement variance; no speed gain is claimed (`benchmark-mega-proof.json`,
`benchmark-mega-summary.json`). All 63 original ROM/ADF/HDF files still match
their SHA-256 inventory (`media-proof.json`). Next: build independent format-A/B
data-fault fixtures for split transfers and `MOVEM`, then implement bus-cycle
continuation without repeating already retired device writes. The other
gameplay, graphics, CPU, FPU/040, Zorro III and relay gates stay open. No
commit, push or release was made.

### 68030 split-long source retry and MOVE-path timing — 2026-10-12

Fresh evidence: `%APPDATA%/AmigaEmulator/diagnostics/MmuSplitRead-20261012-125351/`.
An independent `MOVE.L (A0)+,D0` fixture reads its first word at logical
`$3FFE`, then faults on the unmapped second page at `$4000`. Before the fix,
the guest A0 is already `$4002`, so a mapping repair followed by `RTE` cannot
recover the original `$ABCD1234` longword (`before.log`). The bounded 68030
MMU MOVE retry now also restores the source address for this long-read/register
destination case. Direct and deferred execution both retain A0=`$3FFE` and
unchanged D0 at the fault, record logical `$4000` in the frame, and complete
with D0=`$ABCD1234` and A0=`$4002` after mapping repair, ATC flush and RTE.
The earlier protected word-write retry stays covered. Re-reading a RAM source
on retry is safe for this fixture; device read side effects, split writes with
an already retired first word, MOVEM, and format-A/B internal cycle resumption
remain unimplemented. The [MC68030 user manual](https://www.nxp.com/docs/en/reference-manual/MC68030UM-P2.pdf),
sections 8.2.1–8.2.3, defines the broader recovery behavior; this bounded
repair is not a full implementation of it.

The initial branch ordering in the hot MOVE path kept all ten A4000/Mega CPU
and framebuffer endpoint hashes identical, but measured a median -18.30% FPS
(-24.79..+10.10%). The EA-shape conditions now precede the MMU-state query.
A second alternating comparison with six exact-state endpoints measured +3.08%
(-3.98..+38.10%). The large spread makes a speedup or regression claim
unreliable; retain these figures and repeat under a quieter machine before
accepting a performance change (`benchmark-mega-proof.json`,
`benchmark-mega-summary.json`, `benchmark-ordered-proof.json`,
`benchmark-ordered-summary.json`). This test specifically covers an inactive
MMU gameplay path; it does not prove active-MMU performance.

The source suite passes **344/344**, zero skips both before and after reordering
the branch (`full-tests.log`, `full-tests-ordered.log`); the rebuilt
GameRunner and its **14/14** preflight guards pass. Current A4000/Mega and
A500+/Siedler gameplay checkpoints match four earlier captures each across
cold restore in CPU text, framebuffer, disk DMA and exceptions
(`game-controls/`). An A3000 AmigaOS 3.1.4 Shell checkpoint likewise matches
four prior captures on a fresh private 256-MiB HDF copy (`a3000-os/`); this is
an OS regression control, not proof that the OS enabled its MMU. The source
test HDF and private copy hashes remain unchanged (`hdf-final-hashes.json`).
All 63 original ROM/ADF/HDF files match their baseline SHA-256 hashes
(`media-proof.json`). Next: a split-long write fixture that catches duplicate
first-word/device writes and tests safe continuation, then MOVEM. Broader
gameplay, graphics, 020/030 timing, FPU/040, Zorro III and relay work remain
open. No commit, push or release was made.

### 68030 split-long register write across an MMU page — 2026-10-12

Fresh evidence: `%APPDATA%/AmigaEmulator/diagnostics/MmuSplitWrite-20261012-140152/`.
An independently encoded `MOVE.L D0,(A0)+` fixture starts at logical `$3FFE`
with the second 4-KiB page unmapped. Before the fix, the second-word fault
leaves A0 at `$4002` (`before.log`); the first physical word may also already
have been sent to a device. The current bounded path restores the destination
postincrement and checks both page translations before issuing either word
of a page-spanning long write. The test tracks physical word writes, confirms
zero before mapping repair and exactly one after ATC flush and `RTE`, with
`$CAFEBABE` at the two mapped physical words. Direct `Step` and deferred
`Tick(true)` paths pass (`after-targeted.log`). Same-page writes retain their
prior scheduling. This is a preventive atomic translation check for the
register-source MOVE case, **not** the MC68030's full format-A/B partial-cycle
restart: transfers whose first physical cycle already retired, side-effecting
source reads, MOVEM, and precise frame/internal state still need work.

The full source suite passes **345/345**, no skips (`full-tests.log`); the
GameRunner build has zero warnings/errors and **14/14** preflight checks pass
(`runner-preflight.log`). A4000/Mega and A500+/Siedler gameplay controls plus
an A3000 AmigaOS 3.1.4 Shell continuation each match their earlier four
capture labels, frames, complete CPU text, framebuffer hashes, nonblack pixel
counts, exception observations and disk-DMA state across cold restore
(`game-os-proof.json`). The OS run uses a new private 256-MiB HDF copy; its
SHA-256 and the source test HDF's SHA-256 are unchanged after the run
(`hdf-initial-hashes.json`, `hdf-final-hashes.json`). It does not prove the OS
enabled its MMU. All 63 original ROM/ADF/HDF files match their baseline hashes
(`media-proof.json`).

Five alternating A4000/Mega benchmark pairs compare the preceding build with
this change after 500 warmup frames, using two 300-frame samples per pair.
All ten complete CPU-state and framebuffer hashes match. Median FPS difference
is +1.39% (-11.01..+12.33%); this spread does not establish a speed gain
(`benchmark-mega-proof.json`, `benchmark-mega-summary.json`). Next: independently
exercise a fault *after* an actual first bus write, including device side
effects and save/restore of fault state; then multi-transfer MOVEM and full
format-A/B resumption. The remaining gameplay, graphics, timing, FPU/040,
Zorro III and relay gates stay open. No commit, push or release was made.

### Cannon bounded aiming and 68030 MOVEM postincrement retry — 2026-10-12

Fresh evidence: `%APPDATA%/AmigaEmulator/diagnostics/CannonModes-20261012-150023/`.
The native A500/1.3 single-drive Cannon Mission-1 survivor checkpoint was
continued with two further right-button aim points and 20- or 50-million-clock
holds (`cannon-mid-run/`, `cannon-east-exact-run/`). Both reviewed final images
still show live Jops and the jungle, with no new non-ROM guest exception.
Neither shows a kill, score, visible disk request or Mission 2. The east point
puts the crosshair near the visible lower-right sprite but is not accepted as
an objective pass. The previously established cold DF0 sequence remains the
accepted route to initial Mission 1; this continuation adds no later-campaign
claim. Next Cannon step: identify an enemy/weapon hit and verify an actual
score and later disk transition, using image and input comparisons.

An independently encoded `MOVEM.L (A0)+,D0-D1` fixture reads from `$3FFC`
then faults on the next unmapped 4-KiB page. Before the repair, A0 had already
advanced to `$4000` (`movem-before.log`). With active 68030 MMU, the bounded
postincrement source path now checks translations for every selected word
before reading data or changing any destination register. The test covers
direct and deferred execution: A0 and both D registers retain their original
values at the fault, no first-page data read retires before repair, and after
ATC flush and `RTE` the two longwords and final A0=`$4004` are correct. The
first physical word is read once (`movem-after-readcount.log`). This covers
this postincrement read form, not write-side MOVEM, other addressing modes,
actual partial-cycle continuation, side-effecting device reads after a first
cycle retired, or complete format-A/B state.

The full source suite passes **346/346**, zero skips (`full-tests-final.log`),
the GameRunner build has zero warnings/errors, and **14/14** preflight checks
pass (`runner-preflight.log`). A4000/Mega and A500+/Siedler gameplay states and
an A3000 AmigaOS 3.1.4 Shell state each match four earlier frame/CPU/pixel/
exception/disk-DMA captures across cold restore (`game-os-proof.json`). The OS
control uses a fresh private 256-MiB HDF copy, with identical source and copy
hashes before and after (`hdf-initial-hashes.json`, `hdf-final-hashes.json`);
it does not establish active guest MMU use. All 63 original ROM/ADF/HDF files
match their baseline SHA-256 hashes (`media-proof.json`). Five alternating
A4000/Mega benchmark pairs retain all ten complete CPU-state and framebuffer
hashes; median FPS difference is +0.82% (-6.22..+5.89%), within measurement
variance (`benchmark-mega-proof.json`, `benchmark-mega-summary.json`). No
speed gain is claimed. Next independent MMU step: test and implement a true
partial-transfer restart, including already retired writes and save/restore of
fault state. Graphics modes, broader gameplay/model matrix, timing, FPU/040,
Zorro III and relay remain open. No commit, push or release was made.

### 68030 MOVEM indirect write probe and Cannon paired fire — 2026-10-12

Fresh evidence: `%APPDATA%/AmigaEmulator/diagnostics/MovemWrite-20261012-155554/`.
An independent `MOVEM.L D0-D1,(A0)` fixture starts at `$3FFC`: the first
longword targets mapped physical `$9FFC`, and the second page at logical
`$4000` is absent. Before the fix, the first physical word had already been
written once when the later translation faulted (`before.log`), so replaying
the instruction after `RTE` would issue a second write. The bounded 68030
address-register-indirect store path now probes all selected destinations
before writing any data. Direct and deferred tests confirm zero physical
first-word writes at the fault and exactly one after descriptor repair, ATC
flush and `RTE`; both original longwords reach their distinct physical pages.
A direct-path CPU save/reset/load while in the fault handler also completes
the retry without replaying a write (`after-targeted.log`).

The initial probe exposed a second error: it marked the first page's MMU
descriptor modified even though no data write had retired
(`modified-before.log`). A separate probe now checks access without updating
descriptor Used/Modified bits or ATC history; the actual transfer still uses
the ordinary translation path. The write fixture checks the modified bit, and
the earlier MOVEM read fixture checks that its first page is not marked used
before a data read (`modified-after.log`, `mmu-after-probe.log`). These are
preventive full-instruction checks, **not** a complete MC68030 format-A/B
restart after a bus cycle already retired. Other MOVEM modes, side-effecting
devices, partial-cycle state and 020/030 timing remain open.

A fresh A500/1.3 DF0 Mission-1 survivor continuation compares a held
right-button aim near the north edge with an equally timed no-button run.
Both finish at frame 24708 with live Jops and no new non-ROM guest fault
(`cannon-north-run/`). The reviewed images differ in 896 pixels, including
384 pixels in the lower playfield outside the top pointer region. They do
not show a kill, score, visible disk request or Mission 2; the established
initial cold DF0 route is still the only accepted single-drive campaign
prefix. Next: obtain a visually confirmed enemy hit and completed mission,
then trace any actual later disk request before altering the emulator.

The final source suite passes **347/347** (`full-tests-final-probe.log`), the
GameRunner build has zero warnings/errors and **14/14** preflight guards pass
(`runner-preflight-final.log`). A4000/Mega and A500+/Siedler gameplay controls
and an A3000 AmigaOS 3.1.4 Shell control each match four prior frame, CPU,
pixel, exception and disk-DMA captures across cold restore
(`game-os-final-probe-proof.json`). The OS control uses a fresh private
256-MiB HDF; both it and the source test HDF retain their initial SHA-256
(`hdf-probe-initial-hashes.json`, `hdf-probe-final-hashes.json`). All 63
original ROM/ADF/HDF files still match the baseline (`media-proof.json`).

Five alternating A4000/Mega benchmark pairs on the final build retain ten
identical complete CPU-state and framebuffer hashes. Median FPS difference
is -2.72% (-5.51..+8.33%) against the preceding build
(`benchmark-probe-proof.json`, `benchmark-probe-summary.json`). This can be a
small regression amid run-to-run variance, so no performance improvement is
claimed; repeat it under a quieter host and profile active-MMU paths before
accepting more prechecks. Full partial-cycle continuation, the remaining
graphics/OS/gameplay matrix, FPU/040, Zorro III and relay gates stay open. No
commit, push or release was made.

### 68030 MOVEM predecrement write retry and paired performance recheck — 2026-10-12

Evidence: `%APPDATA%/AmigaEmulator/diagnostics/MovemPredec-20261012-170325/`.
The independent `MOVEM.L D0-D1,-(A0)` fixture starts at `$4004`: D1's
first destination is mapped `$4000`, while D0's later destination at
`$3FFC` initially faults. Before the fix, the test observed a physical
write to the first destination even though the instruction had not retired.
The active-030-MMU path now probes every descending destination before
writing data or changing A0. Direct and deferred execution both leave the
first page untouched and its descriptor unmodified on the fault. After
mapping the second page, flushing ATC and executing `RTE`, both longwords
arrive on their respective pages, A0 becomes `$3FFC`, and the first
physical word is written exactly once. A CPU save/reset/load in the fault
handler preserves the direct-path retry. This is a bounded prevention for
this addressing form, **not** general partial-cycle or format-A/B restart.

The final source suite passes **348/348**, and all **14/14** GameRunner
preflight guards pass. The current runner continues a real A500/1.3 Cannon
Mission-1 survivor state through a northward right-button fire order without
a new non-ROM fault. Its frame 24708, PC, all reported CPU registers and
framebuffer hash match the preceding build; the PNG SHA-256 also matches
(`cannon-recheck/`). The reviewed image shows the jungle and Jops panel,
but no kill, score, visible disk request or Mission 2. This preserves the
already proven first single-drive mission prefix, without upgrading later
campaign coverage. All **63/63** original ROM/ADF/HDF files retain their
baseline hashes (`media-proof.json`).

Three alternating A4000/Mega benchmark pairs compare the preceding frozen
build to this build at six identical complete CPU-state and framebuffer
endpoints. The median FPS delta is **+1.99%**, range **-5.94..+6.03%**
(`benchmark-probe-proof.json`, `benchmark-probe-summary.json`). Given the
spread and the earlier negative median, this is inconclusive; no sustained
performance gain is claimed. Next: trace a visually observed Cannon enemy
hit and subsequent mission/disk request; for the MMU, exercise a later
device-side partial transfer that genuinely needs cycle-level continuation,
then implement and test the corresponding saved internal state. Complete
030 timing, ECS/AGA modes, broad OS/game/controller coverage, optional FPU,
A4000/040, Zorro III and the relay deployment remain open. No commit, push
or release was made.

### Cannon late-sprite presentation and extended-EA MOVEM writes — 2026-10-12

Fresh evidence: `%APPDATA%/AmigaEmulator/diagnostics/CannonAim-20261012-180056/`.
Starting at the previously reviewed A500/1.3 DF0 Mission-1 survivor
checkpoint, four bounded pointer positions and three left-click movement
routes were captured (`aim-sweep-run/`, `move-sweep-run/`). The eastern route
still shows live Jops and jungle after twenty million clocks. A matched
right-button fire/no-button continuation at the same final frame 24771
caused a partially black image in both branches; moving the pointer back
restored the normal jungle. This is **not** an enemy-kill, objective, score or
later-disk acceptance.

The presentation diagnostics isolated a host-side defect. During the right
pan, the old build used a late sprite line as the top of the whole monitor
window, selecting only `source:128,225+650x88` for fire or
`source:128,233+666x80` for idle. The line-specific DIW/DDF debug still
reported the ordinary top field at beam line 44. The corrected presentation
accepts sprite-framed vertical bounds only when their first line is near the
registered display top. The two branches now select `source:128,44+650x256`
and `source:128,44+666x256`, with full jungle images and unchanged CPU and
disk-DMA reports (`east-pair-current/`, `east-pair-presentation-fix/`). An
independent visible-pixel guard of 130,000 rejects both old branches at
90,444/81,187 pixels and accepts both corrected branches at
143,254/139,806 (`visibility-guard-before/`, `visibility-guard-after/`).
This confirms the presentation fix without claiming a Cannon campaign win.

The Siedler A500+ wood frame remains pixel-identical at all four control
captures across cold restore and at a further gameplay capture; the A4000
Mega control is pixel- and CPU-identical at four captures
(`game-controls-current/`, `game-controls-presentation-fix/`,
`siedler-extended-before/`, `siedler-extended-after/`). The A3000/3.1.4 OS
control matches four CPU/image captures across cold restore and uses a new
private HDF with an unchanged SHA-256
(`a3000-os-current/`, `a3000-os-presentation-fix/`). The original 63 ROM/ADF/HDF
files retain their hashes (`media-proof.json`).

An independently encoded 030 `MOVEM.L D0-D1,d16(A0)`/absolute-long pair
showed the same repeated first physical write when its second destination
faulted. The old build failed the new fixture; the active-MMU store path now
probes all selected destinations after EA resolution and before writing data.
Both direct and deferred variants of both addressing modes fault without a
first-page write and complete after table repair, ATC flush and `RTE`, issuing
the first physical word once. This remains a bounded full-instruction probe;
indexed EA reads, devices and true partial-cycle continuation are not proven.
The final source suite passes **349/349**, all **14/14** Runner preflight
guards pass, and `git diff --check` has no whitespace errors.

Three alternating A4000/Mega benchmark pairs against the preceding frozen
build preserve all six complete CPU-state and framebuffer hashes. Median FPS
delta is **-0.30%**, range **-4.45..+6.24%** (`benchmark-probe-proof.json`,
`benchmark-probe-summary.json`). The spread is larger than the median, so no
sustained gain or regression is claimed; profile a specific hot path before
making a performance claim.

Next: continue Cannon from a visually confirmed live combat position with a
bounded target and verify an actual enemy kill, score, next requester/disk
transition and Mission 2; distinguish pointer-driven panning from game-state
progress. Then add a real device-side partial-transfer fixture for 030
format-A/B continuation. Wider gameplay/model review, ECS/AGA modes,
further performance profiling, optional FPU, A4000/040, Zorro III and the common
relay deployment remain open. No commit, push or release was made.

### Siedler lower wood-frame report and overscan tolerance — 2026-10-12

Evidence: `%APPDATA%/AmigaEmulator/diagnostics/SiedlerBottom-20261012-190000/`.
The user reports the lower wooden bar broken in both windowed and fullscreen
presentation. Their saved settings select A500, Kickstart 1.3, 2 MiB Chip +
8 MiB Fast RAM, `NONE` scaling and the accurate floppy drive. A fresh native
Disk-1/2/3 sequence with that model and RAM reaches the actual game and castle
construction (`native-a500-current/`). The local frame/menu golden also passes
from both the intro and this construction save; an offscreen Windows `OnPaint`
probe of the construction save renders `NONE` at 704x576 and stretched
1280x720 with zero differences from pixel-center nearest-neighbor sampling
(`UiProbe/`). Eighty consecutive PAL-field captures of the same gameplay
state are nonblack and pixel-identical across the previous and current
presentation builds (`fields-before/`, `fields-after/`). These sampled scenes
do **not** yet reproduce the user's exact visual defect.

The preceding Cannon late-sprite guard introduced one measurable Siedler
aperture instability: after horizontal/bottom mouse movements, the debug
source window sometimes changed from the framed `92,32+722x273` to
`92,18+722x294`, because the wood's first sprite-framed line is 14 scanlines
below the initially selected overscan top and the guard allowed only 12.
The guard now allows 20 lines, restoring the stable `92,32+722x273` for all
ten construction/pointer captures while keeping Cannon's right-pan source at
`128,44+650x256`/`128,44+666x256` and its >130,000 visible-pixel checks
(`construction-before/`, `construction-after/`, `construction-wood-fix/`,
`cannon-wood-fix/`). A new local Siedler golden assertion checks that mouse
movement leaves the complete source aperture unchanged. The construction
images were already pixel-identical before this tolerance change, so it is
a verified stability fix rather than proof that the user's particular
broken bar has disappeared.

The installed `C:/Program Files/AmigaEmulator/AmigaEmulator.dll` is dated
2026-10-10 and differs from the freshly built repository DLL. An isolated
`OnPaint` probe of that installed build with a compatible older A500 gameplay
state also showed intact wood (`UiProbeInstalled/`); a newer save cannot be
loaded by the installed version. The installation was not overwritten.
The fresh x64 Release build has zero warnings/errors; **349/349** source tests,
the native local golden and **14/14** Runner preflight guards pass. All **63**
original ROM/ADF/HDF files retain their baseline hashes (`media-proof.json`).
Next: obtain the exact affected frame/scene or save from the user's current
session and compare its lower rows with the game's playfield/sprite aperture;
do not close the Siedler defect on the current sampled scenes alone. No
commit, push, installation or release was made.

### Siedler right-hand lower shield report — 2026-10-12

The user supplied a current screenshot showing the round lower-right control
against the game image edge. A fresh inspection of the established native
A500/1.3 construction save shows the same placement. At beam line 305, the
raw framebuffer has rendered sprite pixels through host X=799 and no sprite
or playfield pixels at X=800..829 in the lower menu. A direct SpriteUnit query
at four menu beam lines likewise yields no sprite pixels past X=799. Thus the
host presentation stage has no additional right-hand shield pixels to reveal
in this sampled state. A published Amiga gameplay image independently shows
the two round end controls against the left and right image edges
(`https://spillhistorie.no/2016/10/27/levende-skjermbilde-the-settlers/`).

A synthetic experiment extending the sprite presentation aperture to the
bottom panel's widened DDF right edge did reveal an artificial marker there,
but on the real save it produced a black strip at the upper-right frame and
shifted the cursor by six presented pixels. This experiment was reverted;
the existing line-adaptive and local Siedler golden tests pass again. No
new emulator rendering change is justified by the supplied image alone.
Next: if a real session differs from this sampled state, capture its savestate
and compare raw sprite data, selected DIW/DDF aperture and presentation pixels
at the affected lower-right rows before altering the geometry. The earlier
wood-frame source-window stability fix remains in place. No commit, push,
installation or release was made.

### 68030 MOVEM source fault before register retirement — 2026-10-12

Fresh evidence: `%APPDATA%/AmigaEmulator/diagnostics/MovemRead-20261012-191127/`.
An independent active-MMU fixture demonstrated a remaining partial-commit
bug in `MOVEM.L <ea>,D0-D1`: with the first source longword mapped at logical
`$3FFC` and the second page unmapped, the indirect form changed D0 before
entering the bus-error handler. The same risk applied to displacement and
absolute source EAs. All valid memory-to-register forms now probe the complete
source transfer before any data read or destination-register update; the
previous special handling of postincrement still defers its base-register
change. Direct and deferred CPU tests cover indirect, displacement and absolute
forms, then repair the page descriptor, flush the ATC and verify `RTE` retries
the complete instruction. The first mapped physical data word is read exactly
once after repair. The new test failed before the fix and passes afterward.

The full source suite passes **350/350** with no skips (`full-tests.log`), the
Runner preflight passes **14/14** (`runner-preflight.log`), and the x64 Release
Runner build reports zero warnings/errors. A native A3000/AmigaOS 3.1.4 Shell
state matches four baseline frames, complete CPU text, framebuffer hashes,
exception counts and disk-DMA state across cold restore (`os-pair-proof.json`);
its two fresh private HDF copies retain their identical input SHA-256 after
the run. An A4000/Mega lo Mania state likewise matches four prior frame/CPU
captures with visible island gameplay (`mega-control-proof.json`). All **63**
original ROM/ADF/HDF hashes remain unchanged (`media-proof.json`).

This is bounded whole-instruction retry protection for MOVEM translation
faults, not a complete 68030 restartable bus-cycle implementation. Index-EA
indirection, faults from side-effecting devices after a successful translation,
full format-A/B resumption, 020/030 timing and caches remain open. Cannon
Mission 1 still needs an actual objective/score and later disk transition;
the supplied Siedler shield screenshot is assessed above; any different
lower-bar artifact still needs its affected savestate and exact rows. Further game/model coverage,
ECS/AGA modes, FPU, 040, Zorro III and relay deployment remain open. No
commit, push, installation or release was made.

### 68030 MOVEP spaced-byte page fault — 2026-10-12

Fresh evidence: `%APPDATA%/AmigaEmulator/diagnostics/Movep-20261012-200758/`.
`MOVEP.W/L` accesses two or four bytes at two-byte intervals. An independent
active-MMU fixture places the first byte on mapped logical page `$3000` and
the next relevant byte on unmapped page `$4000`. Before the repair, the first
physical `$9FFx` read had already retired when the later translation faulted;
whole-instruction `RTE` would read it again, which is wrong for a
side-effecting device. The 68030 path now probes every spaced-byte translation
before issuing any read or write. These probes leave descriptor used/modified
bits and ATC replacement history untouched. Eight direct/deferred,
word/long, read/write variants verify zero first-byte bus accesses at the
fault; after descriptor repair, ATC flush and `RTE`, each first physical byte
is accessed exactly once and the assembled data is correct. The test failed
before the fix and passes afterward.

The full source suite passes **351/351** (`full-tests.log`), all **14/14**
Runner guards pass (`runner-preflight.log`), and the x64 Release Runner build
has zero warnings/errors (`runner-build.log`). Fresh private A3000 HDF copies
retain their identical input SHA-256 after paired native AmigaOS 3.1.4 Shell
runs. Four OS captures match frame, full CPU text, framebuffer hash,
exception count and disk-DMA state across cold restore (`os-pair-proof.json`).
An A4000/Mega lo Mania island state matches four earlier frame/CPU/hash
captures and remains visible after cold restore (`mega-pair-proof.json`). All
**63** original ROM/ADF/HDF files retain their baseline SHA-256
(`media-proof.json`).

This is bounded translation-fault protection for MOVEP, not full 68030
partial-cycle continuation. Device faults after successful translation,
restartable format-A/B frames, indexed-EA bus side effects and precise
020/030 timing remain open. Next independent CPU fixture: inject a bus/device
fault after an actually retired first transfer and verify genuine mid-
instruction continuation rather than preflight retry. Next gameplay gate:
obtain a Cannon Mission-1 objective/score and its next visible disk request;
the existing first-mission cold DF0 route remains accepted but does not prove
later missions. Graphics modes, broader game/model sessions, performance
profiling, FPU, 040, Zorro III and relay deployment remain open. No commit,
push, installation or release was made.

### Cannon Mission 1 input continuation — 2026-10-12

Fresh evidence: `%APPDATA%/AmigaEmulator/diagnostics/Cannon-20261012-211428/`.
The Runner now supports explicit `mouse-button-down` and `mouse-button-up`
actions for the three physical Amiga mouse buttons. Its preflight rejects
invalid and combined button names and accepts a held right-button/left-button
sequence (15 guards). This makes the game's simultaneous aiming and secondary
action reproducible from a cold-restored native A500/1.3 Mission-1 state.

Several paired right-button aiming locations and a separate right/left chord
were executed against equal-cycle controls. Both chord and right-only runs
reached frame 24549 with identical framebuffer SHA-256
`345CAAD089E762543840D72FEC2438823A3A03EC1F5777F0F17EC682A1182687`
and full CPU text, while their audio DMA/nonzero-sample counters differed.
Reviewed captures still show live jungle gameplay and Jops, but no confirmed
enemy kill, score change, objective completion or next disk requester. The
audio difference cannot establish a grenade or successful shot. These runs
must not be counted as a passed later mission.

Next: establish the game's actual on-screen target coordinates and required
weapon state using visible cursor/enemy movement in this saved scene, then
repeat equal-cycle input/control branches until a kill and score change are
visibly demonstrated. Continue the ensuing disk requests from a fresh private
state. Broader model/game, graphics, timing, FPU, 040, Zorro III and relay
work remains open. No commit, push, installation or release was made.

### Cannon input-port proof and lower-right target pairs — 2026-10-13

Fresh evidence: `%APPDATA%/AmigaEmulator/diagnostics/Cannon-20261012-220959/`.
The Runner capture now logs the guest-visible CIA-A fire bits and POTINP
secondary-button bits. This is read-only instrumentation; it does not change
emulated input or rendering. A native A500/1.3 Mission-1 right/left sequence
records `ciaAPortA=C0,potInp=5500` at rest, `C0,5100` with the right button
held, `80,5100` while both buttons are held, and the original values after
release (`chord-trace-run/`). Thus the requested simultaneous buttons reached
both hardware input paths; the previous lack of visible progress cannot be
attributed to the Runner silently dropping the left-button edge.

Three new lower-right target locations, `(45,25)`, `(65,20)` and `(45,10)`
mouse-counter deltas, were each run with twelve million right-button chip
clocks and an equal-cycle no-button control from the same cold-restored save
(`target-pairs-run/`). All six cases retained the visible jungle scene and
zero new non-ROM exceptions; the reviewed `(45,25)` crosshair covers the
lower-right orange/red object, but it remains present. Framebuffer hashes
and some CPU states differ between input and controls, without a confirmed
enemy kill, score, completed objective or next disk request. The Runner's
new trace confirms `potInp=5100` during the held-button interval
(`input-trace-run/`); cursor-only pixel differences are not a gameplay pass.
The x64 Release Runner build succeeded, all **15/15** preflight guards pass,
and the full source suite passes **351/351** with no skips (`runner-build.log`,
`full-tests.log`).

Next: identify an actual moving enemy from a short-interval capture series,
then align the crosshair with its position while firing and compare a visible
kill counter or mission transition to an equal-cycle control. If that still
fails despite verified POTINP/CIA input, trace the game's guest reads and
weapon/game state before altering emulation. The other roadmap items remain
open. No commit, push, installation or release was made.

### Cannon short-interval tracking and input assertions — 2026-10-13

Fresh evidence: `%APPDATA%/AmigaEmulator/diagnostics/Cannon-20261012-231230/`.
Eight one-million-chip-clock captures from the native A500/1.3 Mission-1
checkpoint track the bright moving object near the top from roughly x=635 to
x=490, while the lower-right jungle object remains stationary (`motion-run/`).
The top object is not yet independently identified as an enemy. Paired
right-button/fire and equal-cycle idle continuations aimed toward its path at
mouse deltas `(20,-180)`, `(35,-180)` and `(0,-180)` retain visible jungle,
Jops and that moving object, with zero new non-ROM faults (`top-pairs-run/`,
`top-center-run/`). Their framebuffer/CPU differences do not establish a kill,
score change or later mission; no such pass is claimed.

The Runner now accepts optional action-level `expectedCiaAPortA` and
`expectedPotInp` hexadecimal guards. They compare guest-visible input values
after a capture, so an input-specific regression can fail independently of
cursor graphics. A real held-right/pressed-left Cannon continuation passes
the expected `C0/5100` then `80/5100` readings and release values
(`chord-assert-run/`). An intentionally wrong `C0` expectation while both
buttons are down fails at that exact step, retaining its screenshot
(`chord-negative-run/`). The x64 Release Runner builds successfully and all
**16/16** preflight guards pass (`runner-build.log`, `runner-preflight.log`).
The complete source suite passes **351/351** with no skips (`full-tests.log`).
An independently wrong POTINP expectation also fails at the held-right step
and retains its diagnostic capture (`pot-negative-run/`).

Next: identify the moving sprite and the game's own objective counter or
enemy state before assuming a hit target, then align and compare a short
fire/control window at a known enemy position. Continue to the visible score,
single-drive request and Mission 2 only after an actual kill/transition.
All other roadmap gates remain open. No commit, push, installation or release
was made.

### Cannon moving-object attribution — 2026-10-13

Fresh evidence: `%APPDATA%/AmigaEmulator/diagnostics/Cannon-20261013-001431/`.
The checkpoint used for the preceding aiming runs was produced by
`aim-east-low-move`: it gave Jops a left-button movement order, waited twenty
million chip clocks, then saved. That provenance makes the bright object
crossing the upper edge a poor assumed enemy target. A new equal-cycle pair
from that save changes only a subsequent left-button movement command. At
four and eight million clocks the upper sprite follows a different visible
course than in the no-click control (`object-identity-run/`), consistent with
the game's response to that movement order. This alone does not identify the
sprite as friendly: the original Mission-1 capture shows Jools/Jops near the
bottom while the bright object starts to their right. Both branches
retain the jungle/Jops display and report zero new non-ROM guest faults.

Two additional held-right versus idle pairs moved the crosshair left in eight
short steps to follow the object's trajectory. The crosshair overlaps its
screen area in reviewed captures, including a lower aiming line, but the
object persists and neither branch reaches a score, objective completion or
disk request (`track-pair-run/`, `track-lower-run/`, `track-aligned-run/`).
These are evidence that the previous target choice was unsound, not proof of
an emulation shooting defect or a later-mission gameplay pass. The Amiga
[game manual](https://www.lemonamiga.com/doc/cannon-fodder/278) documents
left-button movement and held-right aiming; the paired command response is
the local evidence for this particular sprite.
The existing source suite passes **351/351** and the Runner preflight passes
**16/16** (`full-tests.log`, `runner-preflight.log`).

Next: return to the accepted native DF0 Mission-1 start or west survivor
checkpoint, locate a distinct enemy sprite by short-interval no-input and
movement-control captures, then shoot that independently identified target.
Require visible kill/score and the subsequent disk requester before claiming
Mission 2. Continue independent hardware and OS gates; the seven-point
roadmap remains open. No commit, push, installation or release was made.

### Cannon original Mission-1 targeting and mouse pacing — 2026-10-13

Fresh evidence: `%APPDATA%/AmigaEmulator/diagnostics/Cannon-20261013-011502/`.
The reviewed original native A500/1.3 DF0 Mission-1 checkpoint has Jools/Jops
near the lower centre and a separate bright moving sprite initially at the
right. This corrects the preceding overconfident suggestion that the bright
sprite was likely Jops. Its exact game-side allegiance remains unverified;
the left-click pair only proved a different trajectory after a new movement
order. Several equal-cycle right-button/idle pairs aimed near that sprite
from the original state (`first-target-pairs-run/`, `target-fine-run/`,
`target-intercept-run/`, `target-direct-run/`) retain live play and zero new
non-ROM faults but no visible kill, score change, objective or disk request.

The Runner's `mouse` action now accepts optional `mouseStepCycles`; omission
retains its prior 120,000-chip-clock pacing for each small counter movement.
This is a test-harness control, not a claim that faster mouse input improves
gameplay. Five native Cannon calibration runs apply the same `(270,70)` raw
movement with 20k/40k/60k/80k/120k clocks per step (`pointer-pacing-run/`).
The final JOY0 mouse counter is identical, but the game's displayed pointer
and moving sprite positions differ because the guest processes input over
different time spans. The 20k run places the pointer visibly short of its
120k position; it cannot be treated as equivalent aiming. Two additional
60k right-button/idle pairs (`paced-aim-run/`, `paced-aim-70-run/`) remain
live without a confirmed hit. All tested button-down/up captures assert the
guest-visible POTINP and CIA-A values. The x64 Release Runner build and all
**17/17** preflight checks pass (`runner-build.log`, `runner-preflight.log`).
The full source suite passes **351/351** without skips (`full-tests.log`).

Next: select a target based on the unmodified original Mission-1 scene and
confirm its allegiance with a visible reaction or mission counter, then use
small, paced mouse corrections during firing. Keep cursor movement, elapsed
chip clocks and the equal-cycle control together in the evidence. Only count
a later mission after the game's own score/requester and playable transition.
The other roadmap stages remain open. No commit, push, installation or release
was made.

### 68030 odd MOVEM page-boundary retry — 2026-10-13

Fresh evidence: `%APPDATA%/AmigaEmulator/diagnostics/MmuOddMovem-20261013-021833/`.
A new active-MMU fixture puts an odd MOVEM word at logical `$3FFF` or an odd
long at `$3FFD`, so its final byte falls on unmapped page `$4000`. Before the
fix, the CPU's MOVEM precheck visited only the starting address (and `+2`
for a long), then a real first-page device byte read retired before the final
translation fault (`before.log`: 352 tests, one failure). On 020/030, the
actual odd word is two byte bus accesses; an odd long is byte/word/byte.
MOVEM now probes those same logical pieces without setting page U/M bits or
changing ATC replacement history before it commits any transfer.

The repaired indirect read/write fixture covers word/long and direct/deferred
execution. It verifies no first-page physical read/write at the fault,
unchanged source/destination registers, the logical `$4000` fault address,
descriptor repair plus ATC flush, `RTE` retry, correct resulting value, and
exactly one first-page device access. A separate word/long direct/deferred
predecrement fixture verifies An remains unchanged at the fault and commits
only after a repaired complete write. The full suite passes **353/353** with
no skips (`final-tests.log`). This is translation preflight, not complete
format-A/B mid-instruction device-fault continuation.

Native A3000/AmigaOS 3.1.4 Shell and A4000/Mega lo Mania island continuations
were run with the new x64 Release core. Four captures in each case match the
previous reference's frame, full CPU text, framebuffer SHA-256 and fault
count; A3000 disk-DMA debug state also matches (`os-proof.json`,
`mega-proof.json`). Reviewed final captures show the live Shell and island,
including cold restore. The A3000 run used a fresh private HDF copy whose
SHA-256 remained `50564DA3A4AD2EE9A6709F477B5D4C760DEFDD614D1BC261954E2683A3E3A035`
before and after; all **63** original ROM/ADF/HDF hashes still match their
inventory (`media-proof.json`). The Runner passes **17/17** preflight guards.

Next: construct a device-fault fixture that fails after a transfer really has
retired, then implement genuine 68030 format-A/B partial-cycle continuation
rather than relying only on whole-instruction retry. Continue Cannon's
objective/score and later disk request, ECS/AGA modes, profiling, broader OS
sessions and optional FPU/040/Zorro III work. No commit, push, installation or
release was made.

### 68030 odd single-operand MMU page retry — 2026-10-13

Fresh evidence: `%APPDATA%/AmigaEmulator/diagnostics/MmuOddSingle-20261013-032004/`.
An independently encoded `MOVE.W`/`MOVE.L` test (both `(A0)->D0` and
`D0->(A0)`, direct and deferred) places the first byte on a mapped 4-KiB
page and the last byte on unmapped logical `$4000`. The pre-fix run showed
**353/354 tests passing**: a first-page physical read retired before the
later translation fault (`before.log`). The generic odd word/long read and
write paths now precheck their exact byte/word/byte bus pieces only when an
active 68030 MMU transfer spans pages. A fault occurs before any data bus
side effect. After mapping the page, flushing ATC and `RTE`, the original
MOVE completes with the correct data and exactly one first-page read or
write. The full source suite passes **354/354** (`after.log`); Runner
preflight passes **17/17** (`runner-preflight.log`). This covers translation
faults, not general mid-instruction device-fault continuation.

The new x64 Release core continued native A3000/AmigaOS 3.1.4 Shell and
A4000/Mega lo Mania island save states. Each of their four loaded, active,
cold-restored and post-restore captures matches the previous run's frame,
full CPU text, framebuffer SHA-256, exception count and disk-DMA state
(`paired-proof.json`). Both final screenshots were inspected. The A3000
run used a fresh private HDF copy; its SHA-256 stayed
`50564DA3A4AD2EE9A6709F477B5D4C760DEFDD614D1BC261954E2683A3E3A035`.
Original media and save states were not written.

Next: exercise a true physical device fault after a transfer has retired
and implement format-A/B partial-cycle continuation. Continue Cannon's
objective/score and later disk requests, the Siedler border/shield visual
reproduction, ECS/AGA modes, profiling, broader OS sessions and optional
FPU/040/Zorro III work. No commit, push or release was made.

### Siedler reproduction and 68030 aligned split-read retry — 2026-10-13

Fresh evidence: `%APPDATA%/AmigaEmulator/diagnostics/SiedlerVisual-20261013-042504/`
and `%APPDATA%/AmigaEmulator/diagnostics/MmuEvenRead-20261013-042504/`.
The current x64 Release Runner continued the native A500/1.3 Siedler
construction save through ten loaded/live/left/right/bottom/top pointer
captures. Inspection again finds an intact lower wood bar, while the round
right-hand menu control meets the image edge and looks cropped in the same
way as the user's screenshot. The previous raw sprite query found no pixels
to the right of this edge, so no guessed host crop change is justified.
The exact user's live savestate remains needed to
distinguish a game-side clipped sprite from an emulator presentation error.
This visual defect is **open**, not a pass.

An existing `MOVE.L (A0)+,D0` active-68030-MMU fixture at logical `$3FFE`
already verified the completed result after mapping the missing `$4000`
page, but did not count physical source reads. The new assertion failed
before the change: its first mapped device word was read once *before* the
later translation fault (`before.log`, 353/354). Aligned long reads now
probe both pages without a bus side effect before reading either word.
The direct and deferred fixture then verifies an unchanged D0/An at fault,
`RTE` retry, correct longword, and exactly one first-page physical read.
The full suite passes **354/354** (`after.log`), x64 Runner builds with zero
warnings/errors, and Runner preflight passes **17/17**.

On the new core, all ten Siedler pointer captures and four each of native
A3000/AmigaOS 3.1.4 Shell and A4000/Mega lo Mania island continuation match
their immediately preceding frame, full CPU text, framebuffer hash, fault
count and disk-DMA state (`paired-proof.json`). The OS and game runs include
cold restore. A new private A3000 test HDF retained SHA-256
`50564DA3A4AD2EE9A6709F477B5D4C760DEFDD614D1BC261954E2683A3E3A035`
(`hdf-proof.json`); original media and saves were not written.

Next: capture a live Siedler savestate from the affected window and compare
raw lower-right sprite registers/pixels, DIW/DDF aperture and presented rows
before changing border geometry. Continue Cannon's score/objective and later
disk requests and a true physical 68030 device-fault continuation. ECS/AGA,
performance, broader applications and optional FPU/040/Zorro III gates remain
open. No commit, push or release was made.

### 68030 cached split-operand MMU retry — 2026-10-13

Fresh evidence: `%APPDATA%/AmigaEmulator/diagnostics/MmuCachedSplit-20261013-052435/`.
With the 68030 data cache enabled, `MOVE.L` at logical `$3FFE` could fill
the first mapped cache line before faulting on unmapped `$4000`. The new
direct/deferred read/write fixture failed before the fix: its physical
first-page read count was already one at the fault (`before.log`, 354/355).
Both cached reads and writethrough writes now precheck every logical part
when a word/long crosses active MMU pages, before a cache fill, hit update
or physical transfer. After descriptor repair, ATC flush and `RTE`, the
fixture verifies original D0/An, complete data and one first-page physical
access. The source suite passes **355/355** (`after.log`). This remains a
bounded translation-fault fix, not full format-A/B partial-cycle restart.

The x64 Release Runner builds without warnings/errors, and its preflight
passes **17/17**. Native A500/1.3 Siedler construction/pointer captures
(ten), A3000/AmigaOS 3.1.4 Shell (four) and A4000/Mega lo Mania island
(four) all match their preceding frame, complete CPU text, framebuffer
hash, exception count and disk-DMA state (`paired-proof.json`). OS and Mega
include cold restore. The new private A3000 HDF copy remained at SHA-256
`50564DA3A4AD2EE9A6709F477B5D4C760DEFDD614D1BC261954E2683A3E3A035`
(`hdf-proof.json`). Original media/saves were not written.

Superhires inspection found no isolated register-only change: current
DMA fetch stepping, Denise pixel spans and the 896-pixel raw framebuffer
are all defined at hires resolution. Native 1280-pixel SHRES needs a
coordinated fetch, raster and presentation change with its own reference
and restore tests; it remains unsupported. Next: add a minimal SHRES
register-to-pixel golden and derive the necessary raw/presented geometry,
then implement and validate the mode without changing legacy 640-pixel
screens. Continue true 68030 device-fault restart, Cannon's later
objective/disk states, Siedler's exact live border state and the other
roadmap gates. No commit, push or release was made.

### ECS/AGA SHRES register decoding — 2026-10-13

Fresh evidence: `%APPDATA%/AmigaEmulator/diagnostics/ShresDecode-20261013-062136/`.
The ECS BPLCON0 mask discarded bit 6, so a selected SHRES mode was
indistinguishable from legacy lores in BitplaneUnit. The new OCS/ECS/AGA
fixture failed before the fix (355/356): ECS did not retain the request.
OCS continues to ignore SHRES; ECS and AGA now report it through an explicit
`IsSuperHiresMode` flag. Clearing the bit and save/restore are covered, and
the plane count is unchanged by the flag. This is the register decoder only:
DMA timing, native 35 ns pixels, 1280-pixel raw and presented geometry,
color handling and interlace are **not** complete and must not be offered
as compatible. Hardware reference: Commodore's *Amiga Hardware Reference
Manual*, ECS appendix, BPLCON0 SHRES bit 6 and SuperHires mode
(https://www.ikod.se/wp-content/uploads/2020/08/Amiga_Hardware_Reference_Manual_3rd_Edition.pdf).

The full source suite passes **356/356** (`final-tests.log`), x64 Runner
builds with zero warnings/errors, and Runner preflight passes **17/17**.
Ten Siedler construction/pointer captures and four each of native
A3000/AmigaOS 3.1.4 Shell and A4000/Mega lo Mania island continuation
match their immediately preceding frame, complete CPU text, framebuffer
hash, fault count and disk-DMA state (`paired-proof.json`). The latter two
include cold restore. A new private A3000 HDF kept SHA-256
`50564DA3A4AD2EE9A6709F477B5D4C760DEFDD614D1BC261954E2683A3E3A035`
(`hdf-proof.json`); original ROMs, disks and saves were not written.

Next: create a native 1280-sample, two-plane ECS golden with known bit
edges; extend fetch timing and raw/presented raster geometry together,
then check AGA color/fetch variants and restore without changing legacy
screens. Continue the independent Cannon, Siedler, CPU/MMU, performance,
OS and optional-hardware gates. No commit, push or release was made.

### ECS SHRES fetch and native bit sampling — 2026-10-13

Fresh evidence: `%APPDATA%/AmigaEmulator/diagnostics/ShresBits-20261013-072407/`.
The new two-plane ECS SHRES golden failed before the change: only 20 of the
80 expected words were fetched per plane (`before.log`). ECS SHRES now uses
the two-slot plane order and two-cycle repeat distance, giving 80 words per
plane for the `$3C..$D4` test window. Its native sampling method distinguishes
all 1280 bit positions: the first two samples select different planes, an
empty next bit stays empty, and both final-word edge bits remain visible.
The test also checks those edge bits after bitplane save/restore. A 640-pixel
preview samples the complete native row at every second bit, so it reaches
the right edge instead of displaying only the left half. This is **partial
SHRES support**: Denise, the raw framebuffer and presentation pipeline still
need native 1280-pixel output, including correct horizontal scroll and color
handling. AGA SHRES fetch/color variants and interlace remain open; the
mode must not be advertised as fully compatible.

The full source suite passes **357/357** (`final-tests.log`), x64 Release
Runner builds with zero warnings/errors, and Runner preflight passes **17/17**.
On the final build, ten native A500/1.3 Siedler construction/pointer steps
and four each of A3000/AmigaOS 3.1.4 Shell and A4000/Mega lo Mania island
continuation match the preceding frame, full CPU text, framebuffer SHA-256,
exception count and disk-DMA state in all **18/18** steps
(`paired-proof.json`); OS and Mega include cold restore. The fresh private
A3000 test HDF kept SHA-256
`50564DA3A4AD2EE9A6709F477B5D4C760DEFDD614D1BC261954E2683A3E3A035`.
Original media and saves were not written.

Next: add a native 1280-pixel Denise/framebuffer/presentation path and a
visible border/palette/scroll reference, then test AGA SHRES fetch and
interlace. Continue the open Cannon objective/disk transitions, the user's
live Siedler lower-right state, MMU physical-fault restart, performance,
OS/application and optional FPU/040/Zorro III gates. No commit, push or
release was made.

### Savestate preview row alignment — 2026-10-13

The GUI copied the 704x288 presented image into a savestate while labeling
its screenshot as the model's 640x256 visible area. The preview bitmap then
used a 640-pixel row stride, turning the image into striped/wrapped rows and
dropping the bottom. Five existing user saves have exactly this legacy
metadata/pixel-count mismatch; seventeen older 640x256 saves have matching
dimensions and remain unchanged. New GUI saves now capture the presented
frame with its own width and height. Metadata reads repair only the exact
legacy 640x256-label/704x288-pixel combination in memory, without rewriting
any save file.

Evidence: `%APPDATA%/AmigaEmulator/diagnostics/SavePreview-20261013/`.
One affected Siedler preview was rendered with both strides: the old
640-pixel interpretation shows the reported stripes; the corrected
704-pixel interpretation restores the complete field, lower wood bar and
menu. A new source regression covers first/right/bottom pixels for current
and legacy metadata and confirms both files still load. The source suite
passes **358/358**; the x64 Release GUI build has zero warnings/errors.
Original saves and media were not modified. Native SHRES output and the
other hardware/gameplay roadmap gates remain open. No commit, push or
release was made.

### ECS SHRES encoded palette pairs — 2026-10-13

Fresh evidence: `%APPDATA%/AmigaEmulator/diagnostics/ShresPalette-20261013-093803/`.
The ECS SHRES 640-pixel preview previously resolved each bitplane index
through the ordinary 12-bit COLORxx palette. Commodore's *Amiga Hardware
Reference Manual*, ECS appendix, "SuperHires Mode and the Denise Color
Registers" specifies a different encoding: each of COLOR00..15 combines
the two 35-ns pixel indices of one pair and stores two RGB bits per pixel
(https://www.ikod.se/wp-content/uploads/2020/08/Amiga_Hardware_Reference_Manual_3rd_Edition.pdf).
The new known-pair fixture failed on the previous renderer: the first
nonblack SHRES sample appeared black (`before.log`). The ECS path now
selects the pair register from both adjacent bitplane indices and expands
its high/low component halves separately. The host-resolution preview
shows the first pixel of each native pair. COLORxx writes update the
derived colors immediately, and derived caches are excluded from the
savestate layout and rebuilt after legacy restore.

The fixture checks both native colors, the preview's first pixel, a
midline COLOR09 rewrite at the next pair and save/restore. The full source
suite passes **358/358** (`final-tests.log`), x64 Release Runner builds
with zero warnings/errors and Runner preflight passes **17/17**. Ten native
A500/Siedler steps plus four A3000/AmigaOS 3.1.4 Shell and four A4000/Mega
lo Mania island steps match their preceding complete CPU text, framebuffer
SHA-256, frame, exception and disk-DMA state at **18/18** endpoints
(`paired-proof.json`). OS and Mega include cold restore. A fresh private
A3000 HDF remained at SHA-256
`50564DA3A4AD2EE9A6709F477B5D4C760DEFDD614D1BC261954E2683A3E3A035`.
Original media and saves were not written.

Native 1280-pixel output, the second pixel in the presented image, sprite
palette/positioning, ECS scrolling, AGA SHRES variants and interlace remain
open. Continue the Cannon objective/disk transitions, Siedler live border
state, 68030 physical-bus-fault restart, performance, OS/application and
optional FPU/040/Zorro III gates. No commit, push or release was made.

### Cannon Mission 1 pointer calibration and paired fire control — 2026-10-13

Fresh external evidence: `%APPDATA%/AmigaEmulator/diagnostics/CannonAim-20261013-103650/`.
The native A500/1.3 DF0 Mission-1 checkpoint from the verified cold-boot
disk sequence was loaded read-only for all cases; its SHA-256 remains
`9FEAD5FE40ADB8BE62330A59EAFFD89AEC885BB884B38C59C690ED2938481A9B`.
The GameRunner's `mouse` values are relative JOY0 counter increments, while
the game cursor moves about 2.2 presented pixels per X increment. A fast
335-increment motion looked aimed near x=570 immediately afterward but
settled at the right edge before firing. A 260/-50 motion followed by a
two-million-chip-cycle wait instead settled near x=590,y=40, beside the
visible red upper-field sprite. This explains why the prior larger-X shots
cannot be treated as aimed hits.

From that same settled frame (23170, framebuffer SHA-256
`15A7CFAAC09F5B38375D7706DE30F2968D89CEC724FFB17C3182063093CBAFE5`),
paired six-million-cycle fire and idle runs ended on the same frame 23283
and PC `$C1EB10`. The right-button hold changed POTINP from `$5500` to
`$5100`, displayed a red crosshair and produced a different final image
(`74377C15...` versus idle `F871A57A...`); both reported zero non-ROM
exceptions. The HUD still shows Jools and Jops, and neither image proves an
enemy kill, score change or disk request. A further left-click movement and
equal-cycle idle pair reached frame 23463 with different scene images and no
reported synchronous fault, but the dark vegetation prevents a reliable
objective assessment from these captures alone. These are input/aim
controls, **not** gameplay or mission-completion passes.

Next: identify an unambiguous enemy and track its position at firing time,
move the squad into line of sight if necessary, and require a visible hit,
score/objective update and the next genuine single-drive request before
claiming later Cannon progress. Keep the same-frame idle control. The wider
game, SHRES/interlace, MMU, performance, OS/application, FPU/040, Zorro III
and relay gates remain open. No commit, push or release was made.

### Savestate screenshot write validation — 2026-10-13

The frontend screenshot-size fix left another entry point open:
`SaveStateFile.Write` accepted a pixel buffer whose length disagreed with
the declared width/height, allowing a future caller to create another broken
preview. The writer now rejects null pixels, negative/inconsistent dimensions
and mismatched pixel counts **before** creating the savestate path. Empty
0x0 previews used by metadata-only tests remain valid. The existing GUI
preview test now fabricates the exact older 640x256-header/704x288-pixel
layout by modifying a generated private fixture, then verifies that the
reader still corrects its metadata without rewriting the file. It also
checks that a new mismatched write throws and leaves no partial file.

Evidence: `%APPDATA%/AmigaEmulator/diagnostics/CannonAim-20261013-103650/`.
The targeted preview test and all **21/21** save-state-related tests pass;
the full source suite passes **358/358**, zero skips (`full-tests.log`). The
x64 Release GameRunner builds without warnings/errors, and its **17/17**
preflight checks pass (`runner-preflight.log`). Replaying the same native
Cannon fire/idle pair with the rebuilt writer produced new checkpoints and
the same final frame 23283, PC `$C1EB10` and complete framebuffer SHA-256
for both cases (`writer-control-run/` versus `red-target-fire-run/`). This
verifies the save path in real gameplay; it does not advance Cannon's mission
objective. Original saves, ROMs and disks were not written. No commit,
push or release was made.

### Cannon follow-up and protected savestate replacement — 2026-10-13

Fresh external evidence: `%APPDATA%/AmigaEmulator/diagnostics/CannonFollow-20261013-113451/`
and `%APPDATA%/AmigaEmulator/diagnostics/SaveAtomic-20261013-113451/`.
The native A500/1.3 Mission-1 state after the proven move command was
continued through a settled westward aim and 20-million-chip-cycle right
button hold, paired with equal-cycle idle from the same state. Both reached
frame 23918, PC `$C1D886`, with live Jools/Jops HUD and no non-ROM CPU
fault; the fire and idle images differ in the crosshair and jungle region.
The reviewed frames still do **not** establish a kill, score change or
disk requester. A later mission remains open; next test should identify
a visible enemy and line of sight before firing, then require its removal
and an objective/score change on the same-frame fire versus idle pair.

The save writer previously opened the destination slot with `File.Create`,
so a serialization or replacement failure could truncate a good existing
state. It now serializes into a unique temporary file in the same directory
and replaces the slot only after the writer closes successfully. On failure
it removes only that temporary file. A new test locks an existing slot,
forces replacement failure, verifies its bytes remain identical and no
temporary file remains, then retries and checks the newly published preview.
The complete source suite passes **359/359**, zero skips (`full-tests.log`);
x64 Release GameRunner builds without warnings/errors and **17/17** runner
guards pass. Rebuilt-runner Cannon fire and idle replays reach the same
frame, PC, complete CPU text and framebuffer SHA-256 as their prechange
counterparts (`cannon-replay/` versus `CannonFollow.../run/`). Existing
user ROMs, disks, saves and HDFs were not modified. No commit, push or
release was made.

### Corrupt save previews remain loadable — 2026-10-13

Fresh external evidence: `%APPDATA%/AmigaEmulator/diagnostics/SavePreviewRead-20261013-124222/`.
After the earlier screenshot dimension and atomic-write fixes, an older or
externally modified file could still carry impossible preview dimensions.
`ReadMetadata` now discards only such a preview (negative dimensions, unequal
pixel count, or one zero dimension) after applying the exact known 640x256
legacy correction. It retains the occupied machine state and mounted-media
metadata, so the user can load that state with a blank thumbnail. A private
fixture sets a current save's preview width to `int.MaxValue`: metadata has
no screenshot, remains `Occupied`, and the core state loads. The valid and
legacy previews retain their first, rightmost and final-row pixels.

The full source suite passes **359/359**, zero skips (`full-tests.log`), the
x64 Release runner builds with zero warnings/errors, and **17/17** runner
guards pass (`runner-preflight.log`). Fresh native A500/Siedler construction
and A4000/Mega gameplay replays, plus A3000/AmigaOS 3.1.4 Shell on a **new
private** 256-MiB HDF copy, match all **18/18** preceding labels on frame,
complete CPU text, framebuffer SHA-256, visible-pixel count and synchronous
exception count (`siedler-run/`, `mega-run/`, `a3000-run/`). The private HDF
hash stayed at
`50564DA3A4AD2EE9A6709F477B5D4C760DEFDD614D1BC261954E2683A3E3A035`.
The reviewed Siedler construction image has its lower wood bar, but this is
not the user's newer shield/border state; that report stays open. Next:
reproduce that specific state when available, while continuing Cannon's
verified objective/disk transition, native SHRES/interlace, MMU and broader
game/OS/performance/hardware gates. No user media was written. No commit,
push or release was made.

### Bounded AGA FMODE0 SuperHires fetch — 2026-10-13

Fresh external evidence: `%APPDATA%/AmigaEmulator/diagnostics/AgaShres-20261013-133723/`.
A new independent two-plane, 16-bit FMODE0 fixture put sentinel bits at
both ends of a 1280-pixel AGA SHRES row. Before the change the emulator read
only **20** words from the first plane rather than the required **80** (the failing
pre-change targeted test). The bounded Lisa/Alice path now schedules SHRES DMA every
two chip slots for this two-plane/FM0 case and samples both native-bit
indices. Its 640-pixel host preview samples the first bit of each pair with
AGA's direct color palette. The test checks the first, second, penultimate
and last native bits, both preview edges, a direct red COLOR01 pixel and
save/restore of the fetched row. The AGA SHRES request was already decoded;
this corrects its **FMODE0/two-plane fetch**, not the whole video mode.
The [Commodore AA chipset specification](https://shanson.com/spencer/Amiga-AA-Chipset.pdf)
is the hardware reference; FMODE1/3, deeper AGA SHRES, native 1280-pixel
output including the second pixel, scrolling, sprites and interlace remain
unimplemented or unverified.

The rebuilt full source suite passes **360/360**, zero skips
(`full-tests-rebuilt.log`), x64 Release GameRunner builds with zero warnings/
errors and **17/17** runner guards pass. Fresh native A500/Siedler, A4000/
Mega gameplay and A3000/AmigaOS 3.1.4 Shell runs match the prior complete
CPU text, frame and framebuffer hash at **18/18** steps. The OS test used a
new private 256-MiB HDF copy; its SHA-256 stayed
`50564DA3A4AD2EE9A6709F477B5D4C760DEFDD614D1BC261954E2683A3E3A035`.
Five alternating 300-frame A4000/Mega speed pairs with identical gameplay
endpoints vary from -1.19% to +8.35%, median +0.40%; that spread does not
support a speed claim (`benchmark.json`). A separate attempt to hoist a
stable sprite branch from the presentation pixel loop varied around zero
on six alternating Siedler pairs and was reverted (`PresentationBranch-20261013-133723/`).
Next: implement and test the second native SHRES pixel and FMODE1/3 fetch,
then interlace/programmable modes, while continuing actual objective and
disk-change gameplay tests. No original media or saves were written. No
commit, push or release was made.

### AGA SuperHires fine-scroll phase — 2026-10-13

Fresh external evidence: `%APPDATA%/AmigaEmulator/diagnostics/AgaShresScroll-20261013-143854/`.
The two-plane/FM0 SHRES fetch added above exposed another mismatch: Lisa's
BPLCON1 fine delay was still divided as if each source bit occupied a lowres
pixel. A failing-before fixture set a **one-native-pixel** odd-plane delay;
native X=0 still contained the old plane-one bit. The SHRES decoder now keeps
the eight-bit delay in 35-ns native-pixel units. Only the existing 640-pixel
preview converts that delay to host pixels. The fixture checks independent
one-pixel odd and even delays, their first native-bit positions, the combined
two-plane index and the preview's left edge; the previously tested left/right
DMA edges, AGA palette, restore and ECS behavior remain covered.

The rebuilt source suite passes **360/360**, zero skips
(`full-tests-final.log`); the x64 Release GameRunner builds with zero warnings
and errors, and **17/17** runner guards pass. Fresh A500/Siedler construction,
A4000/Mega gameplay and A3000/AmigaOS 3.1.4 Shell continuations match the
preceding full CPU text, frame, framebuffer SHA-256, nonblack-pixel count and
exception count at **18/18** steps (10 + 4 + 4). The OS used a new private
256-MiB HDF copy, unchanged SHA-256
`50564DA3A4AD2EE9A6709F477B5D4C760DEFDD614D1BC261954E2683A3E3A035`.
This proves the bounded AGA SHRES scrolling fixture and regression parity;
native 1280-pixel presentation, FMODE1/3, deeper SHRES, sprite composition and
interlace are still open. Next: provide and test the second native SHRES pixel
in the presentation pipeline, then broaden FMODE and programmable-mode tests
while continuing Cannon objective/disk and the reported Siedler border state.
No original media or saves were written. No commit, push or release was made.

### Native SuperHires color-pair resolution — 2026-10-13

Fresh external evidence: `%APPDATA%/AmigaEmulator/diagnostics/ShresPixelPair-20261013-153824/`.
The fetched SHRES row already retained two independent 35-ns indices, but the
Denise preview resolved only its first color. The compositor now exposes a
paired color resolver: ECS interprets the two indices as one packed COLORxx
register with separate high/low component groups; AGA resolves two independent
palette entries (including its playfield palette selection). Both existing
preview render paths call this common resolver for SHRES. Independent ECS and
AGA fixtures check the first **and** second native ARGB values: ECS COLOR09
produces `FFFF0055`/`FF00FFAA`, while AGA COLOR01/02 produce direct red/blue.
The presented 640-pixel image still selects the first value of each pair;
the second value is now available for a future 1280-pixel target. This is a
bounded color/compositor step, **not** completed native SHRES presentation or
sprite/overscan acceptance. Hardware-mode background:
[AmigaOS classic graphics documentation](https://wiki.amigaos.net/wiki/Classic_Graphics_Primitives).

The source suite passes **360/360**, zero skips (`full-tests.log`), x64 Release
GameRunner builds with zero warnings/errors and **17/17** runner guards pass.
New A500/Siedler construction, A4000/Mega gameplay and A3000/AmigaOS 3.1.4
Shell runs match the preceding complete CPU text, frame, framebuffer SHA-256,
nonblack-pixel count and exception count at **18/18** steps. The OS used a fresh
private 256-MiB HDF copy with unchanged SHA-256
`50564DA3A4AD2EE9A6709F477B5D4C760DEFDD614D1BC261954E2683A3E3A035`.
Next: carry both native SHRES colors through a selectable 1280-pixel
presentation target, including sprite composition and edge geometry; then
verify FMODE1/3 and interlace. Cannon objective/disk progression and the
user's Siedler border state remain open. No original media or saves were
written; no commit, push or release was made.

### Transient native SuperHires playfield target — 2026-10-13

Fresh external evidence: `%APPDATA%/AmigaEmulator/diagnostics/ShresNativeSidecar-20261013-163855/`.
The 35-ns color pair from the previous step now reaches a separate, lazily
allocated raw-framebuffer plane with **two ARGB samples per 70-ns host pixel**.
Both ECS and bounded AGA SHRES paths write their first and second playfield
colors. AGA's 640-host-pixel fixture checks native indices 0, 1, 1278 and
1279 against separate red/blue palette entries, so the second pixel and the
far right edge are no longer merely values returned by an isolated resolver.
The native plane clears at each SHRES field, is released when returning to a
non-SHRES mode, and is discarded on framebuffer restore because old save
formats contain only the normal framebuffer. Ordinary frames do not retain
the extra allocation. ECS's encoded pair is checked at native indices 0/1.

This is **playfield data**, not yet a selectable 1280-pixel frontend image:
native-resolution sprite priority, border/window reconstruction, source
selection, upscaling and savestate presentation need separate work. The normal
640-pixel preview remains unchanged. The source suite passes **360/360** with
zero skips (`full-tests-final2.log`), x64 Release GameRunner builds with zero
warnings/errors, and **17/17** runner guards pass. Fresh A500/Siedler,
A4000/Mega and A3000/AmigaOS 3.1.4 continuations on a new private HDF copy
match the preceding CPU text, frame, framebuffer hash, nonblack count and
exception count at **18/18** steps. The HDF SHA-256 stayed
`50564DA3A4AD2EE9A6709F477B5D4C760DEFDD614D1BC261954E2683A3E3A035`.
After the final mode-exit cleanup, a rebuilt Siedler continuation again
matched all **10/10** preceding steps. Next: compose sprites and display
window/borders on the native plane, expose a selectable 1280-pixel frontend
source, then validate FMODE1/3 and interlace. The Cannon objective/disk and
reported Siedler border states remain open. No original media, saves or HDFs
were written; no commit, push or release was made.

### Space-conscious gameplay runner after diagnostics cleanup — 2026-10-13

The user's disk cleanup removed the old `%APPDATA%/AmigaEmulator/diagnostics/`
tree after the prior run. Earlier roadmap results remain recorded above, but
their external screenshots, logs and private HDF copies are no longer on disk;
repeat a specific gate with fresh inputs before using it as new acceptance
evidence. User ROMs, installed HDFs, games, normal saves and retained
`codex-checkpoints` are separate and were not changed by this runner work.

The GameRunner now accepts `--no-final-checkpoint` for broad sweeps. It still
captures images and full `steps.jsonl`, but omits the automatic final
`.amigasav` for each case; an explicit `checkpoint` action still saves its
named state. Default behavior remains unchanged for continuation workflows.
The CLI rejects unknown flags and duplicate case names before creating output.
The x64 Release runner builds with zero warnings/errors; **18/18** Python
preflight guards pass. A real Siedler checkpoint restore and one-million-cycle
continuation produced two reviewed capture entries and **no** final save with
the new option. The default run wrote its final checkpoint; the explicit
checkpoint action wrote exactly its requested save under the compact option.
The paired compact/default runs matched CPU text, frame, framebuffer SHA-256,
nonblack pixel count and exception count at **2/2** steps. All test output was
made under the ignored repository `tmp/` directory for immediate cleanup.

Next: keep future sweeps compact by default, retain only checkpoints required
for a concrete continuation, and continue native SHRES sprite/window work and
the remaining game/MMU/OS gates. No original media or saves were written. No
commit, push or release was made.

### Native SuperHires border continuity — 2026-10-13

The transient 1280-sample SHRES plane had a transparent/uninitialized border
when its first playfield pair was allocated during a field. It also missed
COLOR00 raster changes and background spans outside the display window. Denise
now allocates and clears the native plane at the start of a SHRES field,
initializes a mid-field allocation with the current border color, and mirrors
background/border span fills into both native samples. The ECS border restore
path updates the native plane after sprite/window clipping as well. The 640-pixel
composite path and legacy savestate format remain unchanged.

The new ECS/AGA fixture checks both 35-ns border samples at the left and right
edges, a mid-line COLOR00 change across all eight samples of one chip cycle,
and retention of the unvisited span. Source tests pass **361/361**, zero skips;
the x64 Release GameRunner builds with zero warnings/errors and **18/18**
preflight guards pass. A restored A500/Siedler continuation advanced one
million chip cycles to frame 7301 with a nonblack frame and zero exceptions;
this is a regression smoke test, not new Siedler gameplay acceptance. All
short-lived test output was in ignored repository `tmp/` and removed after
review. Native SHRES frontend presentation and sprite composition are still
open, so this change alone cannot be treated as a complete SHRES mode.

Next: compose native SHRES sprite samples and expose an optional 1280-pixel
presentation source, then test border geometry and FMODE1/3 on visible guest
screens. Continue Cannon mission objectives and the reported Siedler lower
frame/shield reproduction separately. No user media or normal save was changed;
no commit, push or release was made.

### Native SHRES sprite sampling and Siedler edge diagnosis — 2026-10-13

AGA sprite output can now be sampled at each 35-ns SHRES position rather than
only at every second position used by the 70-ns host preview. The ordinary
sprite path shares the same native coordinate decoder, so there is no
second, drifting latch model. The restored wide-sprite fixture checks
resolutions 1, 2 and 3, all FMODE sprite widths, the last native bit and its
exclusive right edge. At resolution 3 it distinguishes the adjacent
penultimate/final native colors within one old host pixel. This exposes the
data required by a future native compositor; the current frontend still
presents 704 host pixels and does **not** yet display these native sprites.

The four retained user Siedler saves were restored read-only and continued
one million chip cycles each. Before/after the sprite-coordinate refactor,
all **8/8** capture steps match in full CPU text, frame, framebuffer SHA-256,
nonblack-pixel count, exception count and presentation geometry. Their visible
gameplay panel reproduces the cropped right wheel. A separate raw-frame
probe shows the panel becomes black at raw X=800 even though the selected
presentation source extends to X=814. At that line the guest programmed
`DIWSTART=$1871`, `DIWSTOP=$31D1`, `DDFSTRT=$002C`, `DDFSTOP=$00D1`;
the legacy HSTOP comparator from `$D1` ends at raw X=800. Thus a simple
host-side crop or scale adjustment cannot reveal the absent wheel pixels.
This is a diagnosis, not a completed correction or proof that the original
game intended the same cut; compare a native reference with matching display
registers before changing DIW behavior. The [Commodore Amiga Hardware
Reference Manual](https://www.ikod.se/wp-content/uploads/2020/08/Amiga_Hardware_Reference_Manual_3rd_Edition.pdf)
describes the implicit legacy HSTOP high bit.

The complete source suite passes **361/361**, zero skips, and the x64 Release
GameRunner builds with zero warnings/errors. Next: connect native sprite and
playfield samples through the presentation compositor; separately reproduce
Siedler on a reference emulator with the same media/scene and inspect the
DIW edge before altering clipping. Temporary probes and game captures used
ignored repository `tmp/` and were removed after review. Original ADFs,
saves, ROMs and HDFs were not modified; no commit, push or release was made.

### Priority-tested native SHRES composite target — 2026-10-13

The transient SHRES output now keeps a sparse, independently sampled sprite
overlay above the two 35-ns playfield colors. The compositor obtains the raw
second-sample plane mask as well as its palette index, so a dual-playfield
priority decision can be made for each native sample. A caller can copy the
full raw-width native composite without changing the existing 704-pixel
frontend output or the savestate format. Background and border fills clear
stale native sprite pixels; a new field and legacy restore discard them too.
The sprite overlay is allocated only if a visible native sprite is recorded.

An AGA FMODE0 fixture places a resolution-3 sprite on the **second** native
pixel of a red/blue playfield pair. The new composite returns red/green,
while the raw playfield remains red/blue and the 70-ns preview remains red.
The fixture also checks that the following field clears the overlay. Source
tests pass **361/361**, zero skips, and the x64 Release GameRunner builds with
zero warnings/errors. A read-only Siedler checkpoint continuation matched its
previous exact framebuffer SHA-256
`FC259448B7A736256F207D8C68C248BB79FBAAD3B1D4C7656579E90F9B876C16`
at frame 7301, with 146,960 nonblack pixels and zero guest exceptions.
This guards the ordinary path only; no real SHRES application has been visually
accepted. The native target is still not selectable in the frontend.

Next: carry the native composite through the line-adaptive presentation crop
and frontend source selection without shifting mouse coordinates; then test
ECS/AGA SHRES border, sprite and wide-fetch reference images, including
save/restore. Temporary outputs remain in ignored repository `tmp/` and
regenerable build directories because automated cleanup was rejected.
Original media and normal saves were untouched;
no commit, push or release was made.

### Native SHRES monitor-aperture copy — 2026-10-13

The register-selected presentation framebuffer now retains two independent
35-ns samples for each of its 704 horizontal positions when the raw frame
contains SHRES data. Its normal aperture copy uses the same nearest-neighbor
source-column and row map as the 70-ns preview. The line-adaptive copy uses
its existing per-row playfield map and independent sprite-frame map for both
native samples, preserving the sprite border anchor. A subsequent non-SHRES
frame discards the transient native output. The frontend still draws the
70-ns preview; native 1408-sample display selection and mouse mapping remain
open, so this is not a complete SHRES mode.

The new aperture fixture checks both samples at the left and right edges,
an independently sampled sprite, a horizontally/vertically scaled crop, and
stale output removal. The full source suite passes **362/362** with no skips;
the x64 Release GameRunner builds with zero warnings/errors. A live SHRES
application, adaptive sprite-frame golden and frontend visual acceptance
remain outstanding. A read-only Siedler checkpoint continuation still reaches
frame 7301 with the exact expected framebuffer SHA-256
`FC259448B7A736256F207D8C68C248BB79FBAAD3B1D4C7656579E90F9B876C16`.
Next: provide an optional native presentation surface in
SharpPixels with the same window geometry and input coordinates, then run
ECS/AGA reference screens with border sprites and save/restore. No commit,
push or release was made. The rejected recursive cleanup left ignored scratch
and regenerable build outputs pending a safe cleanup path.

### Automatic native SHRES frontend presentation — 2026-10-14

SharpPixels can now paint a transient source with two horizontal samples per
logical pixel. Its target rectangle, menu overlay, crop setting and host-mouse
coordinate scale stay at the established logical viewport; only the paint
source and horizontal crop coordinates double. Sharp, CRT, TV and pixel-art
upscalers process the native source at its actual width. The Amiga window
switches automatically when a presented SHRES frame is available, and returns
to the ordinary source on a non-SHRES frame or remote-client update. The
logical image remains the save-thumbnail and network-frame source.

A Win32 DIB fixture checks all four distinct native samples across a cropped
pair, unchanged target geometry, Scale2x/HQ2x edge pixels, CRT/TV plus HQ4x,
and clean return to the ordinary frame. Source tests pass **363/363** with no
skips; the x64 Release GameRunner builds with zero warnings/errors. A read-only
Siedler checkpoint continuation reaches frame 7301 with its previous exact
framebuffer hash. Real ECS/AGA SHRES screens, border sprites, wide FMODE
fetches and physical mouse interaction still require visual acceptance; do
not mark the full SHRES roadmap complete on the synthetic fixture alone.
Next: capture a native SHRES application/reference frame and compare its
pixels and host mouse mapping at both crop edges, then verify save/restore
and interlace. No commit, push or release was made.

### Native SHRES capture and exact runner guard — 2026-10-14

GameRunner now writes a companion `-native-shres.png` and SHA-256 for a
completed ECS/AGA SHRES presentation frame. Its existing logical screenshot
and hash remain unchanged. A scenario can set `expectedNativeFrameHash` to
assert the native output; an absent native frame fails that guard rather than
silently accepting the 70-ns preview. Preflight rejects malformed native
hashes. The 19 runner-guard tests pass. A read-only Siedler continuation still
reaches frame 7301 with the established logical framebuffer hash, and the
native-hash negative control correctly reports `no native frame` for that
ordinary game scene. The x64 Release GameRunner build has zero warnings and
errors. This provides the capture and exact-pixel gate for a real SHRES app;
it does not supply a real application pass yet. Next: run an ECS/AGA SHRES
application on matching media, inspect native screenshots against a reference
and set the expected native hashes, including after save/restore. No commit,
push or release was made. Ignored test/build outputs remain because recursive
cleanup was rejected by the automatic safety review.

### Chip-cycle SHRES fields and exact native restore — 2026-10-14

The first full-system native presentation fixture now drives actual bitplane
DMA through an ECS A500+ and an AGA A1200 for completed fields. It sets two
chip-RAM planes so the first and second 35-ns samples use different palette
entries, checks the programmed COLOR00 border and searches the selected
aperture for those adjacent colors. The fixture saves machine state, advances
two further fields, restores, advances the same chip cycles and compares the
entire native presented framebuffer byte for byte. Both boards pass. The
source suite is **364/364**, zero skips. This is hardware-path evidence for
DMA, Denise, aperture composition and cold restore, but it is still a
synthetic register program, not a real AmigaOS screen or game. The previously
documented real-application visual gate remains open. Next: load a real
ECS/AGA SuperHires application or screen mode, use GameRunner's native PNG
and hash guard, and compare against an independent reference. No commit,
push or release was made; original media, HDFs and saves were untouched.

### Regenerable output cleanup — 2026-10-14

The earlier safety rejection applied to a broad recursive PowerShell removal.
After a `git clean -ndX` preview and inspection of ignored `tmp/` contents,
`git clean -fdX` removed only the ignored test captures and build `bin/obj`
directories listed in that preview. All targets were inside this repository;
tracked and untracked source changes, ROMs, ADFs, normal saves and installed
HDFs were retained. This supersedes the temporary-output notes above. Future
tests should again use compact scratch output and repeat the preview before
cleanup. No commit, push or release was made.

### Mid-field SuperHires transition continuity — 2026-10-14

A Copper BPLCON0 change can enable SHRES after ordinary pixels have already
been drawn in the same field. The native buffer now initializes from that
partially rendered composite, duplicating each earlier 70-ns pixel into its
two native samples instead of replacing the earlier scene with the current
COLOR00 border. When the Copper returns to lores, later pixels update both
native samples and remove any stale native sprite overlay. The ordinary
logical framebuffer remains the established save/network image.

The ECS and AGA mixed-mode fixture checks earlier distinct pixels, an
unvisited border, the subsequent lores portion and removal of a stale native
sprite. Source tests pass **365/365**, zero skips. The x64 Release GameRunner
builds with zero warnings/errors. The existing A500 Siedler disk-3 scenario
still reaches frame 9059, with gameplay construction visible in its captured
image and no runner error; that is an ordinary-mode regression, not a SHRES
application pass. The lower/right wooden frame in this capture still needs
the separately requested reference comparison and edge correction. Next:
verify a real ECS/AGA mixed-resolution or SHRES screen against an independent
reference, then continue that Siedler edge investigation. No commit, push or
release was made; original media and normal saves were untouched.

### Native SHRES guest boot and hash guard — 2026-10-14

An original, ROM-free 880-KiB diagnostic ADF now programs two bitplanes from
a guest boot block after Kickstart enters Supervisor mode. The same generated
ADF boots on A500+ ECS with the user's Kickstart 2.0 and on A1200 AGA with the
user's A1200 3.1 ROM. Both reach frame 915 without a GameRunner error, and
their native 1408x288 screenshots remain identical across the last two
captures. The persisted scenario checks exact native SHA-256 values:
`DC9450A44A0099E6BF9AEF56EB6ADCA38A494C5C853584462B3A68E8161B6517`
for ECS and
`47CF55780948D25DF2F0A59E7D8AA2AA6B39D7557F3AA77B95221C025044C37D`
for AGA. Pixel inspection finds red/blue adjacent native samples at X=0..3
on AGA and two distinct packed ECS colors at X=0..3; the rest of the simple
diagnostic is the programmed red border. This validates Kickstart boot,
guest register writes, DMA, native capture and the positive runner hash guard
as one path. It is not a real SHRES application, a general graphics pass or
an independent Amiga hardware comparison.

The generated ADF and captures were held only in ignored scratch output.
Next: compare a real ECS/AGA SHRES screen and mixed-resolution transitions
against a reference, then continue interlace weaving and the Siedler edge
investigation. No original ROM, ADF, HDF or normal save was changed; no commit,
push or release was made.

### Native SHRES cold restore and reset continuation — 2026-10-14

The guest diagnostic scenario now checks the exact native image after a cold
restore and after a separate cold reset followed by another disk boot. Both
the A500+ ECS and A1200 AGA cases complete with their earlier native hashes,
and the reset returns to the same guest halt loop after booting the generated
ADF again. GameRunner verifies complete CPU and bus serialization and the
ordinary presented image at the restore point before advancing to the next
native field. Thus the native plane is regenerated after restore instead of
being silently accepted from stale host memory. This remains a small guest
register program, not an AmigaOS SHRES application or proof of interlace
weaving. Next: test a real high-resolution application and add field-weaving
acceptance with both LOF polarities. No commit, push or release was made.

The same generated disk was also booted on A600, A2000 ECS, A3000 and
A4000/030 with matching user ROMs. Each reached the guest display after cold
restore and reset without a runner error. Their final native hashes match the
ECS or AGA value above, respectively; the six-case persistent manifest now
guards the first stable native field on every supported ECS/AGA board. OCS
A500/A2000 are intentionally excluded because they do not implement SHRES.
This broadens the guest diagnostic only; application-level graphics and
model compatibility remain separate open gates.

### PAL interlace field-pair staging — 2026-10-14

The machine now retains the last completed short and long PAL fields in a
transient 704x576 buffer when BPLCON0 LACE is active. The fields occupy
separate even/odd scanlines, and the buffer is reported as complete only
after both polarities have arrived. Reset, restore and non-LACE presentation
invalidate the old pair so stale rows cannot be mistaken for a new frame.
The extra buffer is allocated lazily so ordinary non-LACE games do not pay
for another full-screen pixel allocation.
The completed-field decision uses Agnus's actual 312/313-line boundary, not
the host frame count. An end-to-end machine test advances one short and one
long field and checks both scanline sources, plus reset invalidation; a
separate small-buffer test checks row replacement and invalidation. The full
source suite passes **367/367**, zero failures or skips.

This staging buffer is not yet selected by the frontend, exported by the
GameRunner, or paired with native SHRES samples; it therefore does not close
the full interlace presentation gate. Next: add a guest LACE diagnostic with
different field content, expose the woven 576-line image through host and
runner output, then verify scrolling and mode transitions against an
independent hardware/reference capture. No commit, push or release was made.

An ECS A500+ cold-boot regression of the generated SHRES diagnostic completed
through its exact native image hash, cold restore and cold reset (`DONE`,
frame 844, PC `20A8CC`). The x64 Release GameRunner build had zero warnings
and errors. This is a non-LACE regression only; it does not establish that a
guest interlace screen is displayed correctly.

### Guest PAL-LACE capture and exact runner guard — 2026-10-14

The original diagnostic ADF generator can now boot with `--lace`: guest code
polls VPOSR/LOF and alternates COLOR00 between blue and red on successive
fields. The GameRunner exports a separate woven 704x576 PNG, records its
SHA-256, and accepts an `expectedInterlacedFrameHash` guard. The persistent
two-board scenario reached frame 916 on both A500+ ECS/Kickstart 2.0 and
A1200 AGA/Kickstart 3.1 with no runner failure. Its exact woven hashes are
`21BE50D35191A20A2A85931047D845CD4A8E94EB80454D774E43E324394E00A6`
and
`3B1240D04A45F7F4680D08A38EB6A5287900E8DF801231EB57406A455043EC52`,
respectively. Pixel inspection at x=100 shows blue/red/blue/red in rows
0..3 on both boards. The actual booted code, LOF timing, two field captures,
weaver, PNG and hash guard are thereby exercised together. The x64 Release
runner builds with zero warnings/errors; 20 Python runner guards and 367 C#
source tests pass with no failures or skips.

This diagnostic still does not establish correctness against real hardware
or show a full application. The main frontend still selects the 288-line
field, so the full interlace presentation gate remains open. Next: adapt
SharpPixels to variable native source height, select the completed woven
image in the frontend, check mouse coordinates and 288-line fallback, then
compare a real LACE application with an independent reference. No commit,
push or release was made; all diagnostic ADFs/PNGs were scratch outputs.

### Native interlace presentation through the frontend — 2026-10-14

The earlier frontend limitation is addressed for completed field pairs.
SharpPixels accepts independent native X/Y sample factors and scales the
source crop in both axes while retaining the same logical target and mouse
coordinates. AmigaWindow selects the 1408x576 native SHRES pair for combined
SHRES+LACE, or the 704x576 logical pair for ordinary LACE. Non-LACE images
continue through their existing 704x288 or 1408x288 path. Reset, restore and
mode exit invalidate the paired image before another native presentation.
The 2D paint fixture verifies alternating rows through the real DIB path,
unchanged target geometry and fallback to the ordinary image.

The runner also exports the native woven image and checks a SHA-256 guard.
The A500+ ECS and A1200 AGA guest boot cases both reached frame 916 with
their earlier logical guards plus native 1408x576 hashes
`F86EBA276448A494DABBC9039F7ABAFF71AFB345ADB1390273A386C04A031566`
and
`6FEE979A4BD93A5044644BFC7D7063CAA9C542E0C11DC83F3126154EBF98B876`.
At x=100 the native captures alternate blue/red on four consecutive rows.
The full source suite passes **368/368**; **21** runner guard tests pass; the
x64 Release runner builds with no warnings/errors.

Remaining interlace gates: test a real Workbench or game LACE screen with
scrolling, interlace/mixed-resolution Copper transitions and mode exit,
compare against an independent reference, and verify live host input over
that application. The diagnostic boot block alone does not prove those
behaviors. No commit, push or release was made; original media, HDFs and
normal saves were untouched.

### Interlace cold-state and mode-exit continuation — 2026-10-14

The persistent ECS/A500+ and AGA/A1200 LACE guest cases now assert both
704x576 and native 1408x576 image hashes after cold restore and again after
a separate cold reset and re-boot. Both cases completed every guard without
a runner error, returning to frame 916 after reset. The core test also
disables LACE after a complete pair, verifies the pair becomes unavailable,
and requires both fresh polarities after re-enabling LACE; a reset invalidates
it as well. The full source suite remains **368/368**, zero failures/skips;
the x64 Release runner builds with zero warnings/errors. These results close
the specific stale-field and cold-continuation regression, while real
Workbench/application LACE behavior and independent visual comparison remain
open. Next: exercise an actual LACE mode from the installed AmigaOS image,
including mouse input, window movement, mode switching and screenshots;
then compare output to a reference before marking interlace complete. All
scratch ADFs, captures and regenerable build output were removed after
inspection. No commit, push or release was made.

### Real AmigaOS 3.1.4 PAL-LACE parity correction — 2026-10-14

A fresh read-only private copy of the user's installed A4000 DH0 image was
booted with its matching 46.143 ROM. Workbench's actual `SYS:Prefs/ScreenMode`
opened from Execute Command, selected **PAL:High Res Laced**, and switched
the live desktop. The first woven output exposed a real defect that the
synthetic red/blue diagnostic could not: Workbench title and icon text had
interleaved/garbled rows. Reversing the long/short field row placement made
the same captured pixels form a crisp Workbench title, both desktop icons
and the mouse pointer. The machine now places the long field on even output
rows and the short field on odd rows. This replaces the opposite mapping in
the earlier staging section above.

The corrected runner reproduced the actual OS screen. Four successive field
captures alternated between two stable single-field hashes while the woven
704x576 hash remained stable. Mouse movement visibly changed the pointer
location in the woven image, and Right Amiga+E opened a readable Execute
Command dialog in the laced screen. A separate cold reset returned to the
ordinary 704x288 Workbench desktop with exactly the original pre-LACE frame
hash `A796899ACCB8AF5D3F10E01914FB8E2A8F2E64984C8CC52D060E6A673FA15175`;
no stale woven image remained. The persisted guarded scenario is
`Tools/amigaos314-a4000-lace-scenario.json`, using only a private HDF copy
and a user-owned ROM. Original and private HDF SHA-256 both remained
`09957EC42DE994FCFA5E81E48C8A25545DF0F7A5805C30DC0331E23883253ADA`.

The ECS/AGA original guest LACE hashes were updated for the corrected row
ordering. Both boot, cold-restore and cold-reset cases pass their logical
and native interlace guards. The full source suite passes **368/368** and
the runner suite **21/21**, no failures/skips; x64 Release builds with zero
warnings/errors. This is real OS and input evidence for one A4000 high-res
laced mode. Other PAL/NTSC LACE modes, scrolling, A500+/A1200 application
screens and independent hardware comparison remain open. No commit, push or
release was made; all private HDFs and scratch captures were removed after
review.

### Real A4000 Workbench PAL Super-High Res Laced — 2026-10-14

The installed AmigaOS 3.1.4 A4000 HDF was again tested from a fresh read-only
private copy with the matching Kickstart 46.143 ROM. Workbench's actual
ScreenMode Preferences selected **PAL:Super-High Res Laced**. The native
1408x576 woven image shows the full desktop, readable title and icon text,
and the mouse pointer. Its stable SHA-256 is
`E238CAA22184419898ECCB7B047C98EB53E05DBBE7CA46236E791FE6B3E1FE8F`;
the corresponding 704x576 logical woven hash is
`6224BD4BD98066A4BEB9EBBB3BBE194272ABD58F5E86FE2604430230ED470B9B`.
Moving the mouse changed both images, and Right Amiga+E opened a readable
Execute Command dialog in the native woven image. A cold reset cleared the
interlace pair and returned to the original ordinary desktop hash
`A796899ACCB8AF5D3F10E01914FB8E2A8F2E64984C8CC52D060E6A673FA15175`.
The initial 51-action guarded replay in
`Tools/amigaos314-a4000-shres-lace-scenario.json` completed without a runner
failure. The source HDF and private copy had the same SHA-256
`09957EC42DE994FCFA5E81E48C8A25545DF0F7A5805C30DC0331E23883253ADA`.

This adds a real OS/application check of the native SHRES+LACE path and host
input, but it is not an independent hardware comparison. A PAL Low Res Laced
mode was also reached experimentally; its woven Workbench text and icons were
readable, but that exploratory run lacks persistent hash and input guards.
Next: validate scrolling and Copper/mixed-resolution transitions in a real
application, then compare A500+/A1200 and NTSC LACE output against independent
reference captures. No commit, push or release was made.

### Super-High Res Laced Workbench window interaction and cold restore — 2026-10-14

The same private A4000 AmigaOS 3.1.4 HDF was used to continue the real
Super-High Res Laced screen beyond the Execute Command dialog. The guest
accepted a mouse click on Cancel, a double-click on the System icon, and a
mouse-button-held drag of the System drawer window. The native 1408x576
woven capture showed the drawer and its readable title/icons both before and
after the move; its post-drag hash is
`AB465B3219A63038C5E11EA61740076E619863892DEAB5A6410CD2F91A61673E`.
A cold state round-trip restored the exact CPU, memory, beam and presented
framebuffer state; after two fresh fields the same native and logical woven
window hashes returned. A separate cold reset returned to the original
704x288 desktop hash. The persistent scenario now contains 70 guarded actions
and passed in full. The x64 Release runner build reported zero warnings and
errors. No source HDF or normal save was written; scratch files were removed.

Window dragging exercises real application graphics and mouse-button hold,
but it does not yet prove scrolling, mixed-resolution Copper transitions,
all Amiga models, or independent hardware parity. Next concrete test: scroll
a populated laced Workbench drawer and check successive native field hashes
and image content, then test mode switches with a hardware/reference capture.
No commit, push or release was made.

### Super-High Res Laced Workbench scrolling — 2026-10-14

The A4000/AmigaOS 3.1.4 private read-only HDF continuation opened the real
System:Prefs drawer in PAL Super-High Res Laced. Three clicks on its vertical
scroll arrow moved the icon rows upward in successive native 1408x576 woven
captures. The fully scrolled drawer remained readable; the native hashes
progressed from `C21F5977A8D611D622C22A22B07CFBE544A70A4D146E63266C0DEDA27396BFB5`
before scrolling through `1759BA4CCF97359363767EE970482B46BDA3A2B69F9546E733E0AF5E0FE44A64`
and `0C6215FDFC4166D66036F647AD2A80A81A5788EB355E8FC7E79E531C2BE9B140`
to `0852E56B4F194E5A4C0B5CDB6EE4BF42F1759394E639EABAFCFD69F7A6F9B2FF`.
The corresponding logical 704x576 hashes are guarded as well. A cold
state round-trip recovered the final scrolled image exactly after two fresh
fields; another cold reset restored the original non-laced desktop hash.
The expanded 87-action persistent scenario passed every guard. The x64
Release runner build reported zero warnings/errors, and the original HDF
remained untouched. Scratch images and the private HDF copy were removed.

This closes the specific real Workbench scrolling test on A4000 SHRES+LACE.
Still open: Copper mixed-resolution transitions, NTSC and other LACE modes,
matching A500+/A1200 application tests, and independent hardware comparison.
Next concrete step: test a guest program that changes horizontal resolution
and interlace within a frame or during a mode switch, and inspect the native
capture against a reference. No commit, push or release was made.

### Copper lores-to-SHRES split inside PAL LACE — 2026-10-14

An original 386-byte guest boot block now builds with `--mixed-lace` and
programs a restarting Copper list. Each field reloads two patterned bitplane
pointers, displays two-plane lores above raster `$80`, changes BPLCON0 to
SuperHires below it, and leaves LACE active. The first diagnostic attempts
were deliberately rejected: a one-line DIWSTOP left only the top scanline
visible, and a data-fill DBRA target was off by one word. Both fixture bugs
were corrected before acceptance; they were not counted as emulator passes.

The final native 1408x576 capture visibly has four-sample-wide red/blue
runs in the upper playfield and alternating single-sample colors in the
lower one, with the Copper split at the same line on ECS A500+ and AGA
A1200. Pixel samples at x=200..207 on row 100 have the 4+4 pattern;
row 220 alternates every sample. Both boards cold-booted into the guest
loop, cold-restored the complete CPU/bus/beam/frame state, and cold-reset
back to the same logical/native woven hashes. The exact native hashes are
`DF912CC1B9664040960683AA580EB401F740FDE8385C8CE6A35B1D8A6261C255`
for ECS and `B44D15E378AF14AE5E542FA140081D402ADB4034C7026848B42CC011934D9C5B`
for AGA. The persistent two-board scenario is
`Tools/mixed-lace-guest-diagnostic-scenarios.json`; all its guards pass.
The x64 Release runner build reported zero warnings/errors. All media and
captures are generated in ignored scratch space and are removed after review.

This establishes the guest-to-native-presentation path for one Copper
resolution split within a PAL LACE field. Exact colors and timing still
need independent hardware/reference comparison; NTSC, other display-window
programming, and interlace changes mid-frame remain open. Next: compare the
split raster, colors and 35 ns pixel positions against a verified reference,
then test mode changes that enable or disable LACE at a frame boundary.
No commit, push or release was made.

### Real A1200 AmigaOS 3.1.4 Super-High Res Laced interaction — 2026-10-14

The installed AmigaOS 3.1.4 HDF was copied to a fresh private read-only test
image. A native A1200 with the existing Kickstart 3.1 ROM showed an empty
Workbench, so that combination was rejected as an OS test. The matching
A1200 Kickstart 3.1.4 version 46.143 (512 KiB, SHA-1
`6355A9ED5DC840422F9B73308A91BE0D0BB506BD`) was placed only in the
user's local ROM directory. With that ROM, the same HDF copy cold-booted
to the populated Workbench desktop.

The guest Execute Command path switched Workbench to PAL 1408x576
Super-High Res Laced. The native woven image was readable, and mouse input
opened and dragged the System drawer, then opened its Prefs drawer. The
full 77-action `Tools/amigaos314-a1200-shres-lace-scenario.json` passed
exact logical and native frame guards at those points, through cold restores
of both the moved System and open Prefs windows, and a cold reset back to
the original desktop. The open Prefs native hash was
`D3106570680B8DB843913B4C403FF47F1742731E1BA75006FF50658E30DBE51E`;
the restored image matched exactly. The original HDF and private copy had
the same SHA-256 before scratch cleanup:
`09957EC42DE994FCFA5E81E48C8A25545DF0F7A5805C30DC0331E23883253ADA`.

The initial Prefs window shows every icon; this first interaction run did
not test scrolling. Its follow-up scroll continuation is recorded below.
Independent hardware parity, other LACE modes and full A1200 application
compatibility remain open.
No commit, push or release was made.

### A1200 Super-High Res Laced Workbench scrolling — 2026-10-14

The same real A1200/AmigaOS 3.1.4 PAL SHRES+LACE path continued inside the
Prefs drawer on a new private, read-only HDF copy. A mouse-button-held drag
on its right-hand scrollbar moved the icon content upward. Two further
clicks on the bottom scroll arrow moved the icon rows upward again. The
native 1408x576 captures show the Font row progressively clipped at the
top while the lower rows move into its place; this is actual drawer content
scrolling, not only a changed pointer or window focus. The native hashes
progressed from `73D8C24FC296E3437C7B376CA483D25A2DB3DA39FE49A04A73EA96330DCD624B`
after the drag to `2044BC52973F3A6E46A74BD3B1BD5EA5427F50EC328398D8DA440639A80DB803`
and `5EBFE9071F2AEE6F21A1B8ED0FED3939FCF1028DAE0DD552DD58419D3C1D0D1C`
after the two clicks. A cold restore exactly reproduced the last native
and logical images; a separate cold reset restored the original non-laced
desktop. The expanded 89-action persistent A1200 scenario passed every
exact image guard. The installed HDF was never mounted for writes.

This closes the specific A1200 Prefs scrolling test. Other A1200
applications, alternate LACE modes, NTSC and hardware-reference parity are
still open. Next: test a guest-controlled transition into and out of LACE
at a frame boundary while a real application is active, then compare the
native image and beam timing with a verified reference. No commit, push or
release was made.

### A1200 Workbench LACE-to-non-LACE transition correction — 2026-10-14

The earlier visual-failure report above was a **misdiagnosis**: the inspected
1408x288 `-native-shres.png` contains one field while LACE is active. The
paired `-native-interlaced.png` is the complete 1408x576 picture and renders
the ScreenMode warning and parent window correctly. The former probe's
confirmation click landed on the edge of the small Use button, leaving the
requester open. Its failure guard rejected a normal single-field image and
did not establish a rendering defect. No display-code fix was needed.

The runner now records stored BPLCON0, DIW, DDF, first two bitplane
pointers and modulos at captured steps. A new opt-in `TraceDisplayControl`
trace records CPU/Copper writes to display control registers with beam and
frame position. In a real A1200/AmigaOS 3.1.4 probe, 4,978 traced writes
included 524 BPLCON0 Copper writes over 262 fields: `$0347` at beam 9,0
for blanking and `$2346` at beam 181,42 for visible drawing. Both values
have LACE set. The earlier one-off `$0347` snapshot was therefore a normal
blanking-phase sample, not evidence of a failed mode transition.

Moving the confirmation pointer to `(-145,-105)` and clicking Use completes
the transition. The resulting real Workbench is a readable 1408x288 native
PAL Super-High Res non-LACE image; BPLCON0 is `$2342` (LACE clear), and no
interlaced framebuffer is emitted. The same logical and native hashes remain
after another 100 million chip cycles. The persistent
`Tools/amigaos314-a1200-lace-exit-probe.json` now guards both final frames
and validates successfully. A further cold reset/restore preserves the exact
logical framebuffer and serialized CPU/bus/beam state; after one million more
chip cycles the native non-LACE image matches its pre-restore hash. The runner
completed at frame 5447 without an unexpected CPU fault. The private HDF copy
was read-only; the original installed HDF was untouched. Cleanup of the 268 MB scratch copy at
`tmp/os-a1200-test.hdf` was rejected by the command policy even for its
verified exact path; it remains for later cleanup. This verifies this ScreenMode route only, not
all mode changes or applications. Next: exercise other ECS/AGA mode switches,
restores and applications, and review their complete field pairs where LACE
is enabled. No commit, push or release was made.

### A4000/030 Workbench LACE exit and restore — 2026-10-14

The same real AmigaOS 3.1.4 ScreenMode route was rerun with the matching
A4000/030 Kickstart ROM, 8 MiB motherboard Fast RAM and a read-only private
copy of the installed HDF. The PAL Super-High Res Laced Workbench produced
the complete 1408x576 native field pair; the requested non-LACE mode then
produced a readable 1408x288 native image, visually reviewed. BPLCON0 was
`$2342` after the switch, with no native interlaced frame. Its logical hash
`C3BD1B2A43FFC4C7150FF8518D4B76AE80B29CDC0C15F4E5D473C230641B2166`
and native hash
`4FAD1E1BCCB81D88F07690EFC2ABAB827D446816D04A7E2C9489948055D76C4D`
persisted for another 100 million chip cycles and through a cold reset/restore
plus one million cycles. The runner checked exact serialized CPU, bus, beam and
logical framebuffer state at restore, and completed at frame 5447 without an
unexpected CPU fault. `Tools/amigaos314-a4000-lace-exit-probe.json` records
reviewed hashes for LACE entry, non-LACE exit and restore; its independent
guarded replay passed. All 21 runner-guard tests also passed. The installed
HDF and ROMs were untouched. Other modes, applications and hardware-reference
parity remain open; next test a transition while a real application is active.
No commit, push or release was made.

### Native interlace transition guards — 2026-10-15

The GameRunner now accepts a nullable `expectNativeInterlacedFrame` action
guard. It rejects a contradictory false presence request combined with an
expected woven-frame hash. Both A1200 and A4000/030 real AmigaOS 3.1.4
ScreenMode scenarios require the complete native field pair before switching
out of LACE and require its absence at the resulting non-LACE screen, after
100 million further chip cycles and after cold restore. Both complete guarded
replays passed at frame 5447. The x64 runner builds without warnings or
errors; all 22 runner preflight tests pass. This protects against stale LACE
presentation across these two transitions, but does not cover arbitrary
mid-field switches, programmable modes or other applications. Next: exercise
those transitions with a guest program that changes modes during an active
application frame. No commit, push or release was made.

### Repeated PAL field polarity — 2026-10-15

The host field buffer previously kept `HasBothFields` true if the guest
completed the same LOF polarity twice in a row. In that case it could weave
the new field with an older opposite field and present a misleading complete
image. It now invalidates the opposite half on a repeated polarity and waits
for a fresh complementary field. An independent presentation fixture covers
short/long/short/short/long sequencing, preserving ordinary alternating
updates while withholding the duplicate-polarity pair. All 368 source tests
pass; the x64 Release build has zero warnings/errors. The real A1200 and
A4000/030 AmigaOS 3.1.4 SHRES LACE entry, non-LACE exit and cold-restore
scenarios both retain their reviewed hashes and complete at frame 5447.
The integrated A500 test now additionally forces a second short field with
VPOSW after an already complete pair. The woven frame becomes unavailable
until a fresh long field arrives; the alternating case remains visible.
All 368 source tests still pass. Non-PAL timing and independent hardware
reference parity remain open. No commit, push or release was made.

### ECS LACE boot coverage on A600, A2000 and A3000 — 2026-10-15

The original 208-byte boot-block diagnostic was regenerated as a fresh
private ADF and booted with matching local Kickstart ROMs on A600, A2000 ECS
and A3000/030. Each model reached the alternating long/short red-blue PAL
field pattern. The complete 704x576 logical pair has SHA-256
`A89F4CC2DBDE10C7F3B092C17CF42799D0FD65C2C7C7C89B7626AEF0EE783FFC`;
the 1408x576 native pair has
`85E0B0D6053CF666D37E0CFD4A203FC8AD2C676508F6640AF763597FF67DA670`.
The native A600 image was visually reviewed. Cold restore and cold reset
returned both reviewed hashes on all three models, and independent guarded
reruns passed at frame 916 (restore at frame 930). These cases are now in
`Tools/lace-guest-diagnostic-scenarios.json` alongside A500+ and A1200.
This proves the synthetic boot/display path, not real game or application
compatibility; next exercise PAL SHRES LACE with actual Workbench interaction
on A600/A3000 and an independent reference capture. No commit, push or
release was made.

### Real AmigaOS 3.1.4 mode transition on A600 — 2026-10-15

The user's purchased `ROMs/unsplit_unswapped/kick.a500.46.143` Kickstart
image (524288 bytes, SHA-256
`CA52081BE6809A9EC344908C738109D79DBF341E20A7169A29B5A34F150C08B3`)
was copied to the private local ROM folder as `KICK314-A500.ROM`. The
`to_burn` variant from the same archive was rejected by the ROM resolver;
the original files were not changed. This A500-labelled family ROM is not
claimed to be a board-specific A600 ROM.

With that private ROM, A600, 2 MiB Chip RAM and 4 MiB Fast RAM booted the
installed AmigaOS 3.1.4 HDF read-only to a visible Workbench after 100 million
chip cycles. The real ScreenMode workflow switched to PAL Super-High Res
Laced: the reviewed native 1408x576 field pair had SHA-256
`CF36B8EA6350A1B4087CBDD6A9987ED371640770B4CB5AC2161B80E4B2C09D40`.
It then switched to a readable 1408x288 non-LACE Workbench with native hash
`B5F75466C55D0845594FCF0451BFE27D80FB1C13BAD39363369541445810500C`.
The non-LACE image and absence of a woven frame persisted for another
100 million chip cycles and through a cold reset/restore. The independent
guarded replay of `Tools/amigaos314-a600-lace-exit-probe.json` completed at
frame 5448 with no CPU exceptions and exact logical/native image guards.
This verifies one real OS display transition on A600, not arbitrary
applications, all screen modes or complete A600 compatibility. Next exercise
a live application during the transition and compare against an independent
hardware/reference capture. No commit, push or release was made.

### A600 laced Workbench interaction and A3000 boot control — 2026-10-15

The A600/3.1.4 real Workbench case now continues from the reviewed PAL
Super-High Res Laced mode into an actual System drawer interaction. It opens
the drawer, drags its window, cold-restores that state and checks the full
1408x576 native field pair throughout. The moved window was visually
reviewed; its native interlaced SHA-256 is
`21B4B262FF1DFAF532FEF30A07C473608FB59F2452D45830EA51F66C2D2A5CD3`,
unchanged after restore. The independent guarded replay of
`Tools/amigaos314-a600-laced-system-window.json` completed at frame 3272,
with zero CPU exceptions. It uses the same private ROM and read-only test HDF
as the preceding A600 case.

An A3000 control using its local Kickstart 3.1 ROM and that A4000-installed
test HDF reached only an empty Workbench screen with a title bar at both 100
and 200 million chip cycles. Trying the A4000 3.1.4 ROM on A3000 produced a
Kickstart boot screen. Neither is counted as an OS boot pass or evidence of
an A3000 display defect; this media/ROM combination is not an established
A3000 installation. Next test real A3000 mode changes with a matching
A3000-installed HDF and board ROM, then exercise an active application across
the transition. No commit, push or release was made.

### Real AmigaOS 3.1.4 mode transition on A2000 ECS — 2026-10-15

The A2000 ECS profile also booted the user's AmigaOS 3.1.4 installation from
the existing private read-only test HDF with the purchased A500/A600/A2000
Kickstart family ROM. A visible Workbench reached frame 1407. The real
ScreenMode dialog entered PAL Super-High Res Laced (BPLCON0 `$2346`), whose
complete 1408x576 native field pair has reviewed SHA-256
`7EEF774A59AEB40537AF5863EBB18179B1DA0A7DE816E901431B22BDE729FCCD`.
It then returned to a readable non-LACE Workbench (BPLCON0 `$2342` at the
result, native hash
`3D75993FE11DA5A7EDA6D7E1977497A1A93DB6589F807AD20509C39DD8ED2C30`).
The image and absence of a woven frame persisted over another 100 million
chip cycles and a cold reset/restore. The independent guarded replay of
`Tools/amigaos314-a2000-ecs-lace-exit-probe.json` completed at frame 5448
with no CPU exceptions; both images were visually reviewed. This is one real
OS display workflow on an ECS A2000 profile, not complete A2000 application,
SCSI-controller or peripheral compatibility. Next test an active application
during the mode transition and compare with a hardware/reference capture.
No commit, push or release was made.

### A2000 ECS laced Workbench window interaction — 2026-10-15

The A2000 ECS/AmigaOS 3.1.4 Workbench case now continues from its real PAL
Super-High Res Laced screen into the System drawer. The guest opens the
drawer, drags the window and restores that state after a cold reset. The
1408x576 native interlaced capture of the moved window was visually reviewed;
its SHA-256 is
`4DBB9B1BBF1BE7A6D5C37700ECC58156092C5B87EFE7A9326A1DA61BE382316A`
before and after restore. Independent replay of
`Tools/amigaos314-a2000-ecs-laced-system-window.json` passed exact image
and woven-frame presence guards at frame 3272 with no CPU exceptions.
This extends the A2000 evidence to real mouse-driven Workbench interaction;
application behavior during a screen-mode switch and independent hardware
parity remain open. No commit, push or release was made.

### A500 AmigaOS 3.1.4 Workbench drawer and restore — 2026-10-15

The A500 profile booted the user's purchased Kickstart 3.1.4 family ROM and
the existing private read-only AmigaOS 3.1.4 test HDF to a visible Workbench
at frame 1407. A mouse double-click opened the populated System drawer;
the capture was visually reviewed and has logical SHA-256
`6E22C0A7C20241FC0B9697F5FE30FF4F3B56C449AFFE1105F2C4D5DEC2AE154F`.
A cold reset/restore retained that exact image at frame 1591, with zero CPU
exceptions and no HDF writes. The independent guarded replay in
`Tools/amigaos314-a500-system-window.json` passed. This configuration uses
the emulator's synthetic hard-disk expansion, so it does not establish a
stock A500 hard-disk path or broader application compatibility.

A separate A2000 ECS attempt to switch out of PAL SHRES LACE while the
System drawer was open reached the ScreenMode confirmation dialog, but the
automated confirmation never completed; it remained in LACE. That sequence
is not counted as a mode-transition pass. Next make its pointer/dialog
interaction robust and check the resulting non-LACE screen, or reproduce the
same window-open transition manually. No commit, push or release was made.

### Real AmigaOS 3.1.4 ECS mode transition on A500+ — 2026-10-15

The A500+ profile booted the same private, read-only AmigaOS 3.1.4 test HDF
with the user's purchased A500-family Kickstart 3.1.4 ROM. In the real
Workbench ScreenMode workflow it entered PAL Super-High Res Laced at frame
2527, with a complete visually reviewed 1408x576 native field pair (SHA-256
`7EEF774A59AEB40537AF5863EBB18179B1DA0A7DE816E901431B22BDE729FCCD`),
then returned to a readable non-LACE image at frame 4026 (native SHA-256
`3D75993FE11DA5A7EDA6D7E1977497A1A93DB6589F807AD20509C39DD8ED2C30`).
That image and absence of a woven frame persisted after 100 million chip
cycles and cold restore. The independent guarded replay of
`Tools/amigaos314-a500plus-lace-exit-probe.json` passed at frame 5448.

Another real Workbench run opened and dragged the System drawer while laced,
then cold-restored the window. The independent guarded replay of
`Tools/amigaos314-a500plus-laced-system-window.json` passed at frame 3272
with zero CPU exceptions. The A500+ screenshots matched the A2000 ECS
profile's reviewed hashes in these two paths; this is useful ECS regression
coverage, not independent hardware parity. Broader A500+ applications,
peripherals and mode timing remain open. No commit, push or release was made.

### Compact guarded replays — 2026-10-15

GameRunner now supports `--no-images` for subsequent runs of scenes whose
screenshots have already been visually reviewed. It still computes and checks
logical, native SuperHires and woven-field hashes, presence guards and all
normal CPU/input diagnostics; the log records null image paths. On the real
A500 AmigaOS 3.1.4 System-drawer scenario, an independent guarded replay
without images passed at frame 1591 and wrote only `steps.jsonl` (22,565
bytes) and `result.json` (254 bytes), with no PNG or final checkpoint. The
real A500+ laced System-window replay likewise passed its logical/native
interlaced guards at frame 3272 with zero PNGs and zero CPU exceptions. The
x64 runner builds with zero warnings/errors. This reduces diagnostic growth
but is not a substitute for image review on a new visual state. Next use the
flag for later already-reviewed regression sweeps, retaining selected PNGs
only when investigating a failure. No commit, push or release was made.

### A1200 AGA laced Workbench window interaction — 2026-10-15

The real A1200/AmigaOS 3.1.4 Workbench reached PAL Super-High Res Laced,
opened the populated System drawer with a mouse double-click, dragged its
window and cold-restored that state. The 1408x576 native field-pair image was
visually reviewed; the moved window's native interlaced SHA-256 is
`D83F7F664EFC24EDED81DE51C11FDDAF75BC699B00D3E98810E9C13E61DB889A`,
identical after restore. An independent replay of
`Tools/amigaos314-a1200-laced-system-window.json` passed exact logical and
native interlaced image guards at frame 3272, with zero CPU exceptions, zero
HDF writes and no generated images. This verifies one AGA mouse-driven OS
window workflow, not arbitrary applications or complete A1200 compatibility.
Next exercise an active application across the laced/non-laced transition and
compare its output with an independent hardware or reference capture. No
commit, push or release was made. The roughly 1.5 MiB of ignored temporary
probe output remains under `tmp/a1200-laced-window-20261015-1007` and
`tmp/a1200-laced-window-guard-20261015-1007`: automatic approval review
blocked the requested cleanup, so no alternate deletion was attempted.

### A1200 AGA mode exit with an open Workbench window — 2026-10-15

The A1200/AmigaOS 3.1.4 case now switches from PAL Super-High Res Laced
to non-laced PAL Super-High Res while its populated, dragged System drawer
remains open. The ScreenMode warning was accepted with a visible click; the
resulting 1408x288 native image was reviewed and retains the full drawer.
Its SHA-256 is
`009A6D35234FEB2F60D249C447FFB6DAB517972FBF9B61F7882DF7914EB7330A`,
unchanged after 100 million additional chip cycles and cold restore.
BPLCON0 moved from `$2346` to `$2342`; no native woven field remained.
The independent guarded replay in
`Tools/amigaos314-a1200-laced-window-mode-exit.json` passed exact logical,
native and field-presence guards at frame 6062 with zero CPU exceptions,
zero HDF writes and zero generated PNGs. This proves this one open-window
mode transition, not another application, arbitrary transitions or hardware
parity. Next run a separate real application through the transition and
compare a captured frame with an independent reference. No commit, push or
release was made. About 8.5 MiB of new ignored visual/guard output remains
under the `tmp/a1200-*-20261015-1102` probe directories; the previous
automatic cleanup rejection remains in force, so no alternate deletion was
attempted.

### A1200 AGA laced Clock application — 2026-10-15

With the real AmigaOS 3.1.4 System drawer still open in PAL Super-High Res
Laced, Workbench Execute Command launched `SYS:Utilities/Clock`. Clearing
the requester's retained previous command before typing was necessary; the
first exploratory command appended to it and was correctly rejected by the
guest. The command's Output Window was then closed, revealing a readable
analog Clock beside the populated System drawer. Two reviewed 1408x576
native field-pair captures, 100 million chip cycles apart, differ within
the Clock face/hand area (full-image hashes
`16A1A73A7D15C6CB5E7A827913C44018D9948FB8E2593DD8C3DF8221C107A976`
and `44A548DB03AA3A63F6B19C0A4D932367DB7C81C472D7AA295498A9FEB746E843`);
a control region outside the Clock was pixel-identical. The independent
`Tools/amigaos314-a1200-laced-clock.json` replay passed both logical/native
image guards at frame 5826 with zero CPU exceptions, zero HDF writes and
zero generated PNGs. This establishes one separately running OS application
in LACE, not a mode change while it remains open. An exploratory ScreenMode
continuation with Clock open stayed in LACE; its pointer/dialog actions were
not yet recalibrated and it is not counted as a display failure. Next open
ScreenMode over Clock and verify a visible, confirmed non-laced transition
while the Clock keeps running. No commit, push or release was made. Roughly
5.6 MiB of new ignored `tmp/a1200-*-20261015-1202` probe output remains;
the earlier automatic cleanup rejection was respected.

### Targeted visual capture and Clock mode-switch diagnosis — 2026-10-15

GameRunner now accepts `--image-label=LABEL` to write PNGs for one exact
action label while retaining every step's hashes and guards. The option is
mutually exclusive with `--no-images`, rejects empty/invalid labels and
duplicates, and leaves unselected image paths null. The x64 Release build and
all 22 Runner preflight tests pass. This makes a first visual inspection of a
long OS/game scenario possible without hundreds of PNGs or a final checkpoint.

Targeted replay of the A1200 3.1.4 laced Clock continuation identified why
the first ScreenMode attempt was invalid: Workbench's second Execute Command
requester retained `Run SYS:Utilities/Clock` with the cursor after `Run`.
Thirty Backspaces erased only that prefix. The subsequent command therefore
ran as `SYS:Prefs/ScreenMode:SYS:Utilities/Clock` and opened an error Output
Window; the apparent display failure was a guest command-entry error.
Thirty Delete keys after the Backspaces cleared the suffix, and the corrected
command opened the real ScreenMode Preferences window. The non-laced row was
selected and its Use action opened the real warning requester. Two targeted
replays then clicked near the visible Use button, including a longer
mouse-down; both left that warning visible with identical before/after
framebuffer hashes. This was not a demonstrated graphics mode failure or a
passed transition. The follow-up below establishes the actual guest response.
These exploratory outputs remain ignored under `tmp`; no user ROM, installed
HDF, game, normal save, commit, push or release was changed.

Follow-up calibration resolved the apparent unresponsive Use button. The
earlier mouse position missed the active requester target in this different
window layout; aligning the final pointer to the previously successful
`JOY0` position and clicking it advanced the real guest dialog. AmigaOS then
displayed a **System Request**: `Intuition is attempting to reset the
Workbench screen. Please close all windows, except drawers!` The reviewed
1408x576 native field-pair capture shows Clock still open underneath and the
System drawer beside it. At frame 6084 the requester's logical/native-pair
hashes were `09E50F321912F5190498896311B16A4E1F84D69828759AFFC1E66C32FF9DADAF`
and `E03E4FCAA66991410F62066620D4704EEBA0281B9111BE939B063DC1561DDC14`;
there were zero HDF writes. This is an AmigaOS refusal to change the Workbench
mode while an independent Clock window is open, not an emulator graphics
failure. The previous no-Clock System-drawer transition remains the valid
screen-mode transition test. Next close Clock through its gadget, use the
requester's Retry path and verify the mode change with the System drawer
still open; keep Clock's laced operation as a separate application test.

### Clock close, ScreenMode exit, and alternate-row follow-up — 2026-10-15

A real click on Clock's close gadget removed its window while the laced System
drawer stayed open. The following corrected Execute Command, ScreenMode
selection, Use and confirmation then switched Workbench from native PAL
Super-High Res Laced to PAL Super-High Res non-laced. The reviewed 704x288
stable frame shows readable Workbench icons and the System drawer; at frame
7588 its logical/native hashes were
`D88EE775C66E2FA561ACF1F2D3A0F371F844564D284216891A4F6C745944622F`
and `883BD6DDEFA068F43BC8C95FD22B198537AC4E0FE664CB3DCE005EDB939C53F0`.
After another 100 million chip cycles and a cold restore, both hashes still
matched exactly. The persistent
`Tools/amigaos314-a1200-laced-clock-close-mode-exit.json` fixture guards the
Clock closure, non-laced frame and cold restore; a separate 240-step replay
passed with zero CPU exceptions, zero HDF writes, zero PNGs and no automatic
final save. Only the private read-only OS test HDF was mounted.

The first attempt after closing Clock selected a list row ten mouse counts
above the intended PAL Super-High Res row. Its stabilized logical frame
showed clipped/misaligned Workbench content after 100 million cycles.
That frame is **not** counted as a correct mode pass. Pointer alignment to
the known row (`JOY0=$5228`) selected PAL Super-High Res and produced the
readable frame above. Next identify the alternate selected mode from its
ScreenMode Preferences text, reproduce it without Clock, and compare its
geometry/bitplane fetch against the guest's requested mode before changing
the renderer. No commit, push or release was made.

The targeted 1408x576 ScreenMode Preferences capture identifies that alternate
row as **PAL: Low Res Laced** with Width 640, Height 512 and four colors.
Thus the earlier description of the attempt as “non-laced” was wrong:
`BPLCON0=$2306` has LACE bit 2 set, and the Runner recorded a completed
*logical* interlaced field pair at both result and late captures. Its
`nativeInterlacedHash` was null because that image is reserved for SuperHires
samples; absence of a native pair does **not** imply non-laced output. The
apparent clipping was resolved by inspecting the completed 704x576 logical
weave rather than one field: Workbench icons and text are readable.
ScreenMode Preferences displayed Width 640 and Height 512 with both Default
checkboxes checked and AutoScroll enabled. Its Mode Properties panel lists
Visible Size 320 x 512 and Minimum Size 640 x 200, so the default width
itself makes this a virtual Workbench. The official
[Workbench Preferences manual](https://wiki.amigaos.net/wiki/AmigaOS_Manual:_Workbench_Preferences)
documents that dimensions larger than the visible mode create a virtual
Workbench and that moving the pointer to an edge scrolls it. A real
pointer-to-right-edge replay shifted Workbench left and exposed the
previously unseen right-hand icons and drawer. The earlier single-field
image was misleading; it is not evidence of a renderer regression. This
is still not an independent pixel comparison with original hardware.

The Runner now has a separate
`expectInterlacedFrame` guard and rejects contradictory logical-pair/hash
requirements. The Clock-close fixture requires the logical pair while LACE
is active and its absence after the correct non-laced switch. x64 Release
build, 23 Runner tests and a fresh 240-step real OS replay all pass with zero
CPU exceptions, HDF writes, PNGs and automatic final saves. This prompted a
direct PAL Low Res Laced replay from a clean desktop and inspection of its
completed 704x576 logical weave.

The direct A1200/AmigaOS 3.1.4 replay without Clock did exactly that. At
frame 6065 the stable logical and woven hashes were
`73EC7828E6781DE7A260DBECDE6ED049CE25DCCFF29429594ED7A27FA0C48E52`
and `7A3AB7A4B40CEA0A57D56E0F52877A78D950CE2E875463EAA37F236BFD8CA8ED`.
After edge scrolling, frame 6642 hashes were
`13EA98E731C89D96EC04FA4C93F65A609538FED362D3294971C16A01070A27A6`
and `31E84CD749680A083E50EDDA2F31A38A2E465C24543E8F5A0E57B7B1624C4DC5`.
The persistent 142-step `Tools/amigaos314-a1200-lowres-laced-autoscroll.json`
scenario guards these distinct images, logical interlace presence, native
SuperHires-pair absence, and exact hashes after cold restore. Validation and
the fresh 142-step replay passed (`frame=6656`, `PC=F8154E`) with zero CPU
exceptions and HDF writes; only the private read-only OS test HDF was mounted.
The Runner marks the result `executed-unreviewed`, so this evidence covers
the guarded display/input behavior, not a full OS compatibility pass. The
Low Res Laced mode already had Default dimensions selected; its default
width is larger than the visible width. Next test a PAL Hi Res Laced mode
whose default width fits the visible screen, then compare final geometry
against an independent reference before claiming full mode fidelity.

### PAL Low Res Laced bidirectional AutoScroll — 2026-10-15

The follow-up moved the real A1200/AmigaOS 3.1.4 pointer back to the left
edge after the guarded rightward AutoScroll and cold restore. The completed
704x576 woven image again shows the left-side Workbench icons and System
window, whereas the right-edge image exposes more of the virtual screen's
right side. The reverse result at frame 7087 has logical hash
`C4DCB2415843ABB11CBA824F82284ECFE9AF514EE34E1DCACCBA8A716FEEE272`
and woven hash
`F59302C70982AA9BD5E644AACC0321144280BD9E58DFE6EE90ACA0D2E700A45E`.
The difference from the initial left view includes the pointer position.
The persistent `Tools/amigaos314-a1200-lowres-laced-autoscroll.json`
fixture now checks both directions and exact reverse hashes. Its 144 actions
validate, and a fresh no-image/no-final-save replay passed at frame 7087
with zero CPU exceptions and zero read-only HDF writes. This remains a
display/input check, not an independent hardware pixel-parity or complete
AmigaOS compatibility pass. Next test PAL Hi Res Laced at its Default
dimensions and compare its non-virtual screen against a reference.

### PAL Hi Res Laced default-size comparison — 2026-10-15

The direct A1200/AmigaOS 3.1.4 ScreenMode Preferences replay selected
**PAL: Hi Res Laced**. An initial attempt left the preferences window open:
moving the pointer 20 counts higher to select the new row also moved the
subsequent relative `Use` click onto the wrong gadget. Correcting that
click by 20 counts completed the mode change. `BPLCON0=$A306` at the late
capture has HIRES and LACE active without SHRES; the 704x576 completed
logical weave shows a clear full-width Workbench and System drawer. Its
frame-6054 logical and woven hashes are
`83649D84122F01BF99B060D61E5C55D24AB908E70C7BA4CF61C1D4D8A2F3E6EA`
and `057ADCCFA7EFFBC343666AE42AB26D12E0490A06A7F5925713BE11B8AB306490`.
The captured run had zero CPU exceptions and zero writes to the read-only
private OS HDF. The persistent
`Tools/amigaos314-a1200-hires-laced-default.json` fixture guards logical
interlace, absence of native SuperHires pairs, exact late hashes and cold
restore; validation passed. The screen is
visually coherent. The fresh no-image/no-final-save guarded replay then
passed at frame 6068 with exact late and post-restore hashes, zero CPU
exceptions and zero HDF writes. Independent hardware pixel comparison and
broader OS interaction remain open. Next compare
mode geometry and content against a trusted PAL Hi Res Laced reference.

The Runner now accepts `expectedBplcon0` as a four-digit hexadecimal
register guard. This checks the selected hardware mode directly in addition
to image hashes and field-pair presence, so a missed ScreenMode Use click
cannot accidentally count as the requested mode. The A1200 PAL Hi Res Laced
fixture requires `$A306`; the bidirectional Low Res Laced fixture requires
`$2306` at stable checkpoints. x64 Release GameRunner build and 24 Python
Runner tests pass. The Hi Res Laced real OS replay also passed at frame 6068
with the new guard, zero CPU exceptions and zero read-only HDF writes. The
Low Res Laced guarded replay also passed at frame 7087 with the new
`$2306` checks, zero CPU exceptions and zero HDF writes. Next extend
mode-register checks to the other laced OS fixtures and compare mode geometry
with independent references.

### A2000 ECS laced mode-register check — 2026-10-15

The native A2000 ECS/AmigaOS 3.1.4 System-window scenario now requires
`BPLCON0=$2346` at the stable laced Workbench, opened System drawer, moved
drawer and post-cold-restore checkpoints. This confirms that these visible
screens are still using the intended ECS SHRES/LACE mode, in addition to
their existing completed field-pair and image guards. The 68-action
scenario validated and replayed to frame 3272 (`PC=F815D2`) with zero CPU
exceptions and zero writes to the read-only private HDF. No screenshots or
automatic final save were generated in this repeat run; the earlier
visual review remains the image evidence. The A4000 SHRES fixture needs a
fresh private HDF before a repeat run; no original installed image was
mounted. Next extend the mode-register guard to the A1200 SHRES scenario,
then test it on the private read-only OS HDF and review any mismatch.

### A1200 SHRES laced mode and cold-reset guards — 2026-10-15

The native A1200/AmigaOS 3.1.4 PAL Super-High Res Laced scenario now checks
`BPLCON0=$2346` at its laced Workbench, System drawer, scrolling
Preferences and post-cold-restore checkpoints. A first 89-action real OS
replay passed these register guards plus the existing logical/native image
hashes through frame 3690; the later cold reset returned to a readable
desktop at frame 1407 with `BPLCON0=$A302` and neither logical nor native
interlaced field pair. The final reset checkpoint now requires that mode
register and both absent-field guards. The expanded fixture validated and
replayed successfully through the same cold-reset desktop at frame 1407.
Both runs had zero CPU exceptions and zero writes to the read-only private
OS HDF, with no screenshots or automatic final checkpoints. Next extend
the same checks to an A4000 test using a fresh private HDF copy without
touching the installed image.

### A4000 SHRES laced mode and cold-reset guards — 2026-10-15

A fresh private 268,369,920-byte copy of the installed A4000 AmigaOS 3.1.4
HDF was mounted read-only with its matching Kickstart ROM. The existing
87-action PAL Super-High Res Laced scenario passed its image-hash replay.
Its sampled stable Workbench, System drawer, Preferences and post-restore
states all had `BPLCON0=$2346` with completed logical/native interlace
pairs. The cold-reset desktop at frame 1407 had `BPLCON0=$A302` and no
interlace pair. Those register and absent-pair expectations were added to
`Tools/amigaos314-a4000-shres-lace-scenario.json`; validation and a second
real no-image/no-final-save replay passed at frame 1407 (`PC=F8155A`), with
zero CPU exceptions and zero HDF writes. The private copy's SHA-256 matches
the installed original:
`09957EC42DE994FCFA5E81E48C8A25545DF0F7A5805C30DC0331E23883253ADA`.

Cleanup of only `tmp/os-lace-test.hdf` was attempted after the replay, but
automatic command review rejected both the checked-path and exact-path
PowerShell `Remove-Item` calls. The private copy therefore still occupies
268,369,920 bytes in the ignored repository `tmp` directory; the installed
original is preserved. Do not make another large HDF copy until this exact
private file can be removed through an approved path. Next resolve this
cleanup block, then continue mode/reference checks and the remaining
seven-point roadmap work.

### A4000 PAL Hi Res Laced mode transition — 2026-10-16

Independent of the blocked cleanup, the same existing private read-only HDF
was reused without making another copy. The 57-action native A4000/AmigaOS
3.1.4 PAL Hi Res Laced fixture now requires `BPLCON0=$A306` and a completed
logical interlace pair, with no native SHRES pair, at the stable Workbench
and Execute Command dialog. After a cold reset it requires `BPLCON0=$A302`
and no interlace pair. Validation and a real replay passed through frame
1407 (`PC=F8155A`); the laced checkpoints occurred at frames 2822 and
3025. CPU exceptions and HDF writes stayed at zero. Existing image hashes
were checked; the repeat run used `--no-images --no-final-checkpoint`.
This verifies one more OS mode transition, not full A4000 compatibility or
independent hardware pixel parity. The 268,369,920-byte private HDF still
remains in `tmp/os-lace-test.hdf` because automatic command review blocked
its deletion in the preceding run. The installed original remains intact.
Next continue tests that need no large new copies, and resolve the exact
private-copy cleanup before any fresh A4000 HDF test image is created.

### A600 and A500+ ECS laced System-window checks — 2026-10-16

Without making any new HDF copy, the existing private AmigaOS 3.1.4 HDF
was mounted read-only on the A600 and A500+ profiles. Their 68-action
System-window scenarios now require `BPLCON0=$2346` at the laced
Workbench, open/moved System drawer, and post-cold-restore checkpoints,
alongside the existing native interlace/image guards. Both validated and
completed real replays to frame 3272 (`PC=F815D2`). At their final
checkpoints, the native woven field pair was present; CPU exceptions and
HDF writes were zero. The runs used `--no-images --no-final-checkpoint`;
prior visual captures remain the image review, and these checks do not
establish full AmigaOS compatibility or hardware pixel parity.

The separate 268-MB A4000 test copy remains in `tmp/os-lace-test.hdf`
because the prior deletion request was blocked by automatic command review.
No additional large test image was created. Next inspect the remaining
ECS/AGA mode transitions and resume gameplay/CPU work that does not depend
on a fresh HDF; resolve the exact-copy cleanup before creating another one.

### A3000 native 3.1 Solid Gold movement and restore — 2026-10-16

The existing 13-action `A3000-native31-SolidGold` floppy scenario reached a
visually reviewed, live side-scrolling level under the native A3000 Kickstart
3.1 ROM. Targeted captures of `move-right` and `move-left-after-restore`
show the player and scrolling level after real Right, Up+Right and Left
joystick input; the cold in-memory restore returns to the `jump-right`
frame before Left continues play. This is bounded gameplay evidence, not a
title-screen or black-frame pass. Score remained zero, and a level objective
or later stages have not been demonstrated.

The scenario now guards five gameplay/restore checkpoints with exact frame
hashes and at least 180,000 nonblack pixels, and rejects non-ROM CPU
exceptions. A second no-image/no-final-checkpoint replay passed every guard
through frame 5667 (`PC=7FA286E`); the restored frame exactly matched the
pre-restore hash. There were zero non-ROM exceptions and zero floppy writes.
The trace reports 1,599 ROM exceptions, so this does not establish absence
of all CPU exception activity. The read-only game ADF was unchanged and no
HDF was mounted. Next use a controlled objective/score interaction and a
later stage on this profile, then compare a reference run before extending
compatibility claims.

### A3000 native 3.1 Celtic Heart level control — 2026-10-16

The existing 15-action `A3000-native31-CelticHeart` floppy scenario also
reached a visually reviewed snowy side-scrolling level. Targeted captures
after Right input and after Up+Right, cold in-memory restore and Left input
show the on-level character at different positions and animation phases.
Five level/input/restore checkpoints now require exact frame hashes and at
least 190,000 nonblack pixels; the post-restore frame must match the
pre-restore jump frame. A second real replay passed all guards through frame
6004 (`PC=7FEA7C8`). There were zero non-ROM exceptions, zero floppy writes
and no HDF; 1,385 ROM exceptions were observed. The captured display is
actual level play, but no objective, score change or later stage has been
verified. Next drive a level objective with explicit input and inspect the
result before calling this game/model variant a full gameplay pass.

An additional bounded A3000/Celtic Heart continuation now holds Right+Fire
for four million chip cycles after the cold restore/Left sequence, then
releases the controls and observes two million more cycles. The reviewed
frame at 6094 still shows the character on the snowy level; it is neither
a menu nor a black/fault screen. Both new checkpoints have exact image-hash
and 190,000-nonblack-pixel guards. An independent no-image replay passes
all seven level/input/restore guards through frame 6094 (`PC=7FEA7C8`), with
zero non-ROM exceptions and zero floppy writes. The visible score did not
change and no objective or next stage was reached, so this extends bounded
control coverage only. The single review PNG and short traces are in ignored
`tmp` and no final checkpoint or HDF was created. Next identify an actual
level target and script a short, reviewed interaction that changes score or
stage state.

A further read-only A3000/native-3.1 continuation held Right+Fire for another
16 million chip cycles and observed four million cycles after release. The
final reviewed image at frame 6381 (`PC=7FEA7CC`) remains a live snowy level,
with the character back toward the left side of the viewport; it does not
establish whether the character died and respawned or the camera scrolled.
The visible score and stage did not advance. Both added checkpoints have exact
frame hashes and a 190,000-nonblack-pixel floor, verified by a separate
no-image replay; there were zero non-ROM exceptions and zero floppy writes.
The probe uses a temporary manifest and compact output under ignored `tmp`,
without a final checkpoint or HDF. Next identify a level objective from the
game's instructions or a reference playthrough and use deliberate shorter
inputs around hazards; a longer blind Right+Fire hold is not an objective pass.

The next native-A3000 route now uses the documented level objective rather
than continuous Fire: [Lemon Amiga's Celtic Heart review](https://www.lemonamiga.com/review/celtic-heart/476)
describes a green mirror exit and notes that Fire can cancel a jump. From the
reviewed cold-restore state, two bounded Up+Right runs advance the visible
terrain; the second reviewed frame at 6277 (`PC=7FEA7CC`) still shows the
level, with zero non-ROM exceptions and floppy writes. The later frame-by-frame
review below revealed that the knight is already absent in this frame, so it
must not be counted as verified live control.
The reproducible route is in `Tools/a3000-celtic-heart-jump-route.json` with
exact hashes and 190,000-nonblack-pixel guards at both post-run frames. A
separate no-image/no-final-checkpoint replay passed those guards. A third
eight-million-cycle Up+Right probe ended on a **GET READY** respawn screen at
frame 6423, so this route is explicitly not a level-completion pass. Next
branch from the second guarded frame with shorter movement/jump/fire pulses,
inspect the nearby red hazard, and reach the green mirror before claiming
stage progression. These probes used one targeted PNG per run in ignored
`tmp`, no HDF, and preserved the original read-only ADF.

Three further native-A3000 probes narrowed that failure from the guarded
frame 6277. Four 500,000-chip-cycle Up+Right steps show the knight still in
the live level at frames 6289 and 6302, then back at the left-hand start at
frame 6315; by frame 6327 the **GET READY** respawn overlay is visible. A
separate 500,000-cycle stationary Fire followed by 2,000,000 cycles of
Right+Fire also respawns, as does a 1,000,000-cycle Up+Right approach followed
by a 1,000,000-cycle release and another Up+Right pulse. The reviewed images
show a nearby red hazard before the return to start; none demonstrates a
score increase, green mirror, or stage completion. All three real cold-boot
probes completed with zero non-ROM exceptions and zero floppy writes, using
short-lived ignored `tmp` outputs, no final checkpoint and no HDF. The next
specific test should branch before frame 6302, vary the release/jump timing
in sub-500,000-cycle steps while inspecting each frame, and guard against a
life loss or **GET READY** screen before extending the route.

A segmented replay exposed the actual control issue: the earlier continuous
eight-million-cycle Up+Right action drove the knight into a ground enemy. In
the new route, eight separate one-million-cycle Up+Right inputs produce a
different jump cadence. Reviewed frames 6150–6295 show the red knight jumping
over the yellow/green enemy; four further bounded inputs reach the raised
stone approach at frame 6381 with the knight still visible and no **GET
READY** overlay. The image at frame 6381 is live level play, not an exit or
score pass. `Tools/a3000-celtic-heart-segmented-jump-route.json` preserves this
route with exact image hashes and a 190,000-nonblack-pixel floor at six
checkpoints. An independent no-image/no-final-checkpoint cold replay passed
all guards through frame 6381 (`PC=7FEA7C8`), with zero non-ROM exceptions,
zero floppy writes and no HDF. The compact review captures are in ignored
`tmp`. Next use short, visually reviewed inputs to climb the raised stone
approach, locate the green mirror, and verify an actual score or stage change
before claiming level completion.

Six more one-million-cycle Up+Right pulses now carry that same guarded route
onto the raised stone platform. The reviewed frames 6401–6506 show the red
knight climbing and then jumping above two armed enemies; the final frame is
still gameplay with no visible **GET READY** overlay. Three additional exact
frame-hash/190,000-nonblack guards were added to the permanent segmented route,
and an independent no-image/no-final-checkpoint cold replay passed through
frame 6506 (`PC=7FEA7C8`), again with zero non-ROM exceptions and floppy
writes. Four further exploratory Up+Right pulses do **not** pass the platform:
the knight disappears after frame 6526, a black transition appears at frame
6565, and **GET READY** appears at frame 6585. Those latter frames are a
failure/respawn, not stage progress. The next targeted branch should start
from the guarded frame 6506 with shorter release, dodge or attack timings
around the two platform enemies, then verify both knight survival and the
green-mirror/score state. The visual probe reused its prior ignored `tmp`
output directory and created no checkpoint or HDF.

Finer platform probes refine the limitation. Eight 250,000-cycle Up+Right
actions from the guarded frame 6506 show the red knight shrinking to a
stationary damage/death sprite beside the armed enemies; the scene then resets
to the left and reaches **GET READY**. Branching earlier, from the guarded
frame 6440, into four 500,000-cycle Right+Fire actions puts the knight on the
platform next to an enemy, but continued Right+Fire leaves the same small red
sprite at frame 6525 without a visible enemy defeat or score change. This
means the frame-6506 picture is a reproducible *approach*, not evidence that
the knight survived the encounter. Both branches were real native-A3000 cold
replays, kept the game disk write-protected, and used the existing ignored
`tmp` output directory without a final save or HDF. Next vary attack/jump
timing before frame 6440 and inspect the enemy/projectile and life counter at
short intervals; only extend the permanent route after a later frame confirms
survival beyond the platform.

The runner now supports explicit `joystick-down`/`joystick-up` edges on port 1
and an `expectedJoy1` guard. Unlike a `joystick` pulse, a held edge adds no
implicit 400,000-chip-clock released interval; `steps.jsonl` records the
guest-visible JOY1DAT value. This makes jump/attack timing measurable rather
than an accidental side effect of action segmentation. The GameRunner x64
Release build has zero warnings, all 26 preflight tests pass, and a native
A3000/3.1 Celtic Heart cold run confirmed held `Up,Right` as JOY1DAT `$0103`,
an independent `Up` release with `Right` still held as `$0003`, and full release
as `$0000`. No HDF or final checkpoint was created.

Using those edges, a seven-frame release of `Up` near frame 6430 followed by
a fresh `Up` press changes the next jump arc. Reviewed frames 6458–6501 show
the red knight passing *above* both armed platform enemies and landing at the
far right edge, with the enemies behind him; this is a longer live level
continuation than the prior pulse route. The reproducible
`Tools/a3000-celtic-heart-platform-clear-route.json` guards three reviewed
images, a 190,000-nonblack floor and held/released JOY1DAT values. An
independent no-image/no-final-checkpoint cold replay passed through frame 6501
(`PC=7FEA70C`, exact presented hash
`9D566FD4A0F2E6AE23BBA3E18A07D2B6B09B21D2B5B03CC36C5C8430C039F249`),
with zero non-ROM exceptions, zero floppy writes and no HDF writes. The score
and stage have not advanced. Next inspect the gap and airborne hazard beyond
the platform with short held-input samples, then reach the green mirror and
verify an actual stage transition. The review used the existing ignored `tmp`
output directory and did not create another persistent diagnostic tree.

### Siedler lower-right shield reproduced from the user's save — 2026-10-16

The user's preserved `save-20261014-202335.amigasav` was found under the
normal `Die Siedler (disk 2)` saves, after the earlier diagnostic cleanup.
A read-only GameRunner load reproduces the partly cut-off lower-right round
shield visible in the supplied screenshot. After 1,000,000 chip cycles the
live frame is still affected (frame 54638, `PC=033414`, presented hash
`996E4E928BA333EC68EFD5A6FCB76316095888388BC908C65EB8DBE20AC2A0C5`).
The aperture is `92,32+722x273`, but its bottom scanline reports a wider
`-48+734` line. The opposite lower-left control is visible. DF0 remained
write-protected with zero writes; neither the save nor the ADF was changed.
The probe used a targeted PNG and no final checkpoint in ignored `tmp`.

A candidate that unions the sprite-framed horizontal bounds across all lines
produced the **same** full frame hash, so that change was reverted. This
rules out the first-framed-line selection alone as a fix for this saved state.
The defect is now reproducible; inspect the full Denise raster and the
playfield/sprite source-column maps on this exact bottom panel, then add a
failing shield-edge regression before changing the crop/compositor. Earlier
wood-frame goldens do not close this specific report.

The runner now has a targeted `--image-label=state --raw-frame` diagnostic,
which writes one full 896x312 Denise raster beside the presented PNG. On the
same protected save, sampling near the completed bottom panel (after
1,035,000 chip cycles) shows nonblack source pixels through x=799 on rows
270, 280 and 290, with black pixels immediately to their right; the separately
reported beam-line-280 aperture spans source x=80..814. The raw raster rows
and beam-line numbers were initially treated as having different origins;
the later layer probe below resolved that ambiguity.
The incomplete shield is therefore already present in the raw raster; changing
the 704-pixel presentation crop alone cannot restore it. Expanding Denise's
line-tail bounds to include the sprite-frame extent also left the exact
presented hash unchanged and was reverted. The reusable raw capture option
builds with zero warnings, and all 24 runner preflight tests pass. Next
inspect the rightmost 16 source pixels through sprite output and bitplane
fetch/DIW gating on those bottom-panel lines, using this saved state as the
failing visual reference. Do not infer a hardware-correct fix from aperture
width alone.

The follow-up layer capture now writes raw sprite and playfield PNGs beside
the composite when `--raw-frame` is requested. On this exact save, the
wooden right frame is present in the sprite layer, while the cut-off round
control is in the playfield layer. The raw playfield ends at x=799 on beam
rows 270, 280 and 290 despite fetched right-hand image data. Here the raw
framebuffer's Y offset makes its row number equal the beam line, removing
the earlier origin ambiguity. The OCS `DIWSTOP=$xxD1` comparator currently
maps to raw x=800, exactly the cutoff. A bounded Copper trace shows the
game changes `DIWSTOP` vertically from `$10D1` to `$31D1` at beam 265,
but leaves HSTOP `$D1`; it also widens DDF at beam 264. A temporary +16
horizontal DIW experiment exposed the missing 14 playfield pixels and
visually completed the shield. It was reverted: WinUAE's Denise reference
compares its decoded HSTOP directly, and the experiment is not yet a
hardware-correct global rule. This localizes the remaining fault to the
horizontal DIW/fetch-to-pixel coordinate relationship, rather than the
presentation crop or sprite overlay. The layer diagnostic builds without
warnings; all 24 runner preflight tests and `git diff --check` pass. Next
compare the same saved scene against a trusted reference raster and align
the DIW/DDF coordinate mapping with an explicit pixel-edge regression before
shipping a rendering change. The original save and ADF remain untouched;
temporary captures are kept only in ignored `tmp`.

An independent decoded-DIW cross-check against the Amiga Hardware Reference
Manual and current WinUAE/`rsemu` Denise sources shows that `$D1` denotes
HSTOP `$1D1` and should be compared directly, not enlarged merely because
the game fetched more bitplane words. Pixel comparison of the paired local
candidate/baseline captures confines the changed presentation region to
14 columns by 41 rows at the lower right (574 changed RGB pixels). The
candidate restores the visible circle, but that alone cannot distinguish an
emulator coordinate error from source artwork placed beyond the programmed
DIW. Keep the original comparator until an independent Siedler reference
frame or a timing-level comparator test establishes the intended output;
do not encode a title-specific exception. Primary source references:
[Commodore Hardware Reference Manual](https://amigadev.elowar.com/read/ADCD_2.1/Hardware_Manual_guide/node0071.html),
[WinUAE Denise implementation](https://raw.githubusercontent.com/tonioni/WinUAE/master/drawing.cpp),
[rsemu Denise coordinate model](https://docs.rs/rsemu/latest/src/rsemu/dev/amiga/denise.rs.html).

An external visual cross-check now strengthens the game-side expectation:
two independent scenes on the [Dazeland Amiga Settlers page](https://www.dazeland.com/en/Amiga/Settlers.html)
([scene 2](https://www.dazeland.com/images/Amiga/S/Settlers-2.png),
[scene 3](https://www.dazeland.com/images/Amiga/S/Settlers-3.png)) show the
complete lower-right circular control and surrounding wooden frame. The
user-save baseline visibly cuts that control at the right edge, whereas the
local +16-DIW experiment resembles the complete reference control. These
historical images establish the intended artwork, but do not expose the
reference emulator's register trace or source crop. The +16 change remains
reverted; next capture the same game's DIW/DDF and raw raster in an independent
Amiga emulator, then write a pixel-edge regression for the measured hardware
coordinate offset before changing the common DIW comparator.

### Celtic Heart floating-ice crossing on native A3000 — 2026-10-16

The previously guarded plateau route reaches frame 6501 with the knight at
the right edge and two armed enemies behind him. A new real-input continuation
jumps `Up+Right` across the wide gap. Holding the direction throughout the
descent misses the far ledge and ultimately respawns, despite the intervening
frames staying nonblack. Releasing both controls at frame 6543 instead lets
the knight settle on the first small floating ice platform at frame 6560.
Another `Up+Right` jump visibly crosses to the right-hand stone plateau,
above its spider, by frame 6623. The knight is momentarily to the spider's
right at frame 6644; subsequent held Up+Right or Right-only input leaves him
within its patrol path. The reviewed frame 6679 shows a collapsed knight and
the later frame 6700 shows a respawn, so this is **not** a cleared obstacle or
stage pass. The orange airborne enemy and green exit mirror remain ahead.

`Tools/a3000-celtic-heart-ice-step-route.json` preserves the two jumps,
explicit JOY1DAT press/release guards (`$0103`/`$0000`), four exact reviewed
frame hashes, and a 190,000-nonblack floor. An independent native Kickstart
3.1/ADF replay with `--no-images --no-final-checkpoint` passed through frame
6644 (`PC=7FEA7C8`, hash
`574460C71C6F009873164F8B1242860A86F42C726129511B87F22A44D03F0902`);
the route is a traversal checkpoint only. The probes reported zero non-ROM
exceptions, zero floppy writes and no HDF writes. Existing ignored `tmp`
outputs were reused. Next vary the second ice-platform launch timing and
control release to land beyond the spider's patrol, then verify continued
movement past the airborne enemy and a real green-mirror stage transition.

### Celtic Heart two-ice route clears the first spider — 2026-10-16

The first ice platform can be used as a stepping stone rather than jumping
straight to the spider's plateau. From frame 6560, a short 500,000-chip-cycle
`Up+Right` press followed by release lands the knight on the second small ice
platform at frame 6629. A fresh jump from there lands **beyond** the spider
at frame 6706; a separate Right-only run passes the orange airborne enemy,
descends the snowy slope and reaches the lower path at frame 6847 with the
knight still moving. This resolves the earlier immediate spider collision
for this A3000/Kickstart 3.1 route. The images at each landing and on the
lower path were inspected; a nonblack count by itself was not accepted.

`Tools/a3000-celtic-heart-post-spider-route.json` has JOY1DAT edge guards,
reviewed exact hashes at the second ice landing, past the spider and on the
lower path, plus a 190,000-nonblack floor. A separate native cold replay with
`--no-images --no-final-checkpoint` passed through frame 6847 (`PC=7FEA7C8`,
hash `18B72EEF727101B5C4553B69AEAD7EF204BC13A600CFC727A9A4BC8822160369`).
The probe reported zero non-ROM exceptions, zero floppy writes and no HDF
writes. The source ADF stayed read-only, and the ignored `tmp` output directory
was reused.

A subsequent **unpromoted** input probe jumped toward two orange ground
enemies and the later spiders, but the red player sprite appears flattened
before frame 6896 and a respawn follows by frame 6963. Releasing and
re-pressing Up at frames 6896 or 6908 did not start another jump; those probes
were already too late to establish a live second jump. The exact collision
frame and culprit remain unisolated, so these later images are not evidence
of a cleared enemy or a complete gameplay pass. Next sample frames around
the lower-path descent at finer intervals, identify the first death frame,
then change the jump timing before that collision and verify survival beyond
the later spiders and the actual green-mirror stage transition.

The next lower-path timing audit narrowed this failure without promoting it
to a pass. At the original immediate jump, 100,000-chip-cycle samples show an
upright knight at frame 6890 and a horizontal red posture by frame 6892;
later frames become black/`GET READY`. The second orange ground orb vanishes
near the descending knight around frame 6885, while the nearest visible
spider is still ahead at frame 6892. This suggests an orb hit, but the image
alone does not prove which game's collision test fired. Delaying the launch
by 300,000, 500,000, 700,000 and 1,000,000 chip cycles moves the descent
toward the first spider; none gives sustained live movement, and the 1,000,000
variant later reaches a black frame. Midair release and a Fire press before
descent also failed to clear the spider. The probes preserved the read-only
ADF and reported zero non-ROM exceptions, zero floppy writes and zero HDF
writes. Next add a compact guest-life/collision-state watch to distinguish
orb damage from spider damage, then adjust the input route and verify actual
survival rather than treating a nonblack hazard frame as success.

`Tools/a3000-celtic-heart-lower-hazard-repro.json` now pins the original
lower-path descent with four reviewed full-frame hashes, JOY1DAT `$0103`,
100,000-chip-cycle samples, and a 190,000-nonblack floor. Its independent
native no-image/no-final-checkpoint cold replay passed every diagnostic guard
through frame 6892 (`PC=7FEA7CC`, hash
`082C16F6FAD7F6F8256F39FB3EC2660F512895C3471B00C0D5FC09E95772DB46`).
This is a reproducible failure boundary, explicitly **not** a gameplay pass.

Follow-up 2026-10-16: GameRunner can now record a bounded, read-only
`memoryWatchAddresses` list (up to 32 distinct RAM bytes below `$A00000`) at
every action. Preflight rejects duplicates, out-of-range addresses and overly
large lists before mounting media; 27 preflight tests pass. A native A3000
Celtic Heart cold replay of the established post-spider route passed through
frame 6847 with the same reviewed image hash
`18B72EEF727101B5C4553B69AEAD7EF204BC13A600CFC727A9A4BC8822160369`,
while the new watch logged `$000000=00`, `$000004=07`, `$000100=00` at the
lower-route endpoint. This confirms the watch does not disturb that replay;
those three sampled bytes are only a transport check, not identified life or
collision state. The earlier lower-hazard failure remains open. Next identify
the game's life/damage byte from small before/after guest-memory comparisons,
watch it across the orange-orb disappearance and the first spider, then change
the route and require live movement beyond both hazards. The compact native
probe used `--no-images --no-final-checkpoint` and reused its ignored `tmp/`
output; ROM, ADF, HDF and saved games were not changed.

A separate native A3000 cold replay tests Fire before the lower hazard in
`Tools/a3000-celtic-heart-fire-before-hazard-repro.json`. From the live
post-spider position it holds Fire stationary for 1.5 million chip cycles,
releases it, jumps right, then keeps moving. The CIA-A port-A guard reads
`$40` while Fire is held and `$C0` after release, confirming that the guest
received the button edge. Independent replay passes the pinned stationary
and final frame hashes with zero non-ROM exceptions. At frame 6988 the
reviewed image shows the knight prone on the lower path with an orange orb
still ahead; it does **not** establish survival or stage progress. Fire at
this timing therefore does not resolve the hazard. Continue by correlating
damage-state bytes with the frame-6885 orb disappearance, then try an input
route whose subsequent captures show an upright moving knight beyond both
ground orbs and the spider.

Follow-up 2026-10-16: segmented jumps clear this lower-path hazard. A
read-only checkpoint made at the already verified frame-6847 post-spider
position allowed short control trials without repeating the cold boot. Holding
Up+Right for 1 million chip cycles, releasing Right midair for 2 million,
then releasing Up and waiting 3 million lands the knight upright before the
two ground orbs at frame 6931. A second Up+Right hop for 1.5 million followed
by another 2-million-cycle horizontal brake lands upright beyond the two
orbs and the nearby spider at frame 7009. A further 2-million-cycle Right
command visibly moves the knight right; at frame 7051 he remains upright in
the next snowy screen region, with the earlier spider behind him. This is
actual continued gameplay, not an isolated nonblack frame.

`Tools/a3000-celtic-heart-lower-orb-clear-route.json` incorporates those
edges into a **fresh native A3000/Kickstart 3.1 cold replay**, including the
earlier platform and spider crossing. Its independent no-image,
no-final-checkpoint run passed reviewed full-frame hashes at frames 6931,
7009 and 7051, explicit JOY1DAT direction/release guards and a 190,000-pixel
visibility floor. The final hash is
`17681F33394FD3DD201FE7133E4E373E06F587EED57CF8145231B7D11DE0D336`;
the same state showed zero non-ROM exceptions and zero HDF writes. The three
sampled endpoint images were visually reviewed in the checkpoint trials.
The level, subsequent floating platforms, later enemies, goal and score
remain open. Next continue from the frame-7051 live state toward the green
mirror, verifying movement, hazards and the actual stage transition.
Three temporary checkpoint files (about 10.5 MiB each) remain in the ignored
`tmp/celtic-heart-second-extended-run-20261016/` tree: the automatic approval
review blocked their targeted removal despite an absolute-path containment
check. They are not user save states or part of the replay manifest; remove
them when local cleanup is permitted.

Follow-up 2026-10-17: the live frame-7051 route now crosses the **next**
ground-orb pair and climbs the short/tall ice-pillar section. From a private,
ignored frame-7009 checkpoint, controlled Right movement reaches the next
pair at frame 7149. A 1.5-million-cycle Up+Right jump with a 2-million-cycle
midair Right release lands upright between them at frame 7241; repeating that
hop lands past the second orb at frame 7318. Right movement reaches a short
ice pillar. Up+Right for 1.5 million clocks lands visibly on its top at
frame 7438; the next jump needs 2.3 million clocks of Right before braking
to land visibly on the taller pillar at frame 7520. A subsequent 2.5-million
Right command continues to frame 7569, with the knight upright on the ground
beside the next short pillar. The reviewed images show this movement and
hazard traversal; the goal, upper spider and later stage are still open.

`Tools/a3000-celtic-heart-tall-pillar-route.json` encodes the entire path
from a **fresh native A3000/Kickstart 3.1 cold boot**, not the private
checkpoint. An independent `--no-images --no-final-checkpoint` replay passed
the prior frame-7051 guard and six new full-frame checkpoints through frame
7569 (final hash
`A1459EEFC3E42808C42E9914A63198E657C692C6BBA63ACA06BEBC9C31D1AF51`,
PC `$7FEA7C8`), with explicit held/released JOY1DAT guards and zero non-ROM
exceptions. No permanent game, ROM, normal save state or installed HDF was
modified. Next navigate from the frame-7569 short pillar toward the upper
platform and green mirror, requiring a visible stage transition and resumed
control before counting further progress.

Follow-up 2026-10-17: a further Up+Right jump from frame 7569 climbs the next
short pillar (frame 7639). Another jump with 2.1 million chip cycles of
horizontal movement lands the knight upright at the **left edge of the
upper floating platform** at frame 7701, with its red spider still ahead.
The checkpoint-trial screenshots were inspected, and
`Tools/a3000-celtic-heart-upper-platform-route.json` independently repeated
the complete path from a native A3000/Kickstart 3.1 cold boot, passing exact
frame hashes at 7639 (`628BF4E81EF9FE37BB6B01CBA3AF42F45712F32FE0F553366B1C76B57E2D1436`)
and 7701 (`C9128DD92521844123BBD008A422BB1E84E1E6C45FAAA932A89464C49FD6CD37`),
plus held/released direction guards, with zero non-ROM exceptions. The replay
used no images or final checkpoint and touched no permanent media.

The upper-platform spider remains a real blocker. A 2.3-million-cycle
approach leaves the knight too close to the spider; a subsequent jump leads
to a respawn/`GET READY` screen. Shortening the approach to 2.1 million
creates a visibly safer gap, but tested follow-up waits of 0, 0.3, 0.7 and
1.5 million clocks and jump holds of 1.0, 1.4 and 1.8 million clocks still
did not show sustained live movement past the spider; one route faded fully
black before the respawn. Those dark and respawn frames are failures, not
gameplay passes. Next sample the landing and spider motion more finely,
then test a continuous or earlier jump from the left platform edge with
visible post-spider movement and a later goal transition.

Follow-up 2026-10-17: the upper-spider failure is now reproducible from a
fresh cold boot in `Tools/a3000-celtic-heart-upper-spider-failure-repro.json`.
After reaching the floating platform, releasing Right midair and immediately
holding Up+Right for another 1.8 million chip cycles yields an entirely black
frame at 7771 (`C9926CBFA109FB0E876776ECF213DE72260B7EA4E7EB1AA4B0C90BFE164E2345`,
zero nonblack pixels); five million clocks later the route reaches a
`GET READY`/respawn state at 7842
(`89DCA9D1DEE07493145C937B88C0E2B754E0DBA500FC1E009119AEFB66314624`).
The independent no-image/no-final-checkpoint run matched the black hash and
reported zero non-ROM exceptions and zero HDF writes. The hash guard marks
a **failure boundary**, never a gameplay pass. Short local trials with Fire,
including verified CIA-A `$40` while held, leave the spider visible; a lower
ground bypass also failed to keep a visible knight. The current input route
or collision timing is unresolved, with no evidence yet of a core exception.
Next sample the airborne landing around frame 7697 more finely, release Up
before touchdown and re-press after confirmed landing while the spider gap
is still large; require the knight to move upright to the platform's right
side and remain alive before searching for the stage goal.

Follow-up 2026-10-17: the upper spider has now been cleared in continued
play. At the reviewed frame-7701 landing, a fresh Up+Right jump held for
2.4 million chip cycles, followed by 1 million cycles with Right released,
lands the knight upright on the **right side of the floating platform** at
frame 7760. The spider remains visibly behind him. A subsequent 1.2-million-
cycle Right command moves him off the platform to the ground beyond it;
frame 7795 still shows an upright, controllable knight with the spider behind
him. The two captured checkpoint-trial images were inspected. The independent
`Tools/a3000-celtic-heart-upper-spider-clear-route.json` replay starts from
a fresh native A3000/Kickstart 3.1 boot and passes exact full-frame guards at
7701, 7760 (`1D94F67B779D7FE5EA6294333212ACEB5BB3497B1E7A2054B21EED35867BF0AD`)
and 7795 (`9797AB7382B3FB42B73CFE49439FB0CA26B3E91CA2B660FC09D8A1751ECDD811`),
held/released JOY1DAT guards and a 190,000-pixel visibility floor, with zero
non-ROM exceptions or HDF writes. It uses no final checkpoint and creates no
new permanent media. This supersedes the preceding spider *input-route*
blocker, but does not establish a stage-clear pass.

A short continuation reaches the next tall ice pillar at frame 7838 with the
knight upright. A direct 2.3-million-cycle Up+Right jump from the ground
does **not** clear it: the later frame-8004 image confirms the red knight
still stands on the pillar's left side. The green figure on the right is an
enemy, not the player. These are local checkpoint trials; no guest exception
was observed.

Follow-up 2026-10-17: the next section requires staying on the **upper
floating platform** after clearing its spider. From the reviewed frame-7760
position, Up+Right for 2.6 million chip cycles with a 1.5-million-cycle
horizontal brake lands the red knight upright on the tall pillar at frame
7832. A further 2.3-million-cycle jump lands him on the short ice pillar
beside the frozen arch at frame 7900. Another jump moves him to the ground
near the arch's first enemy at frame 7966. A following short Right command
moves the red knight visibly at frame 7977, proving continued control there.
The trial images at these positions were inspected; the first enemy, other
ground enemies, orbs, the level goal and stage transition remain ahead.

`Tools/a3000-celtic-heart-pillar-and-arch-route.json` independently replays
the complete path from a native A3000/Kickstart 3.1 cold boot. Its final
no-image/no-final-checkpoint run passed exact full-frame hashes at frames
7832 (`42228D9E2293677A1C21A70985FB63C77FEAC7D49C426DA4CD79541DCA5BA7ED`),
7900 (`1CBA57E61E794C6EA02BA8BFB9580C031E263DD6A134C5E48E99621EB74847F2`),
7966 (`112FAA8D127C10D2751591DDF17D20CC1A1CC16A55EF0BAD582C067C271FC890`)
and the live movement check at 7977 (`1628B9F31576C8DCBA8F818020B2A2978176340CCD237360CA14134C9EEEC47A`),
plus explicit held/released input and visibility guards, with zero non-ROM
exceptions and zero HDF writes. It preserves all original media. Next use
short checkpoint trials to time jumps across the remaining enemy/orb cluster,
then require sustained live movement and an actual stage change; do not
count dark, death or menu frames as successful play.

Follow-up 2026-10-17: a longer second jump from frame 7966 was initially
misread as a live pass at frame 8040. A new Right and Up+Right control leaves
the red sprite at the same ground position, followed by a return to the
earlier green checkpoint at frame 8104; this is a **death/respawn**, not
progress. The route was shortened to the independently repeated live
frame-7977 input check. Attempts with 2.5- and 3.0-million-cycle jumps,
continuous Up+Right and a short approach before the jump all converge on
the nearby hazard; a separate Fire+Up+Right trial changes the path without
establishing a pass. This is why frame hashes and nonblack-pixel guards must
be paired with a later movement check and image review. Next sample the
landing around frames 8015-8040, identify the exact orb/enemy collision and
time a jump or attack that preserves control beyond the first enemy.

Follow-up 2026-10-17: the first green ground enemy after the arch can be
defeated without losing the knight. Starting from the independently live
frame-7977 position, hold Fire+Right for 3.5 million chip cycles and then
release both. At frame 8033 the first green enemy is absent, the red knight
is upright and the next enemy is still ahead. A separate Right command
visibly moves him farther right; frame 8053 remains live. This corrects
the earlier jump route, which ended in a death despite nonblack output.
`Tools/a3000-celtic-heart-first-enemy-clear-route.json` replays the entire
route from a fresh native A3000/Kickstart 3.1 boot and matches the reviewed
full-frame hashes at 8033
(`0500F1C358423EA06178C5AA91F96C65AAA5598F9EE6B987CD5DF130C084642B`)
and 8053 (`A539BECED5310B261E969B48FF16EFFA82EB5EBD4F841715F01D84B9433926A0`).
The held attack explicitly checks guest-visible CIA-A `$40`/JOY1DAT `$0003`;
the released controls return to `$C0`/`$0000`. The final independent run
reported zero non-ROM exceptions and zero HDF writes, without images or a
final checkpoint. Trial images of the enemy disappearance and actual
post-attack movement were reviewed. The second green enemy remains: simply
holding Fire+Right from frame 8053 or approaching for 0.3/0.7 million cycles
before attacking leaves a stationary red death sprite while that enemy
survives. Next vary its approach and attack timing, or jump over it, then
require a separate live movement check before pursuing the level goal.

Follow-up 2026-10-17: the second green enemy is also cleared with a different
timing. From the live frame-8053 position, **hold Fire without Right** for
2.5 million chip cycles while it approaches, then release and wait 0.5
million. The frame-8095 trial image shows the red knight upright beside the
second enemy's fallen sprite. A separate Right command moves the knight
visibly into the next section by frame 8116, where no green ground enemy
remains in view. Earlier Fire+Right trials, including 0.3/0.7-million-cycle
approaches, instead produced a stationary red death sprite; they are not
passes. `Tools/a3000-celtic-heart-two-enemies-clear-route.json` independently
replayed the full native A3000/Kickstart 3.1 cold boot, matching the reviewed
frame-8095 hash
`69DFF10D5B99062E504A7C9D4FFE1DCC4800003BE3D220432A70BCC8F9AE519D`
and post-attack movement hash at 8116
`2BBC13B741FA5E78B8114D69F972E4DF6AA6A5ACD1235FCF65B0663AD5934FB2`,
with explicit Fire CIA-A `$40`/`$C0`, JOY1DAT and visibility guards, zero
non-ROM exceptions and zero HDF writes. No final checkpoint or new permanent
media was created.

The next tall ice pillar is still unresolved. Immediate Up+Right, a vertical
lead-in of 0.7 or 1.5 million clocks, and a short Left backup before a long
jump all leave the red knight at the pillar's left base in reviewed trial
images. The first of those trials also caught an incorrect expected Left
JOY1DAT value: the actual held Left value is `$0300`; the temporary guard
was corrected before evaluating the jump. Next identify an elevated approach
or reachable intermediate platform, then demand a landing on or beyond the
pillar followed by live movement. The stage goal remains unverified.

Follow-up 2026-10-17: the apparent dead end at the next tall pillar led to
an alternate **elevated** Celtic Heart route from the earlier pillar at
frame 7832. Up+Right for 1.0 million chip cycles, release for 1.8 million,
then wait 1.2 million lands the red knight on the small floating ice platform
at frame 7888. A separate 0.4-million-cycle Right command moves him along
that platform by frame 7904; both frames were visually inspected, with the
knight upright. This route branches before the arch and its two ground
enemies, so it does not replace the independently proven ground-enemy route.
The nearby green swirl remains visible even after jumping vertically through
it; no goal or stage transition has been established. An exploratory jump
toward the next higher ice bridge brings the knight alongside its top at
frame 8040, but a later frame shows him falling to ground, so that bridge
has **not** been counted as a landing. The platform branch is recorded in
`Tools/a3000-celtic-heart-floating-platform-route.json`. Its independent
native A3000/Kickstart 3.1 cold replay passed exact full-frame hashes at
7888 (`220B399B7DCBCFE5F66A31554BDDEA0EFA33874A5219DBFD00B28447144F4F1F`)
and after actual Right movement at 7904
(`CC5FF1F070A7144463A2CAFEA142981F71D99A3A933BFDE733CE22BDAA365679`),
plus held/released JOY1DAT guards and a 190,000-pixel visibility floor,
with zero non-ROM exceptions and zero HDF writes. The run used neither a
final checkpoint nor new permanent media. The next continuation below
establishes the subsequent bridge and pillar crossing; an actual stage
change still requires image and input confirmation.

Follow-up 2026-10-17: the higher ice bridge is reachable only with a
**timed second jump** in this continuation. From the small floating platform
at frame 7904, move Right for 0.5 million chip cycles, then Up+Right for
3.0 million. Release only Up for 0.5 million while retaining Right; at
frame 7960 the upright knight reaches the higher bridge. Re-press Up for
the next 1.0 million clocks: the reviewed frame-7974 image shows a fresh
airborne ascent, demonstrating that the second jump actually triggered.
Continuing Up+Right for 2.5 million more, then releasing and braking lands
on the tall pillar past the arch's ground-enemy cluster at frame 8032.
The knight remains upright there at 8049; a subsequent Up+Right jump
crosses to the pillar's right, and a separate Right command moves the live
knight farther into the next section by frame 8178. These images were
reviewed. Earlier single-jump bridge trials reached its height but fell to
the ground: a high or nonblack frame alone did not prove a bridge landing.
`Tools/a3000-celtic-heart-over-pillar-route.json` records the full branch.
Its independent native A3000/Kickstart 3.1 cold replay passed exact
full-frame hashes at frames 7960, 7974, 8032, 8049, 8157 and 8178;
the last live-control hash is
`5C8BB8E808CB5EE335B7665D1A9C4BFC48F45D3D9236ABB8F0CC4EEAC77EAA84`.
The replay also passed held/released JOY1DAT and visibility guards, with
zero non-ROM exceptions and zero HDF writes, no final checkpoint and no
new permanent media. This branch does not yet show a goal transition or
complete the level. The continuation below extends it with separate
movement checks after each hazard; an actual stage-change image remains
required before counting a level clear.

Follow-up 2026-10-17: the elevated branch continues beyond the tall pillar
into the later snowy section. A long jump over the next ground enemy followed
by separate Right movement reaches reviewed, upright play at frame 8489.
Simply holding Right farther produces a stationary red death sprite by
frame 8534; subsequent Up and Right controls confirmed that this was not
a live pass. Shorter approaches to frame 8529, then a 2.2-million-clock
Up+Right jump and horizontal brake, land the knight on the raised stone
ledge at frame 8582. Another jump passes the enemy atop its short column;
at frame 8676 the red knight is upright and a separate Right command has
moved him onto the snowy ground beyond the ledge. The corresponding trial
images were inspected; the next dead-tree/spider section and stage exit
remain open.

`Tools/a3000-celtic-heart-raised-ledge-route.json` independently replays
the complete branch from a fresh native A3000/Kickstart 3.1 cold boot. It
passes exact full-frame guards at the reviewed frames 8243, 8363, 8412,
8475, 8489, 8529, 8582, 8596, 8655 and 8676; the final hash is
`4B04E69285939E2A7286AFAA7622A8EB92147769BCECECC5EEF18845C0F5189B`.
Held/released JOY1DAT and visibility guards pass with zero non-ROM
exceptions and zero HDF writes. No final checkpoint or new permanent
media was created. Next approach the dead-tree spider from the live
frame-8676 state in short increments, test a timed jump/attack with a
later independent movement check, and continue only after reviewing the
resulting image. This does not establish a level-clear pass.
