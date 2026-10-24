# Machine models: implementation and validation

Status: 2026-10-22 (roadmap ongoing). Select a machine through **F10 -> SETTINGS -> HARDWARE**.
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
physical Ramsey SIMM bank geometry is not fully modeled. An opt-in single
Zorro III RAM development card is available for A3000/A4000/030 core tests;
it is not a selectable or generally validated expansion subsystem.
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
| 2. Native gameplay | Full local ADF sweep; Boxx 4 ECS window fix with failing-before test; Tinyus bounded movement/fire on all eight boards; verified live Pinball ball/flippers/pause/quit and score-sector writes on all four tables with an explicit repaired Disk 1 on native A3000/A4000; Desert Strike lift-off/flight/fire/map/EXIT/reset/restore on all eight models; Mega lo Mania live islands; Cannon Fodder's first mission on all eight native boards with data drives; Celtic Heart Snow Mountains Stages 1 and 2 completed on A3000 through real inputs, followed by independently replayed active Stage-3 movement, stable passage of its first gap, defeat of the first far-side type-9 enemy, a timed Fire kill of the type-12 bat, and two grounded Fire kills followed by live movement through the raised knight corridor to X=780 and independently replayed timed passage across three water stones, castle landing, Type-6 knight defeat and sustained upper-platform landing beyond the Type-5 enemy and two defeated lower-castle knights, first castle-wall ledge, near Type-12 bat, higher castle step, sustained roof landing, timed passage to the right-side platform at X=1492/Y=73, and independently replayed Fire defeats of both far bats and the lower Type-9 enemy, followed by timed passage beneath the far castle wall to X=1680/Y=185 and a raised Type-5 defeat with stable live movement to X=1836/Y=137, a higher-platform landing at X=1738/Y=89, a safe jump over deadly GID 66 to X=1696/Y=89, a tiny high-step landing at X=1668/Y=41, a grounded Fire defeat of the high Type-6 knight at X=1752/Y=9, an independently replayed living landing past the X=1888 pillar at X=1924/Y=89, the grounded Fire defeat of the next Type-6 knight, and timed far-side jumps with stable landings at X=2180/Y=121 and X=2324/Y=25, a jump over the animated upper tile to X=2380/Y=25, a living landing past the second tall column at X=2464/Y=185, a grounded Fire defeat of its Type-6 knight, and a jump over the Type-7 ground enemy, followed by the animated-tile and two-water-gap passages, far-side Type-6 and Type-8 kills, three fixed steps and a wall overjump, and independently cold-replayed visible Stage-3 and Stage-4 completions, followed by active Stage-5 passages through the Type-7 ground enemy, three Type-5 patrols and a timed foreground-tile jump to X=2232 and an independently repeated Type-15 magical-fire bypass through the X=2304 wall, a Type-9 Dino defeat, animated hazard and Type-8 spider bypass, moving-platform water crossing and two fixed-step landings to X=2888/Y=57 and a second independently replayed moving-platform passage to X=3052/Y=57, then a defeated lower Type-5 spider and genuine raised-stone gameplay through the visibly complete Stage 5 and Stage-6 title, then active cave movement, first skeleton bypass, first cavern gap, two further Type-17 defeats and a Type-20 green-walker defeat at X=364/Y=121 (2026-10-21). Later detailed full-cold routes below document visible completions through Stage 10; Stage 11 activation is under full-cold validation. | Complete the 268-case visual/control review and title-specific disk/menu continuations; naturally earned Pinball highscore entry and broader reload/model coverage, Desert land objectives, Cannon single-drive requests and longer campaigns, Celtic Heart Stage 11 and later stages, Mega later islands, Sqrxz text/reference checks, native AGA software and longer sessions remain open |
| 3. Performance | Display/opcode caches and allocation reductions; vectorized framebuffer clear measured with identical complete CPU/frame states; profile-driven floppy selection/index/tick optimization with paired identical CPU/frame states (2026-10-12); timing lookup and duplicate bus-classification candidates rejected | Continue profiling remaining allocations and repeat paired measurements after further changes |
| 4. Installed OS | A4000/030 and separate A3000/SCSI 3.1.4 installations; both caches exercised; all eight boards have matching 46.143 desktop/application evidence; A1200 IDE/A3000 SCSI Shell/Clock/ShowConfig and independently verified file persistence/reset/restore repeated on the current core (2026-10-12) | Broader applications and complete controller coverage; A1200 native 3.1 comparison remains unresolved; optional hardware remains gated |
| 5. ECS/AGA modes | Extended AGA lores/hires scrolling and fractional compositor phases implemented; LOF/VPOSW field selection and 312/313-line PAL field timing corrected; native 1408-pixel SuperHires and 576-line field-pair presentation exercised through real 3.1.4 Workbench mode changes, window dragging and restore on bounded ECS/AGA configurations (2026-10-15) | Compare against independent hardware/reference output; mixed-resolution and mid-field Copper changes, programmable timing, scan doubling and broader application transitions remain open |
| 6. 020/030 correctness | Integer/MMU and repair fixtures; locked-read protection and PTEST B/I fixes; actual 020/030 instruction caches and separate 030 data cache implemented and boot/application tested; overlapping TT windows combine cache inhibit correctly; fixed-slot ATC history replacement and legacy restore checked; bounded 030 MOVE.B/W postincrement retry, split-long source read, prechecked register-source split-long write, MOVEM read/write transfer probes including odd byte/word page crossings, and MOVEP word/long byte-transfer probes after MMU faults (2026-10-13) | Complete timing and full restartable format A/B faults, including real partial-transfer continuation, device reads after successful translation, indexed-EA side effects and multi-transfer writes |
| 7. Further hardware | Opt-in native Zorro III RAM board (8/16/32 MiB) is under core/guest validation; A1000 WCS decoder, CPU/bus handoff, RESET and guarded restore pass synthetic guest regressions; unsupported models stay unavailable | Installed-OS Zorro III enumeration and multi-card/IRQ/DMA coverage; optional 68881/68882; distinct 68040 CPU/MMU/FPU; A1000 bootstrap/WCS and full boot |

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
The overall scope remains active. Historical test output was recorded in
`%APPDATA%/AmigaEmulator/diagnostics/RoadmapSeven-20261012-4fcc1fe9/`;
the user-requested AppData cleanup later removed that tree. The results
below are the durable summary, not a claim that its files remain available.
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

Follow-up 2026-10-17 (dead-tree approach): the independent native
A3000/Kickstart-3.1 cold replay now reaches frame 8733 with the knight still
upright beside the dead tree. From the verified frame-8676 state, an early
1.8-million-clock Up+Right jump, 1-million-clock horizontal brake, release,
then a separate short Right input moves the live knight closer to the tree.
The reviewed frame-8733 image and exact full-frame guards at frames 8723
(`9A092E8D7B2A7FB56555ACE153ACA64ACF4F7D1C3FE292E337B73F2499A280F6`)
and 8733 (`4E1FFDF53567E2727C7F60BFB5D532AB2E29BD3A219CF80BAD2103EFCA4EDDD3`)
passed a second fresh cold replay with `--no-final-checkpoint --no-images`;
the guards are in `Tools/a3000-celtic-heart-dead-tree-approach-route.json`.
Direct Right movement from 8676 instead ends in a horizontal death pose by
frame 8712. A second Up+Right jump and two Fire+Right advances from the
frame-8733 vicinity likewise end in a death pose; the runner's zero exit and
nonblack image were rejected as gameplay evidence. These negative trials had
zero non-ROM exceptions and HDF writes. The next step is to vary the timing
of the first jump/landing before the tree and inspect short-interval images,
then verify any passage with a separate movement input and a stage-change
image before calling the level complete.

Follow-up 2026-10-17 (timing controls): repeated compact checkpoint
replays narrowed this to a moving hazard, rather than a static passage.
From the live frame-8733 state, even a neutral 2-million-clock wait ends in
the horizontal death pose by frame 8761. Standing Fire for 1.8 million
clocks, a short Fire pulse followed by a separate Right command, and a
second jump started immediately at frame 8723 also end in death poses.
An earlier 0.3-million-clock Right prestep before a longer jump and a
2.3-million-clock Up+Right+Fire jump do not clear the tree either. The
Up+Right+Fire branch is still upright at frame 8729 and after a short
Fire+Right approach at frame 8755, but the following jump is fatal. Each
trial held the runner's input/visibility checks with zero non-ROM exceptions
and HDF writes; all apparent passes were rejected on image review.
Next capture one or two intermediate frames around 8745-8760 to locate
the knight and moving spider just before collision, then adjust the first
jump and attack release at that point. Do not promote the current state to
a level pass.

The intermediate attack-approach capture at frame 8744 shows an upright
knight near x=388 and the approaching ground spider near x=437 in the
presented image; by frame 8755 their separation has visibly narrowed.
Starting the second Up+Right jump at frame 8744 or about frame 8737 still
ends in the horizontal death pose. The next useful probe is to inspect the
first jump's ascent/landing frames and try a different takeoff before frame
8676 or a released/repulsed attack before the spider reaches this corridor.

Follow-up 2026-10-17 (first-jump timing): the first `Up+Right` press
visibly lifts the knight above the ground by frame 8684. In contrast,
fresh `Up+Right` presses around frames 8723 and 8744 leave him near ground
height seven frames later, despite the expected `$0103` JOY1DAT input.
Releasing Up earlier in the first jump and shortening its horizontal
travel also failed to produce a safe second takeoff; the short-travel
branch reaches a horizontal death pose by frame 8733. A segmented first
jump (1.5 million clocks Right, 2 million clocks brake) shows the knight
above ground near the tree at frame 8733, but later Right and second-Up
controls do not establish a sustained live pass. This isolates the next
question: determine the guest's landed/alive state after the first arc
before interpreting a second Up press as a failed jump. Use short image
intervals and a bounded RAM watch around landing/collision, then try a
takeoff that clears the patrol while preserving a separate post-landing
movement check. No graphics or CPU exception was observed in these runs.

`Tools/a3000-celtic-heart-dead-tree-second-jump-repro.json` preserves this
specific boundary from an independent native A3000/Kickstart-3.1 cold boot:
the proven live frame-8723 image is followed by a fresh `Up+Right` press
with JOY1DAT `$0103`; at frame 8730 the reviewed knight has not visibly
risen. The exact final full-frame hash is
`EC89C3696C590A3CB0ECC3B5444F3D79E0DA117C4108095FEE9D925D259221A7`.
A second cold replay passed the stored hashes without images or a final
checkpoint, with zero non-ROM exceptions and zero HDF writes. This is
a reproducible diagnostic observation, not proof of an emulator input bug:
the game's airborne, landing and damage flags have not yet been identified.

Follow-up 2026-10-17 (Snow Mountains Stage 1 **completed** on the native
A3000/Kickstart-3.1 route): retreating left during the first jump gives
the knight room to finish that arc on live ground at frame 8740. A fresh
`Up+Right` press then visibly starts the second jump at frame 8747, crosses
above the dead tree at frames 8764/8781 and lands beyond its spider patrol
by frame 8795. After a separate release and Right-only movement check,
the knight is upright past the tree at frame 8819. Further Right inputs
enter the green mirror: the reviewed frame 8907 explicitly reads
`LEVEL COMPLETE / PRESS BUTTON`. Fire confirmation produces the
`SNOW MOUNTAINS STAGE 2 / CODE: 35DH` screen at frame 8982. A second
Fire press enters the next active snowy gameplay scene at frame 9056;
the subsequent Right command is delivered to JOY1DAT. The images,
input edges and actual transition were reviewed; this is a Stage-1 pass
for this one title/model route, not a pass for later stages or all models.

`Tools/a3000-celtic-heart-stage-two-route.json` independently replays the
entire path from a fresh native cold boot without a save. It passed exact
full-frame guards at the live landing (8740), second ascent (8747), airborne
crossing (8764), post-tree movement (8819), mirror approach (8851), level
complete (8907), Stage-2 title (8982), Stage-2 scene (9056), and post-input
Stage-2 scene (9068; hash
`170CBBC2160C7220C70A939F5BC45A2B0CBE4B74246B96FE4C85FE8BD0EB998E`).
JOY1DAT and CIA-A Fire press/release guards passed, with zero non-ROM
exceptions, zero floppy/HDF writes, no final checkpoint and no new
permanent media. The post-input image guard verifies replay stability,
not yet player movement in Stage 2; retain the other unresolved
game/model variants.

Follow-up 2026-10-17 (matched Stage-2 input control): starting from the
same frame-9056 checkpoint, an 800,000-clock neutral wait reaches frame
9068 with exactly the same full-frame hash as the earlier Right pulse and
release (`170CBBC2160C7220C70A939F5BC45A2B0CBE4B74246B96FE4C85FE8BD0EB998E`).
The visual change at 9068 is therefore autonomous animation, not evidence
that the Stage-2 player moved. The later `GET READY / PRESS BUTTON` overlay
at frame 9086 also occurs after a matched neutral 2.1-million-clock wait
from frame 9056 (hash
`8036929963DF384F6021084F05C920B38A1A48BDF012D7DA2BEC9E31E48C6B80`),
so it cannot be attributed to the Right input either. These checkpoint
probes had no non-ROM exceptions or media writes. Fire at that prompt
resumes the live scene by frame 9097. A paired one-million-clock branch
from there isolates an actual Right response at frame 9111: the left-hand
knight has moved from about x=96 to x=143, while the matched neutral
branch leaves him near x=96. The Right branch hash is
`555967BF5CBDBD469971A3A161CD5D16503C8FFF163CF887B67D434C5071308C`;
the neutral branch hash is
`FD3994EB29D210876067A01FC63EDD48769876C11EAD6D79BD6311D902F54A85`.
The extended `Tools/a3000-celtic-heart-stage-two-route.json` passed a
fresh native cold boot through this post-prompt Right movement, including
exact frame-9086 and frame-9111 guards, zero non-ROM exceptions and no
media writes. Stage 2 remains open: continue from this verified movement
through its hazards and objective, then require a visible level-complete
transition before calling the stage passed.

Follow-up 2026-10-17 (Stage-2 terrain traversal): after the verified
post-prompt movement at frame 9111, a fresh `Up+Right` jump carries the
upright knight past the first dead tree by frame 9154. Continued Right
movement scrolls the snowy scene by frame 9182. A second jump clears the
floating hazards at frame 9210, and the knight is back on the ground
approaching the raised stone slope at frame 9238. A third `Up+Right`
input puts him visibly airborne over the slope at frame 9266. The
extended independent cold-start route now asserts exact full-frame
hashes at 9154, 9182, 9210, 9238 and 9266; replay passed with zero
non-ROM exceptions and no floppy/HDF writes. Releasing Up and continuing
Right from 9266 produces a horizontal red pose near the slope by frame
9280 and no clearly visible knight by frame 9294. Pressing Fire during
that interval did not visibly clear the hazard. These later pictures
are not a Stage-2 pass. Next bracket frames 9266-9280 and compare
earlier/later slope takeoff and horizontal release timings, then require
an upright post-slope movement check before proceeding to the objective.

Follow-up 2026-10-17 (slope controls): compact checkpoint branches from
the live frame-9238 approach tested the takeoff 0.5 million clocks
earlier and later. The early branch has no visible knight by frame 9273;
the late branch shows a horizontal red pose on the slope by frame 9287.
Releasing both Up and Right after the original airborne frame 9266 keeps
an upright knight at frame 9273, but by frame 9301 a separate Right
command leaves only the horizontal red pose. Holding Fire with the
cliff jump changes the route: the knight remains upright before the
ledge at frame 9280, and a later fresh Up press visibly ascends at
frame 9322. The apparent red figures beyond that point are not proof
of passage: continued Right input returns the whole scene to the
Stage-2 starting tree by frame 9407, consistent with a death/respawn.
All branches reported expected JOY1DAT inputs and zero non-ROM
exceptions. Keep the independent cold route guarded only through the
reviewed frame-9266 ascent. Next capture short intervals around
frames 9266-9301 and identify the knight versus the moving slope enemy;
vary jump release or Fire timing before contact, then verify survival
by a separate post-slope movement command and image.

Follow-up 2026-10-17 (slope patrol phase): delaying the frame-9238
takeoff by 1, 2, 3, 4 or 5 million chip clocks while releasing Right
during the wait changes the sprite overlap, but none of the checked
branches establishes a durable passage. The one-million-clock wait
shows an upright knight at frame 9294, followed by apparent knockback
at frame 9322 and a return to the opening tree after continued Right.
With a five-million-clock wait, the airborne knight and a ground enemy
are distinct at frame 9351; Fire pauses him at the ledge while the
enemy moves away, but Right after release again returns to the opening
tree at frame 9407 (full-frame hash
`E123656C343398AC559E357023A3F5CA43E09D1A9EC67F4D9C8BDDCE4D2E8D41`).
The bounded runs passed input checks with zero non-ROM exceptions and
media writes. Next inspect a short frame sequence and the game-side
sprite/position state around the ledge; try a Fire pulse only when the
patrol is within range, then require a separate rightward movement on
the *far* side of the slope. Do not count the momentary upright frame
as a level or obstacle pass.

Follow-up 2026-10-17 (GameRunner hash preflight):
`expectedFrameHash` now receives the same 64-character hexadecimal
SHA-256 validation as the native and interlaced expected hashes. Before
this correction, a typo could start a full guest replay and create
diagnostic output before failing only at the affected action. The x64
Release GameRunner build passed; a malformed `BAD` hash was rejected by
`--validate` before output creation, while the existing Stage-2 route
validated. A new native A3000/Kickstart-3.1 cold replay of that route
also passed every stored image/input guard through frame 9266 using
`--no-images --no-final-checkpoint`. This hardens diagnostics but does
not advance the open slope/Stage-2 objective; next isolate the moving
hazard with short input-controlled captures and continue the broader
seven-point hardware and gameplay checks.

Follow-up 2026-10-17 (targeted captures and game-side control): GameRunner
now accepts several `--image-label=LABEL` selectors in one replay, while
retaining all per-action hashes and guards. This avoids repeating the
long Stage-1-to-Stage-2 prefix merely to review two frames and keeps PNG
output small. The x64 Release build and a real A3000/Kickstart-3.1
checkpoint replay passed: exactly the requested frame-9238 and frame-9435
images were recorded, with zero non-ROM exceptions and HDF writes.
[The game's author explains](https://night.owl.de/index.php?id=3) that
Eric's magical strike has a short delay and only a near-range hit;
the [published controls](https://night.owl.de/index.php?id=8) identify
Fire as that strike. The released source archive's `hero.asm` also
suppresses horizontal movement and jumping during a strike animation.
This explains why holding Fire from far before the slope is a poor
passage test. Short Fire presses at two late airborne points still
produced the horizontal collision pose by frame 9280; a long grounded
hold in another patrol phase returned to the opening `GET READY` scene
by frame 9435. These are negative gameplay observations, not an
emulator fault. Next capture the patrol at several nearby frames in
one bounded replay and time a grounded short-range strike, followed
by an independent rightward traversal beyond the slope.

Follow-up 2026-10-17 (grounded strike control): a real A3000/Kickstart-3.1
checkpoint replay pressed Fire for one million clocks from the verified
frame-9238 landing, released it, jumped for three million clocks, landed,
and continued Right for another three million clocks. Targeted captures
show the knight reach the ledge and jump above a separate red ground
enemy; after landing the red horizontal collision pose returns on the
ledge. This is still no durable Stage-2 obstacle pass, but establishes
that simply striking before the ledge does not clear the enemy. The
bounded replay reported expected JOY1DAT values, no non-ROM exceptions
and no media writes. Next bracket the enemy's ledge position, time Fire
close enough after landing or use an airborne bypass, and require a
separate upright rightward movement on the far side before adding a
Stage-2 route guard.

GameRunner's multi-label capture preflight now rejects a misspelled
`--image-label` before creating an output directory. The x64 Release
build passed; a nonexistent label returned exit 2 with no output tree,
while a real Stage-2 route label passed `--validate`. This prevents a
successful scenario exit with silently absent requested visual evidence.

Follow-up 2026-10-17 (Stage-2 ledge contact): two further bounded
A3000/Kickstart-3.1 input branches from the same verified landing kept
the enemy and knight visible separately on the first raised ledge.
Starting Fire while airborne briefly leaves the knight over the moving
enemy, but continuing Right ends with the horizontal collision pose.
Waiting 350,000 chip clocks after landing brings the upright knight
closer to the enemy; starting a grounded Fire strike there still collides,
then the scene returns to the starting tree by frame 9384. Neither
branch advances the level. Both scripts exited normally with no
non-ROM exception or HDF write; the images, not the process exit,
determine this negative gameplay result. Next try the documented
crouching strike (`Down+Fire`) or adjust ledge timing so the short-range
strike activates before contact, then require continued upright
movement beyond the enemy and a fresh independent cold replay.

A fourth branch tested `Down+Fire` from the same near-ledge position.
The controller trace confirms `Down+Right` as JOY1DAT `$0002` and the
Fire edge reached the guest. The enemy remained visible beside the
knight; continued Right returned to the initial tree by frame 9382.
This crouching strike timing also fails the obstacle, without a
non-ROM exception or media write.

Follow-up 2026-10-17 (later moving jump): moving Right for 600,000 chip
clocks after the frame-9238 landing before taking off changes the ledge
contact. The knight appears upright on the ledge at frame 9324, but a
separate two-million-clock Right continuation returns to the Stage-2
starting tree by frame 9352. The momentary upright image is therefore
not a passage. The run reported expected JOY1DAT values, zero non-ROM
exceptions and no HDF writes. Next test an earlier active short-range
strike against the patrolling enemy or a jump landing beyond it, then
require sustained far-side movement and a cold route before a pass.
The ignored targeted replay-output folders are small. A checked
PowerShell cleanup confined to repository `tmp` was rejected by
automatic command review, so those files remain; no ROM, ADF, HDF or
user save was touched.

Follow-up 2026-10-18 (longer ledge jump): holding `Up+Right` for four
million chip clocks from the reviewed frame-9238 landing, then releasing
Up and continuing Right, produced a single upright red figure on the
ledge at frame 9337. A further two-million-clock Right input returned
to the opening `GET READY`/tree scene at frame 9365; the apparent ledge
survival was transient. Sprite identity in a single frame is uncertain,
so this is not a confirmed knight passage. The bounded native A3000
replay held JOY1DAT guards, zero non-ROM exceptions and zero HDF writes.
Next isolate player and patrol sprites with the game's published sprite
frames or an independent memory-state probe, then test one phase-adjusted
landing; require sustained rightward movement on the far side before
extending the independent Stage-2 route. The graphics status table above
has also been reconciled with the already documented native SuperHires
and interlace Workbench tests; hardware-reference parity remains open.

Follow-up 2026-10-18 (A4000 laced application control): reused the
existing private `tmp/os-lace-test.hdf` **read-only** and the guarded
A4000/030 AmigaOS 3.1.4 PAL Hi Res Laced Workbench route. A new bounded
continuation entered the Clock command through Execute Command, using
the already documented A1200 key sequence. The guest opened a `Clock`
screen title and a CLI Output Window; reviewed logical and 704x576
interlaced captures at frame 3705 show that window covering the work
area, so this is **not** a visible running-clock acceptance pass.
Mouse input closed the Output Window and returned to a readable laced
Workbench desktop by frame 4224. Further screen-depth input changed
the instantaneous BPLCON0 sample from `$A306` to `$8307`; the logical
capture at frames 4315 and 4456 showed a clipped screen transition,
while the last completed interlaced pair retained the Workbench image
hash `DC2E418A0C9DE4659254EE1509163184FF3147BA4AD8B41458FCFF96E5E3FD1F`.
That difference may reflect the current beam versus the last complete
field in a multi-screen Copper transition; it is not yet proof of a
renderer defect or of Clock operation. The guarded replay reported zero
non-ROM exceptions and zero HDF writes, with a compact ~1.1-MB ignored
output folder and no new HDF copy. The following continuation resolves
the missing visible Clock control; the mixed-screen transition still
needs independent reference comparison. The CLI window and clipped
current field alone were not counted as an application pass.

Follow-up 2026-10-18 (visible A4000 laced Clock): from the same guarded
cold A4000/030 Kickstart 3.1.4 route, moving the pointer to the top-left
of the CLI Output Window (`x=10, y=32`) and clicking its close gadget
exposed the **running analog Clock face** at frame 3815 in the completed
704x576 interlaced pair. After another 100 million chip clocks, frame
5225 showed the hand position changed while the Clock window remained
open (144 changed pixels inside the face on the guarded cold replay,
bounded to image coordinates 84–134, 100–108). Clicking the Clock close
gadget (`x=10, y=18`) returned to the readable PAL Hi Res Laced
Workbench at frame 5313. All three images were
visually reviewed, with BPLCON0 `$A306`, zero non-ROM exceptions, and
zero writes to the existing read-only private HDF. Repeated cold runs
showed different Clock image hashes, so the new reusable
`Tools/amigaos314-a4000-laced-clock.json` guards the mode and field-pair
presence rather than an invalid fixed live-clock hash. This establishes
one real A4000 OS application launch, visible operation, and clean exit;
it does not close the wider application/controller matrix. Next compare
the 020/030 mixed-screen/field timing against independent reference
captures and run another application/controller transition with the
same read-only HDF; preserve the installed source image. The two compact
ignored A4000 Clock replay-output folders total about 2.1 MB. Their
resolved paths were verified inside repository `tmp`, but automatic
command review rejected recursive deletion; they remain for manual
cleanup. No other data was removed.

Follow-up 2026-10-18 (Stage-2 patrol strike timing): inspected the
author-published Celtic Heart source archive in ignored `tmp` without
building or altering the game. `monster.asm` identifies the spider
variants as killable only by a cowering strike; `hero.asm` starts that
strike three animation phases after the Fire edge and suppresses
horizontal movement while cowering. This was an initial reason to test
Down+Fire, but the source-map inspection below distinguishes the red
ledge patrol from a spider; these crouching tests remain negative controls.
Three bounded A3000/Kickstart-3.1 continuations from the verified route
varied the post-jump approach to 50,000 or 500,000 chip clocks and the
pre-Fire Down hold to 200,000 or 1,200,000 clocks. JOY1DAT confirmed the
Down+Right input (`$0002`), all runs had zero non-ROM exceptions and
zero floppy/HDF writes, but visually reviewed frames 9378, 9384 and
9396 all returned to the Stage-2 opening tree after contact. A longer
Down hold had already lost the left-hand figure by frame 9351. These
are deaths/respawns, not patrol clears; the screenshots do not yet prove
that the hero was grounded and actually crouched when Fire arrived.
Next capture short intervals around the first ledge landing, identify
the hero and patrol separately using the published sprite frames, then
time a standing strike against the red patrol. Require sustained upright
movement beyond the ledge and a fresh cold-route replay before
recording a Stage-2 advance.

Follow-up 2026-10-18 (source-map correction): the source archive's
`maps/level02.tmx` places a red enemy object with GID 681 at world
`x=432, y=176`; its monster sheet starts at GID 641, giving animation
index 40. `monster.asm` uses index 40 for `moveKnight`, whose comment
does **not** require a cowering strike. The extracted `gfx/hero.bmp` and
`gfx/snowenemy.bmp` provide a way to distinguish the hero from the red
knight-type enemies. The first ledge patrol's exact source-map object
still needs a scroll-offset/position match, but the earlier
spider-specific inference was too broad for this obstacle. `hero.asm`
delays a standing strike four animation phases after Fire, so the next
bounded test must trigger it before contact at matching height and
inspect a short frame sequence for an enemy death animation and durable
passage.

Two A3000/Kickstart-3.1 standing-strike continuations triggered Fire
10,000 or 150,000 chip clocks after the frame-9330 landing step, then
held Right long enough to test sustained passage. The reviewed
frame-9344 close-up shows the hero's yellow strike flash but the
red patrol remains beyond its visible edge; subsequent frames lose
the hero, and frames 9407/9409 show `GET READY` at the opening tree.
Both runs had zero non-ROM exceptions and zero floppy/HDF writes. This
is still a gameplay failure, not a strike hit.

Follow-up 2026-10-18 (ledge-contact timing): a neutral-input control
after the supposed frame-9330 landing held JOY1DAT at `$0000`, yet the
left-hand upright red figure walked right while the right-hand low
figure stayed fixed. Short captures at frames 9313, 9317, 9321, 9325,
and 9330 show the airborne figure descending and a low horizontal pose
by frame 9330. This is consistent with a death animation already under
way, so the later Fire probes cannot establish a valid standing strike
against a living, grounded hero. A separate direct ledge jump without
the preceding ground strike likewise shows the low pose at frame 9301;
its frame-9344 image has returned to the opening tree instead of
demonstrating a far-side pass. All three continuations had zero non-ROM
exceptions and zero floppy/HDF writes. Stage 2 remains open. Next vary
the jump trajectory or attack **before frame 9325** and capture the
contact frames closely enough to identify the hero and knight by their
source sprites. Accept progress only with sustained upright far-side
movement, then replay the successful route from a cold start.

Follow-up 2026-10-18 (Stage-2 ledge variations): four more bounded
A3000/Kickstart-3.1 continuations branched from the verified frame-9238
orb landing. Delaying the ledge jump 600,000 chip clocks still produced
the low pose on the upper platform and returned to the opening tree by
frame 9352. Three airborne Fire starts at 1.2, 1.4, and 2.0 million
clocks after takeoff briefly showed an upright airborne or ledge figure,
but the longer Right holds all returned to `GET READY` by frames 9410,
9413, and 9421 respectively. Those brief upright images are **not**
durable passage or proof of a patrol kill. All four tests had zero
non-ROM exceptions and zero floppy/HDF writes; they mounted no HDF.
Next inspect the first collision in smaller intervals and match the
hero/knight sprites and their world coordinates before changing attack
timing again. The visible sequence alone does not yet distinguish a
missed strike from an upper-platform collision or hazardous tile.

Follow-up 2026-10-18 (contact bracket): six 150,000-clock captures
after the 1.4-million-clock airborne Fire branch narrow the first
upright-to-horizontal change to frames **9286–9288**. At frame 9286 the
left-hand figure is upright on/above the upper ledge with another red
figure farther right; at frame 9288 it has entered a low horizontal
pose that persists through frame 9299. The game's `monster.asm`
`hero_coll` kills the hero when the rectangles overlap, and the
source-map knight at world x=432 is spatially consistent with the
right-hand figure, but the captures alone do not yet prove the exact
object or rule responsible. Airborne Fire started at 1.6 and 1.8
million clocks after takeoff also briefly showed an upright ledge
figure, then returned to `GET READY` by frames 9415/9418. Both had
zero non-ROM exceptions and zero floppy/HDF writes. Next measure the
world-to-screen offset and strike rectangle at frames 9284–9288,
then adjust the first Fire trigger only if its active rectangle can
actually meet the knight before hero contact; replay any candidate
through sustained far-side movement from a cold start.

A further 1.4-million-clock airborne-strike branch inserted a second
Fire press after only 900,000 clocks of renewed Right input. Its first
selected frame already shows the low horizontal pose on the ledge;
the next frames show a red figure upright but the run still reaches
`GET READY` at frame 9370. The apparent second attack therefore does
not establish a living hero's successful strike. It had zero non-ROM
exceptions and zero media writes. The immediate next diagnostic is a
short-interval capture between the first Fire release (frame 9286 in
this branch) and that low pose, rather than more post-contact attacks.

Follow-up 2026-10-18 (verified first Stage-2 ledge): changing the
trajectory, rather than only the attack timing, passed the obstacle.
From the guarded frame-9238 orb landing, Up+Right runs for 1,200,000
chip clocks; releasing Up and Right leaves a neutral 1,400,000-clock
landing interval. Fire is then held and released for 1,000,000 clocks
each while stationary, before holding Right for three successive
3,000,000-clock intervals. The reviewed images show the hero upright
on the first ledge at frame 9303, then farther right at frame 9387,
and at the next raised ledge beside the castle and a new enemy at frame
9429. There is no `GET READY` or return to the opening tree. This is
**sustained Stage-2 progress past the first ledge**, not a completed
Stage 2 or proof that the knight was killed.

`Tools/a3000-celtic-heart-stage-two-first-ledge-route.json` replays
the entire route from a real A3000/Kickstart-3.1 DF0 cold boot, with
no save loaded, through Stage 1 completion and the Stage-2 guard.
The existing frame-9238 guard and all seven new nonzero-cycle frames
matched their full SHA-256 image hashes from the independent checkpoint
continuation, including frames 9303, 9387 and 9429. The cold run ended
at frame 9429 with zero non-ROM exceptions, zero floppy/HDF writes and
visually reviewed live play. The route then applies Up+Right for
1,200,000 clocks, releases both directions for a neutral 1,400,000-clock
landing, and waits another 2,000,000 clocks. A second full cold replay
matched the three added full frame hashes at frames 9446, 9466 and
9494. Visual review shows the hero airborne over the next rise and
then upright on its upper platform by the castle; a moving enemy and
orb remain farther right. The second cold run also had zero non-ROM
exceptions and zero floppy/HDF writes. Next test controlled movement
and a timed attack near those hazards, with a later frame proving the
hero remains active beyond them. Do not mark Stage 2 complete until
its actual transition is visible and replayed.

Follow-up 2026-10-18 (castle approach controls): from the guarded
second ledge at frame 9494, another 2,000,000 clocks of Right reaches
the castle frontage at frame 9522 with the hero still upright near an
orb and several moving enemies. Holding Up+Right through the obstacle
and then continuing Right returned to the opening tree by frame 9638.
A second branch released both directions for a neutral landing, tried
a stationary Fire strike, then continued Right; its short-lived castle
figures did not establish a hit or passage and it too returned to the
opening tree by frame 9714. Both branches had zero non-ROM exceptions
and zero floppy/HDF writes. The verified cold route still ends safely
at frame 9494. Next bracket the orb/hero contact and enemy movement
near the castle, then test a timed strike or jump with a long enough
continuation to rule out a delayed respawn before extending the cold
route.

Follow-up 2026-10-18 (early castle jump): from the same guarded frame
9494, Up+Right began immediately, before the separate 2,000,000-clock
castle approach. The hero was airborne at frame 9529; the later castle
images at frames 9550–9621 did not show a sustained passage. Continuing
Right for another 6,000,000 clocks returned to the Stage-2 opening tree
at frame 9705, confirming a delayed respawn. The run reported zero
non-ROM exceptions and zero floppy/HDF writes. Keep the verified cold
route at frame 9494. Next capture shorter intervals around the castle
contact and time Fire against the first nearby enemy or orb; require
continued live movement beyond the castle before adding a route guard.

The early-jump variant with Fire held in midair from frame 9508 to
9520 and released at frame 9529 still showed the hero near the castle
approach at frames 9634 and 9718. A second Up+Right jump moved the
camera across more of the castle front by frame 9836, but continued
Right ended on the Stage-2 `GET READY` opening at frame 9921. Thus the
intermediate castle image is **not** a passage or Stage-2 completion.
This branch also recorded zero non-ROM exceptions and zero floppy/HDF
writes. Next capture the second-jump contact between frames 9766 and
9836 in shorter intervals and vary attack timing against the orb and
moving enemies; only add a new cold-route guard after a sustained live
screen beyond those hazards and a clean independent DF0 replay.

Follow-up 2026-10-18 (castle contact bracket): the latter branch was
replayed with ten 500,000-chip-clock observations at frames 9773–9836.
The castle view scrolls through frame 9801, then stays around the same
group of red enemies, pink hazards and an orb. The small red sprite in
that group is ambiguous against the game's published `hero.bmp` and
`snowenemy.bmp` sheets; these intermediate frames do not prove the
hero survived. The original continuation reaches `GET READY` at frame
9921. Triggering Fire at frame 9787, or issuing a further Up+Right jump
there, also ended back at the opening tree by frames 9919 and 9914.
`hero.asm` confirms that a running one-shot strike suppresses horizontal
movement and jumping, making attack timing material. All three bounded
branches recorded zero non-ROM exceptions and zero floppy/HDF writes.
Next identify the hero's alive/death state at the first castle contact
with a guest-memory or sprite-state probe, then move the strike or jump
before that contact and require a sustained frame beyond the castle.

Follow-up 2026-10-18 (guest-state confirmation): GameRunner now permits
read-only byte watches in a desktop board's motherboard Fast RAM and an
explicit, bounded `memory-snapshot` action. The latter produced a
65,536-byte snapshot without changing the frame, framebuffer hash or CPU
debug state; invalid sizes and the address immediately beyond Fast RAM
were rejected by `--validate`. Comparing snapshots with the published
`hero.i` status layout located this Celtic Heart build's `HSalive` at
`$07FF750A` and `HSflags` at `$07FF751C` (game-instance addresses, not
portable symbols). A fresh A3000/Kickstart-3.1 checkpoint replay watched
those bytes through the castle contact: at frame 9794 they were
`FF/00` (alive/no death flag), and at frame 9801 `00/40`
(deactivated/invulnerable). This proves the apparent red sprites after
frame 9801 in the unmodified branch are enemies or death animation, not
surviving gameplay.

Fire triggered at frame 9794 was too late: `HSalive` had cleared by
frame 9798. Triggering it at frame 9791 retained `FF/00` at frame 9801,
but the next 500,000-clock interval ended `00/40` at frame 9808;
additional Up or Fire inputs at frame 9801 also failed. The corresponding
screen sequences and continued runs still return to `GET READY`, so no
castle passage or Stage-2 completion is claimed. These diagnostic runs
had zero non-ROM exceptions and zero floppy/HDF writes. Next use the
confirmed alive-state watch to test an earlier launch/strike that is
still active before frame 9801, then continue through a distant castle
frame and repeat any successful sequence from a DF0 cold boot.

Follow-up 2026-10-18 (earlier castle actions): the guest-state watch
confirmed that an Up+Right jump started at frame 9787 (`HSflags=01` at
9791), but the hero was dead by frame 9797; jumping at frame 9780 had
already landed by frame 9794 and died by 9805. Holding Fire from frame
9789 kept `HSalive=FF` through frame 9803, but a second Fire at 9803
ended `00/40` by frame 9810. A shorter Fire pulse at frames 9789–9792
kept the hero alive through 9806, then the unchanged Right continuation
ended `00/40` by 9813. Adding Up+Right at 9806 produced the same death
frame. Visual inspection still shows the hero left of multiple red
enemies and an orb at the castle; none of these intermediate images
establishes a passage. All new branches had zero non-ROM exceptions and
zero floppy/HDF writes. The verified cold route remains at frame 9494.
Next vary the castle approach before frame 9780 (including a short
neutral pause or an earlier strike) and use the live-status watch plus
later images to require sustained movement beyond the enemy/orb group;
only then extend and independently replay the DF0 cold route.

Follow-up 2026-10-18 (castle patrol wait controls): releasing Right at
frame 9780 for 1,000,000 clocks left the hero alive at frame 9794, but
resumed Right died between frames 9801 and 9808. A longer 5,000,000-clock
neutral wait at 9780 itself ended `HSalive=00/HSflags=40` by frame 9850.
Releasing Right earlier, at frame 9766, for 2,000,000 clocks also ended
`00/40` by frame 9794. Thus pausing at either of these castle-front
positions is unsafe; the nearby hazards can reach a stationary hero.
The live-state watch and image review reject all three branches as
Stage-2 passages. No non-ROM exceptions or floppy/HDF writes occurred.
Next change the path before the second castle-front jump (frame 9718),
keeping the hero above or behind the moving hazards, then bracket the
first enemy/orb contact with the same alive-state probe. Do not extend
the guarded cold route without a sustained live continuation.

Follow-up 2026-10-18 (live gameplay assertion): GameRunner now accepts
per-action `expectedMemoryBytes` for up to 32 validated, read-only RAM
bytes. This makes a reviewed scene fail if the guest hero has died even
when enemy/death sprites resemble ongoing play. A positive checkpoint
replay asserted Celtic Heart's `HSalive=FF/HSflags=00` at frame 9794 and
`00/40` at frame 9801; a negative control requiring `FF` at 9801 failed
with the actual `$00`, and an out-of-range Fast-RAM address failed
validation. The full DF0/Kickstart-3.1 cold route to the guarded second
ledge now asserts `FF/00` at frame 9494 as well as its existing exact
frame hash. The fresh replay passed with zero non-ROM exceptions and zero
floppy/HDF writes. This proves the guarded ledge is live gameplay, not
just a matching image. On a separate checkpoint branch, moving the
second castle-front jump 1,000,000 clocks earlier or later and
3,000,000 clocks later still ended `00/40` near frames 9787, 9808 and
9808 respectively. No Stage-2 completion is claimed. Next find a route
that keeps `HSalive=FF` well beyond the castle enemies and orb, then
extend the full cold guard and visually confirm a level transition.

Follow-up 2026-10-18 (castle cower/strike sequence): the game's published
`hero.i` identifies flag `$10` as cowering, and the snow-enemy sprite
sheet contains the large pink monsters visible at this castle frontage.
From the same live checkpoint, Down at frame 9780 set `HSflags=10`;
Fire during the crouch and a return to Right kept `HSalive=FF` through
frame 9829, past the ordinary branch's death at frame 9801. Continued
Right alone then died by frame 9836. A second Fire at 9829 kept the hero
alive through frame 9878 with visibly changed castle frontage; without
a further action he died by frame 9885. An earlier third Fire at 9864
extended live status to frame 9885 but died by 9893. A jump begun at
9871 was genuinely airborne (`HSflags=01` at 9876), landed by 9883,
and died by 9890; a Fire started at 9878 also failed by 9893.
These are checkpoint branches, not yet full DF0 cold-route guards or
a sustained passage beyond the whole hazard group. The image at 9878
still contains an orb and hostile sprites, and no level transition is
visible. Runs had zero non-ROM exceptions and zero floppy/HDF writes.
Next bracket the remaining contact between frames 9878 and 9893;
try an attack or jump before the next collision, then require a live
continuation beyond the castle and independently replay it from cold.

Follow-up 2026-10-18 (late castle contact/restore): an explicit private
checkpoint at live frame 9864 (`FF/00`) restored with the exact same
frame hash, and unmodified Right reproduced the hashes at frames 9871,
9878 and the dead frame 9885. This allows bounded input comparisons
without repeatedly rebuilding the long Stage-1/Stage-2 prefix; the
published cold route remains the independent acceptance gate. Cower
plus Fire begun at 9864 retained `FF/10` through frame 9891, but
normal continuation died at frame 9897. A one-frame contact bracket
showed cower clearing at frame 9895 and death within two frames.
Jump or standing Fire at 9895 also died at frame 9897. Re-cowering at
9893 retained the live state through frame 9907; releasing Down died by
9914, and a longer Down hold still died by 9928. Re-cower plus another
Fire died by 9912. These results show that crouching delays contact but
does not yet clear the hazard. Next vary the third strike earlier than
9864 and inspect whether the hostile orb/monster is actually removed;
require live Right motion after the crouch before claiming a passage.

Follow-up 2026-10-18 (earlier strike/retreat controls): a second private
checkpoint at live frame 9850 restored exactly and reproduced the
unchanged frame hashes at 9857 and 9864. Standing Fire already pressed
at 9850 did not improve the unmodified death at 9885; an attempted
crouch at 9850 did not initially set the crouch flag, consistent with
the preceding strike animation still inhibiting the action. A crouch
initiated at 9857 became active by 9866 but also died by 9891.
From the effective 9864 crouch branch, reversing Left at 9893 and then
resuming Right kept `HSalive=FF` to frame 9914, but continuing Right
alone died by 9921. A preemptive standing Fire after the retreat kept
the hero alive to 9922, and crouch plus Fire after the retreat retained
`FF/10` through frame 9928; the next observation at 9935 was `00/50`.
The castle enemies/orb remain visible in the captured active scene;
these timing changes delay contact, not a verified passage or Stage-2
completion. The short private checkpoint files were removed after the
comparisons; normal saves and source media were untouched. Next inspect
the guest enemy/collision state around the last live frames, then time
an actual enemy removal or alternate jump path and replay any sustained
success from DF0 cold boot.

Follow-up 2026-10-18 (guest-coordinate control): the Celtic Heart
source defines the hero's world X/Y words at offsets 4/6 of `Hero`.
Watching the corresponding live guest RAM on the reproducible Stage-2
branch yielded X/Y `452/121` at frame 9494, `480/121` after the first
castle jump, `638/105` at the second strike, and `668/105` at the last
live observation at frame 9878. The unchanged Right continuation died
at frame 9885 with X/Y `682/105`. Two alternate jumps begun at 9874
and 9878 also died, by frames 9890 and 9887 respectively. This bounds
the hazard in world coordinates; jumping or striking that late has not
passed it. The cold-start first-ledge route now additionally asserts
its live hero position at frame 9494. Next bracket the contact between
world X 668 and 682 with shorter steps, identify the responsible enemy
or orb, and test an earlier evasive/attack input. Require sustained live
rightward world-X motion and then a visible level transition before
calling Stage 2 complete.

Follow-up 2026-10-18 (castle-contact bracket): a restored, hash-matched
Stage-2 continuation advanced the live hero from world X/Y `668/105`
at frame 9878 to `680/105` at frame 9884; the next observed frame,
9885, was dead at `682/105`. The corresponding six selected frames
show an on-screen moving round foe/projectile and another red character;
the exact damaging object is still unproven. An Up input begun earlier
at frame 9857 briefly raised the hero to Y=100 but landed by frame
9871 and died at the same X=682/frame 9885. Standing Fire begun at
frame 9864 held X=642 during its attack and delayed death to frame
9893, without clearing the hazard. These are negative gameplay controls,
not a Stage-2 pass. Next identify the moving object's type and collision
window from guest state or a denser visual bracket, then test an input
that demonstrably removes or avoids it and verify sustained live X>682
from a full DF0 cold start.

Follow-up 2026-10-18 (strike latency and map evidence): the Stage-2
source map places a `MONSTER` sprite with sheet index 100 at X=768,
Y=144; the game source identifies that index with the low spider
animation and comments that the spider needs a cowering attack. This
is a plausible identity for the round foe at the castle, but its exact
guest MOB and the fatal collision have not yet been linked. RAM watches
of `HeroStrike` show why late crouch/Fire attempts failed: Fire at
frame 9880 started the hero animation, but the strike MOB remained
inactive through frame 9891, before the hero died. An earlier cowering
Fire at frame 9866 launched a low strike at X=658 by frame 9891, kept
the hero alive at X=642 through frame 9919, then died by frame 9926.
Waiting in crouch until frame 9878 before Fire died even earlier, by
frame 9897, before its delayed strike became active. Skipping the
preceding second standing strike was also fatal by frame 9836 at
X=642, so that attack cannot simply be removed from this route.
Next trace the spider MOB's world position/active state against the
strike MOB's active animation window and adjust earlier positioning or
attack timing; a new route counts only after live movement past the
hazard is reproduced from a DF0 cold boot.

Follow-up 2026-10-18 (fatal actor identified): a private frame-9880
checkpoint and the known hero RAM signature let us locate two adjacent
80-byte monster records in the serialized guest RAM, then confirm their
addresses through live GameRunner memory watches. The map's X=768
spider (type 8) was at world X=746/Y=121, moved only to X=739 by
frame 9894, and was not the immediate contact. The neighboring
X=624-origin knight (type 6) was active and visible at X=696/Y=105
while the crouched hero was at X=670/Y=105. It advanced to X=682 by
frame 9894 and X=675 at the first dead observation at frame 9901;
the spider was still at X=746 then. Thus the next action must clear
or evade the approaching knight before addressing the spider. A
standing Fire attempted at frame 9869 from hero X=650 did not create
a fresh strike: the previous strike MOB was still active, the knight
remained active and the hero died by frame 9890. This explains why
simply adding another early Fire did not help. Next inspect the two
knight records and strike cooldown around frames 9829–9885, then
alter the prior attack/movement sequence so a new valid strike or
other evasion precedes knight contact. Preserve the Stage-1 and
first-ledge cold-route gates; claim passage only after the live hero
moves beyond X=682 and the change replays from DF0 cold boot.

Follow-up 2026-10-18 (castle knight passed): holding at hero X=640
after the earlier strike, releasing Right, waiting for its cooldown,
and pressing standing Fire at frame 9876 produced a fresh strike at
X=656 by frame 9904. The approaching knight changed to killed
animation type `00` by frame 9918 while `HSalive=FF`. Resuming Right
then passed the old fatal X=682 alive at frame 9974 and reached world
X=738 alive at frame 10002. This route was independently replayed
twice from a DF0 disk-1 cold boot (A3000/Kickstart 3.1), through the
completed Stage 1 and the previous first-ledge guards. The durable
`Tools/a3000-celtic-heart-stage-two-knight-passage-route.json` asserts
the exact final framebuffer SHA-256
`85E683C77288CFB38DA94DA54BC7F3688BE189992C2E40E69477FF451FDEBCC4`,
live hero X=738, killed knight type, and active next spider; no final
checkpoint was generated. A continued Right input subsequently died
at world X=740 by frame 10009 with the spider active, so Stage 2 has
not been completed. Next use the live X=738 approach to time the low
cowering strike against that spider, then require a visible level
transition and repeat the full cold route. The two short private test
checkpoints created during the knight analysis were removed afterward;
original media and normal saves were untouched.

Follow-up 2026-10-18 (Stage-2 spider passed): the elevated walkway
left the hero's standing strike above the spider's hit box; two jump
attempts and continued Right died before passage. From live world
X=710, a brief Down/wait let the spider move right, then Right moved
the hero to X=740 and the lower ground. Down+Fire produced a low
strike at X=756/Y=133. The spider's guest MOB changed from type `08`
to killed animation type `00` at frame 10067 while `HSalive=FF`.
After rising and moving right, the hero reached X=796/Y=121 alive at
frame 10149, beyond the former fatal contact. The full route in
`Tools/a3000-celtic-heart-stage-two-spider-passage-route.json` replayed
from DF0 disk 1 cold boot on native A3000/Kickstart 3.1, with memory
guards for the killed spider and living hero and exact final framebuffer
SHA-256 `B3DA26C829022A6209111FBF26EE59130F2A6ACA00D4E7A7B0654B4710C36663`.
The inspected frame shows the hero at the castle's right edge with a
gap and floating orb ahead, not a level transition. Holding Right
without jumping falls from X=810 and is dead by frame 10170 at
X=838; this continuation is a negative control, not passage. Stage 2
remains open. Next time a jump from the guarded X=796 edge across the
gap, account for the orb, and require visible level completion plus a
second cold replay before marking Stage 2 complete. This run produced
no final checkpoint and made no floppy/HDF writes. Two short private
checkpoints from the exploratory frame-9988/10002 runs remain only in
the ignored repository `tmp` directory because automatic approval
review blocked their explicit removal; remove those exact files when
the policy permits.

Follow-up 2026-10-18 (first moving platform reached): jumping with
Right+Up from the guarded X=796 castle edge kept the hero alive over
the gap. Releasing Right near world X=852 let the hero land at
Y=130 on the small horizontally moving platform. With controls
released, it carried the hero left to X=810 and then right to X=894
at frame 10325, still alive. The complete DF0 disk-1 cold route in
`Tools/a3000-celtic-heart-stage-two-moving-platform-route.json`
asserts this guest position and exact framebuffer SHA-256
`DFAFE27542117E1B50AED920D90F069EC2A4D41C688351E9514039AEC1C83FA5`;
the reviewed image shows the hero standing on that platform, with a
pillar and another moving platform ahead. Blind Right during the
first leap falls into the gap and dies by frame 10212. A second
Right+Up jump from platform X=852 and one from its rightward X=894
phase both miss the next safe surface and die at frames 10346 and
10388 respectively. These are negative controls, not completion.
Next inspect the moving-platform and pillar collision geometry and
vary the second jump's launch phase and Right release so the hero
lands alive on the next support. Then demand a visible Stage-2
transition and cold replay before calling the level complete. No
floppy/HDF writes or final checkpoint were produced.

Follow-up 2026-10-18 (second moving platform and far castle): the
source level map places two moving supports over the gap. A second
Right+Up jump from the first platform at X=894 overshot the next
support and died when Right remained held. Releasing Right midair at
X=947 instead landed at Y=130 on the second moving platform at frame
10374; it carried the live hero right to X=1038 by frame 10458.
Another Right+Up jump reached the far castle floor at Y=121 and X=1251
alive at frame 10564. The inspected frame shows an active black ground
enemy ahead; it is not a Stage-2 transition. The complete route in
`Tools/a3000-celtic-heart-stage-two-second-platform-route.json` replayed
from a DF0 disk-1 cold boot on native A3000/Kickstart 3.1, asserting
the hero's live coordinates and exact framebuffer SHA-256
`BBDE509C08E8A3C4502DE35966CC32C9B73FB721CF718C6502189CEBF761E6AE`.
The prior overshoot remains a negative control. Stage 2 remains open:
next identify and pass the far-castle enemy with actual input, then
continue until a visible level transition and reproduce it from cold
boot. No floppy/HDF writes or final checkpoint were produced.

Follow-up 2026-10-19 (far-castle patrols, pillars and ice gap):
continuing Right from X=1251 reached the lower path, but died at the
first ground patrol by frame 10683. An early or late standing Fire
also failed. Starting a Right+Up jump at X=1475 cleared the first
pair of ground enemies, then releasing Up on landing and jumping
again at X=1615 cleared the second pair; the hero reached X=1827
alive. At X=1837 a new ground hazard killed an unguarded walk.
Jumping from X=1813 cleared it and reached the short pillar at X=1903.
A released, rearmed jump landed on top of that pillar at X=1927/Y=137;
another carefully stopped jump landed on the tall pillar at
X=1999/Y=105. A Right+Up leap from there reached the upper ice
platform alive at X=2167/Y=57. Blind continued Right died at X=2215;
early and late attempted jumps into the next spike patrol also died.
Stopping horizontal movement midair at X=2224 instead survived and
dropped through the gap between ice platforms to the lower ground at
X=2228/Y=185. A further Right+Up jump passed the ground patrol and
reached the far building at X=2448/Y=185 alive, frame 11479.
The complete `Tools/a3000-celtic-heart-stage-two-far-building-route.json`
replayed from DF0 disk-1 cold boot on native A3000/Kickstart 3.1,
asserting a living hero at X=2448 and exact framebuffer SHA-256
`353A88DA57BEBB2FA4EA104DDB9F86F77D05554CE938DBCAF38EC76BB65FB4D0`.
The frame was visually inspected: it shows active game graphics and
the hero at the building face, not a level-completion screen. A
rearmed Right+Up attempt into this face died at X=2498/Y=169 by
frame 11521; the cause is still unresolved and this is a negative
control. Next inspect the building's tile collision/entrance and
the nearby guest actor state, vary the approach and inputs, and
require a visible Stage-2 transition followed by an independent cold
replay. Stage 2 and the broader roadmap remain open. These runs
made no floppy/HDF writes and generated no final checkpoint.

Follow-up 2026-10-19 (far-building ground approach): three private
continuations from the existing far-castle checkpoint isolate the
obstacle. With Right held and Up released, the living hero remains at
X=2448/Y=185 for 70 frames, so the facade blocks a ground-level walk.
A vertical jump with Right released rises to Y=135 and returns to
X=2448/Y=185 alive; the later death while standing there is a separate
incoming-enemy collision, not evidence of a passage. A run that rearms
Up earlier at X=2396 and jumps while moving Right reaches X=2448 at
Y=138, then falls to Y=169 while crossing toward X=2498 and dies.
The framebuffer at the face shows the hero below the dark foundation's
top and no visible ground-level doorway. These observations narrow the
next test to the elevated ice-platform route: time the upper spider
and spike gaps, approach the building above its foundation, and verify
the entrance and Stage-2 transition with a full DF0 cold replay. These
negative probes used only ignored `tmp/` outputs, produced no final
checkpoint, and did not write floppy or HDF media. Stage 2 remains open.

Follow-up 2026-10-19 (farther upper ice ledge): the first attempt to
continue over the upper ice killed the hero at X=2262/Y=57; a later
jump died at X=2277. Releasing Right at X=2224 instead let the hero
reach X=2228/Y=57 alive between the raised ice sections. Releasing Up
there and pressing it again produced a new, visibly rising jump; with
Right held it cleared the second spider. Stopping Right at X=2298
landed the hero alive at X=2300/Y=57, sustained for ten 500,000-clock
intervals. Stopping earlier at X=2284 died on the spider; walking or
jumping onward from X=2300 drops to the lower path and is not a roof
passage. The complete independent DF0 cold route
`Tools/a3000-celtic-heart-stage-two-ice-beyond-spike-route.json`
reproduced the living upper-ledge position at frame 11387 with
framebuffer SHA-256
`7945FB72A35FAC1A02FB8D09FDAAB247D4CA17A167286E62F5FFE58CC02AD2FF`;
the screenshot shows active play on the upper ice, not level completion.
Next determine how to bridge the remaining upper-to-building gap or
reach a real ground entrance, then require a visible Stage-2 transition
and full cold replay. The runs wrote no floppy/HDF media or final
checkpoint. Stage 2 and the seven-point roadmap remain open.

Follow-up 2026-10-19 (far-building lower corridor): the published
`w1types.tmx` tile attributes in the Celtic Heart source archive show
that the ice castle is solid around a narrow lower passage. At
X=2448/Y=185, Right alone is blocked by the raised floor. Right+Up
enters the passage at Y=169, but an unmodified continuation died at
X=2498. Releasing Right at X=2496 kept `HSalive=FF` for 84 frames;
resuming directly still died at X=2500. A Fire edge at X=2496,
followed by release and Right, allowed living movement through the
passage to X=2576/Y=169, where the next raised step blocks Right.
The complete independent DF0 cold route
`Tools/a3000-celtic-heart-stage-two-castle-corridor-route.json`
reproduced frame 11612 with `HSalive=FF` and exact framebuffer SHA-256
`AB8CF1B3F03DFB91A7F590E00F6597F0C94F03D2019E1BFAF8B336E56343A2CC`.
The inspected image shows the hero standing inside the castle corridor,
with a pink hazard behind and another red sprite ahead; it is active
gameplay, not a level transition. Right+Up from the raised step,
earlier jump timing, a preliminary Fire, a midair Fire and a cowering
strike all ended with `HSalive=00` between X=2596 and X=2634. They
remain negative controls. Next identify and time the nearby moving
hazard using the game's monster/hero state, clear the step while alive,
then extend the independent cold route toward the actual Stage-2 exit.
The probes wrote no floppy/HDF media or final checkpoint. Stage 2 and
the wider roadmap remain open.

Follow-up 2026-10-19 (raised-step timing): private `--no-final-checkpoint`
continuations of the corridor route shifted the wait before the first
Fire strike by 2, 4 and 8 intervals of 500,000 chip cycles. Two extra
intervals still reached X=2630/Y=137 alive at frame 11641 but died at
X=2632 by frame 11648; four and eight extra intervals died earlier in
the lower corridor at X=2508 and X=2500. An aerial Fire edge at
X=2616, or after finely spaced approaches to X=2620/2622/2624/2626,
left the hero stationary during the strike and then dead at or before
X=2628. Starting Fire at X=2628 was already too late. These are
negative controls, not a passage or game regression. The level source
places monster tile 40 (TYPE 6) at X=2688/Y=160, a candidate for the
red moving sprite near the raised step; its live position and collision
must still be confirmed. Next measure that monster's position and
attack-active interval near frames 11635-11648, then time a strike or
avoidance route that remains alive beyond X=2632. The tested saves and
outputs were private files under ignored `tmp`; no disk media, normal
save, or final checkpoint was written. Cleanup of only this follow-up's
regenerable scenarios and run outputs was rejected by the automatic
command approval review (`Remove-Item -LiteralPath ... -Recurse -Force`,
with each resolved target checked inside repository `tmp`); these
ignored files remain and must be removed when permitted. Stage 2 and
the roadmap remain open.

Follow-up 2026-10-19 (castle knight phase): instead of striking at the
raised step, the hero can release Right at X=2568/Y=169 and remain alive
while the upper knight traverses its patrol. Screenshots at frames
11605, 11648, 11676, 11704 and 11732 show the red sprite move left,
turn, and return right. A jump after 28 stationary intervals of 500,000
chip cycles avoids the immediate collision and carries the hero alive
past the former X=2632 limit to X=2758/Y=137 at frame 11887. Holds of
20, 24 and 32 intervals instead died at X=2654, X=2642 and X=2656;
the 28-interval timing is a measured window, not a general immunity.
The independent DF0 cold-start route
`Tools/a3000-celtic-heart-stage-two-knight-phase-route.json` reproduced
the private result with exact framebuffer SHA-256
`B55F579C0DDB940DA14076CCB69EC1932CFA2B068B9E1FF5F2263473CC33EC90`,
`HSalive=FF`, X=2758, Y=137, zero non-ROM exceptions, and an inspected
active-gameplay image. Continuing Right, or stopping at X=2758, still
causes death at X=2760-2762 by frame 11894; the screenshot shows the hero
and another red sprite but does not identify the fatal contact. Next
isolate that collision with shorter frame steps and the castle tile/
monster definitions, then extend the verified cold route. Stage 2 is
not complete. The cold replay used `--no-final-checkpoint` and wrote no
floppy/HDF media or normal save. Regenerable `tmp` outputs remain
because the previous automatic cleanup rejection still applies.

Follow-up 2026-10-19 (animated trap and next knight): narrow opt-in
`traceMemoryWriteAddresses` tracing in GameRunner now records watched RAM
writes with frame, PC and guest stack context. At the former X=2762 death,
the `HSalive` write returns to `$07FEBADE`, immediately after
`foregd_left_coll` in `hero.asm`. The Stage-2 foreground tile at X=2768
animates from a harmless TYPE 0 base into TYPE 9 lethal frames: this is
a timed foreground trap, not an unexplained graphics failure. Shorter
waits at X=2568 changed the window; 25/26/27 intervals passed X=2762
briefly, whereas 28 hit the trap and 29-31 died earlier. An independent
full DF0 cold replay with 26 intervals, in
`Tools/a3000-celtic-heart-stage-two-trap-window-route.json`, reached
frame 11887 at X=2786/Y=137 with `HSalive=FF`, zero non-ROM exceptions,
and exact framebuffer SHA-256
`6AE337BBA8A6288D66F19A1BF4992AD45B973C897BFB4F4E6875034862FA64E4`.
Its screenshot shows active castle gameplay. At X=2790 two frames later,
the next `HSalive` write returns to `$07FEC580`, the `hero_coll` return
after `killHero` in `monster.asm`: this is a separate knight collision.
Simple continuation, cower, and right-facing Fire from X=2716-2772
failed before the strike became active. A left-facing Fire from X=2744
kept the hero alive stationary until frame 11937, but subsequent Right
movement again died near X=2743-2755; neither result proves the knight
was destroyed. Next determine the knight's live position and the strike
hit frame, then synchronize a surviving attack or use the elevated ice
route to bypass it. Stage 2 remains open until a visible, reproduced
level transition. The opt-in tracer was exercised on real guest writes;
GameRunner builds cleanly and all 27 `Tools/test_game_runner.py` tests
pass. These probes used `--no-final-checkpoint`, touched no floppy/HDF
media or normal saves, and left only regenerable ignored `tmp` outputs
under the earlier automatic cleanup restriction.

Follow-up 2026-10-19 (next-knight negative controls): three bounded
`--no-final-checkpoint` continuations further constrain the passage.
Stopping at X=2644 and firing to the right before the knight approaches
still cleared `HSalive` at X=2644 by frame 11866, so the earlier attack
is not a safe stationary solution. Releasing Up for 200,000 chip cycles
at X=2716, then pressing Up again while continuing Right, left the hero
at Y=137 rather than raising him above the low castle corridor; this
branch reached X=2778 alive and died at X=2792 by frame 11890. A 250,000
chip-cycle shift after the 26-interval hold kept the hero alive through
X=2784/frame 11890, then the next monster collision killed him at
X=2788/frame 11897. None of these is a Stage-2 passage. Next inspect
the moving knight's position and strike-hit overlap at the live
X=2716-2786 endpoints; vary the Fire edge or route above the castle
only after identifying a viable overlap/window. The upper ice route at
X=2300/Y=57 remains a separate living alternative. Private runs wrote
only ignored `tmp` outputs and no floppy/HDF data or normal save.

Follow-up 2026-10-19 (second knight defeated, next trap open): a
64-KiB read-only Fast-RAM snapshot at the live X=2786/frame-11887 route
located the Stage-2 knight whose TMX object is X=2688/Y=160 and whose
runtime `Morgx/Morgy` is X=2688/Y=137. Its monster
record begins at `$07FF7924`: at frame 11887 `Mx=2800`, `Mvx=+1`,
and `MOflags=80`; by frame 11894 it had turned (`Mx=2794`, `Mvx=-1`).
This explains the immediate collision on a direct chase. The hero can
instead stop at X=2744, wait four 500,000-chip-cycle intervals, and
start a right-facing Fire at frame 11894. As the knight returns, it
halts at X=2763 after the strike, with `MOflags=40` (freed/inactive),
while `HSalive=FF` through frame 11971. The independently rerun full
DF0 cold-start route
`Tools/a3000-celtic-heart-stage-two-knight-kill-route.json` asserts
both status bytes and the exact framebuffer SHA-256
`FDF948BF3E8E8D53F1D851AC36B6B4FB78CA99D27F5EDD4BAA853754B98B98CC`;
it passes with zero non-ROM exceptions and an inspected active-gameplay
image. This is a confirmed defeat of that knight, not a Stage-2 exit.

The next Right continuation dies at X=2780/frame 11989. Its watched
`HSalive` write returns through foreground collision code
(`$07FEBA7A`), while the knight remains inactive. Delaying the run by
six more 500,000-chip-cycle intervals also dies from the foreground
trap, now at X=2758/frame 12028. Next map the animated foreground
tile's safe phase after the knight kill, then time a Right traversal
across it and extend the cold route beyond the castle corridor. These
runs used `--no-final-checkpoint`, modified no floppy/HDF or normal save,
and left only ignored `tmp` diagnostics pending permitted cleanup.

Follow-up 2026-10-19 (post-knight foreground phase crossed): a read-only
16-KiB Fast-RAM map capture located the two relevant Stage-2 foreground
columns at X=2720 and X=2768. Their live map words at rows 8 and 9
change from nonlethal type 0 (`30/70` high bytes) to lethal type 9
(`09/09`) during the animation. A frame-bounded DF0 cold run showed
the activation at frame 11989 and the return to type 0 by frame 12042.
Waiting at X=2754 was unsafe: its collision box still touched the trap
when it activated, and `HSalive` cleared by frame 11991. Waiting at
X=2744 kept the hero alive throughout that phase. After eleven
500,000-chip-cycle intervals from the confirmed knight kill, Right
crossed the trap in its safe window, reached X=2800/Y=137 alive at
frame 12077, and left the knight inactive (`MOflags=40`). The full
independent cold route
`Tools/a3000-celtic-heart-stage-two-post-knight-trap-route.json`
asserts the live hero bytes, inactive knight and exact presented SHA-256
`582B41E12D45B6FC98F4A459AB5234011110C3809F661FFCDD0C8E4181131AFF`;
its selected screenshot shows active castle gameplay, with zero
non-ROM exceptions. This establishes passage of the second animated
trap, not Stage-2 completion.

At X=2800 the lower corridor meets a solid castle wall. A separate
Right+Up continuation beginning at X=2772 remained at Y=137 and
X=2800 through frame 12119, so it did not climb the wall. The Stage-2
TMX background puts solid blocks across rows 7-11 at the next column;
the independent upper-ice route at X=2300/Y=57 remains a candidate
for approaching above it. Next follow that upper route toward the
building, or find an earlier valid jump/alternate passage while
watching the hero coordinates and foreground phase. Require a visible,
reproduced level transition before declaring Stage 2 complete. These
tests used `--no-final-checkpoint`; only compact ignored `tmp` output
was written, with no floppy/HDF or normal-save writes.

Follow-up 2026-10-19 (upper-ice alternative tested): the established
DF0 cold route to Stage-2 X=2300/Y=57 was extended with held Right+Up.
The hero jumped to X=2398/Y=79 alive by frame 11436, but was still
falling across an open gap rather than approaching the roof on a
walkable ledge. A second independent cold continuation stopped the
first jump at X=2370, released both inputs and waited seven
500,000-chip-cycle intervals. The hero fell to ground X=2372/Y=185
by frame 11472; the decorative map tiles near X=2352-2384 did not
support a landing. A new Right+Up jump from there reached only
X=2484/Y=169 alive by frame 11528, beneath the castle roof. These
runs preserve the earlier X=2300/Y=57 achievement but do not establish
an upper entry or Stage-2 completion. Next inspect the castle's solid
background tile geometry and the vertical clearance of the lower
corridor before X=2800, then test a specific earlier jump or other
legitimate passage. Continue to require a visible level transition.
The bounded runs used `--no-final-checkpoint`, selected screenshots,
zero media writes and only ignored `tmp` diagnostics.

Follow-up 2026-10-19 (castle wall passed, next patrol open): the
Stage-2 TMX geometry was checked against the correct Snow Mountains
`w1types.tmx` tile properties and a read-only guest background-map
snapshot. Column X=2816 has a solid row-9 block (`$8980`) but the
space over it is open (`$09DC`/`$090A`); the former Right+Up attempt
started beneath a solid ceiling at X=2772. From the confirmed live
post-knight position X=2800/Y=137, releasing Right and pressing Up
**in place** raises the hero to Y=108. Only then pressing Right
crosses the wall. The independent DF0 cold run reaches X=2940/Y=185
alive at frame 12154, with zero non-ROM exceptions and inspected
active gameplay. Its exact presented SHA-256 is
`BBE96994C3BCD7AA7C90F072D1643D3A2D1852DBCCB8AD5EC54E3EFA214D283B`.
`Tools/a3000-celtic-heart-stage-two-wall-lift-route.json` asserts
that hash, the hero's coordinates and `HSalive=FF`, and the defeated
knight's inactive flag. A separate cold continuation also passed this
guarded prefix. The lower castle wall is no longer the blocker.

A straight Right continuation beyond X=2940 died at X=3006/frame
12189 near the dead tree and ground patrol. An immediate Right+Up at
X=2940 did not jump: the previous Up release had not yet been sampled
by the game. A 500,000-chip-cycle neutral wait rearmed the jump and
reached X=3038/Y=185 alive, but contact at X=3040 killed the hero.
Moving to X=2996 before initiating the jump kept him alive as far as
X=3066/Y=179; contact at X=3074/Y=162 then killed him. Those later
positions are short-lived probes, not a proven patrol passage or
Stage-2 exit. Next inspect the three TMX ground-patrol records near
X=3056/3088/3120 and their runtime positions at the guarded X=2940
checkpoint; time a strike or jump that lands beyond their collision
boxes, then extend the full cold route. No game/media/save data was
written. The probes used `--no-final-checkpoint` with selected images
and bounded ignored `tmp` output.

Follow-up 2026-10-19 (ground-patrol collision traced): a further
independent DF0 cold replay preserved the wall-lift guard, captured one
bounded 64-KiB Fast-RAM snapshot at X=2940 and traced writes to
`HSalive` over the failing jump. The death at frame 12221 writes `00`
at `$07FF750A`; the guest PC is `$07FEBE60` and the return stack
includes `$07FEC580`, matching the previously identified
`hero_coll`/`killHero` monster-collision path, rather than a renderer
or CPU exception. The source `level02.tmx` in fact contains **three**
consecutive ground monsters, object IDs 39-41 at X=3056, 3088 and
3120 (plus a separate upper monster at X=3040). The runtime snapshot
has monster records at `$07FF7834`, `$07FF77E4` and `$07FF7794`;
the X=3056 monster is already active and moving left at the wall-lift
checkpoint, while the other two initially sit at their map positions.
With the delayed jump from X=2996, the hero reaches X=3052/Y=138
alive at frame 12211, when the first two active ground monsters are
already behind him at X=2995 and X=3043. Pressing Fire there stops
horizontal travel: he is still alive at X=3056/Y=179 at frame 12225,
then dies on landing at X=3062/frame 12232. The earlier strike issued
at frame 12218 was likewise too late and died at frame 12225. These
are negative timing controls, not a patrol clear. Next watch all three
runtime X/active fields through the landing interval, then try a jump
and right-facing strike timed against the **third** patrol without
pausing over its collision path; assert sustained upright movement in
the independently replayed cold route before claiming passage. Stage 2
still has no visible level transition. All diagnostic outputs are
bounded under ignored `tmp`, with `--no-final-checkpoint` and no
ROM/game/HDF/save writes.

Follow-up 2026-10-19 (three-patrol phase and strike controls): a full
DF0 cold replay of the guarded wall-lift route watched the three
80-byte guest monster records at `$07FF7834`, `$07FF77E4` and
`$07FF7794`. With the hero held safely at X=2940, the first patrol
(map X=3056) moves left from X=3051/frame 12154 to X=2973/frame
12232; the second activates by frame 12175 at X=3088 and reaches
X=3031/frame 12232. The third remains inactive at X=3120 while the
screen is held. Fire begun at frame 12211 leaves the first at X=2961
from frames 12246-12302 and the hero alive at X=2940, but does not
clear it (`Mactive=01`). A subsequent Right+Up from frame 12302 dies
at X=2946/frame 12309, so that stationary strike is not a passage.
Starting Fire later, at frame 12232, is too late: the first reaches
X=2952 and kills the hero by frame 12253 while still active. A
right-facing midair Fire from X=3052/Y=138 at frame 12211 stops the
hero's horizontal travel, and he dies on landing at X=3062/frame
12232. This matches the source `hero.asm` rule that a one-shot strike
temporarily blocks left/right/jump input, so a midair strike cannot be
treated as a moving attack. Next bracket standing Fire onset between
frames 12211 and 12232, watch strike-MOB active state and each monster's
velocity/flags, then either remove the first two from a safe position
or time a jump that lands beyond all three. Add a full cold route and
review active gameplay only after sustained live movement past X=3120;
Stage 2 remains open. The diagnostic runs used `--no-final-checkpoint`
and compact ignored `tmp` output; no normal saves or media changed.

Follow-up 2026-10-19 (three ground patrols passed): the apparently
still-active first monster at X=2961 after the frame-12211 Fire was
actually held in its fixed kill animation. Source `monster.asm`
`killMonster` calls `setAnim(M_KILLED)`; `Mactive` remains `01` during
that animation, so it alone is not a kill test. A second standing Fire
from safe hero X=2940 at frame 12267 arrested the second patrol at
X=2963 by frame 12302. The hero then walked **through** both frozen
positions to X=2996 alive, confirming they no longer caused a lethal
collision. The third patrol activated as the view advanced and moved
left from X=3120. Releasing Right left the hero aligned at X=3000;
waiting eight 500,000-chip-cycle intervals and pressing Fire at frame
12450 arrested this third monster at X=3019 by frame 12485. After
the strike animation the hero walked alive through all three former
patrol positions to X=3184/Y=185 at frame 12640. The active game
frame was inspected: the upright hero is on the ground beyond the
dead tree, with the next raised enemies and golden ground objects
visible ahead. The new **independent DF0 cold-start route**
`Tools/a3000-celtic-heart-stage-two-three-patrol-passage-route.json`
retains the earlier wall guard and asserts the exact presented SHA-256
`BB7CD74252E1F4C33E6D018A40FA8CBAFEF673A1ECE9DE630F059BA362441C23`,
hero X=`$0C70`/Y=`$00B9`, `HSalive=FF` and zero non-ROM exceptions.
`GameRunner --validate` passed, and a separate full cold replay passed
the new guard. This is a reproducible **patrol passage**, not Stage-2
completion. Next inspect the level-02 objects beyond X=3184 (raised
enemies near X=3216/3248 and ground objects farther right), then use
real input to advance to a visible Stage-2 transition. Continue to
distinguish momentary live probes from a guarded cold route. The run
used `--no-final-checkpoint`, selected compact images and no
ROM/game/HDF/normal-save writes.

Follow-up 2026-10-19 (two missiles dodged; next wall mapped): a
straight Right continuation of the guarded three-patrol route walked
alive beneath the raised enemies to X=3366/Y=185 (frame 12731), then
died at X=3372. A bounded `HSalive` bus-write trace placed the death
at frame 12734, guest PC `$07FEBE60` with `$07FEC580` on the stack:
again a monster collision. A 64-KiB read-only Fast-RAM snapshot at
X=3366 identified **two active type-14 `shootMissile` MOBs**:
`$07FF80A4` at X=3388/Y=185, velocity -2, and `$07FF80F4` at
X=3414/Y=137, velocity -1. The source `mob.asm` dispatches type 14
to `shootMissile`; `monster.asm` marks these missiles invulnerable.
Simply crouching at X=3339 died by frame 12752. Holding Right+Up
from X=3338 passed the lower missile while airborne but collided with
the upper one at X=3390/Y=133 by frame 12745. A 14-frame pause much
earlier at X=3184 merely shifted the same lower-path death in time,
not beyond X=3372.

The safe input is more precise: from X=3338/Y=185 initiate Right+Up
for two 500,000-chip-cycle intervals, reaching X=3366/Y=139 at frame
12731; **release Right there** and hold position through ten more
intervals. The hero lands at X=3368/Y=185 and remains alive through
frame 12802, while the lower and upper missiles move left behind him
to X=3248 and X=3344. The active frame was reviewed, and the new
full DF0 cold-start route
`Tools/a3000-celtic-heart-stage-two-missile-dodge-route.json`
asserts presented SHA-256
`EEACA33C9CA49801F3451D92572C8BA16621C7BA5EBBB62594C7EFD8B19B648A`,
X=`$0D28`, Y=`$00B9`, `HSalive=FF`, both missile positions and zero
non-ROM exceptions. `GameRunner --validate` and a separate cold
replay passed. Thus this is a reproducible live passage of both shots,
not a Level-2 exit.

From this guarded spot a further real Right continuation passed to
X=3440/Y=185 alive, but remained at X=3440 despite thirteen more
500,000-chip-cycle intervals. Source `level02.tmx`/`w1types.tmx`
place a solid background column at X=3456 (column 216), with solid
tiles extending from row 6 through row 12. The direct ground route
cannot be credited past that wall. Next find a legitimate ascent to
the upper route before this column, using the earlier raised platforms
and checked tile geometry; verify movement beyond X=3456 and an
eventual visible level transition from a full cold start. These runs
used `--no-final-checkpoint` and compact ignored `tmp` files; no
ROM/game/HDF/normal-save media changed.

Follow-up 2026-10-19 (raised-route probes): the guarded three-patrol
DF0 cold route was extended only in ignored `tmp` for three input
experiments, each using `--no-final-checkpoint` and zero non-ROM
exceptions. A direct Right+Up from X=3184 reached X=3198/Y=156 alive
but `HSalive` became `00` at X=3202/Y=142, before landing on the
raised route. Walking right on the ground to X=3296 stayed alive;
after releasing Right and waiting one 500,000-chip-cycle interval,
Left+Up from there reached X=3282/Y=156 alive and became dead at
X=3278/Y=142. These are failed raised-platform approaches, not
passes. At the guarded missile-dodge point X=3368, pressing Right+Up
immediately after releasing Up did not rearm a jump: X advanced to
3440 while Y remained 185. One neutral 500,000-cycle interval first
did rearm it; Right+Up then raised the live hero to X=3410/Y=135,
but he fell back to Y=185 and remained blocked at X=3440 by the
solid X=3456 column. Thus a ground-height jump cannot clear that
column. Next use the level-02 object and tile data to locate the
raised attackers at X=3216/3248 and a safe landing window on the
mid-height platform; test a timed strike or a different takeoff point
without counting a momentary airborne position as passage. Only
extend a permanent cold route after a live landing above the lower
path, then verify movement beyond X=3456 and the visible transition.

Follow-up 2026-10-19 (mid-height route independently reproduced):
waiting twenty 500,000-chip-cycle intervals at the safe ground
X=3184 let the first raised patrol's dangerous phase pass. Up alone
then lifted the hero to X=3184/Y=137, alive and stable on the
mid-height solid row-10 platform. Right advanced to X=3212. A fresh
Right+Up jump from there crossed the previously lethal X=3202 and
X=3278-3244 encounters and landed alive on that platform; Right
continued to X=3338/Y=137 at frame 12928. The active game image was
inspected. The full DF0 cold-start route
`Tools/a3000-celtic-heart-stage-two-mid-platform-route.json` guards
the exact presented SHA-256
`402ECB5B2339D8754DF9699E9A57CE8F8CA17D3D45FA9494CF996A416A2B6378`,
hero X=`$0D0A`/Y=`$0089`, `HSalive=FF` and zero non-ROM exceptions;
read-only validation and a separate cold replay passed. This is a
reproducible raised-platform passage, not a wall or level pass.

The level-02 tile properties clarify the next obstacle: row 10,
columns 199-209 (`BACK` 76-80), is solid and supports the mid-height
route. The apparent small stepping tiles at row 7, columns 211-212
(`BACK` 9-10), and the apparent upper pieces at row 5, columns
207-210 (`BACK` 11-14), have `SOLID=0`. A jump from X=3338/Y=137
through X=3396/Y=90 therefore fell through those visual tiles and
returned to the ground by X=3440; holding at X=3396 also fell to
Y=185. The tall solid column at X=3456 still blocks both the ground
and this mid-height jump. Next inspect the earlier **solid** upper
route and any moving platform or bounce mechanism that can raise
the hero above the column top. Test a live, landed position above
the wall before adding a full cold-start guard past X=3456.

Follow-up 2026-10-19 (upper platform bridge): the solid-tile map
exposed a legitimate staircase from the **earlier lower castle
corridor**, avoiding the dead-end ground continuation. In the
`wall-lift` route, stop Right at X=2870/Y=121 on the castle platform,
release Up for one 500,000-chip-cycle interval, then jump Right+Up
at the platform edge. The hero clears the overhead roof, reaches
X=2928/Y=74, and, after releasing Right, lands alive at X=2928/Y=89
on the single solid row-7 tile. A second jump lands on the single
solid row-5 tile at X=2988/Y=57. These are **solid** map tiles,
unlike the decorative pieces ahead of the X=3456 wall.

The bounded Fast-RAM snapshot identified the moving type-13 platform
at `$07FF8144`; its live X position oscillates from 2993 to 3120,
Y=89. At frame 12344 it approached the small upper tile from the
right (platform X=3004). Pressing Right for one 500,000-cycle
interval stepped the hero onto it (hero X=3000/Y=66, platform
X=2997). With Right released, the hero stayed attached through the
turn and rode it to X=3122/Y=66 while the platform reached X=3120.
An immediate Right+Up jump from there crossed the gap to the solid
row-4 upper platform. Releasing Right at X=3193/Y=34 let the hero
land and remain alive at X=3196/Y=41 through frame 12562.

The new **independent full DF0 cold-start route**
`Tools/a3000-celtic-heart-stage-two-platform-bridge-route.json`
asserts the exact presented SHA-256
`6D40CFCA354190A6F087DA36A3F35ADEB46FCFB2B6A26ADCC97F13937047B468`,
hero X=`$0C7C`/Y=`$0029`, `HSalive=FF`, moving-platform X=`$0BE4`
and zero non-ROM exceptions. Read-only route validation and a second
full cold replay passed; the active upper-platform image was
inspected. This is a reproducible **upper-platform passage**, not a
pass of the X=3456 pillar or Stage 2.

The next section needs controlled timing. A direct Right+Up jump
from X=3196 survived to X=3266/Y=9 at frame 12598 but became dead
at X=3276/Y=24 by frame 12605; stopping horizontally at X=3240
still became dead while descending through Y=24. A standing Fire
from X=3196 died sooner by frame 12584. A bounded HSalive bus trace
for the direct jump recorded the write at frame 12602, guest PC
`$07FEBE60`, stack top `$07FEBC80/$07FEBAF4/$07FEBA0C/$07FEC794`.
A read-only Fast-RAM snapshot at frame 12598 found one active type-6
upper knight at X=3206/Y=41 and two lower spiders at X=3270 and
3334/Y=137; the **precise** cause of the descending death is not
yet established, so these probes are not a passage. Next identify
the write caller and the knight's actual overlap window, then
reproduce a live move across the row-4 platform and the small solid
row-3 tile at X=3392-3407. From there test a legitimate jump over
the pillar at X=3456. Keep Stage 2 open until a visible,
independently replayed level transition.

Follow-up 2026-10-19 (upper hazards and pillar): controlled full-DF0
cold-start inputs resolved the route above the castle. A vertical-only
Up jump at X=3196 returned to Y=41 alive through frame 12626;
walking Right instead died at X=3212 by frame 12576. The level-02
BACK map has tile 66 at row 3/column 202 (X=3232) and column 205
(X=3280); `w1types.tmx` marks tile 66 as background `TYPE=1`,
which `hero.asm` dispatches to `bg_stddeath`. This explains why a
continuous Right+Up jump dies while descending across those columns;
the earlier nearby knight alone was not a sufficient explanation.
Jumping from X=3196 and releasing Right after four 500,000-cycle
intervals landed safely at X=3256/Y=41 between the two death tiles
through frame 12633. A second four-interval Right+Up jump landed at
X=3312/Y=41 by frame 12682. Holding there until frame 12696 died;
an immediate jump from frame 12682 crossed the following gap and
landed on the single solid row-3 tile at X=3396/Y=25, alive through
frame 12766.

The independently repeated full cold route
`Tools/a3000-celtic-heart-stage-two-upper-gap-landed-route.json`
guards X=`$0D44`/Y=`$0019`, `HSalive=FF` and exact presented SHA-256
`B466D572EE3ED70497B2BE06F099592732610AC041683F59F511231FF6358590`.
Read-only scenario validation passed and its active game image shows
the hero on the small upper tile. From there, seven Right+Up
intervals cleared the solid column at X=3456; releasing movement
at X=3496 let the hero fall onto the ground **to the right of the
column**, still alive through frame 12851. The independently repeated
full cold route
`Tools/a3000-celtic-heart-stage-two-pillar-passage-route.json`
guards X=`$0DA8`/Y=`$00B9`, `HSalive=FF`, zero non-ROM exceptions and
presented SHA-256
`E922030548327747CD4DD93E489BC22DDB4FE37F6856925F2D7974C582A4E1B8`;
the replay image shows active gameplay on the far side of the pillar.
This is a **reproduced pillar passage**, not a Stage-2 completion.
Continue with real Right/Fire/Up inputs from X=3496 past the visible
ground and raised enemies, retain cold-start guards for any durable
progress, and count Stage 2 only after a visible, independently
replayed level transition. Cannon Fodder's later missions and the
other roadmap items remain open.

Follow-up 2026-10-19 (far-side pits): from the independently
confirmed X=3496/Y=185 pillar passage, plain Right died at X=3550.
Right+Up instead stayed alive past the first ground opponent, reaching
X=3622/Y=185 at frame 12914; its full DF0 cold-start route is
`Tools/a3000-celtic-heart-stage-two-post-pillar-first-enemy-route.json`.
The next plain Right step died at X=3632. In `level02.tmx`, the
background row-13 tiles at X=3632-3695 have `TYPE=1`, `SOLID=0`:
the first four-tile death pit. Releasing Up at X=3594 for one
500,000-cycle interval and pressing it again at X=3608 jumped across
to X=3748/Y=185 alive (frame 12978). The independently repeated
full cold route
`Tools/a3000-celtic-heart-stage-two-first-pit-passage-route.json`
guards image hash
`D9FF33DE655B40F8A3F71892C6A24052147050BEAD7C67C7DDF5717660A41B1B`,
`HSalive=FF` and zero non-ROM exceptions; its active game image was
inspected.

The second row-13 four-tile death pit spans X=3760-3823. A jump
immediately from X=3748 cleared its far edge but died at X=3828 while
descending. A bounded Fast-RAM snapshot just before that death found
an active type-6 ground opponent at X=3835/Y=185; this is consistent
with a separate monster contact, but no HSalive caller trace was
taken for that specific collision. A phase shift by releasing Right
and Up at X=3748 and waiting six 500,000-cycle intervals before the
next Right+Up jump crossed the second pit and reached X=3864/Y=185
alive at frame 13076. The independently repeated full cold route
`Tools/a3000-celtic-heart-stage-two-second-pit-passage-route.json`
guards image hash
`B2343E71E9FBA71764D1E70F93729A0B6A265AFB9170E7F6E32F7EE7B4D1368F`,
`HSalive=FF` and zero non-ROM exceptions; the active game image shows
the hero on the far bank. This is a reproducible **two-pit passage**,
not a Stage-2 completion. Five standing Fire intervals from X=3864
kept the hero alive through frame 13111, but releasing Fire and
pressing Right died at X=3896 by frame 13132. Next capture the
nearby ground opponents' MOB positions and flags after Fire, then
time or evade the next contact with real inputs. Preserve the two-pit
cold route as the checkpoint and require a visible, independently
replayed level transition before calling Stage 2 complete.

Follow-up 2026-10-19 (Stage-2 exit): the preceding two-pit route was
extended with real joystick inputs, without modifying the guest game.
At X=3864/Y=185 after the pits, a bounded Fast-RAM snapshot following
five standing Fire intervals found two active type-6 ground opponents:
MOB `$07FF7604` at X=3909/Y=185 and MOB `$07FF75B4` at X=3922/Y=185,
both with flags `$80`. Standing Fire from X=3878 died at frame 13147;
a late Right+Up jump reached X=3934 alive but died near X=3942.
Releasing Right and Up for one 500,000-chip-cycle interval immediately
after frame 13076 rearmed an earlier Right+Up jump. It passed the two
opponents and placed the living hero at X=3976/Y=185 at frame 13139.
`Tools/a3000-celtic-heart-stage-two-far-ground-passage-route.json`
guards `HSalive=FF`, zero non-ROM exceptions, and presented SHA-256
`A04E7B0F28884FB4D7F2C5A8CC1922968EEF34026D48E98D378EE0BC11F5F8E7`;
an independent full DF0 cold-start replay reproduced that image.

Continuing Right from there made the guest write `HSalive=00` near
X=3986/frame 13144, with PC `$07FEBE60` and stack
`$07FEBE70/$07FEBC88/$07FEBACC/$07FEBA0C`. This alone was ambiguous
because the hero is also deactivated by the game's exit path. The same
PC/stack sequence was seen immediately before the known Stage-1
completion. Further real Fire input and waiting resolved the ambiguity:
at frame 13322 the screen visibly displays **SNOW MOUNTAINS STAGE 3**,
`CODE: 0F2U`, `PRESS BUTTON`. The full DF0 cold-start route
`Tools/a3000-celtic-heart-stage-two-complete-stage-three-title-route.json`
guards that final presented frame with SHA-256
`E935C0826473E8D72FEAE4A5E64ABA374DFC7569BE110868CAEBB2BC3358851B`
and zero non-ROM exceptions. This is a visible Stage-2-to-Stage-3
transition. A separate complete DF0 cold-start run reproduced frame
13322 and the exact hash with zero non-ROM exceptions, so **Stage 2 is
complete**; previous notes that call it
open describe earlier checkpoints and remain as the test history.
Next confirm Stage-3 active gameplay after the title with movement,
continue Cannon Fodder's later missions and the other seven-point
roadmap acceptance work. Completion of Stage 2 does not complete the
overall roadmap.

Follow-up 2026-10-20 (Stage-3 entry and live movement): a complete DF0
cold-start replay extended the guarded Stage-2 completion route.
Releasing Fire and pressing it again at the Stage-3 title showed the
Stage-3 mountain/platform scene. Right alone during that scene changed
animation but left the hero watch at X=`$0020`, `HSalive=00` through
frame 13477. A longer neutral wait revealed the actual second prompt,
**GET READY — PRESS BUTTON**, still present at frame 14040. Thus the
earlier Stage-3 scene was a pre-play sequence, not a movement pass.
Pressing Fire at that prompt made `HSalive=FF` by frame 14047. After
releasing Fire, genuine Right input moved the hero from X=`$0020` to
X=`$0082`/Y=`$0052`, alive at frame 14167 in the platform scene.
`Tools/a3000-celtic-heart-stage-three-active-movement-route.json`
guards those bytes, zero non-ROM exceptions and presented SHA-256
`7A3E15DF6B2FAEEC20D6A48E448A193C710012A3F66CFED814C966916E30CE9A`.
A separate complete DF0 cold-start replay passed all guards; the
frame-14167 image visibly shows the moving hero above the first gap.
This establishes **active Stage-3 gameplay and input-driven movement**,
not Stage-3 completion. A further uninterrupted Right interval fell
into the gap and deactivated the hero at X=`$008E`/Y=`$0069` by frame
14174. Next time the jump or moving platform before X=`$0082`, cross
the first gap while alive, and extend the full cold-start route.

Follow-up 2026-10-20 (Stage-3 first gap): plain Right fell from the
starting ledge at X=142. Pressing Right+Up from X=102/Y=57 jumped
out over the gap, but holding Right throughout missed the moving
platform and the hero died at X=211/Y=108. Releasing Right too early
at X=172 also missed it. Releasing Right at X=186/Y=51 instead
landed the hero on the moving platform at X=177/Y=66 by frame 14209;
with no horizontal input the platform carried the living hero left,
then right again to X=204/Y=66 at frame 14561. Releasing Up for one
500,000-chip-cycle interval rearmed a jump; Right+Up then reached
X=273/Y=34 **alive above the far solid pillar** at frame 14603.
`Tools/a3000-celtic-heart-stage-three-first-gap-airborne-route.json`
guards X=`$0111`, Y=`$0022`, `HSalive=FF`, zero non-ROM exceptions
and image SHA-256
`4D1B608A62B07D492C87AEE988C906BCE8F1956B12B7C8C0CBF527339EC1B3EA`.
A second full DF0 cold-start run reproduced the guards and the image
visibly shows the hero above the far pillar. This is a reproduced
moving-platform landing and **airborne gap crossing**, not a stable
far-side landing or Stage-3 completion: continuing Right died at
X=279/Y=37 by frame 14610, while stopping Right at X=231 died
at X=232/Y=43 by that frame. Inspect the far-side tile/enemy collision,
then time a live landing
past X=279 before marking the first gap fully passed.

The follow-up bounded write trace starts only after the guarded
frame-14603 airborne checkpoint. It captures the `HSalive=00` write
at frame 14606 from PC `$07FEBE60`, stack
`$07FEBC80/$07FEBA90/$07FEBA0C/$07FEC794`. The author-published
`hero.asm` confirms that `killHero` serves both monster and deadly-tile
contacts; the shared PC/stack does **not** prove a monster collision.
A bounded Fast-RAM snapshot at frame 14603 found the active type-9
monster behind the hero at X=232/Y=57, moving left, with no active
monster near X=279. The source `maps/level03.tmx` instead puts BACK
tile 66 at row 4/column 17 (X=272-287/Y=64-79), and `w1types.tmx`
marks it `TYPE=1`, dispatched by `hero.asm` to `bg_stddeath`.
The timing and position therefore strongly implicate that deadly
background tile. This is an inference from the map and RAM snapshot,
not yet an isolated tile-collision trace. Adjust the takeoff position
or jump phase to clear X=272-287 while higher; the airborne checkpoint
must not be represented as a safe landing.

Follow-up 2026-10-20 (first Stage-3 gap landed): the second jump was
delayed by one additional 500,000-chip-cycle neutral interval while
standing on the moving platform. The platform carried the hero left
to X=199/Y=66 before takeoff. Two Right+Up intervals then reached
X=226/Y=20; releasing Right let the hero land at X=228/Y=57 on the
far solid pillar. He remained alive there for eight more intervals
through frame 14645. The complete DF0 cold-start route
`Tools/a3000-celtic-heart-stage-three-first-gap-landed-route.json`
guards X=`$00E4`, Y=`$0039`, `HSalive=FF`, zero non-ROM exceptions
and image SHA-256
`61A218CA09CC66DD0F6DA05AE8CA46548CB388C00DBC0550F4F51E96DA71DF10`.
A separate full cold-start continuation passed this guard and its
visible frame shows the hero standing on the far side. This is a
**stable, reproduced first-gap passage**. The previous airborne
route remains a useful but incomplete checkpoint.

From the landed X=228 position, releasing Up for an interval and
jumping Right+Up clears the tile-66 column in the air through
X=300/Y=28, alive at frame 14688. The next interval deactivates the
hero near X=310/Y=44. `level03.tmx` places a monster object at
X=304/Y=80, but the contact source for this later death has not yet
been isolated; it is not counted as a pass. Next snapshot/trace the
active object at X≈304 and time a strike or another safe jump phase,
then extend the full cold route only after a sustained live landing
past it. Stage 3 and the overall seven-point roadmap remain open.

Follow-up 2026-10-20 (far-side contact identified): a bounded 8 KiB
Fast-RAM snapshot at the guarded frame-14688/X=300/Y=28 point found
the active type-9 MOB at `$07FF7E24`, X=317/Y=57, flags `$80`.
The following interval wrote `HSalive=00` at frame 14693 from
`killHero` PC `$07FEBE60`, with stack
`$07FEC580/$07FEC088/$07FEC794`; the hero stopped at X=310/Y=44.
The nearby active MOB and monster-collision return address identify
this contact more specifically than the prior map-only suspicion.
A separate complete DF0 cold-start probe released Right and pressed
Fire at X=300/Y=28. The hero remained alive at X=300/Y=55 on
frame 14695, then died at X=300/Y=57 by frame 14702; that early
airborne Fire did not clear the obstacle. These are negative controls,
not a passage. Next capture the type-9 MOB's X/flags immediately before
and after the strike, then time a grounded or differently phased
right-facing attack from the far pillar, or a jump over the MOB. Keep
the stable first-gap route unchanged until a later live checkpoint
has survived multiple intervals.

Follow-up 2026-10-20 (type-9 timing probes): the published `mob.asm`
maps MOB type 9 to `moveDino`, whose `monster.asm` handler says the
dino-bird can be killed only by a cowering strike. A full cold-start
route with extra memory watches tracked its patrol from X=274 at
frame 14645 to X=318 at frame 14695, then back left at one pixel per
frame. From the stable X=228/Y=57 pillar, a Down+Fire begun at frame
14744 kept the hero alive through frame 14793 but left the MOB active
(`MOflags=$80`) at its left turn X=247. Delaying Fire until frame
14758 caused collision/death by frame 14779. Establishing Down for one
interval before Fire, or beginning that sequence at frame 14737,
also failed to kill it. A bounded Right step from the pillar reached
X=240/Y=57 safely; cowering Fire at frame 14734 kept the hero alive
through frame 14783 while the MOB turned at X=257, still active.
Moving instead to X=260 in the air died at frame 14695 before landing;
that is no safe approach. These are full-DF0 negative controls, not a
passed monster. The next concrete test is a grounded cowering strike
from X=240 with a later activation phase or a slightly closer safe
position, watching the strike MOB's X/Y and active flag as well as the
type-9 MOB; only extend the durable cold route after a killed/inactive
MOB and sustained live movement beyond it.

Follow-up 2026-10-20 (far-side dino defeated and passed): the prior
`MOflags=$80` check alone was misleading because `killMonster` starts a
death animation before the MOB is removed. The published BSS layout
puts `HeroStrike` at `$07FF7522`. A bounded snapshot during the
X=240 cowering strike at frame 14762 found it active at X=256/Y=69,
with collision bounds X=256..267/Y=73; the type-9 MOB at X=257/Y=57
had bounds X=257..272/Y=57..79. The boxes overlap. Its `MOtype`
had become 0 with the killed animation running, explaining why its
X stopped at 257 even while `MOflags` remained `$80`. The flag later
became `$40` when the animation finished. This establishes a real
cowering Fire kill, not merely a harmless pause in the patrol.

After releasing Fire/Down at frame 14783, a real Right+Up jump over
the deadly tile-66 column reached X=336/Y=58 alive at frame 14833,
beyond the dino. `Tools/a3000-celtic-heart-stage-three-dino-passage-route.json`
is a complete DF0 cold-start route guarding hero X=`$0150`, Y=`$003A`,
`HSalive=FF`, the defeated MOB's `MOtype=00`/`MOflags=40`, zero
non-ROM exceptions and frame SHA-256
`C593760E7793C1BA530C14AAFAA65665D98F29CC27507EE2B221DAF688B967D6`.
A separate complete DF0 cold-start replay passed every guard; the
captured frame was visually inspected as active gameplay above the
next stone ledge. Treat the first Stage-3 far-side dino as defeated
and passed. Next determine a safe landing or platform transfer from
X=336/Y=58 and continue Stage 3 with real inputs; no level completion
has yet been shown. The seven-point roadmap remains open.

Follow-up 2026-10-20 (ground route beyond the dino): stopping Right
at X=336/Y=58 caused a fall to X=340/Y=185 and death by frame 14875;
it is not a safe landing. Keeping Right held instead continued the
real jump and fell past the first ground enemy, landing safely and
remaining alive on the lower stone floor through X=450/Y=185 at
frame 14889. The full DF0 cold-start route
`Tools/a3000-celtic-heart-stage-three-ground-passage-route.json`
guards X=`$01C2`, Y=`$00B9`, `HSalive=FF`, the killed dino's
`MOtype=00`/`MOflags=40`, zero non-ROM exceptions, and exact frame
SHA-256
`68038E0DE44C6C88962335C448C0C9A46384C5B736652643235B053940097D9E`.
A separate complete cold-start replay passed all guards; the capture
visibly shows active ground gameplay to the right of the first wall.
This ground passage is independently passed. A subsequent real
Right+Up jump from X=450 reached X=520/Y=137 alive at frame 14924,
then died at X=528/Y=133 by frame 14931. That short-lived jump is
not a passed next obstacle. Next capture a bounded MOB/map-collision
trace around X=520-528 and time a safe jump or strike from the
reproduced X=450 ground checkpoint. Stage 3 remains open.

Follow-up 2026-10-20 (middle platform and bat contact): a bounded 8 KiB
Fast-RAM snapshot at frame 14924/X=520/Y=137 found an active type-12 bat
at X=541/Y=137 (MOB `$07FF7564`, flags `$80`); a separate type-8 ground
enemy was at X=526/Y=185. The following HSalive write at frame 14928,
X=528/Y=133 had PC `$07FEBE60` and monster-collision return address
`$07FEC580`. The bat's height and the collision stack identify this as
the airborne threat rather than the lower ground enemy. A different real
input sequence from the independently passed X=450 checkpoint jumps only
to X=478/Y=139, releases Right and Up, then waits. The hero lands on the
fixed middle platform at X=480/Y=137 and stays alive for nine neutral
500,000-chip-cycle intervals through frame 14966. The complete DF0
cold-start route `Tools/a3000-celtic-heart-stage-three-middle-platform-route.json`
guards X=`$01E0`, Y=`$0089`, HSalive=`FF`, the earlier dino's killed
state, zero non-ROM exceptions and exact frame SHA-256
`7CF0369D9201FAA8362F5F0419B4C4E0EEF1A89E126489E1634148492D05F9EE`.
A separate full cold-start replay passed every guard; the frame shows
active gameplay with the hero on the platform. This landing is passed.
Immediate standing Fire from frame 14966 is a negative control: the bat
continued left from X=499 through X=485 and killed the hero at frame
14980 before its active flag changed. Two further complete DF0 cold-start
negative controls bounded the strike timing. Fire begun at frame 14931
(bat X=534) expired before the bat arrived; at frame 14980 the bat was
still type 12/flags `$80` and the hero died. Fire begun at frame 14938
(bat X=527) changed the bat to type 0 on frame 14980, but the hero died
in that same interval; a simultaneous kill does not establish passage.
Next shift the strike a few frames within this narrow window, or time
an evasive jump from the stable platform, watching bat type/flags and
sustained live movement beyond it. Stage 3 and the seven-point roadmap
remain open.

Follow-up 2026-10-20 (type-12 bat defeated and passed): a fourth complete
DF0 cold-start timing probe inserted 250,000 chip cycles after the stable
frame-14931 platform landing. Standing right-facing Fire therefore began
at frame 14935 with the bat at X=530. At frame 14977 the bat was X=491
and its type changed to 0 while the hero remained alive at X=480/Y=137;
it stayed dead through frame 14991. Releasing Fire, waiting four
500,000-chip-cycle intervals for its death animation (flags `$40` by
frame 15019), then holding real Right moved the living hero past the
former bat to X=544/Y=185 at frame 15075. The complete route
`Tools/a3000-celtic-heart-stage-three-bat-passage-route.json` guards
X=`$0220`, Y=`$00B9`, HSalive=`FF`, bat type 0/flags `$40`, the earlier
dino's killed state, zero non-ROM exceptions, and exact frame SHA-256
`7B7374CE7768C7190A75B7E747294BC3133AEE966123EECA3883056F0FE1899E`.
A separate full DF0 cold-start replay passed; its captured frame was
visually checked as active ground gameplay at the next stone wall. This
is a sustained live bat passage. The hero's Right movement stopped at
X=544 against the next raised stone face. Pressing Up while Right remained
held launched a legitimate jump over the wall, reaching X=642/Y=153 alive
after eight 500,000-chip-cycle intervals at frame 15132. The complete
DF0 cold-start route
`Tools/a3000-celtic-heart-stage-three-post-bat-jump-route.json`
guards X=`$0282`, Y=`$0099`, HSalive=`FF`, bat type 0/flags `$40`, zero
non-ROM exceptions and exact SHA-256
`F536A7A59C51FE901D184D184477CB9211DB9AF0737159CD23C8F5311B16195B`.
A separate full cold-start replay passed; the frame visibly shows the
living hero on the raised snowy platform with further terrain and items
to the right. This raised passage is passed. Continue with real inputs
from X=642, releasing Up to rearm the next jump and testing the nearby
item/enemy sequence; only a visible, reproduced stage transition counts
as a Stage-3 completion. Stage 3 and the seven-point roadmap remain open.

A bounded full DF0 cold-start continuation released Up and held Right
for eight more intervals. The living hero moved to X=656/Y=153 by
frame 15139 and stayed there through frame 15188: the next stone face
blocks plain Right. This is a negative movement control, not passage of
that face. Next press Up anew while holding Right from the reproduced
X=642 or X=656 position and inspect the landing and any object contact.
One complete cold-start probe pressed Up at X=656 while holding Right.
The hero rose to X=656/Y=124 alive at frame 15195, then HSalive became
`00` by frame 15202 at X=666/Y=108. The map's BACK tile at that height
is empty (GID 1). A bounded 8 KiB Fast-RAM snapshot at frame 15195 found
two active type-6 knights at X=681/Y=121 and X=724/Y=121. A trace over
the next 500,000 chip cycles captured the HSalive write at frame 15201,
PC `$07FEBE60`, with monster-collision return address `$07FEC580`.
Thus the first knight, rather than the empty BACK tile, causes the jump
death. This is a failed jump, not a passage. Next time a right-facing
strike before reaching X=666 at knight height, or change the jump phase;
guard a new route only after sustained live movement beyond both knights.

A further full DF0 cold-start control released Right and pressed Fire in
the air at X=656/Y=124, frame 15195. This safely stopped horizontal
movement while the hero rose and landed alive at X=656/Y=153 through
frame 15251, but it did not kill the first knight: its type remained 6
and flags `$80`. Releasing Fire/Up and holding Right for eight intervals
kept the hero alive through frame 15308 but still blocked at X=656;
the knight moved from X=717 at frame 15251 to X=766 by frame 15301,
then turned at X=765. This is a phase measurement, not a passage. Next
rearm Up while the knight is moving away, release Right before the
returning patrol meets the landing point, and only extend a guarded
cold route after a sustained live crossing of the raised wall.

The next full DF0 cold-start timing probe rearmed Up at frame 15265
while the first knight moved right (X=731), climbed over the wall and
reached X=696/Y=106 alive at frame 15294. Stopping Right there did
not save the landing: HSalive became `00` by frame 15301 at X=700/Y=117.
The other type-6 knight, which starts at X=724, was at X=716 on frame
15294 and X=709 on frame 15301, type 6/flags `$80`; it was closing
from the right as the first knight moved away to X=766. This is a
second, separate knight contact rather than failure to clear the wall.
Next time a right-facing Fire strike during the jump before the second
knight reaches X≈709, or phase the jump to land while it is elsewhere.

A full cold-start airborne-Fire control from X=682/Y=103 at frame 15287
released Right and struck toward the approaching second knight. The
hero stopped at X=684/Y=121 and survived through frame 15308 while the
knight closed from X=723 to X=701, but died by frame 15315 with the
knight still type 6/flags `$80` at X=694. The published `monster.asm`
`moveKnight` calls the normal `hero_coll` strike check, so the knight
should be vulnerable; this particular aerial strike did not overlap
its collision rectangle during the active frames. Capture the
HeroStrike X/Y/active flag near frame 15308 and retime the attack or
use a safe higher landing position. This control is not a knight pass.

The bounded 8 KiB snapshot at that exact frame showed why this aerial
Fire did not work: `HeroStrike` still held the earlier X=496/Y=141
coordinates and flags `$40` (inactive), while the hero was X=684/Y=121
and the second knight was X=701/Y=121, type 6/flags `$80`. The input
stopped horizontal motion, but no active strike overlapped this knight.
The next test must let the hero land on the raised surface at a safe
X before pressing Fire, or use another jump phase; compare the strike's
live X/Y/flags with the knight's collision rectangle before calling it
a kill.

A full DF0 cold-start grounded-strike probe now defeats this second
knight. From the safe X=656/Y=153 position at frame 15251, release
Fire/Up, wait one interval, then press Right+Up and stop Right at
X=668/Y=107 (frame 15272). Four neutral intervals land the hero on
the raised stone at X=668/Y=121, alive at frame 15301 while the knight
approaches at X=709. Pressing Fire **after landing** changes that
knight's type from 6 to 0 at frame 15329/X=680; the hero remains alive
through frame 15357. Its flags still indicate a running death animation
at that checkpoint. The earlier airborne strike had left HeroStrike
inactive and did not kill it; the grounded attack gives a concrete
mechanical distinction. The first knight remains active to the right,
so this is a killed second knight, not yet a sustained passage of both.
Next release and rearm Fire promptly after the second knight's kill,
then monitor the approaching first knight and the hero through the
return trip before guarding a full cold route beyond them.

The rearmed strike worked in a complete DF0 cold-start probe: release
Fire at frame 15329 just after the second knight becomes type 0,
wait one 500,000-chip-cycle interval to frame 15336, then press Fire
again while standing at X=668/Y=121. The first knight came left from
X=743 to X=696 and changed from type 6 to 0 at frame 15378; the hero
remained alive through frame 15399. Both knight records were type 0,
though the first still showed an in-progress death animation. This is
a verified two-knight kill sequence. A cold replay with post-animation
movement and independent frame/hash guards is still needed before the
raised corridor can be counted as passed.
Keep both knight passages unpassed until a live cold route persists.

Follow-up 2026-10-20 (both knights passed): that full DF0 cold-start
continuation now passes. After releasing Fire at frame 15399, four
neutral 500,000-chip-cycle intervals let both knights finish their
death animations (both type 0/flags `$40` at frame 15420). Eight real
Right intervals carried the living hero from X=668/Y=121 to
X=780/Y=121 at frame 15484, beyond the former patrols.
`Tools/a3000-celtic-heart-stage-three-two-knights-passage-route.json`
guards hero X=`$030C`, Y=`$0079`, HSalive=`FF`, both knight death states,
the previously killed bat, zero non-ROM exceptions and exact frame
SHA-256
`7C672B0841F4049893FBC293A415571759F4563E34CC25A1789392682FCABECA`.
An independent complete DF0 cold-start replay passed every guard. Its
image was inspected: active gameplay shows the living hero at the right
edge of the raised stone corridor. This passes both knights and the
corridor; it does not finish Stage 3. The next concrete action is to
map the terrain and gap immediately beyond X=780, then use real
Right/Up input to find a sustained live landing and extend the cold
route. Stage 3 and the seven-point roadmap remain open.

A first full DF0 cold-start continuation from X=780 simply held Right.
The hero left the platform and was still alive at X=796/Y=136 on frame
15491, but HSalive became `00` by frame 15498 at X=806/Y=151. Later
frames were respawn/death animation, not a successful ground landing.
The visible map has lower ground at Y≈176 and sparse floating tiles;
this seven-frame contact must be isolated before planning a jump or
landing. A bounded MOB snapshot and HSalive write trace around frames
15491-15498 is the next diagnostic. The passed two-knight corridor
remains valid; the post-corridor descent is open.

The bounded contact trace confirms `HSalive=00` at frame 15496 from
`killHero` PC `$07FEBE60`, stack
`$07FEBC80/$07FEBA90/$07FEBA0C/$07FEC794`. The preceding 8 KiB MOB
snapshot contains no active monster between X=700 and X=950. This stack
matches the earlier deadly-tile path, rather than the observed
monster-collision return `$07FEC580`. The Stage-3 BACK map has a
continuous lower strip of GIDs 141/144 at columns 48-63 (world
X=768-1023), row 11; `w1types.tmx` marks them non-solid, type 1.
The immediately higher row has isolated solid stepping tiles at
columns 52, 56 and 60 (X=832, 896, 960), with normal ground starting
near column 63. The evidence points to the lower strip as the fall
hazard. Next release Up while still on the safe raised corridor and
press Right+Up at its edge, checking a live landing on the first
stepping tile before building a guarded route across the strip.

A full cold-start jump released Up on the raised corridor at X=724,
then pressed Right+Up from X=780. The hero crossed the former fatal
fall point and reached X=880/Y=143 alive by frame 15533, but died at
X=886/Y=148 before frame 15540. Continued Right overshot the first
isolated stepping tile at X=832-847; that airborne distance is not a
landing or strip passage. A follow-up probe stops Right near X=838 at
the jump apex and waits for a sustained landing on that tile.

Follow-up 2026-10-20 (first water stepping tile landed): the shorter
real Right+Up jump stops Right at X=838/Y=74 on frame 15512. The hero
settles at X=840/Y=105 on the first isolated solid tile, then stays
alive for nine neutral 500,000-chip-cycle intervals through frame
15575. The complete DF0 cold-start route
`Tools/a3000-celtic-heart-stage-three-first-water-stone-route.json`
guards X=`$0348`, Y=`$0069`, HSalive=`FF`, both defeated knights,
zero non-ROM exceptions and exact frame SHA-256
`FBF888BA8A7E2360C402B4D1BE4CF7357390A685EF9E60BB2AA305C4E3CF8E49`.
A separate complete DF0 cold-start replay passed all guards. Its frame
was visually inspected as active gameplay: the hero stands on the
first green stone above the lethal lower strip, with two more green
stones visible to the right. This first landing is passed; the strip
and Stage 3 are not. Next release Up, jump to the second solid tile at
X=896, and guard only a sustained live landing there.

The first full cold-start attempt at the second tile released Up on
X=840/Y=105, waited one interval, then held Right+Up for five
500,000-chip-cycle intervals. The hero reached X=910/Y=73 alive but
coasted two more pixels to X=912 when Right was released, just past
the 16-pixel tile at X=896-911. It fell to Y=127 alive, then died by
frame 15638 at X=912/Y=156. This is a negative control. Stop Right
one interval earlier, at X=896, to land within the tile width; only
count the landing after multiple neutral intervals alive.

Stopping at X=896 rather than X=910 did not fix the second landing:
the hero was alive at X=896/Y=99 on frame 15624 but HSalive was `00`
by frame 15631 without leaving that X. A check against `w1types.tmx`
shows why the visual match was misleading: the first green tile at
X=832 is GID 164, `SOLID=1/TYPE=8`, while the green-looking tile at
X=896 is GID 168, initially `SOLID=0/TYPE=1`. In the tested phase
it was hazardous on contact. The third tile at X=960 is GID 164 and
solid. Later source and live-map analysis below established that
GID 168 animates through solid phases; this negative landing only
excluded the tested timing.

Follow-up 2026-10-20 (third-stone range probe): a complete DF0 cold start
reached the independently proved first stone and released Up to rearm
the jump. A 200,000-chip-cycle Right alignment moved the live hero to
X=846/Y=105. Holding Right+Up then reached X=944/Y=127 alive at frame
15634, but the hero fell and died at X=958/Y=156 by frame 15641,
before a sustained landing on the solid third stone at X=960. This is
an airborne negative control, not a passage. A fresh full cold-start
probe is testing a slightly farther right takeoff from the first stone;
the landing must be observed alive over several neutral intervals.

The separate 300,000-chip-cycle alignment probe reached X=848/Y=106
alive on frame 15586, already one pixel below the standing height on
the 16-pixel first stone. Pressing Up+Right there did not start a jump:
the hero fell through X=876/Y=150 and died at X=878 by frame 15607.
The usable takeoff ends before X=848; the previous X=846 jump fell
short of the third solid stone. Neither airborne trace proves a route
across the water. Next inspect the Stage-3 tile and moving-platform
layout for a supported crossing or a different approach, then test it
from the same proved first-stone cold start.

Source correction 2026-10-20: the GID 168 map entry is animated.
`w1types.tmx` cycles its tile images through IDs 167/168, 148,
166/165, 163/164 and back in 120 ms phases. IDs 167-168 are
non-solid type 1; IDs 163-166 are solid type 8. The published
`tiles.asm` continuous-animation updater writes each phase's tile
code into the live map, including its solid/type bits. Thus the
second green-looking stone is unsafe in the tested phase, but may
become a real platform during another phase. The static GID alone
cannot rule out that intended timed crossing. A bounded live-map
phase probe from the independently verified first-stone route is
underway; land on the second stone only during a measured solid
phase and hold it alive before extending the cold route.

The bounded live-map probe from the complete first-stone DF0 route
sampled the Stage-3 background entry at column 56/row 8 every
500,000 chip cycles from frame 15575 through 15702. Its encoded tile
word was solid-bit-set (`$C948/$C94A/$C94C`) for samples 0-2,
non-solid (`$4928/$494E/$4950`) for samples 3-9, and solid again
(`$C94C/$C946/$C948`) for samples 10-17. This directly confirms
that the apparent second stone alternates between lethal/non-solid
and solid phases. In the failed jump, the hero reached X=896/Y=99
at frame 15624, inside the measured non-solid interval. A full
DF0 cold-start test now delays that same jump by three 500,000-cycle
intervals to target the measured solid window. A landing is passed
only if HSalive remains `FF` and a genuine standing Y coordinate
persists for several further neutral intervals.

Follow-up 2026-10-20 (timed second stone passed): delaying the same
real Right+Up jump by three 500,000-chip-cycle intervals from the
proved first-stone state shifted contact into the measured solid
phase. The hero landed at X=896/Y=105 on frame 15652 and remained
alive there for two more neutral intervals through frame 15667.
The runtime map word was `$C946` (solid bit set) at that checkpoint.
The complete DF0 cold-start route
`Tools/a3000-celtic-heart-stage-three-second-water-stone-route.json`
guards X=`$0380`, Y=`$0069`, HSalive=`FF`, zero non-ROM exceptions
and exact frame SHA-256
`DEF43DFB1C5C0C4AA42F64B292891D2511F0CF0A6B68CBAC5101CFF6D6829F9E`.
An independent complete DF0 cold-start replay passed every guard;
the first run's frame was visually inspected as active gameplay with
the hero standing on the second green stone. This timed landing is
passed. The tile later returns to a non-solid lethal phase, so the
hero must jump onward before the phase change. A full cold-start
Right+Up test to the third permanently solid stone is underway;
Stage 3 and the water passage remain open.

Follow-up 2026-10-20 (third water stone, initial cold run): from the
timed second-stone checkpoint X=896/Y=105, Up was released for one
500,000-cycle interval and then Right+Up held for five such intervals.
The hero reached X=966/Y=73 alive; Right was released and the hero
settled at X=968/Y=105 on the third permanently solid green stone.
Seven neutral intervals kept HSalive=`FF` through frame 15758. The
frame was visually inspected as active gameplay, with the next
castle platform and a living enemy visible to the right. The
guarded full cold-start route
`Tools/a3000-celtic-heart-stage-three-third-water-stone-route.json`
has exact frame SHA-256
`AA82057EC55CCD6BCB833EB96CA2118BFBCD8C9FD2E254A381009CA769A1827F`;
its independent full cold-start replay passed every guard at frame
15758 with zero non-ROM exceptions. The timed second-stone jump and
third-stone landing are passed. The next concrete
step is to jump from X=968 to the returning solid castle terrain
near X=1008, inspect the enemy there and establish a sustained live
landing before extending the route.

Follow-up 2026-10-20 (far castle return, initial cold run): from
the third stone at X=968/Y=105, release Up for one interval and hold
Right+Up for three 500,000-chip-cycle intervals. Release Right at
X=1010/Y=55; the two-pixel coast places the hero at X=1012. Seven
neutral intervals then land him on solid castle terrain at
X=1012/Y=121, alive through frame 15835. The image was inspected:
active gameplay, hero on the near lip and an approaching knight on
the same platform. The guarded full DF0 route
`Tools/a3000-celtic-heart-stage-three-far-castle-landing-route.json`
has exact frame SHA-256
`9E11F9AFAB6430DD68EAE5CB1A37AC3AFAEF003C3C54D6504B4C96360100B74B`.
Its independent full DF0 cold-start replay passed all guards at frame
15835 with zero non-ROM exceptions. The water crossing and live return
to castle land are passed. The next concrete
step is to measure and beat or evade the nearby knight with live
Fire/Up/Right input, then extend the cold route to sustained movement
past it. Stage 3 remains unfinished.

Bounded follow-up at the far castle: an 8 KiB Fast-RAM snapshot at
frame 15835 identifies the nearby active Type-6 knight at
`$07FF7C94`, X=1038/Y=121, MOflags=`80`. Holding grounded Fire
from X=1012 at that frame was too late: HSalive became `00` by
frame 15857 while the knight remained active, and its later X=1033
does not imply a kill. This is a negative control. The previous
castle-landing route reaches the same grounded X=1012/Y=121 at
frame 15814; start the standing right-facing strike at that earlier
landing, inspect its active window and the knight's flags, and only
extend the full route after sustained live movement beyond him.

The earlier grounded right-facing Fire at X=1012/Y=121, frame 15814,
was correctly phased. A full DF0 cold start kept the hero alive
through eight 500,000-chip-cycle Fire intervals to frame 15871;
the nearby knight's MOB at `$07FF7C94` changed to `MOtype=00`
with its death animation active (`MOflags=80`), while X=1030/Y=121.
The first knight is defeated, but this stationary probe alone does
not establish a passage. A fresh full cold start is letting the
death animation finish and testing real Right movement toward the
next edge; require a sustained live image and independent replay.

Follow-up 2026-10-20 (far-castle knight passage, initial cold run):
the correctly phased Fire at X=1012 began on frame 15814 and kept the
hero alive while the Type-6 knight changed to Type 0. Four further
neutral Fire intervals allowed its death animation to finish. After
releasing Fire, six real Right intervals moved the hero through the
knight's former X=1030 position to X=1096/Y=121; three neutral
intervals kept him alive through frame 15962. The knight record is
`MOtype=00/MOflags=40`, HSalive=`FF`, zero non-ROM exceptions, frame
SHA-256
`37F88D4553004343921E81F6C55DDDEE490A5B9C643D55B91F167B8E2E2877E9`.
The guarded full DF0 cold route
`Tools/a3000-celtic-heart-stage-three-far-castle-knight-passage-route.json`
validated structurally and passed an independent full DF0 cold-start
replay at frame 15962 with every image/memory guard and zero non-ROM
exceptions. The visible image was inspected as active gameplay:
the hero stands alive at the next castle edge with the defeated
knight behind him. Treat the Type-6 knight and X=1096 passage as
passed. The
next obstacle is the castle edge ahead; inspect the map and use
real jumps to cross without treating an airborne frame as a landing.

Map check for that next move: `level03.tmx` ends the current castle
top at X=1104; row 11 tiles across the immediate descent are
non-solid decorative tiles. Solid ground resumes at row 13/Y=208.
An upper solid type-8 platform spans X=1152-1231 at row 6/Y=96.
The source map also starts one monster at X=1152/Y=96 and two near
X=1152-1184/Y=208. The next full DF0 cold-start probe should take a
Right+Up jump from the live X=1096/Y=121 edge toward the upper
platform, inspect the active MOB positions and establish a sustained
live landing. Falling onto the lower ground without addressing its
monsters is not an accepted passage.

Follow-up 2026-10-20 (next Stage-3 platform, bounded probes): a full
DF0 cold start from the independently checked X=1096/Y=121 route held
Right+Up for five 500,000-chip-cycle intervals, reaching X=1166/Y=73
above the solid platform, then released Right. The hero remained at
X=1168/Y=73 and alive through frame 16018, but died by frame 16025;
this airborne approach alone is **not** a platform pass. A second
full cold start stopped at frame 16018 and captured only 8 KiB of
Fast RAM under ignored `tmp`. It identified an active Type-5 MOB at
`$07FF7C44`, X=1181/Y=73, flags `$80`, only 13 pixels from the
hero. Two active Type-9 MOBs at X=1168/1175,Y=185 occupy the lower
route. A full DF0 cold-start control started standing right-facing
Fire immediately when Right was released at X=1166; the hero stayed
at X=1167/Y=73 and still died by frame 16025. This is no kill or
passage. Next release Up for one interval on the platform, rearm a
Right+Up jump and test evasion of the Type-5 MOB. The full cold-start
probe did rearm Up after one neutral interval and jumped from
X=1168/Y=73 over that enemy, remaining alive through X=1232/Y=95
at frame 16054. This last point is beyond the upper platform edge,
still airborne, so it is not a passage. The next concrete probe
stops Right at X=1210/Y=23 while above the solid X=1152-1231
platform, waits for a stable live landing, then measures the enemy
separation. Stage 3 and the overall roadmap remain open.

Follow-up 2026-10-20 (Type-5 evasion and upper-platform landing):
release Right at X=1166/Y=73, release Up for one 500,000-chip-cycle
interval, then press Right+Up for three intervals. This moves the
hero above the Type-5 enemy to X=1210/Y=23. Releasing Right coasts
to X=1212; twelve neutral intervals land and keep him on the solid
upper platform at X=1212/Y=73 through frame 16110. This is a
sustained live landing, unlike the earlier X=1232 airborne probe.
`Tools/a3000-celtic-heart-stage-three-upper-platform-landed-route.json`
guards the final image SHA-256
`01F3F64220996FE6863E29B4351996A192B2417EE21F6010CEE547C33BBD3EF7`
and guest X/Y/HSalive. It validated and passed an independent full
DF0 cold-start replay with zero non-ROM exceptions; the captured
image was inspected as active gameplay, with the hero standing on
the upper platform and the bypassed enemy behind him. Treat this
platform landing as passed. Ahead, the source map shows a tall
solid castle face starting at X=1248; next inspect its collision
layout and test a legitimate way onward, keeping the full cold
route and live-gameplay standard. Stage 3 and the overall roadmap
remain open.

Follow-up 2026-10-20 (castle-face descent, negative control): from the
verified live X=1212/Y=73 upper platform, releasing Up and holding
Right for six 500,000-chip-cycle intervals caused the hero to run
off the right platform edge, descend beside the high solid castle
face, and enter a lower opening. He was still alive at frame 16145,
X=1248/Y=169, but died by frame 16152 at X=1262/Y=169. This is not
a passage through the castle. The TMX tile metadata clarifies the
geometry: the upper tower tiles are solid, while row-10 GID 239
and adjacent interior GIDs 153-155 are non-solid; a lower entrance
exists. Next capture a bounded MOB snapshot at live X=1248/Y=169,
identify the immediate contact and time a real strike or dodge
before extending the independently guarded cold route.

Bounded live Fast-RAM snapshot at frame 16145 identified the immediate
danger as two active Type-6 knights: MOBs `$07FF7B54` at X=1263/Y=169
and `$07FF7B04` at X=1295/Y=169, both flags `$80`. A full DF0 cold
start tested an early standing right-facing Fire from X=1234/Y=166
at frame 16138. Releasing Right at strike start let the hero settle
at X=1235/Y=169 and remain alive through nine 500,000-chip-cycle
intervals to frame 16201. This is a safe strike position but does
not by itself prove a knight kill or corridor passage. Next inspect
the knights' Type/flags after Fire and attempt sustained live Right
movement; retain the route only after an independent full cold replay.

The next full DF0 cold-start probe captured another bounded 8 KiB
snapshot at frame 16201 after that Fire: the first Type-6 MOB at
`$07FF7B04` had changed to Type 0 at X=1264/Y=169, while the
second at `$07FF7B54` remained active Type 6 at X=1319/Y=169.
Releasing Fire and holding real Right for six intervals moved the
living hero through the first knight's former position to
X=1319/Y=169 at frame 16244, HSalive=`FF`, zero non-ROM exceptions.
This confirms the first lower knight kill and live movement past it;
the second knight and sustained corridor passage are still open.
Next track the second knight's position while approaching, stop at a
safe distance and phase a grounded strike before claiming passage.

Second lower knight phase control (full DF0 cold start, no gameplay
pass): from X=1319/Y=169, six further Right intervals advanced the
hero to X=1375 alive at frame 16272, while the active Type-6 knight
ran ahead from X=1361 at frame 16244 to X=1389. Continuing Right
closed the separation and killed the hero at frame 16279/X=1387;
the knight was X=1397, still Type 6/flags `$80`. Afterward it
turned near X=1406/16293 and ran left. The next targeted test is to
stop the living hero at X=1361 on frame 16265 (knight X=1382),
start a grounded right-facing Fire strike while the knight is
moving away, and monitor its return, Type/flags and HSalive. Only
a sustained live move past it and a separate guarded full cold
replay can establish the second lower-knight passage.

Follow-up 2026-10-20 (second lower knight defeated, passage pending):
the targeted full DF0 cold start stopped Right at hero X=1361/Y=169
on frame 16265, pressed right-facing Fire and held it for twelve
500,000-chip-cycle intervals. The knight at `$07FF7B54` advanced
from X=1382 to its turnaround at X=1406, returned to X=1393 and
changed from Type 6 to Type 0 by frame 16307 while HSalive stayed
`FF`. By frame 16349 its death animation finished (flags `$40`),
with the hero alive at X=1364/Y=169 and zero non-ROM exceptions.
This proves the kill, not yet live movement past the former knight.
The next full cold start releases Fire and tests sustained Right
movement beyond X=1393 before a durable guarded passage is added.

Follow-up 2026-10-20 (lower castle knight corridor passed): after the
second knight's death animation finished, releasing Fire and holding
Right for three 500,000-chip-cycle intervals moved the hero past its
X=1393 position to X=1406/Y=169. A solid face stopped further Right
at X=1408; five neutral intervals kept the hero alive to frame
16441. The guarded route
`Tools/a3000-celtic-heart-stage-three-lower-castle-knights-passage-route.json`
requires hero X=1408/Y=169, HSalive=`FF`, second knight
Type 0/flags `$40`, exact frame SHA-256
`C5EAB5D91AB09374643572DCDB1393A147CF04395FDD1CB5116C48A42182E71A`
and zero non-ROM exceptions. It validated and passed a separate full
DF0 cold-start replay; the captured image shows active gameplay with
the hero alive beyond both defeated knights. Treat the two lower
knights and corridor to X=1408 as passed. The image and TMX show a
high solid castle face ahead, narrow intermediate ledges and active
Type-12 bats started at X=1408/Y=144 and X=1584/Y=96. Next measure
the live bat positions and test a legitimate staged jump over the
castle face; require a sustained landing and independently replayed
route. Stage 3 and the overall roadmap remain open.

Follow-up 2026-10-20 (first castle-wall ledge landed): a full DF0
cold-start negative control pressed Up directly from X=1408/Y=169,
then Right; the hero stayed at X=1408/Y=169 because the solid ceiling
at map row 9 (X=1392-1423) blocks this jump. Returning left to
X=1354/Y=169, pressing Right+Up for five 500,000-chip-cycle intervals,
then releasing Right lands sustainably on that ledge at X=1408/Y=121.
`Tools/a3000-celtic-heart-stage-three-wall-ledge-landed-route.json`
validates and independently repeats the full DF0 cold start through
frame 16553 with HSalive=`FF`, second knight Type 0/flags `$40`,
zero non-ROM exceptions and exact image SHA-256
`A41D664EB974FAEB607C14AEDA79C7A3C9EE075447633DC0CCF0066C0B8674AF`.
The captured image shows active gameplay and the living hero standing
on the narrow ledge before the high white castle face. This first
ledge is passed; the castle face and Type-12 bats remain open. Next
test a rearmed jump left from the stable ledge to the higher fixed
step, then return right above the face; require a sustained landing
and independent full-route replay before counting any new passage.

The first leftward jump control from X=1408/Y=121 released and rearmed
Up, then held Left+Up for six 500,000-chip-cycle intervals. It reached
X=1380/Y=83 alive at its apex but crossed over the higher fixed step
X=1328-1359 too late, fell to X=1324/Y=169 and stayed alive there.
This is not the higher-step passage. The next test shifts left on the
stable ledge before triggering the jump, so its apex can align with
that step; the full DF0 route must prove both landing and survival.
An over-shift negative control walked Left for two whole 500,000-cycle
intervals: X=1394/Y=121 remained alive after one, but X=1386/Y=121
had HSalive=`00` after two, before the jump input. A shorter
sub-interval shift from the last safe X=1394 is the next concrete
test; it must trigger Up before the ledge edge and align the airborne
hero with the higher solid step while still above its top.
A 250,000-cycle extra Left shift after the safe X=1394 also reached
X=1386 with HSalive=`00` before Up. A finer shift is required; no
higher-step landing is established by these controls.

Follow-up 2026-10-20 (left-jump death isolated): a finer 175,000-cycle
shift stayed alive at X=1388/Y=121, but Left+Up then died at
X=1386/Y=107. Jumping immediately from X=1394 also died in the first
interval at X=1388/Y=100. A bounded 8-KiB Fast-RAM snapshot and a
one-address HSalive write trace for that latter full DF0 cold start
identify **monster collision**, not proof that the ledge edge itself
is lethal: frame 16563 wrote HSalive=`00` through `killHero` at
`$07FEBE60`, with `hero_coll` return `$07FEC580`. The active Type-12
bat record `$07FF8194` moved from X=1373/Y=121 to X=1380/Y=121
across the contact; a second Type-12 bat remained farther right at
X=1579→1572/Y=121. The next concrete test is a left-facing,
grounded Fire from the safe X=1394 ledge position, monitoring the
near bat's Type/flags and the hero's sustained survival before
attempting the higher-step jump again.
The first grounded left-facing Fire control at X=1394/Frame 16560
started too late: the Bat advanced X=1373→1387 while still Type 12/
flags `$80`, and HSalive changed to `00` by Frame 16574. The next
control begins the same grounded strike soon after first landing on
the ledge, before the Bat has approached; a kill and survival must
both be observed.
The earlier grounded strike control is promising but not yet a
replayed passage: land at X=1408/Y=121 by Frame 16511, move Left one
interval to X=1394, stop and press Fire facing left at Frame 16518.
The near Type-12 bat (`$07FF8194`) approaches X=1331→1369, changes
Type 12→0 by Frame 16560 while the hero remains alive at
X=1393/Y=121, and reaches flags `$40` by Frame 16602. The next
probe releases Fire and attempts the leftward higher-step jump;
only a sustained landing and separate full DF0 replay can establish
that passage.

Follow-up 2026-10-20 (near bat and higher castle step passed): after
the early left-facing Fire, the Type-12 bat at `$07FF8194` becomes
Type 0 by Frame 16560 and flags `$40` by Frame 16602. Releasing Fire
and pressing Left+Up from the living X=1393/Y=121 ledge position
lands the hero on the higher fixed step at X=1332/Y=73; eight neutral
500,000-chip-cycle intervals keep him alive through Frame 16687.
`Tools/a3000-celtic-heart-stage-three-high-castle-step-route.json`
guards hero X=1332/Y=73, HSalive=`FF`, bat Type 0/flags `$40`, zero
non-ROM exceptions and exact frame SHA-256
`0EA3738AD2E69D2F20B0816CB1036F34F701B81D36EC5D5050FEFBED773B59CC`.
The route validated and passed a separate full DF0 cold-start replay;
the captured image shows the living hero on the higher platform in
active Stage-3 graphics. Count the near bat and this stable high
step as passed. Next rearm Up, jump right to the castle roof above
the solid face, and verify a sustained landing before extending the
guarded route. The farther Type-12 bat and Stage 3 remain open.

A direct Right+Up jump from the high step X=1332/Y=73 reached
X=1374/Y=53, then met the roof's left face, fell and returned to
the lower ledge X=1408/Y=121 alive by Frame 16800. This is not a
roof landing. The next control moves right while grounded on the
high step before starting Up, to align the jump apex with the roof
above X=1392. Count the roof only after a sustained landing and
independent full DF0 replay.

Follow-up 2026-10-20 (castle roof landed): the aligned jump from
the stable higher step succeeded. Release Up, walk Right from
X=1332/Y=73 for 500,000 plus 200,000 chip cycles to X=1352/Y=73,
then press Up while continuing Right. The hero clears the roof's
left face at X=1394/Y=41 and remains on its solid top at
X=1436/Y=41 through eight neutral intervals to Frame 16795.
`Tools/a3000-celtic-heart-stage-three-castle-roof-landed-route.json`
guards X=1436/Y=41, HSalive=`FF`, the near bat Type 0/flags `$40`,
zero non-ROM exceptions and exact image SHA-256
`476C6CDE6F16AAA7C1A1599A1B6E2DFB6241496986B5900716633A9E6786AA1A`.
It validated and passed a separate full DF0 cold-start replay; the
captured active game image shows the living hero standing above the
formerly blocking white castle face. Count the roof landing as
passed. Next traverse the roof and its right edge with real inputs,
measure the farther Type-12 bat and require another sustained,
independently replayed landing beyond it. Stage 3 and the full
roadmap remain open.

A first right-edge continuation walked from X=1436/Y=41 to
X=1492/Y=46 while alive, released Right and began descending; it
died at X=1492/Y=73 by Frame 16838. The farther active Type-12 bat
(`$07FF8144`) approached from X=1538/Y=121 to X=1496/Y=121 at
that time. This is a negative control, not a landed passage; the
specific kill path was not yet traced. Test a rearmed Right+Up
jump from the roof before its edge to clear the bat and land on the
next solid right-side platform, then verify it independently.

The Right+Up control from X=1464/Y=41 did not gain height: by
X=1506/Y=67 the hero died while the active bat was X=1503/Y=121.
Timing, rather than this jump, gives a reproducible live passage.
Hold still on the roof X=1436/Y=41 for ten 500,000-chip-cycle
intervals, allowing the bat (`$07FF8144`) to move left to X=1489,
turn and reach X=1508 moving right. Then hold Right for four
intervals, stop at X=1492 and wait seven more intervals. The hero
lands at X=1492/Y=73 and remains alive through Frame 16943 while
the bat retreats to X=1584/Y=121. The guarded route
`Tools/a3000-celtic-heart-stage-three-far-roof-landed-route.json`
requires HSalive=`FF`, bat Type 12/flags `$80` at that distant
position, zero non-ROM exceptions and exact image SHA-256
`BB4AC1AA5CF8F8C271A5A7E2A49E6BCB51B52A100A994EBD3897E1DEFCCB41EF`.
It validated and passed a separate full DF0 cold-start replay;
the captured image shows active Stage-3 graphics and a living hero
on the right-side fixed platform. Count this timed roof-edge
landing as passed. The second bat remains active, so the next step
is to measure its safe approach or grounded strike window while
moving right from X=1492; demand a sustained live passage and
independent replay. Stage 3 remains open.

A standing right-facing Fire control from X=1492/Y=73 started after
three neutral intervals (Frame 16964) as the known Type-12 bat
`$07FF8144` approached from X=1563/Y=121. It never changed Type 12
or flags `$80`; HSalive became `00` at Frame 17035/X=1492/Y=73.
This is not a kill. The vertical separation between hero and this
bat makes a direct strike doubtful. A second, previously inactive
Type-12 bat record `$07FF81E4` exists at X=1584/Y=73. The death
source and both bats' live positions require a bounded trace before
assuming which obstacle killed the hero or selecting the next
legitimate route.

The bounded right-edge HSalive write trace now resolves that earlier
Frame-16835 death as a **monster collision** (`killHero` at
`$07FEBE60`, `hero_coll` return `$07FEC580`). The third Type-12 bat
`$07FF81E4` had activated and descended toward the same height as
the hero: X=1515/Y=73 at Frame 16823, X=1508/Y=73 at Frame 16830,
X=1501/Y=73 by Frame 16838. The previously watched second bat
`$07FF8144` remained lower at Y=121. Thus the roof-edge death
cannot be assigned to the lower bat alone. The fire control from
X=1492 must watch **both** bats' Type/flags and HSalive before a
passage or kill is claimed.

Both bat watches in the full DF0 Fire replay show the earlier
Frame-16964 standing strike was **too early for either bat**: the
lower `$07FF8144` moved X=1563→1493/Y=121 and the upper
`$07FF81E4` X=1568→1498/Y=73, but both stayed Type 12/flags `$80`.
HSalive was written `00` at Frame 17029 through `killHero` with
`hero_coll` return `$07FEC580`; the upper bat was X≈1504/Y=73 at
contact. Source `hero.asm` gives a standing strike Y offset of 4
and `monster.asm` requires overlapping strike/MOB rectangles. The
nearer approach occurs only after the initial strike window, so
the next bounded control delays Fire by one 500,000-chip-cycle
interval while watching both bats and the hero. No kill or passage
is claimed from the earlier Fire control.

The one-interval-later Fire control at Frame 16971 hits the **upper**
bat `$07FF81E4`: Type 12→0 by Frame 17013, flags `$40` by Frame
17056, while the hero remains alive at X=1492/Y=73. The lower
bat `$07FF8144` stays Type 12/flags `$80` at X=1504/Y=121.
HSalive has no death write in this bounded trace. This is a live
enemy defeat in a full DF0 cold probe, but a lasting passage beyond
its old position still needs real Right input and independent route
replay; neither bat should be conflated with the other.

Follow-up 2026-10-20 (upper bat passage): after that timed Fire,
release Fire, hold Right for five 500,000-chip-cycle intervals, then
release Right and wait seven intervals on the fixed upper platform.
`Tools/a3000-celtic-heart-stage-three-upper-bat-passage-route.json`
guards the live hero X=1564/Y=73 through Frame 17140, upper bat
Type 0/flags `$40`, lower bat still Type 12/flags `$80` at
X=1581/Y=121, zero non-ROM exceptions and exact image SHA-256
`0FA7EA0740DF9E764B92A6EADCFFDFA3ED95499C7FC1B67EB4022F2A3032FA30`.
The route validated and passed a separate complete DF0 cold-start
replay; its captured image shows active Stage-3 graphics and the
living hero beyond the defeated upper bat. Count this upper bat and
stable rightward passage as passed. The lower bat remains active
beneath the platform. Next map the solid wall at X=1600 and the
available take-off tiles near the platform's X=1567 edge, then test
a legitimate jump or other passage with real input. Require a
sustained live landing and independent full cold replay before
claiming the wall. Stage 3 and the seven-point roadmap remain open.

Follow-up 2026-10-20 (wall geometry and bounded controls):
`level03.tmx` and `w1types.tmx` show solid BACK tiles across
columns 100-102 (X=1600-1647) from rows 2-9 (Y=32-159).
Rows 10-11 are non-solid there, while row 12 is solid, leaving a
possible low approach with the hero standing at Y=169. The upper
fixed platform carries the hero at Y=73 only through column 97
(X=1567); columns 98-99 at row 6 are non-solid. A full DF0 cold
probe from the guarded X=1564 state held Right for three intervals:
the hero stepped off, fell to the lower fixed ledge at X=1584/Y=121
and remained alive through Frame 17182, but Right was blocked by
the solid wall. The lower Type-12 bat stayed active at Y=121 and
travelled left from X=1574 to X=1539 during the hold. A second
full cold probe jumped Left+Up from that ledge while the bat moved
away; the hero rose over it and landed back on the upper fixed
platform X=1512/Y=73, alive through Frame 17267. This does not
cross the wall or reach the lower Y=169 route. Next time the lower
bat's return or strike window, then leave the **lower** ledge to
the left of its X=1520 start without re-jumping onto the upper
platform; test descent to Y=169 and a real Right passage beneath
rows 10-11. Guard any claimed passage with a fresh full cold
replay, live HSalive and image evidence. Stage 3 remains open.

Follow-up 2026-10-20 (lower bat and ground landing): the lower
Type-12 bat `$07FF8144` reverses near X=1488 and returns right.
From the live lower ledge X=1584/Y=121, one Left interval faces the
hero left and leaves him near X=1568. Wait ten 500,000-chip-cycle
intervals to Frame 17260, then use standing Fire. The bat moves
X=1514→1548 and changes Type 12→0 at Frame 17295; its flags become
`$40` by Frame 17337 while HSalive remains `FF`. After releasing
Fire, five genuine Left intervals leave the lower ledge to the left
of X=1520, and eight neutral intervals land the hero stably at
X=1496/Y=169. The guarded route
`Tools/a3000-celtic-heart-stage-three-lower-bat-ground-route.json`
passed validation and a separate full DF0 cold-start replay through
Frame 17464, with both bats Type 0/flags `$40`, HSalive=`FF`, zero
non-ROM exceptions and exact image SHA-256
`4F363F7CA882D820D82D5CEEDAB048A76485A5C5900507CAA57F87738DE4E006`.
The captured frame shows the living hero on the lower castle floor.
Count the lower bat defeat and stable ground landing as passed.

The first Right probe from that ground position reached X=1594/Y=169
alive at Frame 17513 but died at X=1596 by Frame 17520. A bounded
Fast-RAM snapshot immediately before death identified an active
Type-9 enemy record `$07FF7AB4` at X=1606/Y=169 (flags `$80`), near
the hero. This suggests monster contact; the probe did not isolate
the kill call, so do not claim a confirmed cause or a wall passage.
Next stop earlier on the lower floor, measure this enemy's patrol
phase and use a cowering Fire strike as required for Type 9, or
an independently verified safe dodge. Then test a sustained living
Right passage through the mapped Y=169 opening and guard it with
another complete DF0 cold-start replay. Stage 3 remains open.

Follow-up 2026-10-20 (far castle wall passed): a full DF0 cold
probe stopped at ground X=1552/Y=169 and watched the Type-9 enemy
`$07FF7AB4` walk left from X=1627/Frame 17492 to X=1571/Frame
17548. The still hero died by Frame 17555 with the enemy at
X=1564; this is a timing control. Crouch at Frame 17513, allow one
500,000-chip-cycle interval to establish Down, then press Fire at
Frame 17520. The Type-9 enemy changes to Type 0 by Frame 17548,
flags `$40` by Frame 17590, and HSalive remains `FF`. Genuine Right
input then reaches X=1594/Y=169 alive. Immediate continuation
died at X=1606/Frame 17640 despite the defeated enemy. The map's
FRONT tile GID 387 at column 101/row 10 is animated: its initial
tile 66 has TYPE 0, but frames 126-128 have deadly TYPE 9. This
explains a plausible separate timed hazard, though the kill call
was not isolated; do not attribute the death conclusively to it.

Stopping at X=1594 for eight neutral 500,000-chip-cycle intervals
before pressing Right gives a reproducible safe window. The hero
passes the low opening beneath solid BACK columns 100-102 and
lands on the far ground at X=1680/Y=185, alive through Frame 17759.
`Tools/a3000-celtic-heart-stage-three-low-wall-passage-route.json`
guards HSalive=`FF`, both bats and the Type-9 enemy Type 0/flags
`$40`, zero non-ROM exceptions and exact image SHA-256
`77A936D37C2F7C06092A6CECBED71B63FC474D1C5FFD0EE40D27A32BD64AF917`.
It validated and passed a separate complete DF0 cold-start replay;
the captured active game frame shows the living hero beyond the
castle wall. Count the crouching strike and this wall passage as
passed. Next inspect the flying and ground hazards visible to the
right of X=1680, measure their MOB phases and find a sustained
living continuation with real inputs and a fresh independent cold
replay. Stage 3 and the full roadmap remain open.

Follow-up 2026-10-20 (far missiles and raised Type-5 enemy): from
the independently replayed X=1680 route, genuine Right input reaches
X=1778/Y=185 at Frame 17809. Waiting on the ground dies at Frame
17837. A bounded 8 KiB Fast-RAM snapshot finds an active Type-14
missile at X=1768/Y=185 and another at X=1768/Y=89 after that death;
the first is a likely ground hazard, but the kill call was not traced.
Pressing Up at X=1778 instead reaches the raised fixed platform at
X=1780/Y=137 and stays alive while the lower missile travels left.
A neutral continuation then dies at Frame 17914. A second bounded
snapshot at the last live Frame 17907 identifies an active Type-5
monster at `$07FF7A64`, X=1797/Y=137, 17 pixels from the hero;
the kill call is again untraced, so this is a contact hypothesis.

The timed counteraction is a real crouching Fire input begun at Frame
17865 on the raised platform. The Type-5 monster changes Type 5→0
at Frame 17900, its flags settle to `$40` by Frame 17942, and the
hero stays alive. Releasing Fire and Down, then holding Right for
four 500,000-chip-cycle intervals and waiting three more leaves the
hero stably at X=1836/Y=137, beyond that monster. The guarded route
`Tools/a3000-celtic-heart-stage-three-raised-spider-passage-route.json`
validated and passed a separate complete DF0 cold-start replay through
Frame 17999. It checks HSalive=`FF`, defeated Type-5 flags/type
`$40`/`$00`, zero non-ROM exceptions and exact active-image SHA-256
`9A258F19DB3D497440CE1EF5D3388695B3B6DFB5342AEAC2771509E8DFCAFF70`.
The captured image shows live gameplay on the raised platform. Count
the missile dodge, Type-5 defeat and stable raised passage to X=1836
as passed. Next inspect the platform edge near X=1855 and the next
missile/monster phase; test a real jump or timed descent, then require
a sustained living landing and independent full cold replay. Stage 3
and the full seven-point roadmap remain open.

Follow-up 2026-10-20 (upper approach to the next pillar): a full DF0
cold-start continuation from X=1836/Y=137 pressed Right+Up. The hero
rose only to Y=87 and stopped at X=1872 against the mapped solid
BACK column 118 (world X=1888), whose tiles are solid from Y=64
through the ground. He fell to Y=185 and died after stopping at
Frame 18069. This direct jump does not pass the pillar.

A separate genuine route backtracked left on the raised platform to
X=1794, jumped Left+Up, and landed stably on the **higher** fixed
platform at X=1738/Y=89. Ten neutral 500,000-chip-cycle intervals
kept HSalive=`FF` through Frame 18118. The guarded
`Tools/a3000-celtic-heart-stage-three-upper-left-platform-route.json`
validated and passed an independent complete DF0 cold-start replay,
with exact image SHA-256
`4711E21712D9B40044CF6DDC06915FEF8A943A6C369477C3178FC8AC5D0BCE79`,
defeated Type-5 flags/type `$40`/`$00`, zero non-ROM exceptions,
and an active frame showing the living hero on that higher platform.
Count the higher-platform landing as passed; it is an alternative
approach, not a crossing of X=1888.

An independent full cold probe pressed Right+Up from this higher
X=1738/Y=89 platform. Its apex was X=1780/Y=39 at Frame 18139;
the hero reached X=1864/Y=146 while descending, then stopped at
X=1872/Y=185 and died at Frame 18210. This confirms that a direct
long jump from the higher platform also fails to clear the X=1888
column. The result is a negative control, not a passage.

Map collision data also shows the earlier wall at world X=1600 begins
at Y=32. A full DF0 cold-start control jumped Right+Up from the
previously passed upper-bat platform X=1564/Y=73; its apex was
Y=35, leaving the hero blocked at X=1584 and later alive back on
the lower X=1584/Y=121 ledge. This is not a high-wall passage.

Follow-up 2026-10-21: the apparent direct Left approach from
X=1738/Y=89 dies at X=1722/Y=89 (frame 18056). A bounded HSalive
write trace has the BACK-collision return stack $07FEBC80/$07FEBA90/
$07FEBA0C/$07FEC794, matching the animated, deadly GID 66 at
X=1712-1727/Y=96. Releasing Up at X=1724 is too late: the hero dies
while beginning to rise. A Left+Up jump from X=1738 avoids it. Stop
Left after three 500,000-chip-cycle intervals at X=1696/Y=39, release
Up, and wait ten intervals. The full DF0 cold route
`Tools/a3000-celtic-heart-stage-three-dead-tile-overjump-route.json`
lands alive at X=1696/Y=89, frame 18210, hash
`13EC18F1212C05D640372BAD57CFF6778E6505E57636AC74CD13349B1C940E05`.
An independent complete cold replay passed with zero non-ROM exceptions;
the active image was reviewed. From that stable landing, two Left+Up
intervals to X=1668/Y=43 followed by release and ten neutral intervals
land on the tiny solid tile at X=1668/Y=41. The independently repeated
full DF0 route `Tools/a3000-celtic-heart-stage-three-tiny-high-step-route.json`
confirms frame 18294, HSalive=FF, dead Type-5 flags 40/type 00,
zero non-ROM exceptions, and exact image hash
`B3F4BC639C8967198ABB510C741D4C242213AAD8FA0626213BCEC5C9E7ACDFA4`.
The image shows the living hero on that tile. Neither step crosses the
X=1888 wall or completes Stage 3. A first full cold-start Right+Up
probe from X=1668/Y=41 reached X=1724/Y=-6 alive at frame 18322,
but died at X=1738/Y=9 by frame 18329. A bounded full-cold
HSalive write trace records PC $07FEBE60 and return stack
$07FEC580/$07FEC046/$07FEC794: this is the monster collision
path, not the earlier BACK-tile collision path. The two watched
Type-14 missiles were at X=1872/Y=89 and Y=185 at that time, so
neither overlapped the hero. A bounded 8 KiB Fast-RAM snapshot at
frame 18322 identifies the actual overlapping threat: active Type-6
knight record $07FF7A14 at X=1746/Y=9, flags 80. It begins on the
high platform near world X=1808/Y=32 in level03.tmx and patrols left.
An independent full cold probe released Right at X=1724/Y=-6;
the hero survived frame 18343 at X=1724/Y=63, then died by frame
18351 at X=1724/Y=68 while falling back into the deadly GID-66
tile below. Stopping before the knight therefore does not land on
the high platform. Neither negative control is a passage. The high
platform begins at X=1744/Y=32, with FRONT GID 354 at X=1760/Y=16.
The next
concrete step is to offset the Type-6 knight's patrol phase while
safe on X=1668/Y=41, then jump into the platform after it moves away;
if needed, time a genuine right-facing strike while airborne.
A passing route must land alive on the high platform,
survive an independent complete DF0 cold replay and active-image
review, then clear X=1888. Stage 3 and the seven-point roadmap
remain open.

The first phase-offset control held the hero safely on X=1668/Y=41
for ten more 500,000-chip-cycle intervals. The high Type-6 knight
patrols X=1744..1808/Y=9: it was X=1774 at frame 18294, turned
near X=1744 by frame 18329, and returned to X=1782 by frame 18365.
The delayed Right+Up leap reached X=1752/Y=9 alive at frame 18407,
then met the still-active knight at X=1774/Y=9 by frame 18421 and
died. This is not a passage. It establishes a stable window on the
high platform before contact; the next bounded Fire probe stops at
X=1752 and attempts a grounded right-facing strike as the knight
returns. Only a killed or avoided knight followed by sustained live
movement can extend the durable cold-start route.

That grounded Fire probe succeeded. From the tiny tile X=1668/Y=41,
wait ten intervals for the knight to turn right, then hold Right+Up
for six intervals to X=1752/Y=9, release both directions and press
Fire facing right. The knight advances from X=1793 at frame 18407
to X=1765 at frame 18435, where its Type changes from 6 to 0;
the hero remains alive. By frame 18484 its flags are 40, and after
eight further neutral intervals the hero remains grounded and alive
at X=1752/Y=9 on the high platform (frame 18533), with zero
non-ROM exceptions and reviewed active gameplay. The exact image
hash is `7E5B18DAD567C001E024428A37BCAB4E2DD76BE2A161A1A89B95022A1B8547DF`.
The durable `Tools/a3000-celtic-heart-stage-three-high-knight-passage-route.json`
asserts that hash, position, HSalive and the dead knight. A separate
complete DF0 cold replay passed every assertion, including the exact
frame hash, with zero non-ROM exceptions; the active image was
reviewed. This is a stable high-platform landing and knight passage.
The next concrete step is to move right to the platform edge around
X=1808 and attempt a rearmed Right+Up crossing of the X=1888 pillar,
requiring a stable living landing beyond it.

An immediate full-cold Right continuation after the high-knight
route walks from X=1752/Y=9 to X=1810/Y=9, then Right+Up crosses
the solid X=1888 pillar in the air: X=1908/Y=31 at frame 18611,
X=1922/Y=59 at frame 18618, and X=1950/Y=89 alive at frame
18632. The next neutral interval dies at X=1952/Y=89. This is
an airborne crossing, not a completed far-side passage. Map data
show a solid landing platform beginning at X=1920/Y=112 and a
monster start around X=1984/Y=112. A short-stop probe at X=1922
with a bounded MOB snapshot must establish a living landing before
the cold route can be extended.

The short-stop probe succeeded on the far side: release Right and Up
at frame 18618/X=1922/Y=59, two pixels past the pillar, and wait ten
500,000-chip-cycle intervals. The hero lands at X=1924/Y=89 by frame
18632 and remains alive through frame 18688. The active frame was
reviewed; the image hash is
`B49C31A00863CC2559820D403905706C93C7E54DA1A931A00C4DEB457589A789`
with zero non-ROM exceptions. A bounded MOB snapshot before landing
shows a separate active Type-6 knight at X=1956/Y=89 (record
$07FF79C4), plus two Type-14 missiles at X=1864/Y=89 and Y=185.
The durable full-cold route
`Tools/a3000-celtic-heart-stage-three-farside-pillar-landing-route.json`
asserts the living landing. A separate full DF0 cold replay passed
the exact hash and RAM assertions with zero non-ROM exceptions, and
the active image was reviewed. The X=1888 wall crossing and far-side
landing are therefore passed. The next concrete step is to measure the
nearby knight's return phase and test a grounded right-facing Fire
from the stable X=1924 ledge. The wall crossing is not a Stage-3
level transition.

A first full-cold Fire probe from the stable X=1924/Y=89 landing
at frame 18688 was too late: the active Type-6 knight had already
advanced to X=1944/Y=89, and the hero died at frame 18702 while
the knight remained Type 6/flags 80. The knight is X=1972 at
frame 18660 and X=1965 at frame 18667, approaching leftward by one
pixel per frame. The next concrete control begins Fire at frame
18667, while the hero is already grounded and the knight is still
41 pixels away; watch Type, flags and HSalive to determine whether
the strike activates before their collision. A death image is not
evidence of further gameplay.

The frame-18667 grounded Fire timing succeeded in a complete DF0
cold run. From X=1924/Y=89, the approaching Type-6 knight was
X=1965/Y=89 when Fire began; by frame 18695 it was X=1937 and
its Type changed to 0 while HSalive stayed FF. After the death
animation its flags are 40, and the hero remains alive and fixed
at X=1924/Y=89 through frame 18780. The exact final image hash is
`5AC32D2784C1AE5699EEE417C0723C1765151533C21D7739DCF4E8A71D271172`,
with zero non-ROM exceptions. The durable route
`Tools/a3000-celtic-heart-stage-three-farside-knight-passage-route.json`
asserts these values. A separate complete DF0 cold replay passed
the exact image and RAM assertions with zero non-ROM exceptions;
the active gameplay image was reviewed. This far-side knight defeat
and stable ledge position are passed.
The next step is to continue with real Right/Up/Fire inputs beyond
this far-side knight and require another stable, visible living
landing before recording further Stage-3 progress.

Follow-up 2026-10-21 (far-side elevated route): Direct Right from
X=1924/Y=89 dies at X=1960/Frame 18798. A bounded HSalive write
trace records the BACK-collision return stack
`$07FEBC80/$07FEBA90/$07FEBA0C/$07FEC794`, rather than the
monster-collision return; `level03.tmx` places GID 15 (TYPE 16 in
`w1types.tmx`) at X=1952/Y=96. This is a rejected straight-walk
control, not a gameplay pass. A real Right+Up jump avoids that tile
and lands at X=2008/Y=137. Holding still there dies at Frame 18870:
the separate write trace has monster-collision return `$07FEC580`,
and bounded MOB watches show a right-moving Type-14 missile reaching
the hero. One more real jump moves to X=2092/Y=137 but later standing
still also meets the missile. A third rearmed Right+Up jump reaches
the fixed green stone at X=2180/Y=121; eight neutral intervals leave
the hero alive through Frame 18984. The durable full DF0 cold-start
route `Tools/a3000-celtic-heart-stage-three-farside-three-jump-passage-route.json`
asserts X/Y, HSalive=FF and image hash
`021532D382F3DEEF94A3EC86F1E1752A018A9E6BB907D3152BE679F219F75670`.
A separate full cold replay passed all assertions with zero non-ROM
exceptions, and its active game image was reviewed. This is a passed
three-jump missile/tile passage, not a Stage-3 level transition.

From X=2180 a fourth real Right+Up jump lands on the next fixed
stone at X=2236/Y=89. Directly jumping onto the animated GID 168
step near X=2256 in the wrong phase kills the hero at X=2244.
Four 500,000-chip-cycle intervals of waiting on the safe stone
shift the phase: a short jump then lands at X=2268/Y=57 alive,
but staying there through Frame 19156 dies when the animation
changes (same BACK-collision return stack). An immediate rearmed
Right+Up jump instead lands on the upper fixed platform at
X=2324/Y=25 and remains alive for eight neutral intervals through
Frame 19223. The durable route
`Tools/a3000-celtic-heart-stage-three-farside-upper-platform-route.json`
asserts HSalive=FF, X/Y and exact image hash
`3AA8A1767BFA0C93ADE2419A267C229EDDC746BDC3FE53137DB071D5BD7BCC3D`.
An independent complete DF0 cold replay passed the assertions with
zero non-ROM exceptions; the active game image was reviewed. A
short-lived savestate probe then jumped over another animated tile
on the upper platform and remained alive at X=2380/Y=25 through
Frame 19308, but this latter position has not yet had an independent
full cold replay and is not counted as a durable passage. The next
concrete step is to reproduce X=2380 cold, then inspect the Type-12
bat and the tall solid column near X=2448 before attempting a live
crossing. Stage 3 and the seven-point roadmap remain open.

That X=2380/Y=25 upper-platform landing has now passed an independent
complete DF0 cold replay as
`Tools/a3000-celtic-heart-stage-three-farside-high-platform-crossing-route.json`:
Frame 19308, HSalive=FF, exact image hash
`33B125D4B7C689637437A63DDC5157B71ED09D27DEED426CF11B480AC0D6B789`,
zero non-ROM exceptions, active game image reviewed. A further real
Right+Up jump from X=2380 goes above the solid X=2448 column and
lands on its far-side ground at X=2464/Y=185. The durable
`Tools/a3000-celtic-heart-stage-three-second-farside-pillar-route.json`
independently repeated the full DF0 cold sequence and asserted the
stable living landing at Frame 19406, hash
`04525C0CB871E05D754388852CFEE68696743C55B70039689378B80D5C667562`,
with zero non-ROM exceptions; the active image shows the player on
the floor just right of the column. Both passages are accepted. The
earlier pending-X=2380 note above is superseded by these replays.

The next ground Type-6 knight starts at X=2508/Y=185 and approaches
the player at X=2464. A standing right-facing Fire from that safe
position changes its Type from 6 to 0 at Frame 19434; after the
death animation its flags are 40. Real Right input then leads to a
stable living X=2536/Y=185 through Frame 19582. The durable
`Tools/a3000-celtic-heart-stage-three-second-farside-knight-route.json`
asserts position, HSalive=FF, knight Type0/flags40 and exact image
hash `EB4F4B10C4E8FA6373876181C92B2C8EF72239E823F711A9C9E6CF2C44AFF4DB`.
An independent full DF0 cold replay passed every assertion with zero
non-ROM exceptions; its active ground-gameplay image was reviewed.
This knight and the passage to X=2536 are accepted, not a level end.

A bounded MOB snapshot at that point shows active Type-12 bats at
X=2536/Y=137 and X=2499/Y=73, and a Type-7 ground enemy farther
right. Direct Right from X=2536 dies at X=2590/Frame 19609; the
HSalive write has the `$07FEC580` monster-collision return, and the
Type-7 enemy was X=2608/Y=185 shortly before contact. A standing
Fire started at X=2578/Frame 19603 is too late: the enemy remains
Type7/flags80 and the hero dies. A short-lived genuine Right+Up
jump started earlier, from X=2550/Y=185, passes over the enemy and
lands alive at X=2648/Y=185 through Frame 19695. This last landing
has now also passed an independent complete DF0 cold replay as
`Tools/a3000-celtic-heart-stage-three-third-ground-enemy-jump-route.json`.
The route asserts HSalive=FF, X/Y and image hash
`FDBDD29D8D58E5450B913FE09CE8DCB28E44723ABA12EBC1F4B13BA8CCD72A43`;
the replay has zero non-ROM exceptions and its active game image was
reviewed. The Type-7 enemy remains alive behind the player, so this
is a verified jump past it, not a kill. A further short-lived Right
probe remained alive through X=2748/Y=185 and Frame 19779, near the
next raised ice/stone boundary. The durable route
`Tools/a3000-celtic-heart-stage-three-farside-ground-corridor-route.json`
now independently repeats that farther living position from a full
DF0 cold start: HSalive=FF at Frame 19779, exact image hash
`B57A975F743B7FF53E67AFB8DD5F986C59B101EB1286217F440661BABC658F75`,
zero non-ROM exceptions and reviewed active gameplay. The farther
ground segment is passed. The next concrete test is to find the safe
takeoff before the solid rise and animated GID 66 near X=2816,
then prove a sustainable landing above or beyond it with another
full cold replay. Stage 3 and the full roadmap remain open.
All new diagnostic runs used the ignored repository `tmp` directory
and `--no-final-checkpoint` except the explicitly named short-lived
continuation checkpoints. A targeted recursive cleanup of a verified
output directory inside `tmp` was rejected by the automatic command
policy (`blocked by policy`); these regenerable outputs remain there.
No ROM, ADF, installed HDF or normal save was changed or deleted.

The far-side Stage-3 ascent has advanced through the animated BACK
GID 66 near X=2816. From the stable X=2748 ground route, the hero
walks to X=2768, jumps vertically, moves right for one interval, and
releases Right at X=2782; the first solid step holds the living hero
at X=2784/Y=153. A second Right+Up jump crosses the active tile and
stops at X=2855/Y=121, which remains safe for ten neutral intervals.
`Tools/a3000-celtic-heart-stage-three-animated-tile-far-landing-route.json`
passed an independent complete DF0 cold replay at Frame 19976 with
HSalive=FF and image hash
`1627D506305E5FBF95837DD898B06793A574D3D5254FE3F09781BD4F6D5F73E7`;
the active image was checked. Earlier direct jumps from ground at
X=2748 or X=2768 died on the BACK collision, and a longer hold at
X=2768 let a Type-7 enemy approach. These negative probes are not
passes.

The following water gap is also independently cold-replayed. A
short approach to X=2877, one neutral 500,000-chip-cycle interval,
and Right+Up catches the moving Type-13 platform near X=2976/Y=153.
Twenty-four neutral intervals carry the hero to X=3005/Y=130;
Right+Up then lands safely on the far fixed bank X=3103/Y=153.
`Tools/a3000-celtic-heart-stage-three-moving-platform-water-gap-route.json`
asserts Frame 20346, HSalive=FF and hash
`C67FB2330F415C04A37632402B0EAA5800308D54DC22DD92307F9978B68044F5`.
The full DF0 cold replay passed with zero non-ROM exceptions and a
reviewed active image. The next animated GID 168 water stones require
two neutral intervals before a Right+Up jump; a direct jump died
in BACK collision. That timed jump landed alive at X=3232/Y=153,
and a grounded right-facing Fire from X=3260 killed the approaching
Type-6 knight (Type 0/flags 40). A separate complete DF0 cold replay
of `Tools/a3000-celtic-heart-stage-three-far-knight-ground-route.json`
asserted stable X=3360/Y=185 through Frame 20662, HSalive=FF,
the knight death flags, hash
`840775253F6FEA52B722B1A892FFBE4D0AC6D0CA06A5F4ECDEBFFBB4781E5014`,
zero non-ROM exceptions and an active image. An alternative full cold
route, `Tools/a3000-celtic-heart-stage-three-upper-platform-after-water-route.json`,
times a jump while the higher Type-5 patrol moves away and lands
stably on X=3416/Y=121 at Frame 20740, HSalive=FF, hash
`86975D0B5BA43C2BE10DA5DEB466A3194EBF65A12E4F19F00CC2F6A84FD9B23C`;
its independent replay and active image were checked. A first draft
of the later upper-spider cold route used the earlier X=3232 base
instead of this X=3416 base and failed its final hash assertion.
The corrected `Tools/a3000-celtic-heart-stage-three-upper-spider-passage-route.json`
independently passed a full DF0 cold replay: grounded Down+Fire defeats
the Type-5 spider (Type 0/flags 40), and the hero remains alive at
X=3500/Y=121 through Frame 20923. The exact image hash is
`0A89C6EB0ABF6F57B008725897FD2CD8ED1E6562EDF9F2D80291EA7B21173287`;
there were zero non-ROM exceptions and the active image was reviewed.

The Stage-3 end is now independently reproduced from a complete
A3000/Kickstart-3.1 DF0 cold start. After the far-side ground route,
real Right+Up inputs land successively on the fixed steps at
X=3616/Y=137, X=3660/Y=105 and X=3688/Y=73, then jump over the
solid X=3744 wall and settle alive at X=3760/Y=185. A grounded
Down+Fire strike at X=3760 defeats the first Type-8 ground spider;
after walking right, another grounded Down+Fire at X=3984 defeats
the second. Both MOBs become Type 0/flags 40. Continued real Right
input brings up the visible `LEVEL COMPLETE — PRESS BUTTON` screen
at Frame 21486, hash
`014D8F2CA5B789953F5A1FDCE85130696B95D1B8CDF8D14E8FEE00A316984702`.
The game's HSalive byte is 00 there because it has left active play;
the reviewed completion screen and following stage transition, not
that byte alone, establish success. Releasing Right and pressing Fire
produces the visible `SNOW MOUNTAINS STAGE 4` title with code `4TFO`
at Frame 21669, hash
`B93F28C78C27611345E414C5F307980B77EFC24428E61C48833C5DEFE0CB4688`.
`Tools/a3000-celtic-heart-stage-three-complete-stage-four-title-route.json`
asserts both image hashes and all earlier checkpoints; its separate
full DF0 cold replay passed with zero non-ROM exceptions, and its
Stage-4 title image was reviewed. Celtic Heart Snow Mountains Stage 3
is therefore complete by genuine gameplay and a reproduced visible
level transition. Stage 4 and the broader roadmap remain open.

After the Stage-4 title, the first Fire exposes the new castle level;
a second Fire sets HSalive=FF. Real Right input moved the hero from
X=16 to X=64/Y=185 and held him alive through Frame 22217 at a solid
castle wall. `Tools/a3000-celtic-heart-stage-three-complete-stage-four-active-route.json`
independently replayed the entire path from a DF0 cold start, asserted
HSalive=FF, X/Y and exact image hash
`E5534F15F9210CE28310E287CFD46B84C4D8A23D00DEB3FB7E49F20CC275D580`,
and passed with zero non-ROM exceptions. The active Stage-4 image was
reviewed. A short continuation with standing Up followed by Right
reached X=104/Y=169 but died at Frame 22275 near the pink foreground
hazard; this is not a passage. Next, determine the hazard's animation
phase and test a timed lower-corridor or upper-wall route with real
inputs, then independently cold-replay a stable living landing beyond
the first wall. Stage 4 and the seven-point roadmap remain open.

The first Stage-4 castle corridor is now passed with actual movement
and a separate complete DF0 cold replay. Eight initial 500,000-chip-
cycle phase offsets, and several backstep jumps, showed that immediate
Up then Right from X=64 dies at the first foreground hazard near
X=104/Y=169. A 50-interval neutral delay at safe X=64 changes the
hazard phase: the same Up/Right input then advances under the roof to
X=224/Y=169. Releasing both directions and holding ten more intervals
leaves the hero alive at Frame 22844. The independently repeated
`Tools/a3000-celtic-heart-stage-four-first-corridor-route.json`
asserts HSalive=FF, X/Y and exact image hash
`6426285104F4B8228D351644B96B5B0C6CEE73CED7C24D52CD1EB7786BFD8750`;
it passed with zero non-ROM exceptions and the active image was
reviewed. This is a corridor passage, not a Stage-4 completion.

Short-lived continuation from that checkpoint also cleared the next
raised wall with a rearmed Right+Up jump. A direct Right input stops
at X=224; jumping reaches X=320/Y=137 alive, but holding there or
walking to X=344 dies near a Type-15 ground patrol whose TMX start
is X=288/Y=160. An immediate second Right+Up jump from X=320
avoided that contact, landed on the lower floor, and stayed alive
at X=460/Y=169 for ten neutral intervals. A further Right input
reached X=544/Y=169, where another ten-interval hold remained alive
through Frame 23231, short-run hash
`7A24343A1A79E17651BACCD8FE2D0227CB38DAFF2F9919689E70698D528530DB`.
The complete DF0 cold route
`Tools/a3000-celtic-heart-stage-four-far-corridor-route.json`
contains both jumps and the X=544 assertion. Its independent complete
DF0 cold replay passed with zero non-ROM exceptions, HSalive=FF, the
exact X/Y and image hash above; the active image was reviewed. Count
this farther corridor and stable landing as passed.
The next active obstacle is a Type-12 bat starting near X=560/Y=144:
plain Right+Up from X=544 died at X=556-572, while several stationary
Up+Fire timing probes kept the hero alive but did not yet establish a
kill or crossing. Determine the bat strike/flight phase, then require
a stable living crossing and another complete cold replay. Stage 4
and the full roadmap remain open. All probes used ignored `tmp` and
`--no-final-checkpoint`; no user media or normal saves were changed.

The Stage-4 Type-12 actor was narrowed down with a bounded read-only
Fast-RAM snapshot from the safe X=544 checkpoint. Its 80-byte record
is at `$07FF7794`; X=553/Y=121, Type 12/flags `$80` at Frame 23231.
The nearby Type-15 actor is at `$07FF77E4`, X=293/Y=137. A watched
Up+Fire probe showed that the Type-12 bat stayed active while flying
away and returning; deaths at X=556-572 were confirmed as monster
collisions by HSalive write traces returning from `hero_coll`.
A neutral delay of sixteen 500,000-chip-cycle intervals at X=544
instead shifts the bat phase. A Right+Up jump reaches X=592/Y=137
alive; releasing both directions there for one interval rearms Up,
and a second Right+Up jump lands beyond the bat corridor at
X=728/Y=169. Ten neutral intervals keep HSalive=FF through Frame
23540, short-run hash
`BE7FB4F51AE52FCA8EADCAF6FF445FB045F1343ABE608CF11553CC50824858E6`.
`Tools/a3000-celtic-heart-stage-four-bat-double-jump-route.json`
validated and independently passed the full DF0 cold path and exact
assertions. Its replay has zero non-ROM exceptions and the active
image was reviewed. Count X=728/Y=169 and the sustained bat bypass as
passed. The bat remains alive behind the hero; this is timed avoidance,
not a kill. A short-lived direct Right continuation from X=728 reached
X=756 alive but died at X=770/Frame 23561. The HSalive write trace
identifies `hero_coll` monster contact, not a wall collision. Jumping
immediately at X=728 intersected an overhead actor at X=736/Y=113;
trying Up only after Right reached X=756 was too late. These are
negative controls, not a pass. Next locate the approaching Type-15
patrol from the map's X=864 spawn and the overhead Type-12 bat at this
phase; test an earlier/timed jump or valid strike and demand another
stable living full cold replay before extending the route. Stage 4
and the seven-point roadmap remain open.

A bounded X=728 Fast-RAM snapshot identified the immediate ground
patrol at `$07FF76F4`, Type 15/flags `$80`, X=803/Y=169, and the
overhead bat at `$07FF7744`, Type 12/flags `$80`, X=717/Y=105.
Twelve neutral 500,000-chip-cycle intervals measured the ground
patrol advancing left by about seven pixels per interval and the bat
turning near X=735 before moving left. Standing still dies when the
ground patrol reaches X=739 around Frame 23604. Up+Right immediately
from X=728 hits the bat; a three- or four-interval delay is also
fatal. A five-interval neutral delay to Frame 23576 opens a window:
Right+Up then clears both actors and lands the hero at X=848/Y=169.
After releasing directions, ten neutral intervals retain HSalive=FF
through Frame 23745; the ground actor is behind at X=744, bat behind
at X=682, both still Type/flags 15/80 and 12/80. The short-run hash
is `3A1679C0174970ABA48267ED748EB99C6E2CD13310A4BEEAF91BDFAED849ABB8`;
the image shows active gameplay. The durable full-cold route
`Tools/a3000-celtic-heart-stage-four-ground-patrol-bypass-route.json`
passed an independent full DF0 cold-start replay through Frame 23745.
It confirmed the exact image hash above, HSalive=FF, and zero non-ROM
exceptions. The replay image was inspected: the hero stands at
X=848/Y=169 in active castle gameplay. This is a timed bypass; neither
the Type-15 ground actor nor the Type-12 bat was killed. The next step
is to continue with real inputs toward the visible gold objects and
the next fixed castle rise, measuring newly activated MOBs before
claiming a further passage. Stage 4 and the seven-point roadmap remain
open.

From the independently established Stage-4 X=848/Y=169 landing,
continued real Right input collected the three visible gold objects and
reached a solid castle rise at X=960, HSalive=FF. Three short-lived
Right+Up timings gave distinct outcomes: jumping from X=932 collided
after X=962, jumping from X=960 died at X=980, but jumping from X=946
passed the rise and reached X=1088/Y=185 alive. Waiting there died
after a Type-15 ground patrol approached from X=1100; standing Fire
from several measured positions did not deactivate it. Releasing Up
during the first jump and re-pressing it on landing at X=1046 gave a
second actual jump over that patrol. Releasing both directions near
X=1188/Y=185 kept the hero alive for ten neutral 500,000-chip-cycle
intervals through Frame 23998. The actor remains active behind the
hero. Short-run image hash:
`4DE8E33B76B1F5C5F3550FB324BD47D11B18998ED8927384F7ED22F3ABD03587`.
The full replay image shows active gameplay beside a large green orb.
`Tools/a3000-celtic-heart-stage-four-second-patrol-double-jump-route.json`
validated and passed an independent complete DF0 cold-start replay to
Frame 23998, with the exact hash above, HSalive=FF and zero non-ROM
exceptions. The final active image was independently inspected. Count
the solid castle rise and the second Type-15 patrol bypass to X=1188
as passed, without claiming either patrol was killed. A direct Right
or immediate Right+Up continuation from X=1188 dies at X=1196. A
bounded RAM snapshot at the safe landing found active Type-14 actors
at X=1216/Y=57 and X=1216/Y=169. A third jump launched earlier from
X=1160 reached X=1246 alive, but a second Type-14 projectile spawned
near X=1328/Y=169 moved left to X=1270 and killed the hero at X=1260
on landing; an immediate fourth Up rearm was still too low to avoid
that collision. These are negative controls, not a pass beyond the
orb. Next measure the two projectile phases and test a timed hold or
elevated-platform approach before extending the independently replayed
route. Stage 4 and the seven-point roadmap remain open.

The next Stage-4 projectile study measured the first Type-14 pair
moving left 14 pixels per 500,000 chip cycles from X=1216 while the
hero stood at X=1188; neutral waiting died by Frame 24012. Matching
their speed with Left retreat survived until the solid ledge stopped
the hero at X=1078, after which the pair caught him. A vertical jump
from X=1144 let that pair pass beneath, but an immediate second approach
met the next spawned pair; extra waiting allowed the following Type-15
patrol to catch the hero. This rules out the tested ground-only timing,
not every possible input sequence.

An independent elevated alternative starts from the verified X=848
checkpoint: Right to X=946, a first Up+Right jump past the solid rise,
release Up in flight, then a second jump from X=1032. Stopping Right
after the first interval lands stably on the one-tile solid step
X=1048/Y=137; a further Up+Right jump lands on the fixed middle
platform X=1132/Y=105. From there, a timed Up+Right jump lands on the
upper platform at X=1216/Y=73. Releasing and re-pressing Up while
continuing Right clears the active upper/lower Type-14 pair; stopping
at X=1304/Y=185 yields a 15-interval living hold through Frame 24237.
The short-run image hash is
`0756CBCCBA51F44E1E5A788B3F577E7E8EE344AA7C2367EFAFA1430AC1FB4A81`.
The active image shows the hero beyond the green orb and before the
solid tall pillar at X=1344. `Tools/a3000-celtic-heart-stage-four-upper-orb-passage-route.json`
validated and passed an independent complete DF0 cold-start replay to
Frame 24237: exact image hash above, X=1304/Y=185, HSalive=FF,
zero non-ROM exceptions. Its final active image was independently
inspected. Count the elevated approach and Type-14 pair passage as
passed, without claiming a kill or passage through the pillar. At X=1304, a
vertical jump avoids a new projectile but the Type-15 pursuer from
the left catches the descending hero at X=1304/Frame 24279. Several
early left-facing crouched Fire attacks did not deactivate that pursuer.
The next step is to analyze a legitimate route around the tall solid
pillar and its two skull projectiles. `level04.tmx` places a solid
column at X=1344 from Y=32 through ground level; ordinary jumps from
the lower ground cannot clear it. Test a higher approach or a verified
game mechanic before claiming the far side.
Stage 4 and the overall roadmap remain open.

Cleanup note: the independent replay wrote regenerable images under
ignored `tmp/stage4-upper-orb-passage-full` (about 88 MB). After review,
both a path-checked recursive removal and a narrower removal of only
intermediate PNGs were rejected by the automatic command policy, so
the output remains under `tmp` and no other files were touched.

The Stage-4 pillar was a misleading continuation target. Inspection of
`level04.tmx` and `w1types.tmx` found the actual TYPE-7 level-exit
background tiles at X=1216/Y=176-207, **before** the solid X=1344
column. From the independently established X=1132/Y=105 middle-platform
checkpoint, two Right intervals leave its edge at X=1160; three neutral
intervals land at X=1160/Y=185. Right then reaches X=1202, triggers
`exitHero`, and shows `LEVEL COMPLETE`. HSalive changes to 00 on this
path as part of the level transition, not as proof of a death: the
bounded watch saw the nearby Type-15 pursuer still at X=1119 and both
Type-14 projectiles at X=1290 when the transition began. After the
complete-screen button, the visible `SNOW MOUNTAINS STAGE 5` title
appears at Frame 24849. The full DF0 cold-start route
`Tools/a3000-celtic-heart-stage-four-complete-stage-five-title-route.json`
passes with exact title hash
`85E0FD5AD73CA636BBBA13F5BACE1B779E644BCF99739E008CF07C3EFF006573`
and zero non-ROM exceptions; the independently repeated cold start in
`Tools/a3000-celtic-heart-stage-five-active-movement-route.json` checks
the same title hash before continuing. Both complete-screen and title
captures were visually reviewed. **Snow Mountains Stage 4 is complete.**
The earlier X=1304/Y=185 upper-orb passage remains valid gameplay, but
the impassable column beyond it is not needed for the level exit.

Stage 5 now reaches active play after the title button and a separate
ready-button press. The independently cold-replayed active route above
checks Right movement to X=76/Y=41 at Frame 25060, HSalive=FF, exact
image hash
`C0941CCD2E4941E17F8851E7B612D5F49F3E008E73229F0E6EEC040150177F22`,
zero non-ROM exceptions and a reviewed live game image. The first gap
has a phase-dependent GID-168 middle stone at X=208 and a fixed GID-164
stone at X=272 (`level05.tmx`/`w1types.tmx`). Four more Right intervals
reach X=134 on the starting platform; Right+Up for five intervals
reaches X=204, three neutral intervals allow the middle-stone landing,
then a rearmed Right+Up for five intervals reaches the fixed stone.
`Tools/a3000-celtic-heart-stage-five-first-gap-stone-route.json` passed
a complete DF0 cold start at Frame 25243 with stable living hero
X=276/Y=41, HSalive=FF, exact image hash
`4C22F8A11E1DAFB2753DF146979D4A9DEE15D0513BF72C11D7CD21DBF07BA1A8`,
zero non-ROM exceptions and a reviewed active image. A separate full
cold-start repeat independently reproduced the same Frame 25243,
position, alive byte, image hash and active image. Count the passage
to the fixed X=272 stone as passed. Direct Right fell at X=182; an overlong first
jump reached X=260 airborne but died before landing. These are negative
controls, not a gap passage. The next animated GID-168 stone at X=352
still needs a live crossing: immediate
Right+Up jumps from X=276 with four to six intervals died before a
sustained landing. Stage 5 and the seven-point roadmap remain open.

A bounded continuation from a short-lived checkpoint on the fixed
X=276/Y=41 stone tested the next animated stone without changing guest
RAM. Six neutral 500,000-chip-cycle intervals followed by five
Right+Up intervals reached X=348/Y=41 alive through eight more neutral
intervals (Frame 25377), but the next interval deactivated the hero;
other tested phase offsets and jump lengths also died. This is a
temporary animation window, **not** a sustainable passage. The next
test had to launch a further jump before that eighth hold ended;
the short probe alone was never counted as passage.

That timed second jump now passes the second Stage-5 gap. From the
independently verified fixed stone X=276/Y=41, six neutral intervals
phase the GID-168 stone at X=352; five Right+Up intervals land on it
near X=348/Y=41. After eight neutral intervals, a rearmed Right+Up
for nine intervals reaches X=474/Y=119 alive, and releasing both
directions lands on the fixed GID-97 one-tile step at X=464-479/Y=160.
`Tools/a3000-celtic-heart-stage-five-second-gap-fixed-step-route.json`
validated and passed **two independent complete DF0 cold starts** to
Frame 25497, stable living hero X=476/Y=137, HSalive=FF, zero
non-ROM exceptions and identical image hash
`F68EDBFDA84B0840289F164CD34E9FD78F73CD2B49A5E812CF547840CB395D6F`.
Both final active images were inspected. Count this fixed-step landing
as passed. The preceding X=348 animation window is used only as a
launch point; waiting one further interval there kills the hero.

A bounded savestate continuation from the verified X=476 fixed step
also identified a genuine Type-13 moving platform, record
`$07FF80A4`: its X advances from 507 at Frame 25497 left to 433,
then reverses. Holding Up for two 500,000-chip-cycle intervals on
the fixed step and releasing it while the platform approaches catches
the hero on top at Y=98; neutral riding carried him alive to X=700.
After six more riding intervals, a three-interval Right+Up jump from
the returning platform landed at X=740/Y=89 on the fixed GID-96
tile at X=736/Y=112, and nine neutral intervals kept him alive.
These are short-run savestate probes only, **not yet an independently
replayed DF0 cold passage**. Next encode this exact input sequence
after the X=476 route, check a sustained landing and active image,
repeat the full DF0 cold start, then approach the next fixed platform
at X=784-815. Stage 5 and the seven-point roadmap remain open.

The moving-platform continuation is now a verified Stage-5 passage.
`Tools/a3000-celtic-heart-stage-five-moving-platform-far-stone-route.json`
appends the exact Type-13 ride and six-interval platform phase to the
X=476 full-cold route, then a three-interval Right+Up jump and nine
neutral landing intervals. **Two independent complete DF0 cold starts**
matched at Frame 25968: hero X=740/Y=89 alive on fixed GID-96,
HSalive=FF, zero non-ROM exceptions, and exact image hash
`5BA553131589A663280CC8EDD54F2864D6121FBD76E13C173653DB33F0FAC1E4`.
Both captured active game images were inspected. Count X=740 as passed.
The next fixed platform at X=784-815 is also independently confirmed.
From X=740/Y=89, a rearmed Right+Up jump of four 500,000-chip-cycle
intervals, followed by eight neutral intervals, lands stably at
X=796/Y=57 on GID-164. The durable
`Tools/a3000-celtic-heart-stage-five-next-fixed-platform-route.json`
passed **two separate complete DF0 cold starts** to Frame 26053,
HSalive=FF, zero non-ROM exceptions and identical image hash
`D6C13C5B505B9F845F179E2B570DA3450D6AB8D35E45604791A8AEBCAC824EAE`;
both captured active images were inspected. Two jump intervals missed
this tile, while six or more died beyond it. A further rearmed
Right+Up jump of four intervals from X=796, followed by eight neutral
intervals, lands stably at X=852/Y=25 on the next fixed GID-164 tile
at X=848. `Tools/a3000-celtic-heart-stage-five-upper-fixed-stone-route.json`
passed **two independent complete DF0 cold starts** to Frame 26137,
HSalive=FF, zero non-ROM exceptions and matching image hash
`305E42FC42C87196F2CC774B50DB3ECBA10F7950AE38E5426E2421AC22A21860`.
Both final active images were inspected; count this upper tile as
passed. The next concrete step is to analyze and time the animated
GID-168 tiles at X=912-943/Y=48 and jump from there to the fixed
GID-164 tile at X=1008, with sustained landing, active image and two
full DF0 cold replays before claiming passage. Stage 5 and the seven
roadmap points remain open.

Stage-5 continuation after X=852: direct Right+Up attempts toward the
animated X=912-943 upper tiles died near X=908-936 in all tested
0-24-interval launch phases. A bounded RAM snapshot shortly before
one death found an active Type-6 knight at X=945/Y=23 and a Type-12
bat behind at X=860/Y=23; the HSalive write used the `hero_coll`
path. The exact colliding sprite was not isolated, so this is not
evidence of a renderer defect or a passed animated-tile landing.
Regular Right without Up instead drops safely below that encounter
onto the fixed lower bridge, with a stable living position at
X=964/Y=137. A rearmed three-interval Right+Up jump from there lands
on the fixed GID-100 step X=1008/Y=105; ten neutral intervals stay
alive. `Tools/a3000-celtic-heart-stage-five-lower-bridge-passage-route.json`
passed **two independent complete DF0 cold starts** to Frame 26355,
HSalive=FF, zero non-ROM exceptions and exact image hash
`34C987E240642C9FC7AE1F9D234B294E4CC576E046FF728657AB6BA095243196`.
Both active images were inspected. Count the lower bridge and this
fixed step as passed; the higher GID-164 tile at X=1008/Y=48 was
not reached. A further rearmed Right+Up jump of four intervals reaches
another fixed GID-99 step at X=1064/Y=89 and survives ten neutral
intervals. `Tools/a3000-celtic-heart-stage-five-lower-bridge-second-step-route.json`
passed **two independent complete DF0 cold starts** to Frame 26454,
HSalive=FF, zero non-ROM exceptions and image hash
`BFF31A908785DED37EBAD55AF5308BE9FBF6D47AB36BE9E2779C23B3E0EF8D21`.
Both active images were inspected; count X=1064 as passed. Short
savestate probes then use a five-interval jump to the lower castle
ledge X=1136/Y=89 and a rearmed three-interval jump to X=1180/Y=41
on the fixed upper GID-97/98 step, surviving twelve neutral intervals.
`Tools/a3000-celtic-heart-stage-five-castle-upper-step-route.json`
passed **two independent complete DF0 cold starts** to Frame 26665:
X=1180/Y=41 alive on the upper fixed step, HSalive=FF, zero non-ROM
exceptions and exact image hash
`B6473DE8C727C6042B3CD4E8C963F46F5F5293C3766ADA6F68CF37619E4309AD`.
Both active final images were inspected. Count this upper landing as
passed; the Type-9 enemy below remains active. A short savestate
continuation uses five Right+Up intervals to land at X=1252/Y=41 on
the next fixed GID-100 upper step and survive ten neutral intervals.
`Tools/a3000-celtic-heart-stage-five-castle-far-step-route.json`
passed **two independent complete DF0 cold starts** to Frame 26771:
stable living hero X=1252/Y=41, HSalive=FF, zero non-ROM exceptions,
exact image hash
`544AD29C752F788F5472551F5375250A4ABAC7E877B5E632D03390177DF5A294`,
and both final active images inspected. Count this far upper tile as
passed. A direct six-interval Right+Up jump then lands at X=1336/Y=41,
but an active Type-6 knight at X=1348/Y=23 collides with the hero
after six more neutral intervals. A bounded MOB snapshot and HSalive
write trace establish the enemy and `hero_coll` path. In short
savestate probes, one neutral interval after landing followed by a
grounded right-facing Fire strike defeats the knight (Type 0,
flags 40), and subsequent Right reaches X=1452/Y=73 alive atop the
castle wall after ten neutral intervals.
`Tools/a3000-celtic-heart-stage-five-castle-wall-landing-route.json`
passed **two independent complete DF0 cold starts** to Frame 27066:
the knight is Type 0/flags 40, the hero is alive at X=1452/Y=73
on the massive wall, HSalive=FF, zero non-ROM exceptions, exact image
hash `962084BE521CDF6D28BA53A7B25DC7113B0DF4940CE11F8356FC6BA695C21DA7`.
Both active images were inspected. Count the knight defeat and wall
landing as passed. Fifteen Right intervals from X=1452 drop to the
lower ground and hold the hero alive at X=1664/Y=185 for ten further
neutral intervals. `Tools/a3000-celtic-heart-stage-five-far-ground-landing-route.json`
passed **two independent complete DF0 cold starts** to Frame 27242,
HSalive=FF, zero non-ROM exceptions and exact image hash
`2A8124872D22E52164DBA0B03161C151A3D2475202F239469A126B3D012A3E85`;
both active images were inspected. Count this ground landing as passed.
Two more Right intervals die at X=1682/Frame 27251 through the
`hero_coll` path. A bounded Fast-RAM snapshot at Frame 27249 finds
an active Type-7 ground enemy at X=1692/Y=185 (record `$07FF7BA4`),
matching the collision. Short savestate probes from safe X=1664
show a real Right+Up jump of eight 500,000-chip-cycle intervals
reaching X=1776/Y=185 alive through ten neutral intervals.
`Tools/a3000-celtic-heart-stage-five-ground-enemy-bypass-route.json`
then passed **two independent complete DF0 cold starts** to Frame 27369,
HSalive=FF, zero non-ROM exceptions, exact image hash
`71E2B19D21EDF618C9C6D1B7F0F6B8F9789414B23E2537884CD7F6B5C63BC017`;
both active images were inspected. Count this Type-7 bypass as passed.

The next short savestate probes identify a Type-5 enemy at X=1904/Y=153
and two more Type-5 patrols initially at X=2016 and X=2039/Y=121.
The first dies to a grounded right-facing Fire strike from X=1876/Y=153;
`Tools/a3000-celtic-heart-stage-five-raised-type-five-passage-route.json`
confirms Frame 27629, living hero X=1984/Y=153, Type-5 Type 0/flags 40,
zero non-ROM exceptions and hash
`B0CB6435881F64CAE0FB36BEBA173A2295FEB198DB7D532D002C90F66C0B939C`
in a complete DF0 cold start with an inspected active image. Waiting
ten 500,000-cycle intervals there lets the two upper patrols move away.
A Right+Up jump reaches X=2055/Y=121; a delayed grounded Fire strike
kills the returning nearer patrol, and a second rearmed strike kills
the other. `Tools/a3000-celtic-heart-stage-five-twin-raised-type-five-passage-route.json`
passed **two independent complete DF0 cold starts** to Frame 28051,
living hero X=2188/Y=169, both Type-5 patrols Type 0/flags 40, zero
non-ROM exceptions and hash
`9CE812DCF96DAD3470D2CCD83DEC257CBC9F8E68F9C6EEC100D6A9B9A23F06C8`;
both active images were inspected. Count all three Type-5 defeats and
the stable corridor landing as passed.

The next foreground contact kills an immediate Right from X=2188:
HSalive write at Frame 28055 returns through `$07FEBADE`, the known
foreground-collision path. A short alternative stops Right at
X=2126/Y=121, rearms Up, and jumps over the tile to X=2232/Y=169;
it stays alive for six neutral intervals.
`Tools/a3000-celtic-heart-stage-five-post-patrol-hazard-jump-route.json`
passed **two independent complete DF0 cold starts** to Frame 28065,
HSalive=FF, X=2232/Y=169, zero non-ROM exceptions and image hash
`6AA2AEDE81B2A090ADDA214772C9CA1E73AEC22CF56B1DCAD0E20196AB59AA0B`;
both active images were inspected. Count this foreground-tile passage
as passed. Right from X=2232 reaches a solid wall at X=2288. Short
controls found no jump from that low position. An upper alternative
reaches X=2228/Y=121 and can wait there while a Type-15 patrol moves
from X=2247 to X=2318; direct Right or a late jump dies when it turns
back. Three timed standing Fire controls did not kill it (Type 15,
flags `$80`), so the next concrete step is a live upper-route passage
timed around that patrol and the solid wall at X=2304, then an
independent full DF0 cold replay. Stage 5 and all seven roadmap
points remain open.

Further bounded Stage-5 probes (ignored `tmp/stage5-upper-walk-sweep.json`,
`stage5-upper-leap-sweep.json`, `stage5-upper-cower-strike-sweep.json`,
`stage5-upper-near-strike-sweep.json`, and
`stage5-upper-near-cower-strike-sweep.json`) did **not** establish the
next passage. From the live X=2228/Y=121 upper route, waiting four to
six 500,000-cycle intervals and walking right reached at most X=2298
alive before the returning Type-15 patrol collided; immediate and
delayed jumps also failed. TMX tile inspection explains why a jump at
X≈2270 blocks at X=2288 while airborne: BACK columns X=2304-2320
contain solid tiles at Y=64-96 and Y=144-191, leaving only the narrow
Y=112-143 corridor. Timed standing and crouching Fire from X=2228 or
X=2256 did not change the Type-15 MOB's type `$0F`/active flag `$80`,
and all such approaches eventually died. These are negative controls,
not a gameplay pass. Next: inspect the Type-15 collision rectangle and
strike activation against the exact return phase, then test a grounded
attack or safe follow distance through the Y=112-143 opening; require
a sustained living landing beyond X=2336 and two independent complete
DF0 cold replays with inspected active images before promoting it.

A narrower watched strike probe at hero X=2256 explains the failed
timing more precisely: the right-facing HeroStrike became active at
Frame 28122, base X=2272/Y=125. At Frame 28136 its live collision box
was X=2278..2285/Y=129, while the returning Type-15 box was
X=2277..2289/Y=137..143. Their X ranges overlapped, but their Y
ranges did not. Standing and tested Down+Fire variants therefore left
the enemy active and the hero died when it reached X≈2261. The original
Celtic Heart source (`CelticHeart/mob.asm` in the preserved source
archive) identifies type 15 as **magical fire**; `monster.asm` says it
cannot be destroyed. The strike experiments are negative controls.

The upper magical-fire passage is now a proven gameplay pass. From
X=2228/Y=121, wait four 500,000-chip-cycle intervals, walk right three
intervals to X=2272, release Right, and wait six intervals for the
fire to return to X≈2304. Press Right+Up through the narrow corridor,
release movement after the tenth jump interval, then wait fifteen
neutral intervals for a stable landing at X=2376/Y=169. The complete
DF0 cold-start route
`Tools/a3000-celtic-heart-stage-five-upper-fire-bypass-route.json`
passed two independent runs at Frame 28291, HSalive=FF, zero non-ROM
exceptions and exact image hash
`D942C8CA4291648146807CCFF81BCD1F62913B7BE19EABBAB2D612BC465246D0`.
Both active images were inspected. This proves passage through the
wall at X=2304 without claiming the rest of Stage 5.

The next complete DF0 route
`Tools/a3000-celtic-heart-stage-five-high-ice-step-route.json`
passed **two independent cold starts** to Frame 29284, X=2888/Y=57,
HSalive=FF, zero non-ROM exceptions and exact image hash
`BDA4726C700057056AFD170BE5B4C2CDC0F7CD17FC3CC14D7D1E106798EF2D15`.
Both active final images were inspected; the route file validates.
It contains a grounded crouching Fire at X=2460 that defeats the
type-9 Dino, four neutral intervals to phase the animated FRONT
GID387/407 tile, and a timed jump from X=2558 that avoids the type-8
spider and lands on the small solid column at X=2644/Y=153. A type-13
moving platform then carries the hero across water to the fixed tile
at X=2816/Y=153; two rearmed jumps land on fixed steps X=2860/Y=105
and X=2888/Y=57. Intermediate X=2644 and X=2816 image hashes also
matched in both complete runs. Count these passages as passed.

The second type-13 platform (`$07FF7F64`) oscillates approximately
X=2897..3024 at Y=89. From the stable X=2888/Y=57 ice step, one
Right+Up interval, release, and twenty neutral intervals board it
and carry the hero to X=3007/Y=66. Three further Right+Up intervals
followed by twenty neutral intervals land stably on the fixed pillar
at X=3052/Y=57. The complete DF0 cold-start route
`Tools/a3000-celtic-heart-stage-five-second-air-platform-route.json`
passed **two independent runs** through Frame 29594 with HSalive=FF,
zero non-ROM exceptions and exact final image hash
`8AEA24D2A54BA0100AF3779DF643C857140305655CF368C06C61DFB12751B700`.
Both active images were inspected. Count this platform passage as
passed, not the remainder of Stage 5.

The BACK GID5 strip immediately east of the pillar has type 12 in
`w1types.tmx`; the original `hero.asm` dispatches type 12 bottom
contacts to `solid_anim`, so walking east triggers a single-tile
animation and the hero falls to the lower fixed shelf. Short probes
land at X=3056..3124/Y=137 or Y=185; a type-5 spider patrol at
X≈3046..3083/Y=137 caused the first deaths. A bounded Fast-RAM
snapshot identified its 80-byte MOB at `$07FF77E4`. From a short
checkpoint X=3080/Y=137, a 100,000-cycle Left alignment followed by
grounded Down+Fire defeats it (type 0/flags `$40`) while the hero stays
alive. A subsequent short Right probe reaches X=3216/Y=185 and remains
alive ten neutral intervals. These last two observations are still
awaiting independent complete DF0 replays; do not promote them yet.
Next: complete those replays, then handle the raised type-6 knight
and the next ground contact. Stage 5 and the overall roadmap remain
open.

The grounded Type-5 spider and the far lower corridor are now independently
replayed from complete DF0 cold boots. The route
`Tools/a3000-celtic-heart-stage-five-long-ground-route.json` aligns left by
100,000 chip cycles from the small crumble-strip landing, defeats the spider
with a grounded crouching Fire strike, walks to X=3216/Y=185, and continues
right to X=3568/Y=185. Two complete runs both reached Frame 30144 with
HSalive=`FF`, zero non-ROM exceptions and exact image hash
`342F5A652CB1AA7463423A18282AB578EE249B04C2C8B151CB44C0AFA3887830`.
Both active images were inspected. This proves the spider kill and sustained
ground passage; the remainder of Stage 5 remained open at this checkpoint.
A further rightward ground walk encountered a left-moving Type-14 projectile,
so the completed route uses fixed raised stones and avoids that contact.

**Stage 5 is complete, with the Stage-6 title visibly reached and repeated.**
`Tools/a3000-celtic-heart-stage-five-complete-stage-six-title-route.json`
validates and passed two independent full DF0 cold starts. From the lower
X=3216 shelf, genuine Right/Up inputs climbed fixed stones at X=3512/Y=137,
X=3608/Y=89, X=3652/Y=57 and X=3736/Y=57. Grounded Down+Fire at
X=3512 defeated the raised type-9 Dino; the further jump from X=3750
cleared a lethal background hazard, and Right crossed the animated crumble
strip to lower ground X=3904. The final Right+Up sequence visibly reached
`LEVEL COMPLETE — PRESS BUTTON` at Frame 30932 with exact image hash
`D2102663C597D3BA956A5B34DBF769883D876A33561F9A062622475B483B5EC4`.
At the exit the hero is intentionally removed (HSalive=`00`), so this
screen was assessed by its visible title and subsequent transition rather
than by the in-level alive flag. A real Fire press then led to the visible
`LAND OF THE DEAD` Stage-6 title at Frame 31080 with exact hash
`E7A7E8DE4FF6E23C10DCAE486E2A2CE932D347AED8E34F37D2E76A7C85D1B4DC`.
Both full runs matched both hashes with zero non-ROM exceptions; both pairs
of output images were inspected. Stages 1–5 are completed gameplay passes.
Stage 6 and the remaining roadmap work are open. Next: press through the
Stage-6 title and any separate GET READY screen, verify active movement,
then extend an independently repeated complete DF0 route through the first
real obstacle.

A subsequent complete DF0 cold-start probe
(`tmp/stage6-active-full-probe.json`, ignored) tested the title input
sequence. Fire at the first Stage-6 title frame was too early: the same
title hash remained at Frame 31228. A later Fire changed the prompt,
but Frame 31376 still visibly said `PRESS BUTTON`, and the attempted
Right input through Frame 31453 returned to the title art with no active
hero (HSalive=`00`, unchanged position). This is **not** Stage-6
gameplay. Next: wait for the stable `PRESS BUTTON` state, release all
inputs, press Fire again, and confirm an active moving hero with an
independently repeated full cold route before claiming any Stage-6 pass.

Stage 6's entry sequence has now been resolved. `LAND OF THE DEAD` is
followed by a distinct `THE CAVE / STAGE 1 / CODE: 0ABS` card and then
`GET READY — PRESS BUTTON`; each requires a released, fresh Fire edge.
The third Fire must be pressed promptly after the ready screen appears:
a 500,000-chip-cycle wait leaves the first Type-17 skeleton at X≈144,
whereas a 6,000,000-cycle wait lets it reach X≈67 next to the spawn.
The late-start controls died from a monster collision despite showing a
brief live frame. With the short wait, a real Right+Up jump bypasses it
and lands stably at X=188/Y=185. The complete DF0 cold-start route
`Tools/a3000-celtic-heart-stage-six-first-skeleton-bypass-route.json`
passed two independent runs to Frame 31588, HSalive=`FF`, zero non-ROM
exceptions, exact image hash
`FEC394AAD9A5451573BC51BCB3B544D092FE97A8E64B0A5B7EA762A2F2E30CDD`,
and two inspected active cave images. This confirms active Stage-6 play
and the first skeleton bypass; Stage 6 is not complete.

The first cavern gap and three farther enemies are now passed. A timed
jump from X=188 lands at X=264/Y=153. A standing right-facing Fire
after four 500,000-cycle waits defeats both Type-17 skeletons on the
far platform. The next jump lands X=364/Y=121, where an immediate
standing Fire defeats the Type-20 green walker. The full DF0 route
`Tools/a3000-celtic-heart-stage-six-first-gap-greenwalker-route.json`
passed **two independent cold starts** to Frame 31961, X=364/Y=121,
HSalive=`FF`, both skeletons Type 0/flags `$40`, green walker Type 0/
flags `$40`, zero non-ROM exceptions and exact image hash
`E0A5EBB9D14DC4058A5927187B20226ABE3FCEC3A56A6CB678CAA3DA4E307592`.
Both active images were inspected; count the gap and these kills as passed.

The lower-corridor continuation from X=364 is independently
reproduced in `Tools/a3000-celtic-heart-stage-six-lower-corridor-route.json`.
Both complete DF0 cold starts reached Frame 32144, X=588/Y=185 alive,
HSalive=`FF`, zero non-ROM exceptions and exact image hash
`ABA8EF93D81F89330CA685078B16787C64A4490D78144BB65EFDF5A97D359707`.
Both active cave images were inspected; count this lower-corridor passage
as passed. Direct Right from X=588 falls into the red lava and dies
at X=606; a bounded HSalive write trace confirms the kill path. A
four-interval wait at the safe edge phases a Type-21 bat, and a
Right+Up jump reaches the opposite solid bank. The complete DF0 route
`Tools/a3000-celtic-heart-stage-six-first-lava-crossing-route.json`
passed two independent cold starts to Frame 32285, X=704/Y=185 alive,
HSalive=`FF`, zero non-ROM exceptions, and exact image hash
`6F97BD45CFACCD3214407C12A3641BFE30FAD23F6FF443BF975AD621CEB81B99`.
Both active images show the hero grounded across the red lava. Count the
first lava crossing as passed. A short Savestate continuation reached
X=752/Y=185 alive at the next fixed wall; Right+Up there reached
X=816/Y=153 alive on the raised ledge and remained stable for eight
500,000-cycle intervals. The durable route
`Tools/a3000-celtic-heart-stage-six-next-wall-ledge-route.json`
passed two independent complete DF0 cold starts to Frame 32489,
X=816/Y=153 alive, HSalive=`FF`, zero non-ROM exceptions, hash
`174A4510D0C704AD082A4C080CFF61E97CE6133D1B4EB04A2BDE90E4CC663E76`,
with an inspected active image in the first run and identical hash/RAM
in the second. Count this ledge as passed. A further short jump from
X=816 reaches X=840/Y=105, stable for ten intervals. Holding Right
instead died at X=854/Y=105; a bounded HSalive write trace returned
through `$07FEBADE`, the foreground-collision path. A second jump from
X=840 reached X=1012/Y=105 stable in short probes, but a nearby
Type-17 skeleton on that upper platform killed a standing Fire at
X=1012 immediately. An airborne Fire from X=984 let the hero drop to
X=984/Y=153 alive; moving Right from there met a separate monster
collision at X=1002 (bounded return `$07FEC580`). These short probes
do not count as a full cold passage. Next: time the upper skeleton or
use the lower ledge to bypass it, then reproduce a sustained landing
beyond X=1012 from a full DF0 cold start. Stage 6
and the seven-point roadmap remain open.

The upper skeleton can instead be bypassed along the lower ledge. From
X=956/Y=153, two neutral intervals after the jump avoid a fatal landing;
a brief Right+Up input, then standing Fire at X=984/Y=153, keeps the hero
alive. Facing left and striking from X=969 defeats the returning Type-17
skeleton (Type 0/flags `$40`). `Tools/a3000-celtic-heart-stage-six-twin-orb-passage-route.json`
then reaches X=1084/Y=185, climbs through the raised Type-17 skeleton
with a grounded Fire defeat at X=1352/Y=137, and reaches X=1440/Y=137.
Two overlapping Type-20 green orbs beyond the ledge require an airborne
Fire to phase their approach and a second Fire at X=1507/Y=89 to kill
both. **Two independent complete DF0 cold starts** reached Frame 33918,
X=1507/Y=89 alive, HSalive=`FF`, both orbs Type 0/flags `$40`, zero
non-ROM exceptions and exact image hash
`AC524C34CCA949F76C8856EFEC4C4DE2EE51DC9A86B2041B125F2E7DE430B326`.
The active cave image was inspected. The same two runs checked exact
intermediate hashes at X=1084 (`ED44FF632B11E24F27DFDD70930997EE94A7151611932BB35302C17D78FE8F07`)
and X=1440 (`AFEBD48406D2ECC7C641ABE4C80FD76EA03A85CE6C95A977D8819505FF4170CD`).
Count these skeleton and orb passages as passed; Stage 6 remains open.

At the next lava edge X=1580/Y=89, a short checkpoint study identified
the moving Type-22 platform at MOB `$07FF8004`. A six-interval wait,
Right+Up for four intervals, then releasing Right catches it: the hero
rides at Y=98 to X=1732 while alive at Frame 34206. This was the
checkpoint-only precursor to the full cold route documented below.
Jumping from the platform reaches the far bank near X=1848/Y=89 alive,
but a Type-17 skeleton spawned at X=1856/Y=112 collides within three
neutral intervals. A bounded RAM snapshot measured the active skeleton
at X=1834/Y=89, Type `$11`/flags `$80`, at Frame 34283. Left-facing
Fire from X=1848 is too late. Earlier aerial Fire from X=1832 or X=1846
also leaves Type `$11` active, and the hero dies as the skeleton moves
right. Longer platform waits and simple running jumps to X=1876-1960
also failed; these remain useful negative controls for the subsequent
continuous-jump solution.

The second lava crossing is now reproducible from DF0 cold boot. The
Type-22 platform is caught after six neutral 500,000-chip-cycle
intervals at X=1580, four Right+Up intervals and a Right release; it
carries the hero at Y=98 to X=1732. A continuous Right+Up jump stays
ahead of the nearby Type-17 skeleton and lands on the small lower
platform at X=1932/Y=153. Two further Right+Up jumps use its far edge
and the narrow center stone, landing stably at X=1988/Y=153 and then
X=2060/Y=153, before a longer jump lands at X=2188/Y=137. The durable
`Tools/a3000-celtic-heart-stage-six-second-lava-passage-route.json`
checks exact hashes at these intermediate positions and the final
`26CFEB2203FDB953AF1A976771CD7B51E042D11C4EC565FAFAB8E0A76EB68F89`.
Two independent complete DF0 cold starts (this route and its full
continuation) reached Frame 34656, X=2188/Y=137 alive, HSalive=`FF`,
zero non-ROM exceptions. The active far-bank image was inspected. Count
the moving-platform and second-lava passage as passed.

Short checkpoint tests beyond X=2188 have defeated the two Type-20
green walkers on the next bank with two timed standing Fire inputs from
X=2216/Y=137. Right movement then reached X=2356/Y=185. A timed
standing Fire from X=2412/Y=185 defeated the approaching Type-17
skeleton, and immediate Right movement kept the hero ahead of the
other skeleton to X=2584/Y=185. Two independent complete DF0 cold
starts of the durable
`Tools/a3000-celtic-heart-stage-six-far-ground-passage-route.json`
passed through Frame 35332 with HSalive=`FF`, both walkers
and the right skeleton Type 0/flags `$40`, zero non-ROM exceptions and
hash `22EC8322B2CB32A31A5C01A162C9708CA0B56C3A6868CA267B1062E1761D5FB6`;
its active image was inspected. Count the X=2584 ground passage as
passed.
A shorter continuation jumps over the lower Type-17 skeleton and pink
missile to X=2752/Y=185 alive. The durable
`Tools/a3000-celtic-heart-stage-six-missile-bypass-route.json` passed
two independent complete DF0 cold starts at Frame 35473 with
HSalive=`FF`, zero non-ROM exceptions and identical hash
`88F9819E34D28552F2504801770AB2BA33C9F9B4420010D750C34143840B76F9`;
the active image was inspected. Count X=2752 as passed. The next
Right+Up attempt dies at
X=2784/Y=137; a bounded HSalive write trace returns through
`$07FEC580`, identifying monster collision. A Type-17 skeleton on the
raised wall moves from X=2824 toward X=2773 before turning, while a
Type-20 orb patrols above. Seven checkpoint-only waits of 0-12
intervals before the jump all died. A bounded position watch shows
direct jump contact at Frame 35508, hero X=2784/Y=137 and skeleton
X=2789; six 500,000-cycle waits move the skeleton left enough for an
earlier death at X=2764, while twelve waits allow it to hit the
stationary hero. An aerial Fire begun at X=2763/Y=135 after six waits
survives through Frame 35621 as the skeleton retreats to X=2857, but
a straight chase dies at X=2839 and a jump dies at X=2813. A refined
checkpoint probe waits safely at X=2764/Y=137, advances to
X=2807/Y=137, waits six 500,000-cycle intervals and lands a standing
Fire strike. The raised Type-17 skeleton changes to Type 0 and then
flags `$40`, while the hero remains alive. A second timed Fire lets
the upper Type-20 MOB move left past the stationary hero, but Right
still dies at X=2840/Y=137 around Frame 35895. A bounded HSalive
write trace has stack `$07FEBC80/$07FEBA90/$07FEBA0C/$07FEC794`,
the same BACK-tile collision stack observed at earlier fixed hazards,
not the monster-collision return `$07FEC580`. The `level06.tmx`
BACK layer places GID 173 at column 177/row 9 (X=2832-2847,
Y=144-159), and `w2types.tmx` assigns it TYPE 16, the animated
bear trap. A bounded MOB snapshot at X=2836 had no active monster
overlap. Thus the obstacle after the skeleton is a separate BACK
trap, and the previous monster attribution was incorrect.

A checkpoint-only follow-up uses the same timed Fire defeat of the
skeleton and lets the upper Type-20 pass left while the hero waits at
X=2807/Y=137. A rearmed Right+Up jump then passes above GID 173 and
lands stably at X=2936/Y=185, alive through Frame 36001; hash
`38819B57AB2C6585FF5DFDC0297216A5B4CFF860A54E89A50D8049A33C088BAE`.
The durable
`Tools/a3000-celtic-heart-stage-six-raised-wall-trap-passage-route.json`
passed two independent complete DF0 cold starts at Frame 36001 with
X=2936/Y=185, HSalive=`FF`, raised skeleton Type 0/flags `$40`, zero
non-ROM exceptions and identical hash
`38819B57AB2C6585FF5DFDC0297216A5B4CFF860A54E89A50D8049A33C088BAE`.
Its active image was inspected. Count the raised skeleton and separate
animated bear trap as passed, but Stage 6 remains open.

Short checkpoint-only probes beyond that landing reached the low
wall at X=2992, then jumped to X=3056/Y=153. A second rearmed
Right+Up jump reached X=3136/Y=126 before death with active Type-21
enemies nearby; the precise contact is not traced. A standing Fire
hold at X=3108/Y=105 kept the hero alive through Frame 36205 but did
not kill the Type-21 patrol. Stopping instead at X=3152/Y=185 kept
the hero alive to Frame 36331. A bounded HSalive trace then proved
that direct Right died at X=3174 from a monster-collision call
(`$07FEC580`); a 4096-byte Fast-RAM MOB snapshot placed a Type-20
walker at X=3185/Y=185. A grounded right-facing Fire begun at Frame
36303 defeated it by Frame 36345 (Type 0, later flags `$40`). The
next Type-20 walker was defeated with a rearmed standing Fire begun
at X=3194/Frame 36409, Type 0 by Frame 36451 and flags `$40` by
Frame 36493. These are checkpoint-only kills.

The following apparent death at X=3221 was a **level exit**, not a
failed passage. Its HSalive stack `$07FEBE70/$07FEBC88/$07FEBACC`
differs from monster contact. The `level06.tmx` BACK layer places
GIDs 208/209/210 and 228/229/230 at X=3232-3279; `w2types.tmx`
assigns them TYPE 7, and the published `hero.asm` handles type 7
with `bg_exit`/`exitHero`. A checkpoint continuation showed the
visible `LEVEL COMPLETE — PRESS BUTTON` at Frame 36873, hash
`B9D9FA4F0CE31A4EDB3E266F2FABD942D0F40873BA27706620B2148D1E114961`.
After a fresh Fire edge it showed `THE CAVE STAGE 2` at Frame 37014,
hash
`D8371FCA2ED37AB388063A754FC3D372FB2375BE5013DCB54F29336B948CA380`.
The durable
`Tools/a3000-celtic-heart-stage-six-complete-stage-seven-title-route.json`
passed two independent complete DF0 cold starts through both exact
images at Frames 36873 and 37014, with zero non-ROM exceptions. The
full-run images were inspected: the first visibly says `LEVEL COMPLETE
— PRESS BUTTON`, the second says `THE CAVE STAGE 2`. Count Stage 6 as
**complete**. The combined full route passes its read-only validation;
Stage 7 itself remains open.

Further checkpoint-only Fire input brought Stage 7 to active cave
gameplay, HSalive=`FF`, and real Right movement to X=118/Y=169 at
Frame 37331. Two independent full DF0 cold starts of
`Tools/a3000-celtic-heart-stage-seven-active-movement-route.json`
reached that same live RAM/position and image hash
`2AFC5D302781EC192A02CF3690F5747C497188BAA726B7475A915251A60EE140`,
with zero non-ROM exceptions. The captured image shows active cave
gameplay. The saved-state continuation had a different image hash;
the route guards against the independently repeated full-cold hash.

Shorter Stage-7 probes find a solid first wall at X=144/Y=169. A
Right+Up jump reaches X=168/Y=121 alive on the raised ledge but dies
at X=188 without a strike. A crouching Fire from X=168/Frame 37401
keeps the hero alive through Frame 37485; a bounded 8192-byte Fast-RAM
snapshot then records the nearby Type-9 Dino at X=191/Y=121 as
Type 0/flags `$40`. Right movement to X=250 stays alive, but X=262
dies in the first lava. The HSalive write stack matches background
collision; `level07.tmx` places GID 119 at that row and `w2types.tmx`
assigns it deadly TYPE 1.

A timed Type-22 moving-platform catch starts from
the safe X=168 ledge, waits until the platform approaches, and jumps
onto it at X=228/Y=130. A second Right+Up jump passes an active
Type-23 enemy descending from X=352/Y=121; after riding the platform
to X=364, a third jump reaches stable fixed far-bank ground at
X=408/Y=121, alive through Frame 37901. Two independent complete
DF0 cold starts of
`Tools/a3000-celtic-heart-stage-seven-first-lava-far-bank-route.json`
confirmed HSalive=`FF`, the first Dino Type 0/flags `$40`, zero
non-ROM exceptions and identical image hash
`88C91C79D7BF74EF98E40E79BD1624C5855FDDF7B5C602246E1002B8740E7E7D`.
The captured image shows the hero standing on solid ground across
the first lava with active enemies and moving platforms. Count this
first lava passage as passed, but Stage 7 remains open. Direct Right
after X=408 falls toward the next lava; an immediate Right+Up probe
dies around X=438/Y=65 near the upper fixed platform.

Follow-up bounded HSalive tracing resolved the immediate X=438/Y=65
death as a Type-20 green-walker collision, with `$07FF80F4` in A5 and
the `hero_coll` return. Waiting six 500,000-chip-cycle intervals at
safe X=408 before the Right+Up jump, then braking at X=443 and
holding Fire, avoids its patrol. The first provisional continuation
used a restored Stage-6 checkpoint and reached X=612/Y=89, then the
animated GID-195 bridge at X=656, the far wall X=752/Y=89 and an
active Type-21 bat bypass to X=952/Y=169. Two complete DF0 cold
attempts initially died at Frame 38094: the animated GID-198 tile
at X=528 was non-solid when the hero descended, whereas that older
restored checkpoint run had a solid tile at the same frame. A new
complete cold replay to the accepted X=408 far bank produced a fresh
checkpoint that reproduced the cold failure. One additional neutral
500,000-chip-cycle interval at the safe X=443 stand aligned GID-198;
four intervals on the X=612 step then aligned GID-195. This is a
measured route timing correction, not a change to guest memory or
game code.

`Tools/a3000-celtic-heart-stage-seven-animated-bridge-far-wall-route.json`
now passes two independent guarded complete DF0 cold starts. At
Frame 38453 the hero stands sustainably alive X=752/Y=89 on the
solid far wall, HSalive=`FF`, zero non-ROM exceptions, identical hash
`10DCD6CA35B454E35159088E00D3024DFBC63CED030A85CE44553A9B9E5B342B`
and a checked active image. This independently establishes the
X=612 step, animated bridge and far-wall landing as passed.
`Tools/a3000-celtic-heart-stage-seven-type21-bypass-route.json`
then passes two further complete DF0 cold starts: a timed Right+Up
jump from the lower X=824 stand leaves the Type-21 bat behind and
lands X=952/Y=169 alive for ten neutral intervals at Frame 38720,
hash `A745B663513F888E395894FD5B8AA267F8C5B89E1BB61340D5A050F33F194E55`,
zero non-ROM exceptions and a checked active image. The bat was
bypassed, not killed. Stage 7 remains open.

The next obstruction is layered: direct Right from X=952 dies at
Frame 38715 from a newly active Type-23 missile (`A5=$07FF8234`),
while an immediate vertical jump followed by Right dies at Frame
38735 from an upper Type-19 zombie (`A5=$07FF7FB4`). Short-run
timing probes can continue to X=1104/Y=169, where a solid wall
blocks Right and a pursuing lower Type-19 zombie eventually collides;
an Up+Right jump clears the wall but meets a Type-20 green-walker on
the upper shelf around X=1198/Y=121. These later probes are **not**
full-cold passes. Next find a sustainably live missile/zombie/green-
walker passage with real inputs, then repeat two guarded complete
DF0 cold starts and inspect an active image before extending the
durable route.

Stage 7 follow-up (2026-10-21): the layered missile/zombie/green-walker
section is now passed by real inputs. A bounded HSalive write trace at
Frame 38842 identified the previously unexplained death at X=1152 as
Type-19 zombie `$07FF7E74` (X=1162/Y=121 just before contact), not the
Type-20 green-walker. From the timed Type-21 bypass, a second jump clears
the lower Type-19 pursuer and the wall. On the upper shelf a right-facing
standing Fire started at Frame 38812 kills that Type-19 while the hero
remains at X=1152/Y=121; Right then reaches X=1174. A second standing
Fire there kills Type-20 `$07FF7EC4` (Type 0/flags 40 after its death
animation). Subsequent Right inputs land the hero on the lower cave floor
at X=1428/Y=185. The durable full DF0 cold route is
`Tools/a3000-celtic-heart-stage-seven-missile-zombie-greenwalker-passage-route.json`.
Two independent full cold starts reached Frame 39069 with HSalive=FF,
identical image hash
`C3BEF018B62270DD5C5FDF1D2788F51695C5A70D5F8B822F01792A9C2E2648A0`,
zero non-ROM exceptions and the guarded X/Y bytes `$0594/$00B9`;
the captured image shows active cave gameplay. This is a passed Stage-7
section, **not** a level completion.

The next ground skeleton is Type 17 `$07FF7DD4`: direct Right dies at
X=1480/Frame 39100, as confirmed by HSalive write trace. A short
checkpoint-only Fire probe from X=1446 sets it to Type 0 with its
death animation still active (flags 80) and keeps the hero alive
through Frame 39153, but this continuation has not
yet been cold-replayed and does not extend the accepted route. Direct
Right then stops alive at X=1503/Y=185 against the solid cave column
starting X=1520. A ground jump peaks at Y=135 and also stops at the
column; the decorative row at X=1456-1488/Y=176 is non-solid and does
not provide the required step. The BACK map has a solid raised shelf at
X=1280-1328/Y=96 and solid tiles at X=1520/Y=128-208. Next seek a
legitimate higher approach from the earlier shelf, or another verified
game movement, and cold-replay a sustained landing beyond X=1520. Do
not treat the checkpoint-only skeleton kill or blocked jump as passes.

Stage 7 upper-wall follow-up (2026-10-21): a new route branches before
the accepted X=1428 ground descent. From X=1260/Y=121 it jumps to the
fixed X=1316/Y=73 shelf, releases Up, and jumps again onto the high cave
bridge at X=1376/Y=25. Two timed, grounded, right-facing Fire inputs
defeat the two Type-19 zombies there. A bounded HSalive trace showed that
walking right from X=1376 dies at the animated BACK-GID173 tile at
X=1408, rather than from another monster. A Right+Up jump clears the
tile; a controlled stop and landing reaches X=1504/Y=25, and subsequent
Right crosses the previously blocking solid column X=1520. Stopping
before the next lava and waiting ten intervals lands the hero stably on
the far ledge at X=1588/Y=153. The durable
`Tools/a3000-celtic-heart-stage-seven-far-wall-stable-landing-route.json`
passed two independent guarded complete DF0 cold starts at Frame 39533,
HSalive=`FF`, zero non-ROM exceptions and identical image hash
`2D140DF40ACE623B9D7DB64B57A1010EAEB41C63DB2514F7E2EAFE786BD5A208`.
The captured image was checked as active cave play. Count the upper
bridge and X=1520 wall passage as passed; Stage 7 remains open.

Short-lived checkpoint probes from that ledge have identified a
timed second-lava route, but it is **not yet an accepted full-cold
passage**. The Type-22 platform travels between approximately X=1616
and X=1759/Y=185. Waiting 39 neutral 500,000-chip-cycle intervals
before jumping catches it; releasing Right after the seventh jump
interval rides it east to X=1760/Y=162. A Type-23 missile crosses the
same platform: an earlier catch dies on it near X=1668, confirmed by
HSalive trace with A5=`$07FF81E4`. A jump from the platform at X=1760
lands briefly on the animated far bridge X=1856/Y=153. A second jump
must start after one neutral landing interval, before that bridge turns
non-solid; the short probe then lands X=1968/Y=185 and survives eight
intervals. The candidate full DF0 cold route is
`Tools/a3000-celtic-heart-stage-seven-second-lava-far-bank-route.json`;
repeat it twice and inspect an active image before counting this lava
crossing. Then continue from its far bank with real inputs, timed
enemy checks and the same full-cold validation standard.

The second-lava candidate has now passed two independent guarded full
DF0 cold starts. `Tools/a3000-celtic-heart-stage-seven-second-lava-far-bank-route.json`
reaches Frame 40096 with the hero sustainably alive on fixed far-bank
ground X=1968/Y=185, HSalive=`FF`, zero non-ROM exceptions and identical
image hash
`A3F663352A78F1FDBBAE2CBCC728CA010F7B3D25B9F08B6B07671DF053FC23DE`.
The captured frame shows the active cave level, the hero on the far
bank and the lava behind him. Count the second lava as passed. Stage 7
is still open.

Checkpoint-only exploration after that landing found two successive
ground enemies, but **neither is a full-cold accepted passage yet**.
Direct Right dies at X=2108 from Type-20 green-walker `$07FF7C44`;
a grounded right-facing Fire from X=2067 kills it (Type 0/flags 40)
while the hero remains alive. Direct Right after that dies at X=2129
from Type-21 `$07FF7C94`; a grounded right-facing Fire from X=2096
also kills this enemy, followed by live Right movement to X=2240/Y=185.
The candidate full route
`Tools/a3000-celtic-heart-stage-seven-twin-ground-mobs-route.json`
has been started and must pass two independent full DF0 cold runs with
an active image before this sequence is counted. Short probes beyond
X=2240 encounter a raised Type-20 green-walker on the next wall.
After waiting for its rightward patrol, a timed Right+Up jump followed
by Fire from X=2295/Y=137 kills it and remains live; unguarded Right
then dies near X=2443 from a map-tile collision (A5=`$001318` in a
bounded HSalive trace). These remain checkpoint-only observations.
Next validate the twin-ground-enemy route, then isolate that raised
wall/tile sequence and repeat a full cold route before claiming it.

The first complete DF0 cold run of the twin-ground-MOB candidate has
passed its X=2240/Y=185 guard at Frame 40575 with HSalive=`FF`, zero
non-ROM exceptions and hash
`C390991107C9995398BFD5DC7E1E5601AC7A298F90EC38AF5A7A5651D882F75A`.
A second independent full cold run with a single final active-image
capture is underway. Do not count these two kills as accepted until
that run and image inspection pass.

The independent second DF0 cold run of the twin-ground-MOB route has
now matched the first exactly: Frame 40575, X=2240/Y=185,
HSalive=`FF`, zero non-ROM exceptions and hash
`C390991107C9995398BFD5DC7E1E5601AC7A298F90EC38AF5A7A5651D882F75A`.
Its captured frame shows active cave gameplay with the hero at the
next wall. The same genuine grounded Fire sequence killed Type 20
and Type 21 before the stable landing. Count this two-enemy ground
passage as passed; Stage 7 remains open. The next raised Type-20
and the map-tile hazard past X=2435 still require full-cold proof.

Further checkpoint-only inputs found a safe raised route from the
accepted X=2240 stand. First hold position with an early right-facing
Fire while the Type-20 patrol moves away. Release Fire and Up, then
jump Right+Up; stop at X=2295/Y=137 and Fire as Type 20 returns. The
MOB becomes Type 0/flags 40 with the hero alive. A direct Right run
dies around X=2443 because the FRONT-GID373/393 trap at X=2432/Y=176
animates into its active phase. Braking at X=2408 is safe, but a new
jump from there starts too close and dies as the trap opens. A Right+Up
jump started earlier from X=2379 clears it, lands X=2552/Y=185 and
survives ten neutral intervals. The candidate
`Tools/a3000-celtic-heart-stage-seven-raised-walker-trap-passage-route.json`
is running in two independent guarded full DF0 cold starts; the raised
kill and hazard crossing are **not accepted** until both, plus the
active image, pass. Direct Right after X=2552 dies at X=2588 from a
Type-21 ground enemy `$07FF78D4`. An immediate grounded right-facing
Fire at X=2552 defeats it in a checkpoint-only probe, then Right
reaches the next solid wall X=2672 and stops live at X=2656/Y=185.
After confirming the raised route, repeat the Type-21 continuation
cold and analyze the wall before claiming further progress.

The raised Stage-7 walker and FRONT-trap candidate has now passed two
independent complete DF0 cold starts. Both runs of
`Tools/a3000-celtic-heart-stage-seven-raised-walker-trap-passage-route.json`
match at Frame 41053: hero X=2552/Y=185, HSalive=`FF`, raised Type-20
MOB Type 0/flags 40, zero non-ROM exceptions, and exact frame hash
`7A574E60AD933C9BA7C6DEC4EFF2A608E5399E3188B97DA8444547F4157D4D43`.
The captured image shows active cave gameplay on the far side of the
animated FRONT-GID373/393 hazard. Count this kill and trap crossing as
passed; Stage 7 remains open. Direct Right from there still dies at
X=2588 from Type 21 `$07FF78D4`. Checkpoint-only inputs found an
immediate grounded right-facing Fire from X=2552 that defeats it, then
Right reaches X=2656/Y=185 alive before solid wall X=2672. The new
`Tools/a3000-celtic-heart-stage-seven-post-trap-type21-route.json`
encodes that continuation with hero/MOB guards and exact frame hash
`6D425AB8BC3200DE7C60788CB8C05B2C23E23C177F1B11F2A07F8FD2511B77D2`;
two full DF0 cold runs have been launched but must pass and have their
active image reviewed before accepting this next passage. Next analyze
the solid wall tiles around X=2672 and use genuine movement/jump/Fire
inputs to find a stable landing beyond them. No Stage-7 level-complete
screen has been seen.

A bounded Stage-7 wall probe identified why a simple Right+Up jump
from X=2656 fails: a Type-17 skeleton MOB `$07FF8194` moves left at
Y=121, from X=2784 at Frame 41370 to X=2716 at Frame 41405. An
HSalive write at Frame 41417 from the monster-collision path confirms
its contact while the hero waits at X=2680/Y=137. Earlier and later
standing Fire starts at this lower ledge missed; a late jump also
collided with the animated foreground. A genuine two-jump sequence
instead reaches X=2800/Y=137 and remains alive through Frame 41546:
from X=2656 start Right+Up, stop Right at X=2680/Y=137, release Up,
wait one 500,000-chip-cycle interval, then press Right+Up again. This
passes above the approaching skeleton and the animated tile. The
checkpoint-only probe hash is
`E147EFAC55B57B0ABEA721FBEC33E9BCACC96EE0DB557B9A915E22684FCD0D22`.
`Tools/a3000-celtic-heart-stage-seven-double-wall-jump-route.json`
encodes the full DF0 cold sequence with a live-hero and exact-image
guard; two independent full cold runs have been launched. Do not count
X=2800 as passed until both match and an active frame is inspected.
The next solid wall at X=2816 and subsequent MOBs remain to be tested.

The Type-21 continuation is now accepted. Both independent full DF0
cold runs of
`Tools/a3000-celtic-heart-stage-seven-post-trap-type21-route.json`
matched at Frame 41342, hero X=2656/Y=185 alive, defeated Type-21
MOB Type 0/flags 40, exact hash
`6D425AB8BC3200DE7C60788CB8C05B2C23E23C177F1B11F2A07F8FD2511B77D2`,
zero non-ROM exceptions. The captured frame shows active cave gameplay
at the wall; the real Fire input and subsequent Right movement are
therefore reproducible. Stage 7 remains open. The two independently
running full cold starts of the double-wall-jump route must still be
checked before accepting its X=2800 landing.

Subsequent checkpoint-only exploration found that a Right+Up jump from
X=2800/Y=137, followed by a stop at X=2824/Y=89, ten neutral
500,000-cycle intervals, and Right, reaches X=2880/Y=89 alive behind
the next wall. A second jump from a later X=2880 phase dies near
X=2906/Y=41; an earlier jump can pass overhead to X=3046 but falls
into the long Type-1 lava strip (BACK GID119/120, X=2992-3232).
Braking during that high jump lands stably at X=2976/Y=41 on the
solid upper lip. A new leap from this lip reaches only X=3128 before
lava contact, while a one-interval-farther stop X=2992 falls off the
lip. These are short checkpoint probes, not cold-verified passages.
Next determine the legitimate intermediate support or launch timing
for the X=2992-3232 lava, while protecting the independently verified
X=2656 position and reviewing the two X=2800 cold runs.

The double-wall-jump continuation is now accepted. Both independent
full DF0 cold starts of
`Tools/a3000-celtic-heart-stage-seven-double-wall-jump-route.json`
matched at Frame 41546: hero X=2800/Y=137, HSalive=`FF`, exact hash
`E147EFAC55B57B0ABEA721FBEC33E9BCACC96EE0DB557B9A915E22684FCD0D22`,
and zero non-ROM exceptions. The final image shows the hero alive on
the raised cave ledge with active enemies and scenery. This proves the
timed double-jump past the moving Type-17 skeleton; Stage 7 remains
open. A checkpoint-only third jump, timed stop at X=2824/Y=89, ten
neutral 500,000-cycle waits, and Right movement reaches X=2880/Y=89
alive and remains stable. The guarded full-cold candidate
`Tools/a3000-celtic-heart-stage-seven-second-wall-stable-route.json`
is running twice independently; do not count X=2880 as passed until
both results and its active image are checked.

A bounded 8192-byte Fast-RAM snapshot at the high fixed lip
X=2976/Y=41 identified an active Type-22 moving platform
`$07FF8284` over the next long lava strip. A checkpoint probe
launched from this lip as soon as it was safely grounded, held
Right+Up for six 500,000-cycle intervals, then released Right. It
landed on the platform at X=3065/Y=130 and rode alive to
X=3122/Y=130 at Frame 41912, then toward the turning point near
X=3150. The platform has not yet been validated in a full cold run.
A subsequent jump from X=3150/Y=130 reached the far fixed bank near
X=3249/Y=121 but died by a Type-19 zombie (`$07FF7834`) moving left
from X=3275; bounded HSalive trace shows the monster-collision return
at Frame 41995. Early, late and crouching Fire from the landing area
missed. The next step is to find the zombie's genuine strike or
evasion window, obtain a stable live far-bank landing, then encode
and independently repeat the full DF0 cold route. No Stage-7 level
transition is yet visible.

A further Stage-7 checkpoint-only route now crosses the third lava
(X=2992-3232) and lands alive on the fixed far bank X=3352/Y=121
through Frame 42380. From stable high lip X=2976/Y=41, jump Right+Up
six 500,000-cycle intervals, release Right to catch the active Type-22
platform `$07FF8284` around X=3065/Y=130, then make a 250,000-cycle
Right adjustment while riding; this keeps a safe six-pixel offset near
its right edge. Ride to about X=3145 and jump to the single solid
GID211 midstone X=3168/Y=144, releasing Right early enough to land
at X=3168/Y=121. Wait until the far zombies reverse, then jump
Right+Up toward X=3266/Y=121. Stop and begin a standing right-facing
Fire at Frame 42084: Type-19 MOB `$07FF77E4` dies at Frame 42112.
Release Fire, wait four 500,000-cycle intervals, and Fire again at
Frame 42183: Type-19 MOB `$07FF7834` dies at Frame 42211. Both finish
Type 0/flags 40; genuine Right movement reaches X=3352/Y=121 and
survives ten neutral intervals. The short-probe frame hash is
`41E62B106C7171A5546E248B02BC20739B6D207572DF406EACABBC396EF994E2`.
This is not yet an accepted passage: the durable full DF0 route
`Tools/a3000-celtic-heart-stage-seven-third-lava-far-bank-route.json`
has just been launched in two independent cold runs and its active
image, guards and hash must be reviewed. The intervening
`Tools/a3000-celtic-heart-stage-seven-second-wall-stable-route.json`
to X=2880 likewise still has two cold runs pending. Stage 7 remains
open and requires a visible reproduced exit before completion.

The second-wall landing is now independently confirmed. Both full DF0
cold runs of
`Tools/a3000-celtic-heart-stage-seven-second-wall-stable-route.json`
matched at Frame 41743 with hero X=2880/Y=89 alive, HSalive=`FF`, exact
frame hash
`2547E1C5649D11D2896278201C1760FAFFC34EE50E1B55AEDB381FA8FF2A96D9`,
and zero non-ROM exceptions. The captured image shows active cave play
on the fixed ledge above the next lava. Treat this landing as passed.
The third-lava far-bank route remains under two independent full-cold
checks and is not yet counted.

Subsequent checkpoint-only exploration from the verified X=2552 route
found a further live segment after the third lava: Right reaches
X=3492/Y=169, a Right+Up jump avoids the Type-16 ground enemy, and a
standing right Fire from X=3576/Y=121 defeats Type-20 MOB `$07FF76F4`.
After releasing Fire and waiting thirteen 500,000-chip-cycle intervals,
a second standing Fire defeats Type-20 MOB `$07FF7604`; both finish
Type 0/flags 40. Release Fire and Up, wait one interval to rearm the
jump, then twelve Right+Up intervals clear the BACK-GID173 bear trap
near X=3616. The short probe holds the hero alive at X=3744/Y=169
through Frame 42915, hash
`C3AA895E80BF9DC4B16A26F27BBA8CCD7D57CC8F9F2DC2BFD81B7504C685E861`.
This is not a passed full-cold segment yet: the guarded
`Tools/a3000-celtic-heart-stage-seven-post-twin-walker-trap-route.json`
is running in two independent full DF0 cold starts. Next check both
runs, inspect the active final image, and then continue real inputs
from X=3744. Stage 7 and the seven-point roadmap remain open.

The third-lava crossing is now accepted. Two independent full DF0 cold
runs of
`Tools/a3000-celtic-heart-stage-seven-third-lava-far-bank-route.json`
matched at Frame 42380, hero X=3352/Y=121 and HSalive=`FF`, both
Type-19 zombies Type 0/flags 40, exact hash
`41E62B106C7171A5546E248B02BC20739B6D207572DF406EACABBC396EF994E2`,
and zero non-ROM exceptions. The inspected final image shows active
cave play on the fixed far bank, with the traversed lava and moving
platform in view. This confirms the Type-22 platform ride, solid
GID211 midstone landing, and two real Fire kills. The later Type-16,
twin Type-20, and BACK-GID173 segment to X=3744 still needs both full
cold results and an image check; do not count it yet. Stage 7 remains
open, with no reproduced level transition.

The post-lava Type-16/twin-Type-20/trap segment is now accepted. Both
independent full DF0 cold runs of
`Tools/a3000-celtic-heart-stage-seven-post-twin-walker-trap-route.json`
matched at Frame 42915 with hero X=3744/Y=169 and HSalive=`FF`, both
Type-20 MOBs Type 0/flags 40, exact hash
`C3AA895E80BF9DC4B16A26F27BBA8CCD7D57CC8F9F2DC2BFD81B7504C685E861`,
and zero non-ROM exceptions. The inspected frame shows active cave
gameplay after the BACK-GID173 trap. This proves the real-input passage
through the Type-16 jump, two timed Fire kills, and the trap jump.

Exploration beyond the verified X=3744 point is checkpoint-only so
far. Direct Right dies at X=3778 from Type-19 MOB `$07FF7564`; a
standing right Fire started at X=3744 defeats it by Frame 42957.
After releasing Fire and waiting two 500,000-cycle intervals, a
second Fire at Frame 43020 defeats Type-16 MOB `$07FF7794` by Frame
43055. Release Fire after that kill, wait four intervals, then Fire
again at Frame 43083 to defeat Type-16 MOB `$07FF7744` by Frame
43119. Right movement to X=3828/Y=169, followed by a standing Fire,
defeats Type-19 MOB `$07FF75B4` by Frame 43252; the hero remains
alive at Frame 43295. These four kills have not been incorporated into
two independent full DF0 cold routes and are not counted as passed.
Direct Right thereafter changes HSalive to `00` at X=3954/Frame
43358. A Right+Up jump from X=3912 similarly changes HSalive after
X=3954/Y=119. The next paragraph identifies this transition. The
four Fire kills remain checkpoint-only pending full cold repetition.

Correction to the preceding interpretation: the HSalive write at
X=3954/Frame 43358 is the **level-exit path**, not a failed map-tile
passage. Its stack starts `$07FEBE70/$07FEBC88/$07FEBACC`, matching
the independently identified `exitHero` path from Stage 6. The
`level07.tmx` BACK layer places GIDs 208/209/210 and 228/229/230
at X=3968-4015, which the published cave tile definitions mark
TYPE 7. A checkpoint continuation of the four real-input kills
reaches a visible `LEVEL COMPLETE — PRESS BUTTON` display at Frame
43717, exact hash
`964999C9E3C6282FCB2D1F79AF4F002699A273825D34FD6EA9242508032B654F`.
After a fresh Fire edge, it shows `THE CAVE STAGE 3`, CODE `K18G`,
at Frame 43886, hash
`542265D08249F448BEB391819A0041F58A370EE549B8093984DEDA2B429A2039`.
Both screen captures were visually inspected. Two independent full
DF0 cold runs of each guarded route,
`Tools/a3000-celtic-heart-stage-seven-complete-level-screen-route.json`
and
`Tools/a3000-celtic-heart-stage-seven-complete-stage-eight-title-route.json`,
have been started; do not count Stage 7 complete until these full
results, zero non-ROM exceptions, exact hashes, and active-to-exit
transition are verified. Stage 8 gameplay has not yet been entered.

A checkpoint-only Stage-8 continuation confirms why one Fire was
insufficient: after the `THE CAVE STAGE 3` title a first fresh Fire
shows `GET READY — PRESS BUTTON` at Frame 43956. Release Fire, wait
1,000,000 chip cycles, and press Fire again; HSalive becomes `FF` and
real Right input moves the hero from X=64 to X=134/Y=185 by Frame
44026. The active cave frame and RAM movement were inspected; short
probe hash
`13633150FE5854AAB4A232A0D0AA350E2765E368A00E94CD44AD3C9E24EB0BB0`.
This is not yet a full-cold Stage-8 gameplay pass. Continuing Right
dies at X=272 from Type-17 skeleton `$07FF80F4`; a bounded HSalive
write trace confirms monster contact. At Frame 44083 the skeleton is
X=306/Y=185, Type17/flags80 while the hero is X=248/Y=185. Standing
Fire begun at X=248 or X=234 did not defeat it and the hero died;
the exact strike or jump phase remains open. First independently
confirm the full Stage-7 exit and title routes, then encode and
repeat the three-Fire Stage-8 active movement route before extending
the first corridor.

The first pair of full DF0 cold candidates for the Stage-7 exit and
Stage-8 title failed their exact-frame hash guards. Composition had
appended a suffix beginning at X=2552 to the already complete
X=3744 route, repeating the same movement segment. This was a route
construction error, not a guest-emulation failure. Both durable JSON
files have been corrected to append that suffix directly to the
verified X=2552 raised-walker route. A consolidated guarded route,
`Tools/a3000-celtic-heart-stage-seven-complete-stage-eight-active-route.json`,
now checks the visible Level Complete hash, Stage-8 title hash, and
active Stage-8 movement hash/HSalive in one full DF0 cold start;
two independent runs are underway. The checkpoint-only active
continuation confirms a rearmed Right+Up jump from X=220/Y=185
avoids the first Type-17 skeleton and reaches X=368/Y=185 alive
through Frame 44153. From there direct rearmed Right+Up hits the
solid wall at X=384; `level08.tmx` shows solid BACK tiles through
the lower rows. A short Left+Up backjump to X=340/Y=137 lands on a
fixed upper step but a subsequent jump remains blocked at X=368/Y=89.
These are checkpoint probes, not full-cold Stage-8 passes. Continue
only after the corrected full-route guards and active images pass.

The corrected Stage-7 completion prefix has now passed in **two independent
full DF0 cold starts**: both satisfy the exact `LEVEL COMPLETE — PRESS BUTTON`
frame hash `964999C9E3C6282FCB2D1F79AF4F002699A273825D34FD6EA9242508032B654F`
and the subsequent `THE CAVE STAGE 3`/`CODE: K18G` title hash
`542265D08249F448BEB391819A0041F58A370EE549B8093984DEDA2B429A2039`.
Both screenshots were inspected and the non-ROM exception count stayed zero.
Together with the independently replayed active Stage-7 route, this confirms
**Stage 7 complete**; the following Stage-8 title alone is not active gameplay.
The two runs then failed only the later *checkpoint-derived* Stage-8 active
frame hash: both had the same full-cold hash
`1DF7D0D52F7746A14142E059003ACDFBE891F2ADB49300CB7792BF4310C2D6F2`
at Frame 44026, hero RAM X=134/Y=185 and HSalive=FF, but the full-cold
image lacked the visible hero present in the checkpoint image. The active
Stage-8 picture therefore remains under investigation; do not count that
hash mismatch as a completed gameplay pass. The first pair of standalone
completion JSON routes was also repaired after the earlier route-composition
error and still needs its own rerun.
At the same Frame 44026, both runs have CPU PC `$07FEA7C8` and
`BPLCON0=$5201`, but the full-cold image has BPL1/BPL2 pointers
`$00013D2A/$00013D58` while the checkpoint continuation has
`$00024E80/$00024EAE`; only the latter displays the hero. The
full-cold image also has colored edge pixels absent from the checkpoint
image. This is a reproducible rendering/guest-state discrepancy, so RAM
position and HSalive alone cannot clear Stage-8 gameplay verification.
A bounded full-cold continuation with the *full-cold* Frame-44026 hash
accepted ran successfully to Frame 44173. It exposed an additional
limitation: the preceding `joystick-down Right` remained held during
`wait` actions, so the ten intervals below were **not neutral**. Frame 44097
shows a visible hero and scene but HSalive is already `00` and X has
advanced to 272; after ten Right intervals, Frame 44173 shows the hero
back at X=64, still with HSalive `00`, consistent with the death/respawn
transition. The captured active-looking frame is therefore not a
surviving Stage-8 passage. Inspect shorter full-cold slices around the
initial movement and isolate the first kill before extending the route.
A new full-cold probe explicitly releases Right at X=134 before waiting;
its result now **passes**: after ten 500,000-chip-cycle neutral intervals,
Frame 44097 shows the hero visibly standing at X=136/Y=185, HSalive=FF,
zero non-ROM exceptions and hash
`FE6281B285639EE99346862FD837F60172B3D6EA7429185EE1827EA0DEC4BDA2`.
This resolves the apparent persistent image loss: the earlier Frame-44026
render was transient, and the later reported death came from leaving Right
held during what had been called neutral waits. The first fully cold
active image was inspected. The durable route
`Tools/a3000-celtic-heart-stage-eight-active-movement-route.json`
contains the exact full-cold guards; its second independent DF0 replay
has now passed with the same Frame-44097 image hash and RAM/liveness
guards. **Stage 8 active movement to X=136/Y=185 is accepted** after
two full cold starts and the checked active image. Do not infer
spontaneous movement or a persistent graphics
failure from the earlier held-input continuation.

Checkpoint-only Stage-8 exploration found a regular upper route: from
X=134/Y=185, successive rearmed jumps and stable landings reach X=164/Y=137,
X=136/Y=89, X=92/Y=41, the single fixed step X=204/Y=89, the next fixed
step X=260/Y=41, and the high fixed platform X=344/Y=41. The latter appears
in `tmp/stage8-high-far-platform-probe.json` with HSalive=FF, but has **not**
passed a full DF0 cold start. Right then reaches X=428/Y=57 at the first
Stage-8 lava. Right+Up jumps from X=428, X=436 and X=442 all die near
X=572-576 before the far bank X=592; X=448 is too late to jump. The next
step is to resolve the full-cold Stage-8 hero-image discrepancy, then replay
the upper route twice from DF0 and inspect any moving platform or alternative
timing for the first lava. No Stage-8 passage or overall roadmap completion
is claimed from these short probes.

The first-lava mid-gap object is now identified, not guessed: Stage-8
`level08.tmx` places MONSTER GID668 at X=512/Y=192, and a bounded live
Fast-RAM snapshot at Frame 44596 finds its active Type-22 platform MOB
`$07FF8054` at X=472/Y=169, moving left one pixel per frame.
`tmp/stage8-first-lava-platform-phase.json` measures its left turn at
X=451 around Frame 44618, then travel to X=508 by Frame 44681 while
the hero remains safe on the fixed X=428/Y=57 ledge. In a checkpoint
continuation, a rearmed Right+Up jump from that ledge at Frame 44618,
Right released after four 500,000-chip-cycle intervals, lands on the
moving platform at hero X=485/Y=146 by Frame 44653. Eight further
intervals carry the **living** hero to X=534/Y=146 at Frame 44702;
`tmp/stage8-first-lava-platform-intercept.json` records platform X=529
and HSalive=FF. This is an actual moving-platform contact under genuine
inputs, but only checkpoint verified. A subsequent five-interval ride
to X=569/Y=146 survives, then an immediate Right+Up launch sets
HSalive=00 at Frame 44742, hero X≈580. The bounded write trace calls
the common `killHero` path through `hero_coll`; the exact collider still
needs isolation. The candidate far-bank probe is **not** a passage.
Resolve the launch collision and require stable landing beyond X=592,
then repeat the full DF0 cold route independently before promoting it.
Map inspection narrows the hazard: BACK GID113 at X=592-623/Y=144
has TYPE=1, while FRONT GID343 at X=624/Y=128,
X=640/Y=112 and X=656/Y=128 also has TYPE=1 in `w2types.tmx`.
An earlier jump from platform X=541 reaches X=612/Y=114 alive,
then dies at Frame 44747/X≈618 with the map-collision stack; a
Right brake at X=598 also dies on descent. The first GID113 tile
and the raised GID343 sequence therefore constrain the landing;
the exact first touched tile still needs a live map probe. A screenshot
at X=534/Y=146 visibly confirms the hero standing on the moving
platform, and another at X=612/Y=114 shows the airborne approach
to the spiked far bank. None of these checkpoint observations
establishes the far-bank passage.

A bounded four-case platform-launch phase sweep then found a genuine
**checkpoint-only far-bank route**. Departing the Type-22 platform after
two 500,000-chip-cycle ride intervals (hero X=548/Y=146), a continuous
Right+Up jump passes the raised hazards; the hero remains alive at
X=689/Y=158 by Frame 44786. Releasing both controls and waiting ten
intervals yields a stable X=692/Y=185 on solid far-bank ground at
Frame 44857, HSalive=FF and image hash
`7020845D9A65BE2253127CEA379DF466A2C4E78D60182CB86295C37C6FC28DFC`.
The active image visibly shows the hero on the ground past the lava.
Ride counts three and four also passed the hazard in short probes;
ride one died at X=618 and ride five died near X=580.
`tmp/stage8-first-lava-far-bank-full-candidate.json` composes this
suffix after the full DF0-cold Stage-7/Stage-8 prefix and guards final
RAM position/liveness. Its first full cold replay **failed** its final
RAM guard: the hero had died and returned to X=64. Comparing
checkpoint and full-cold steps locates the first divergence immediately
after `active-move-4`: the checkpoint jump had X=148/Y=156 at
Frame 44033, but the full-cold run had X=148/Y=185. Up had not been
rearmed in the cold state, so the checkpoint suffix cannot be grafted
onto the full-cold active prefix unchanged. This is a route/input-state
composition difference, not evidence that the moving-platform passage
itself fails on the cold emulator. An explicit full-cold active route
has since confirmed visible movement and stable X=136/Y=185 after
releasing Right. The first Stage-8 lava still requires two matching
independent cold replays before it is counted as passed.
Action-by-action input-state comparison identifies the exact omission:
the checkpoint prefix contains `joystick-up Up` labeled
`stage8-up-release` between `active-fire-release` and `active-right`,
whereas the assembled full-cold prefix does not. Up remains held
through the Stage-7/Stage-8 transition in the latter, suppressing the
next jump edge. A first alignment probe mistakenly inserted that
release at an earlier identically named Stage-7 action. Its Frame-44026
image **still** had the full-cold
hash `1DF7D0D5...`, rather than the checkpoint hash `13633150...`,
at the same X=134/Y=185/HSalive=FF: releasing Up alone does not
explain the transient presentation difference. Both aligned
far-bank candidates had therefore stopped on the *wrong* checkpoint
hash guard before any Stage-8 jump. A second full-cold probe used the
observed full-cold hash and guarded the first jump's RAM
X=148/Y=156/HSalive=FF at Frame 44033; it failed at X=148/Y=185
because Up was still held. Its action trace exposed the misplaced
release. A repaired full-cold route now adds `joystick-up Up` directly
after Stage-8 `active-fire-release` and before Stage-8 `active-right`.
The repaired full-cold route completed once: Frame 44857, hero
X=692/Y=185/HSalive=FF, exact image hash
`7020845D9A65BE2253127CEA379DF466A2C4E78D60182CB86295C37C6FC28DFC`,
zero non-ROM exceptions, and an inspected image visibly shows the hero
on firm ground beyond the first lava. The first full run matches the
checkpoint result exactly. The durable
`Tools/a3000-celtic-heart-stage-eight-first-lava-far-bank-route.json`
guards those RAM bytes and that image hash. Its second independent
DF0-cold replay also completed with the exact same Frame 44857,
X=692/Y=185/HSalive=FF, hash and zero non-ROM exceptions. The first
full-cold active image was inspected. **The first Stage-8 lava and
stable far-bank landing are accepted.** The initial wrong-hash and
missing-Up probes remain useful negative controls, not competing
claims against the completed crossing.

Checkpoint exploration has progressed a little beyond that bank without
promoting it to a full-cold pass. A direct Right walk from X=692 hits
an animated FRONT-GID373/393 ground trap at X≈746, Frame 44884.
A Right+Up jump from X=692 clears that trap and reaches X=804/Y=185
alive at Frame 44913, but a Type-19 zombie MOB `$07FF7F64`
(X=846/Y=185, vx=-1) returns and kills the stationary hero at
Frame 44947. A standing right-facing Fire beginning at X=804/
Frame 44913 defeats it by Frame 44941; it subsequently reaches
Type0/flags40. Real Right input then carries the hero to a stable
X=876/Y=185 through Frame 45068, HSalive=FF, with a visually checked
active scene (`tmp/stage8-first-zombie-passage-probe.json`). The exact
trap-jump and Fire suffix has now completed **one** full DF0 cold
replay: `Tools/a3000-celtic-heart-stage-eight-first-zombie-passage-route.json`
reaches Frame 45068, X=876/Y=185/HSalive=FF, zombie MOB
Type0/flags40, zero non-ROM exceptions, and image hash
`28AC9612C424C08F682F78CDB202D4085DD3039F066FDF60DE81EF181C4252E6`.
The inspected full-cold image visibly shows the living hero on the
far-side ground. A second independent full DF0 cold replay also
completed at the identical Frame 45068, X=876/Y=185/HSalive=FF,
zombie Type0/flags40, image hash and zero non-ROM exceptions. **The
animated-trap jump and first Stage-8 Type-19 zombie defeat are
accepted.** The next concrete gameplay step is to identify the green
enemy and terrain ahead of X=876, measure its active phase, and extend
the route only after a stable live landing in two full DF0 cold runs.

Stage-8 follow-up (2026-10-22): bounded checkpoint continuation from
the accepted X=876 route identifies the approaching green floor MOB as
Type19 (`MOtype=$13`) `$07FF7EC4`, X=919/Y=185/vx=-1 at Frame 45068. Direct Right
dies at X=890; standing right-facing Fire at X=876 turns it Type0 by
Frame 45096 and later flags40 while the hero remains alive. Real Right
then reaches the solid raised wall at X=992/Y=185. The elevated green
Type20 (`MOtype=$14`) MOB `$07FF7E74` patrols about X=1008-1070/Y=137 and killed
an unbraked jump at Frame 45254 (HSalive write A5 points to that MOB).
The short-route solution uses two actual jumps: Right+Up from X=992,
brake at X=1002/Y=135 and land safely X=1004/Y=137, then Right+Up
again while the Type20 turns, passing above it. Braking at X=1060/
Y=90 lets the hero land on the fixed wall at Y=137. A third Right+Up
jump from there, with Right released above the first green lava stone,
lands X=1132/Y=121 alive and remains alive through twelve neutral
500,000-chip-cycle intervals, Frame 45441. An inspected image shows
the hero on the green stone over the second lava; the checkpoint hash
is `ECE0A340F28F709152720A729BA40CE6135C35F4DD31AC28B21766E48E1738D9`.
`tmp/stage8-second-lava-first-stone-full-candidate.json` completed its
first full DF0 cold replay: Frame 45441, X=1132/Y=121/HSalive=FF,
zero non-ROM exceptions and visibly active hero on the first green
lava stone. The full-cold hash is
`FDC71B210A4E15FF9B591AB7357139336FA7D65581E77A963014AC4BF3922083`,
different from the checkpoint image hash at the same hero RAM and
frame because the animated bridge tiles have a different visible
phase. A composed full-cold continuation through the second stone and
far-bank enemy bypass later passed twice independently, as recorded
below; these intermediate landings are part of the accepted passage.

Further checkpoint-only probing found a timed continuation across the
same lava. BACK GID191 is solid at X=1120 and X=1216/Y=144;
BACK GID195 and the surrounding GID119/120 lava tiles are fatal, and
FRONT GID343 adds a high hazard at X=1168. From X=1132/Y=121,
Right+Up reaches X=1230/Y=121 at Frame 45490. The Type15 MOB
`$07FF7E24` patrols X≈1234-1250/Y=153 and killed nine immediate or
delayed neutral landings in bounded probes. Releasing Right+Up and
holding standing Fire at X≈1231/Y=121 keeps the hero alive on the
second stone through Frame 45546, though the MOB remains Type15.
After releasing Fire, another Right+Up jump reaches the solid far
bank at X=1331/Y=137. A Type11 MOB `$07FF7DD4` then approaches from
the right: neutral wait or early standing Fire died, but releasing
Up+Right at X=1331, waiting one interval to rearm, then jumping
Right+Up clears that MOB and lands stably X=1460/Y=153 alive through
Frame 45729. The checkpoint-only image shows active ground gameplay
past the second lava; hash
`2E8C15F8A87D742B4F22CC9D1844F1C572E12FE0E12BCD3A66E421DF9F2786EE`.
The composed full DF0-cold route completed **twice independently** at Frame 45729,
X=1460/Y=153/HSalive=FF, zero non-ROM exceptions and the exact same
hash `2E8C15F8A87D742B4F22CC9D1844F1C572E12FE0E12BCD3A66E421DF9F2786EE`.
The inspected first full-cold image visibly shows the hero alive on the far bank. The
durable `Tools/a3000-celtic-heart-stage-eight-second-lava-far-bank-route.json`
guards final RAM and image hash; the independent replay matched them.
**The upper Type20 bypass, both green lava stones, the second lava and
Type15/Type11 bypass are accepted.** This does not complete Stage 8.

The next checkpoint-only probes identify two further ground enemies
beyond X=1460. Direct Right reaches X=1532/Y=185 alive at Frame 45765
but dies X=1542 on the Type18 (`MOtype=$12`) MOB `$07FF7D84` (HSalive write A5 matches
the MOB). Standing right-facing Fire at X=1532 through Frame 45835
keeps the hero alive while that MOB walks right from X=1546 to X=1616;
it does not defeat it. A subsequent Right+Up jump is killed at
X=1612/Y=168 by a distinct Type20 (`MOtype=$14`) MOB `$07FF7CE4`: a bounded watch
shows it moving left from X=1660/Frame 45835 to X=1618/Frame 45877,
and the death write A5 points to `$07FF7CE4`. An immediate jump and
five shorter Fire-wait variations also died; none is a passage. These
negative controls motivated the later phase and strike measurements.
The initial probe labels mistakenly read
hexadecimal `MOtype` bytes `$12`/`$14` as decimal Type12/Type14; the
published monster numbering is decimal Type18/Type20.

Further bounded checkpoint probing found a real-input solution to
both: from the accepted X=1460 far bank, Right to X=1532, then Fire
through Frame 45835, release for ten neutral intervals and rearm
standing Fire at Frame 45905. The returning Type20 turns Type0 at
Frame 45941 and flags40 by Frame 45983 while the hero stays alive.
Release Fire at Frame 45997 and jump Left+Up four intervals back onto
the raised ledge X=1476/Y=153. After a brief right-facing alignment
to X=1484, crouched Down+Fire at Frame 46099 defeats the Type18 by
Frame 46134; flags40 follows by Frame 46176. Release Fire/Down, move
Right ten intervals to X=1624/Y=185, then Right+Up eight intervals
over the FRONT-GID343 trap at X=1696. The short checkpoint route
`tmp/stage8-post-second-lava-twin-kill-trap-jump-probe.json` holds
X=1712/Y=185/HSalive=FF through Frame 46416 after twelve neutral
intervals. The composed full DF0-cold route has passed **twice independently** at Frame
46416, X=1712/Y=185/HSalive=FF, both enemies Type0/flags40, zero
non-ROM exceptions and exact hash
`CA6A6BB15AD19E88D840AEEA38E74C1B5683D596C699C303C1D71E6370505D44`.
Its selected image visibly shows active gameplay with the living hero
beyond the trap. The second cold replay matched the frame, hero RAM,
both defeated MOBs, zero non-ROM exceptions, and exact hash. The durable
`Tools/a3000-celtic-heart-stage-eight-twin-ground-enemies-trap-passage-route.json`
guards these final RAM bytes and hash. **Both Stage-8 ground enemies
and the animated trap are accepted.** A short checkpoint-only probe
also jumps from X=1712 to the next fixed raised tile X=1760/Y=153
and holds alive through Frame 46542. Its farther Right is blocked by
the next step; a further jump encounters two active Type19
(`MOtype=$13`) zombies `$07FF7BF4` and `$07FF7BA4` patrolling Y=137
and is not a passage. A phase-aligned one-interval edge jump from
X=1760 reaches the raised lip X=1768/Y=137 alive; standing Fire
begun at Frame 46810 defeats the first zombie by Frame 46845 while
the second approaches. An early rearmed Fire failed and the hero
died at Frame 46908, so this is still checkpoint-only. The next
concrete step is to shift the second strike or use another short jump
to bypass the second zombie, then extend the full cold route only
after an independently repeated stable live landing.

A separate checkpoint-only continuation now bypasses that second
upper Type19 zombie by actual movement: from X=1760/Y=153, wait 32
neutral intervals for the two staggered patrols, then Right+Up for
one interval and brake onto the fixed lip X=1768/Y=137. Standing Fire
at Frame 46810 defeats the first Type19 by Frame 46845. Release Fire
at Frame 46880 and immediately jump Right+Up for eight intervals;
the hero crosses above the second active Type19, lands X=1880/Y=185
and remains alive for ten neutral intervals through Frame 47007.
This is a bypass, not a second kill. Two independent full DF0-cold
replays now confirm Frame 47007, X=1880/Y=185/HSalive=FF, first
Type19 Type0/flags40, second Type19 still `$13`/flags80, zero non-ROM
exceptions, and identical image hash
`55DB0C3267713EAAC87D08B422D062E465F5CAB7525CBF59CB85E9753280BBFE`.
The selected full-cold image visibly shows the living hero on the
far-side ground. The durable
`Tools/a3000-celtic-heart-stage-eight-upper-zombie-bypass-route.json`
guards those RAM bytes and hash. **This timed raised-lip landing,
first upper-zombie defeat, and second upper-zombie bypass are
accepted.** Direct Right+Up from X=1880 hits the
next FRONT-GID343 hazard at X=1888. A short checkpoint probe can
instead brake at X=1852/Y=137, rearm Up, and jump over that hazard to
X=1968/Y=185 alive, but a further Type18 (`MOtype=$12`) ground MOB
`$07FF7C94` reaches the stationary hero at Frame 47024; a grounded
Fire started at landing was too late. These latter probes are not
proof of a sustained passage beyond the second trap. Stage 8 remains
open. The next concrete step is to brake and rearm the second jump
before X=1888, neutralize or dodge Type18 `$07FF7C94` at X≈1984,
then demand a stable live landing and two independent full DF0 cold
replays with an inspected active image.

The second Stage-8 FRONT-GID343 hazard and its following Type18
ground patrol are now bypassed with actual inputs. The hero brakes
the preceding jump at X=1852/Y=137, releases Up, then immediately
rearms Right+Up over the trap. At Frame 47000 he lands at X=1964/Y=185;
Up is released for 250,000 chip cycles and pressed again while Right
remains held. This second jump rises over the approaching Type18
`$07FF7C94` and lands on solid ground at X=2112/Y=185. The patrol
remains active (`MOtype=$12`/flags80) behind the hero; this is a bypass,
not a kill. Two independent full DF0 cold starts of
`Tools/a3000-celtic-heart-stage-eight-second-trap-type18-rejump-route.json`
confirm Frame 47116, X=2112/Y=185/HSalive=FF, zero non-ROM exceptions,
and identical image hash
`6AF3B311D06CC0AE4C1E09097653306B0BD0399E40BAB7EEFD6507CFF4793E4B`.
The captured image visibly shows the active living hero at the next
wall. **The second trap and Type18 bypass are accepted.**

The next wall begins at X=2128/Y=160. Direct Right remains at X=2112.
A direct Right+Up jump from there reaches X=2204/Y=151 but dies when
active Type16 `$07FF7B04` crosses its collision box (bounded HSalive
write trace A5=`$07FF7B04`). A ten-interval delay lets Type16 turn
right; the same jump then avoids it but overshoots the small ledge and
falls into the lava. Short checkpoint-only probes identify a legitimate
three-step route: after the delay, brake at X=2164/Y=137 on the fixed
wall lip; rearm Up and jump to the fixed GID36 tile X=2320/Y=137;
then jump to X=2408/Y=169 on the next GID36 tile. The stable X=2320
landing is now accepted: two independent full DF0 cold starts of
`Tools/a3000-celtic-heart-stage-eight-second-tiny-stone-route.json`
confirm Frame 47404, X=2320/Y=137/HSalive=FF, zero non-ROM
exceptions, and identical hash
`A05413EE1E4527BA9D64108176E4A7BB8EF5D1351F0E9415EBF6CBF9B1AD956B`.
The captured image visibly shows the hero on that tiny fixed stone
above the lava.

A direct far-shore jump from X=2408 reached X=2508/Y=185 alive before
the Type19 `$07FF7A14` caught the hero at Frame 47576 (bounded
HSalive write trace A5=`$07FF7A14`, MOtype=`$13`). Waiting six
intervals on the stone yields a far-shore landing X=2508/Y=185 through
Frame 47679; ten intervals die on the patrol's return. Standing
right-facing Fire starts at Frame 47693 after the six-interval wait.
The Type19 becomes Type0 at Frame 47735 while HSalive stays FF, then
flags40 by Frame 47777; the hero remains alive at X=2508/Y=185 through
Frame 47791. Two independent full DF0 cold starts of
`Tools/a3000-celtic-heart-stage-eight-far-lava-type19-passage-route.json`
confirm those RAM bytes, zero non-ROM exceptions, and identical hash
`EF72A3D79D4DE2AF53C43F601E9634B535FE83165744659A06EB6ACDD1249826`.
The captured image visibly shows the active hero on the far bank.
**The GID36 stone sequence, far-bank landing, and Type19 kill are
accepted.** Stage 8 remains open.

Direct Right after that reaches X=2610 alive but falls into the next
lava at X=2622. The map has a solid upper bridge at X=2592-2784/Y=64,
reached through intermediate solid ledges at X=2512-2560/Y=160 and
X=2528-2544/Y=112. A premature Right+Up jump toward the lower ledge
collides with active Type20 `$07FF7A64` at X≈2561/Y=137 (bounded
HSalive trace A5=`$07FF7A64`); tested four- and eight-interval delays
and stationary Fire did not produce a living passage. These are
checkpoint-only negative controls. The next concrete step is to time
an earlier strike or a delayed vertical ascent around that Type20,
then land stably on the upper bridge and validate it with two full
DF0 cold replays and an inspected active image. The overall roadmap
remains open.

The Stage-8 Type20 at `$07FF7A64` is now bypassed without a kill.
From the proven X=2508/Y=185 far-bank state, a stationary Up jump
reaches the lower fixed ledge; release Up for 250,000 chip cycles and
rearm Right+Up for two 500,000-cycle intervals, then brake at
X=2552/Y=89 on the fixed upper step. Eight neutral intervals keep the
hero alive while Type20 remains active below (`MOtype=$14`, flags80).
`Tools/a3000-celtic-heart-stage-eight-type20-upper-fixed-step-route.json`
reproduces Frame 47893, X=2552/Y=89/HSalive=FF and exact image hash
`5F47D0DB3EF055DF503BF9F3B11B1D1C68B78450C414C43BB49655F1CCBD2126`
in two independent full DF0 cold starts, both with zero non-ROM
exceptions. The selected full-cold image visibly shows the live hero
standing on the narrow upper step and the green Type20 patrol below.
**The upper fixed-step landing and Type20 bypass are accepted; Stage 8
is still open.**

Bounded checkpoint probes then identified two Type17 skeletons on
the upper bridge: `$07FF78D4` moves left from X=2639/Y=41 at Frame
47879, and `$07FF7924` approaches from farther right. Immediate
Right+Up from the upper step collides with the first at X=2604; three
early Fire timings did not kill it. A two-stage airborne dodge reached
X=2780/Y=41 alive and held ten neutral intervals through Frame 48097
without killing either skeleton. Two independent full DF0 cold starts
of `Tools/a3000-celtic-heart-stage-eight-upper-skeleton-bridge-route.json`
confirmed X=2780/Y=41/HSalive=FF, zero non-ROM exceptions and the
identical image hash
`909FB4B3878E6170A299B8411EFC10DDBA231FE0341B0F5B1601DE4B80B9E8B3`.
The selected cold image visibly shows the living hero on the fixed
upper bridge, with the first skeleton behind him. **This upper bridge
landing is accepted.**

The far bridge ends around X=2784. Four direct timed jumps toward
X=2840-2892 fell through the next animated span into lava. A bounded
checkpoint-only follow-up instead turned left at X=2768/Y=41 and
used grounded Fire to defeat the first Type17 (`$07FF78D4`, Type0 by
Frame 48073, flags40 by Frame 48115); the second Type17 remained
active but behind. Six neutral intervals after that strike align the
animated GID198 bridge at X=2848: a Right+Up jump briefly lands at
X=2847/Y=41, then an immediate rearmed Right+Up jump reaches the
far fixed bridge X=2948/Y=41 and stays alive for ten intervals through
Frame 48368. The checkpoint hash is
`70D884A4CEC98462EFC100E6ACCF158CB6971AA155D8885B01F9F903C649043F`.
`Tools/a3000-celtic-heart-stage-eight-far-animated-bridge-route.json`
encodes this candidate. Its complete cold-start results and the corrected
full-cold image reference are recorded below.

**Stage-8 far animated bridge and restore control (2026-10-22):** The
checkpoint-only hash above was not a valid full-cold reference: two
independent DF0 cold runs reached the same living X=2948/Y=41 at Frame
48368, but both produced a different image hash and the original guard
correctly rejected it. The checkpoint image omitted the visible hero and
much of the green bridge, whereas the full-cold image visibly shows
active gameplay. The display BPL1/BPL2 pointers also differed despite
matching hero RAM, so checkpoint-only pictures and hashes are not
accepted as visual evidence for this continuation. After replacing the
guard with the cold reference, two further independent complete DF0
runs of `Tools/a3000-celtic-heart-stage-eight-far-animated-bridge-route.json`
passed: Frame 48368, X=2948/Y=41, HSalive=FF, first Type-17 skeleton
Type0/flags40, second Type-17 still active, zero non-ROM exceptions,
identical exact image hash
`26198E183583035BAEB05B827D92C1E0BAE87CBC37DCC1BBA1086B82D4E9C031`.
The first full-cold image was inspected. **The first skeleton defeat and
far fixed-bridge landing are accepted; Stage 8 remains open.**

Bounded checkpoint probes then found a legitimate path off this bridge:
Right+Up for nine 500,000-chip-cycle intervals from X=2948, release both
and wait ten intervals to stand alive at X=3076/Y=169 on the single
solid GID36 tile (X=3072/Y=192). Another Right+Up for six intervals
lands alive at X=3160/Y=185 and stays alive for twelve neutral
intervals. Direct Right from there stops at X=3184 and dies on the
FRONT-GID373/393 trap (HSalive write Frame 48684, map A5=$0018F8).
A first Right+Up over that trap reaches the raised platform X=3208/Y=137;
the active Type-19 zombie `$07FF7884` patrols X=3200..3233/Y=137.
Air-started Fire at X=3194 and X=3208 did not kill it. Releasing Up on
the raised platform and rearming Right+Up jumps over that zombie, but
continued Right dies at X=3314/Y=168 against a separate ground Type-16
MOB `$07FF7834` (HSalive write Frame 48713, monster A5). Braking the
second jump at X=3278 instead lands stably X=3280/Y=185 through Frame
48780; the Type-16 remains active and turns near X=3373. These are
checkpoint-only findings before cold verification. Two independent full
DF0 cold starts of
`Tools/a3000-celtic-heart-stage-eight-raised-zombie-bypass-route.json`
subsequently confirmed the sustained X=3280/Y=185 landing at Frame
48780, HSalive=FF, both Type-19 and Type-16 still active, zero non-ROM
exceptions, and identical exact image hash
`43C1E6FD2D4C5FF6FAE0D64DA8378378954FCAE95D7069B0FFE3582DE4404AF2`.
The selected cold image visibly shows the active hero on fixed ground
beyond the raised zombie. **The tiny-tile jump, FRONT-trap overjump and
raised Type-19 bypass through X=3280 are accepted.**

Checkpoint-only continuations from X=3280 then established a precise
Type-16 strike window: wait two, three or four neutral 500,000-chip-cycle
intervals, then standing right-facing Fire. With two intervals the MOB
`$07FF7834` becomes Type0 by Frame 48836, flags40 by Frame 48879,
while the hero remains alive. Right then reaches X=3376/Y=185, where
the next solid wall blocks direct movement. Right+Up from there reaches
the higher platform X=3415/Y=137 alive; moving farther right dies at
X=3456/Y=158 to Type-18 MOB `$07FF77E4`. Holding Fire on the platform
keeps the hero safe but leaves that Type-18 active as it moves right.
A timed jump from that platform lands alive X=3544/Y=185 through Frame
49195, while two Type-18 patrols remain active at different heights;
an early or late re-jump from X=3544 hits the upper Type-18
`$07FF7794`. These later probes are **not full-cold passes**. The
complete-DF0 candidate `tmp/stage8-type16-wall-approach-full-candidate.json`
encodes the grounded Type-16 kill and living X=3376 wall approach.
Its subsequent cold verification is recorded below.

**Stage-8 grounded Type-16 defeat and next wall (2026-10-22):**
`Tools/a3000-celtic-heart-stage-eight-type16-wall-approach-route.json`
encodes two neutral 500,000-chip-cycle intervals from the accepted
X=3280/Y=185 landing, a standing right-facing Fire strike, and real
Right movement to the next wall. Two independent complete A3000/Kickstart
3.1 DF0 cold starts ended at Frame 48963 with the hero alive at
X=3376/Y=185, Type-16 `$07FF7834` Type0/flags40, zero non-ROM
exceptions, and identical exact framebuffer hash
`1C9A7A8C8C226B64FD661AC0A912F1AFC4A6600261FAD3247FC7A3834871FB1D`.
The selected cold image shows active gameplay and the living hero on the
fixed ground before the wall. **The Type-16 kill and living wall approach
are accepted; Stage 8 remains open.**

Bounded checkpoint-only follow-ups at the later X=3544/Y=185 landing
isolated the next lower Type-18 `$07FF77E4` as the immediate threat:
it approached from X=3567 at Frame 49195 and wrote HSalive=00 from
`hero_coll` at Frame 49209-49238 in direct standing Fire, crouched Fire,
and one-/two-interval backstep Fire probes. The upper Type-18
`$07FF7794` was behind at X=3486/Y=153 at Frame 49195. None of these
attacks defeated the lower MOB, so they are negative controls, not a
passage. The next concrete step is to change the earlier elevated-jump
phase or landing position, then demonstrate a sustained living passage
beyond both Type-18 patrols in two complete DF0 cold starts with a
visually checked active image. Do not use the older checkpoint-only
framebuffer as a visual reference: its missing hero/bridge graphics and
different BPL pointers disagree with the corresponding cold run.

**Stage-8 twin Type-18 avoidance and elevated approach (2026-10-22):**
From the verified X=3376 wall approach, real Right+Up reaches the fixed
X=3415/Y=137 ledge. After the documented wait and first jump to
X=3544/Y=185, the safe window is to continue Right for one
500,000-chip-cycle interval and immediately rearm Up while still moving
right. The hero rises before the lower Type-18 returns and lands at
X=3616/Y=185; both Type-18s remain active behind him. Two independent
complete DF0 cold starts of
`Tools/a3000-celtic-heart-stage-eight-twin-type18-bypass-route.json`
confirmed Frame 49273, HSalive=FF, zero non-ROM exceptions, identical
framebuffer hash
`96BF58F50FF5529E2B75B3578D312D72505E85190770E21B0A745A38CEFE87D7`.
The selected cold image visibly shows the living hero on the fixed
ground beyond both patrols. **This timed bypass is accepted.**

A subsequent Right+Up jump from X=3616 reaches the fixed elevated step
X=3656/Y=137. A second jump, braked after three full intervals and a
150,000-chip-cycle alignment, reaches X=3716/Y=105. Holding Fire there
keeps the hero alive for fourteen further 500,000-chip-cycle intervals,
while Type-20 `$07FF76F4` remains active ahead at X=3772/Y=105.
Two independent complete DF0 cold starts of
`Tools/a3000-celtic-heart-stage-eight-prewalker-upper-step-route.json`
confirmed Frame 49507, X=3716/Y=105, HSalive=FF, zero non-ROM
exceptions, identical framebuffer hash
`4C3E6F5119ED6E749C9C1D1057685389D756F0DB4D4613D67F74DC31DE84060B`;
the cold image visibly shows the living hero on the raised fixed path.
**The elevated approach is accepted, but the Type-20 is not defeated.**

Checkpoint-only continuation jumps over that Type-20 and lands stably
X=3828/Y=185. Two independent complete DF0 cold starts of
`Tools/a3000-celtic-heart-stage-eight-type20-overjump-route.json`
now confirm Frame 49648, X=3828/Y=185, HSalive=FF, zero non-ROM
exceptions and identical exact framebuffer hash
`7AB0015E4D609ADF2A94536DEBE8F3F68786E0C131DCF86E9CEEEEB95681C387`.
The selected cold image shows the visible living hero on the fixed cave
floor, with Type-20 still active behind. **The Type-20 overjump and
sustained far-side landing are accepted; it was not a kill.** Another
checkpoint-only direct Right from X=3828 clears HSalive at X=3940
through the map-collision path, while later Right+Up variants also
clear it near X=3940 at higher Y. This was initially interpreted as
death, but the source analysis below changes that interpretation.
Stage 8 and the overall seven-point roadmap remain open until a visible
transition is reproduced.

The static `level08.tmx`/`w2types.tmx` inspection now identifies the
upcoming collision more precisely: BACK GIDs 208–210 at
X=3952–3999/Y=160, 228–230 at Y=176, and 248–250 at Y=192 all have
`TYPE=7` and `SOLID=0`. Crucially, `hero.asm` dispatches background
Type 7 to `bg_exit`, and `exitHero` sets `GS_COMPLETE_MSG` and then
deactivates the hero, deliberately clearing HSalive. Thus X=3940 is
likely the Stage-8 exit, **not a lethal hazard**. The short checkpoint
probes stopped before a completion screen could appear. Continue the
genuine Right input into this tile, release it, wait for the visible
`LEVEL COMPLETE` and next-stage title, and only then reproduce in two
independent full DF0 cold starts before accepting Stage 8 completion.
A checkpoint-only continuation with 201 neutral 500,000-chip-cycle
intervals after the X=3940 contact did not display the completion
screen; its image still showed the cave without the hero. Since this
checkpoint family already has a documented visual/state discrepancy,
the two independent **full DF0 cold-start** exit probes in
`tmp/stage8-exit-full-a` and `tmp/stage8-exit-full-b` are running to
settle the transition before interpreting this as a gameplay defect.

Those two full cold starts both reproduced Frame 51119 at X=3940 with
the hero deactivated and an unchanged cave scene after 201 wait
intervals; simply waiting does not reveal a completion overlay.
A bounded checkpoint-only follow-up then pressed Fire, released it,
and waited twice for 20,000,000 chip cycles. Its image visibly showed
the next title, `THE CAVE STAGE 4 / CODE: LG6D`. The correct exit
sequence therefore appears to require this extra button press, like
earlier stage transitions. The title is **not yet a full-cold pass**:
two complete DF0 cold starts using
`tmp/stage8-complete-stage9-title-full-candidate.json` are running in
`tmp/stage8-complete-stage9-title-full-a` and `-b`. Confirm matching
framebuffer hashes and inspect the selected cold image before marking
Stage 8 complete.

Both of those complete cold runs ended identically at Frame 51682 with
zero non-ROM exceptions and hash
`3BDA79798F2207835133201400288923BC09AF215C9BEC7542C44837323E5C28`,
but the selected image is a plain blue transition frame, **not yet a
visible Stage-9 title**. The checkpoint-only title appeared later at
Frame 51738. Two fresh complete cold starts in
`tmp/stage8-complete-stage9-title-delayed-full-a` and `-b` add another
20,000,000-chip-cycle neutral wait, to test the settled title image.
Do not mark Stage 8 complete until this image is inspected.

The delayed full-cold continuations in
`tmp/stage8-complete-stage9-title-delayed-full-a` and `-b` now both reach
Frame 51963 with identical hash
`3BDA79798F2207835133201400288923BC09AF215C9BEC7542C44837323E5C28`,
zero non-ROM exceptions, and the reviewed visible title **THE CAVE STAGE 4 /
CODE: LG6D**. The durable replay is
`Tools/a3000-celtic-heart-stage-eight-complete-stage-nine-title-route.json`
with an exact final hash assertion. Stage 8 is therefore complete through
real DF0-only inputs and two independent cold starts. Stage 9 is not yet
accepted as active gameplay: release Fire after the title, enter the
separate ready prompt, press it again, and require visible movement, live
hero RAM, zero non-ROM exceptions and a second cold replay before accepting
an active Stage-9 route.

A subsequent complete DF0 cold start of
`tmp/stage9-activation-full-probe.json` reached Frame 52244 after the
first Stage-9 title Fire/release. Its selected image visibly shows
`GET READY — PRESS BUTTON` over the cave scene, hash
`712B138B4D8B186B50D3FCDE229A1C9DA849017236D1E73E34C7D508C697DD68`,
with zero non-ROM exceptions. This is the ready prompt, not active play.
The next cold-start candidate `tmp/stage9-active-movement-full-candidate.json`
presses Fire again, releases it, records a temporary active-start checkpoint,
and applies five real Right intervals. Require visible hero motion and a
second independent full cold start before accepting Stage 9 as active.

Follow-up 2026-10-22: that candidate's second Fire was still premature.
Although its cave image changed during Right, the actual hero status stayed
`HSalive=00` and X stayed `$0010` through Frame 52301, so it is **not**
accepted as active gameplay. From its exact full-cold checkpoint, a bounded
probe released all buttons, waited 3,000,000 chip cycles, pressed Fire a third
time for 1,500,000 cycles, released it, waited another 1,500,000 cycles,
then held Right for 1,000,000 cycles. The real `HSalive` byte changed to
`FF` by Frame 52329; the hero's X moved from `$0010` to `$002C` by Frame
52364 while Y stayed `$0089`. The selected image visibly shows the active
cave scene and player. Thus Stage 9 needs a distinct third Fire after its
title and ready sequence. The full DF0 cold-start candidate is
`tmp/stage9-live-full-candidate.json`; require two independently completed
full cold starts with matching live RAM, exact hashes, zero non-ROM
exceptions and reviewed active images before promoting a durable route.

The first complete DF0 cold start of that candidate has now finished at
Frame 52364 with hero X=`$002C`/Y=`$0089`, `HSalive=FF`, zero non-ROM
exceptions and exact image hash
`EBBCDE35FCB76056D1B002ACFC6898CACD10053F8F1BE1CB2C7F550A6FF2E03A`.
Its selected cave gameplay image was reviewed. The second independent full
DF0 cold start in `tmp/stage9-live-full-b` has also finished with the **same**
Frame 52364, X=`$002C`/Y=`$0089`, `HSalive=FF`, hash
`EBBCDE35FCB76056D1B002ACFC6898CACD10053F8F1BE1CB2C7F550A6FF2E03A`,
zero non-ROM exceptions and an independently reviewed active cave image.
The durable exact-hash replay is
`Tools/a3000-celtic-heart-stage-nine-active-movement-route.json`
(`--validate`: one valid scenario). **Stage 9 active gameplay and movement
are accepted; Stage 9 itself is still open.**

Checkpoint-only exploration after that accepted start shows real Right
movement to X=128/Y=185 alive; holding Right without jumping is killed at
X=180. Right+Up from X=128 crosses the initial hazard and reaches
X=240/Y=185 alive, then sustained Right reaches X=326/Y=185 still alive
at Frame 52505. The longer Right control dies at Frame 52509 with a
monster-collision return address (`$07FEC580`, MOB pointer `$07FF81E4`).
These are short restore probes, **not** a full-cold passage. Next: isolate
that MOB's type/phase, find a sustainable real-input route, and promote it
only after two independent complete DF0 cold starts and image review.

The first Stage-9 ground MOB is a Type-17 skeleton at `$07FF81E4`,
X=348/Y=185 at Frame 52505 (`MOflags=80`). Fire begun only at hero
X=326 is too late and the skeleton collides. From the landed X=240,
hold Right for 2,000,000 chip cycles to X=298, release Right and use a
standing right-facing Fire strike: the skeleton changes to Type 0 while
the hero remains alive. After its death animation, real Right reaches
X=351 and a neutral 5,000,000-cycle hold remains alive at X=352/Y=185.
`Tools/a3000-celtic-heart-stage-nine-first-skeleton-passage-route.json`
has now passed **two independent complete DF0 cold starts** at Frame
52688 with identical hash
`11FD221EAAD5748866861A7ADFABDAB5E0CE046000BFC09B3004F3799C70956A`,
hero `HSalive=FF`, skeleton Type0/flags40, zero non-ROM exceptions and
both independently reviewed active images. The first hazard and skeleton
are accepted; Stage 9 is not complete.

Checkpoint-only continuation from X=352: direct Right is blocked by the
first stone rise; Right+Up lands on the next fixed lower platform at
X=432/Y=137, sustaining ten neutral intervals. The next Right+Up jump
is unsafe against two upper Y=89 patrols: Type18 at `$07FF8234` and
Type19 at `$07FF80A4`. A short-delay jump collides with Type18; a later
jump after it moves left collides with Type19. Air Fire at several bounded
timings has not killed either. These are negative checkpoint controls,
not a full-cold upper-platform pass. Next: find a timed strike or route
between the two upper patrols, confirm stable live landing, then extend
the durable DF0 route and repeat it twice from a fresh cold start.

The upper route is now confirmed in two independent complete DF0 cold
starts. Starting at the accepted X=352 skeleton passage, Right+Up reaches
the fixed lower X=432/Y=137 platform. A timed 10,000,000-chip-cycle wait
lets the Type18/Type19 patrols clear the jump. A short Right+Up pulse,
horizontal brake and sustained neutral landing put the hero alive at
X=436/Y=89. A small right-facing grounded step to X=448 and real standing
Fire kills Type19 `$07FF80A4`; the remaining Right input reaches stable
X=496/Y=89. `Tools/a3000-celtic-heart-stage-nine-upper-zombie-passage-route.json`
has matching Frame 53240 and exact hash
`7A80182BF822B9E49EF4FBBF6C9CB5644289D3D5297F0A80330DD4803988708C`
in both cold runs, `HSalive=FF`, Type19 Type0/flags40, zero non-ROM
exceptions and two reviewed active images. Its exact-hash scenario
validates. **The upper landing and Type19 kill are accepted; Stage 9
remains open.** The game source confirms that an effective HeroStrike
requires a grounded hero, explaining the negative airborne Fire probes.

Short restore probes continue beyond this accepted point. A braked
Right+Up jump reaches the narrow fixed X=548/Y=41 column. From there,
Right+Up with a 2,000,000-cycle horizontal pulse lands on a first green
stone at X=604/Y=41 and survives a further 10,000,000 cycles. Right to
X=636, a second timed Right+Up jump, and a brake reach a second fixed
green stone X=748/Y=121. A bounded Type16 patrol watch measures
`$07FF8054` moving left from X=864 at Frame 53824 to X=640 at Frame
54049, then turning right. Waiting 9,000,000 cycles on the safe second
stone, jumping Right+Up for 2,500,000 cycles and braking lands the hero
at X=820/Y=73, alive through Frame 54070. Earlier jumps collided with
that Type16 or overshot the next stone and fell into lava. The permanent
`Tools/a3000-celtic-heart-stage-nine-first-lava-green-stones-route.json`
repeats this exact route in two independent full DF0 cold starts. Both end
at Frame 54070 with X=820/Y=73, `HSalive=FF`, zero non-ROM exceptions,
and the identical frame hash
`552427C8F202EDA1F003364683C828EC2D85720B2ECCD8C64D7D29194C47FB70`.
Both final images show the living hero on the third green stone, and the
exact-hash scenario validates. **The upper column and three stone landings
are accepted, but the first lava crossing and Stage 9 remain open.**
The extracted `level09.tmx` and `w2types.tmx` identify GID198 at
X=880/Y=96 as SOLID=1/TYPE=8 and GID195 at X=976/Y=48 as
SOLID=0/TYPE=1; visual similarity alone does not prove a tile is
currently load-bearing. From X=820, short Right+Up probes braking
between X=884 and X=900 fall into the lava; a bounded 0–11M-cycle
delay sweep at X=884 also found no lasting landing. A bounded live map
watch identified `BgMap` at `$07C08FEA`: the X=880/Y=96 word at
`$07C096D6` alternates between solid `$C98A` (Frames 54091–54126)
and nonsolid `$098C/$098E` (Frames 54133–54175), repeating roughly every
92 frames. The X=816 fixed green tile remains solid `$C97C` through the
same samples. This proves a timed bridge rather than a permanently solid
step. A new checkpoint continuation lands on the bridge at X=884/Y=73
by Frame 54116 and remains grounded until Frame 54123. The bridge loses
solidity at Frame 54130, so releasing and pressing Up again at Frame
54123 is essential: a second Right+Up jump lands on fixed GID191 at
X=948/Y=73 and remains alive through Frame 54260.
`Tools/a3000-celtic-heart-stage-nine-blue-bridge-far-stone-route.json`
confirms the landing in two independent full DF0 cold starts: both report
`HSalive=FF`, zero non-ROM exceptions and identical hash
`6B2DEDFF4072D7CA0350236C92B35948D38172509A43D6D274D42EF20778FDF1`.
Both active images were reviewed. **The timed blue bridge and X=948 fixed
stone are accepted.**

From that checkpoint, walking right dies at X=960/Frame 54196 from the
approaching Type16 MOB `$07FF7FB4` (X=968/Y=89 at Frame 54197;
`hero_coll` return `$07FEC580`). Early and late grounded Fire, including
crouched variants, did not kill it. A direct overjump survives this MOB
but falls short of the next high fixed stone. The map instead offers a
second timed bridge at X=976/Y=48. A short Right+Up jump from X=948
brakes at X=980/Y=25 while its live map word is solid `$C97C`–`$C982`;
the tile becomes hazardous/nonsolid at Frame 54217 and kills a waiting
hero. Releasing Up and jumping again at Frame 54211 reaches the next
fixed GID191 at X=1064/Y=41, alive through Frame 54358.
`Tools/a3000-celtic-heart-stage-nine-first-lava-far-fixed-stone-route.json`
confirms this in two independent full DF0 cold starts. Both end with
`HSalive=FF`, zero non-ROM exceptions and identical hash
`A3C708F3BE50FCFD08AF4E7E585D6F005856CC9CEECDA567CDDA4742A7D486B2`;
both active images show the hero on the distant fixed green bridge.
**The second timed bridge and X=1064 landing are accepted; the first lava
crossing and Stage 9 remain open.** Right beyond X=1092 falls into a
second Type16 `$07FF8004` at X≈1140/Y≈121 during descent. A bounded
hero-alive write trace confirms its `hero_coll` return `$07FEC580` at
Frame 54326. Next, find its safe timing or strike and a sustainable
far-bank landing with two full cold starts and active images.

**Stage-9 third timed bridge and far-wall landing (2026-10-23):**
The X=1152/Y=96 tile is a third phase-switched GID198 bridge. From the
accepted X=1064/Y=41 stone, a quick Right walk reaches its safe edge
X=1092/Y=41. Waiting until Frame 54309 aligns a real Right+Up jump:
the hero brakes X=1148, lands X=1148/Y=73 at Frame 54365 while the
live tile word is solid `$C98A`, and remains alive through Frame 54394.
At Frame 54401 the word becomes nonsolid `$098C`, so Right+Up must be
rearmed sooner. A jump at Frame 54373 brakes at X=1216 in the air,
lands on the permanent far wall at X=1220/Y=73, and remains alive
through Frame 54443. The Type16 `$07FF7F64` then approaches from the
right at X=1229/Y=57 and kills a stationary hero; a grounded Fire
control failed. A genuine Right+Up jump from X=1220 at Frame 54422
passes over that enemy. Braking at X=1282 produces a sustained live
landing at X=1284/Y=73 through Frame 54559. The durable route
`Tools/a3000-celtic-heart-stage-nine-first-lava-far-wall-route.json`
replayed twice from full DF0 cold starts with identical final hash
`87AFD15F2066FA6EBAE26C87B4FC1189CA8D0DAF66D4C6DB0E1BBE0E29AC2ED1`,
`HSalive=FF`, zero non-ROM exceptions and two visually checked active
images. The hero is stably on the permanent far wall. This passes the
first lava crossing; Stage 9 remains open. Next inspect the map and
active MOB phases to the right of X=1284, then require another sustained
living landing, two full cold replays and active images before extending
the route.

**Stage-9 post-wall MOB bypass (2026-10-23):**
From the accepted X=1284/Y=73 wall, real Right descends to the lower
floor. A direct run reaches X=1438/Y=185 alive at Frame 54636, then
dies at X=1440. The bounded `HSalive` write trace has `A5=$07FF7F14`
and the `hero_coll` return `$07FEC580`; a MOB watch identifies a live
MOB with `MOtype=$17` (decimal 23) at X=1449/Y=169, flags `$80`, approaching from the
right. Standing Fire from X=1418, including 0.5–1.5-million-cycle
delays, did not kill it and ended in contact. A timed Right+Up jump
from X≈1410–1418 passes over it. In a restore-only probe, braking at
X=1544/Y=185 after the jump and holding neutral for eight 500,000-cycle
intervals kept the hero alive through Frame 54745. The durable route
`Tools/a3000-celtic-heart-stage-nine-postwall-skeleton-bypass-route.json`
passed two independent full DF0 cold starts with identical final hash
`60E7A1E69E18BF0DAC73460A1885849EF4B664948FD61B5654AFB35C17264DF2`,
`HSalive=FF`, zero non-ROM exceptions and two checked active images.
This confirms a sustained bypass, not a kill; the `$17` MOB remains
active. Stage 9 remains open. Next inspect the nearby FRONT trap and
terrain beyond X=1544 with real inputs before extending the route.

**Stage-9 next FRONT trap and MOB bypass (2026-10-23):**
Direct Right from X=1544/Y=185 dies at X=1586/Frame 54766. The
`HSalive` write trace has `A5=$000C98` and the map-collision return,
consistent with the animated FRONT hazard at X=1600, rather than a
MOB. Immediate Right+Up passes it. Releasing Right at X=1688 then
dies from the `MOtype=$17` (decimal 23) MOB `$07FF7E74` after it resets from X≈1807 to
its X=1664 spawn and walks toward the stationary hero. Holding Right
for twelve additional 250,000-cycle intervals instead reaches
X=1770; braking yields X=1772/Y=185, alive through Frame 54914 after
eight neutral 500,000-cycle intervals. The MOB remains `MOtype=$17`/flags80
and follows at X=1723; this is a timed bypass, not a kill.
`Tools/a3000-celtic-heart-stage-nine-posttrap-skeleton-outrun-route.json`
passed two independent full DF0 cold starts to Frame 54914 with
`HSalive=FF`, zero non-ROM exceptions, identical hash
`971D9FBDC6AC6D5A578CBCFF1E2940A35B9EDDECD02EE9197D4D6AF435FCF68A`
and two checked active images. The X=1600 trap and passage to
X=1772/Y=185 are accepted; Stage 9 remains open. Continue with real
inputs ahead of the pursuing MOB and inspect the next fixed
wall, raised MOBs and traps before extending the route.

**Stage-9 raised-floor and walker checkpoint probes (2026-10-23):**
At X=1772/Y=185, immediate Right+Up collides with the nearby ground
MOB `$07FF7D34`, `MOtype=$13` (decimal 19), X=1781/Y=185. A delayed
vertical jump from Frame 54900 lets it pass beneath the hero, but
the following `$17` MOB catches a stationary landing. A second
Right+Up jump as the hero reaches the ground lands X=1848/Y=137 on
the raised fixed floor. Holding Right collides with upper MOB
`$07FF7BF4`, `MOtype=$14` (decimal 20), at X≈1884. A standing
right-facing Fire strike begun at X=1848/Frame 54988 changes this
MOB to Type 0; by Frame 55066 its flags become `$40`. The short
restore probe remains alive through Frame 55101 with active cave
gameplay. Another `$14` MOB `$07FF7C44` remains active. The durable
route `Tools/a3000-celtic-heart-stage-nine-upper-greenwalker-kill-route.json`
passed two independent full DF0 cold starts to Frame 55101 with the
hero at X=1848/Y=137, `HSalive=FF`, the targeted MOB Type0/flags40,
zero non-ROM exceptions and identical frame hash
`C25C83182119A852C58C7D31B84A6407F3A1BEB6B1188B56ABFB8361F614AD41`.
Both active images were checked. The raised landing and one walker
kill are accepted; Stage 9 remains open. Next handle the other active
`$14` MOB and the GID173 hazard near X1936.
The `MOtype` watch values here are hexadecimal: `$13/$14/$17` mean
decimal 19/20/23, respectively.

**Stage-9 next GID173 trap checkpoint probes (2026-10-23):**
From the accepted X=1848/Y=137 raised landing, direct Right reaches
X=1938 alive and dies at X=1940/Frame 55147. The bounded `HSalive`
write trace has `A5=$000F32` and the map-collision return; the map
places a `TYPE=16`, non-solid BACK GID173 hazard at X=1936/Y=144.
Right+Up begun near X=1896 clears the tile and the still-active
`MOtype=$14` (decimal 20) walker `$07FF7C44` without a kill.
The short restore probe lands at X=2040/Y=137, alive through eight
neutral 500,000-cycle intervals to Frame 55252; the walker remains
Type20/flags80 behind the hero. The durable route
`Tools/a3000-celtic-heart-stage-nine-173-trap-overjump-route.json`
passed two independent complete DF0 cold starts in
`tmp/stage9-173-trap-overjump-full-a` and `-b`: Frame 55252,
X=2040/Y=137, `HSalive=FF`, zero non-ROM exceptions and identical
frame hash `D0F56427E8A1303A34FAB68AD53D42424204D82A49B1FAF183844734524648E3`.
Both images show the active hero on the raised floor. The GID173
trap and remaining Type20 walker are passed, while Stage 9 remains
open. At the drop beyond X≈2040, direct Right dies at X=2076/Y=183;
the nearby FRONT-GID343 tiles are at X2064–2095/Y176. Right+Up
from X2040 clears them in a short restore probe and lands X2166/Y185.
After releasing Up for one 250,000-cycle interval, a second Right+Up
jump reaches X2256/Y185 alive through Frame 55396. Two release
intervals instead die at X2190/Y164 from `hero_coll` against active
MOB `$07FF7A64`, `MOtype=$17` (decimal 23), X≈2200/Y169. The durable
`Tools/a3000-celtic-heart-stage-nine-double-front-jump-route.json`
passed two independent full DF0 cold starts in
`tmp/stage9-post173-double-front-jump-full-a` and `-b`, with identical
frame hash `1D9D7BBC470BBF508305F18124113BE55CBE1A784399C297FA227573179A3178`,
`HSalive=FF`, zero non-ROM exceptions and two reviewed active images.
The FRONT traps and Type23 MOB are passed without a kill; Stage 9
remains open. Next probe the wall at X≈2272, the upper fixed platform
and nearby MOBs with real inputs before extending the full-cold route.

**Stage-9 high wall and next lava passage (2026-10-23):** The wall
at X2272 has solid sides through Y208 and a solid top at Y80; the
lower X2256/Y185 landing cannot jump directly onto it. A separate
real-input route branches from the accepted raised X1848/Y137
position. Right+Up with braking lands on the upper fixed bridge
X1928/Y89. Releasing Up, walking to X1984, then rearming Right+Up
and braking in the air lands stably on the high fixed stone
X2048/Y41. The durable
`Tools/a3000-celtic-heart-stage-nine-high-stone-route.json`
passed two independent complete DF0 cold starts in
`tmp/stage9-high-stone-full-a` and `-b`: Frame 55305,
`HSalive=FF`, zero non-ROM exceptions, identical hash
`DCCB507394695AD088EB0467E22B3B2344E9F0645CCBB0941864DECACB4A6D3C`.
Both active images show the hero on that stone.

From X2048, immediate Right+Up collides with MOB `$07FF7B54`,
`MOtype=$11` (decimal 17), on the farther platform. Eight neutral
500,000-cycle intervals, releasing Up and one 250,000-cycle Right
interval time a jump onto X2160/Y41; releasing Up briefly and
rearming Right+Up clears this Type17 patrol and crosses the wall
top and BACK-GID173 at X2304/Y64. Direct movement then dies at
X2358/Y46 against MOB `$07FF7B04`, `MOtype=$10` (decimal 16),
as the bounded `HSalive` trace shows. Airborne Fire begun near
X2320 delays the hero while that MOB moves right, allowing a live
pass. Brake at X2366/Y57 before the wall edge, release Up and Fire,
wait one neutral 500,000-cycle interval, then rearm Right+Up over
the next lava to the fixed GID191 stone X2540/Y121. Eight further
neutral intervals remain alive through Frame 55692. The durable
`Tools/a3000-celtic-heart-stage-nine-high-wall-lava-stone-route.json`
passed two independent complete DF0 cold starts in
`tmp/stage9-high-wall-lava-solid-full-a` and `-b`, with matching
hero RAM X2540/Y121, `HSalive=FF`, zero non-ROM exceptions and
identical hash `D52C401AACB024C06D7B5E142A2A1CD0C33585522D1336BF2AA4283E63B14D8C`.
Both active images show the hero on the green stone over lava. The
wall and this first stone are accepted, but the second lava and
Stage 9 remain open. Braking at X2552 falls between solid tiles;
waiting too long on the wall lets MOB `$07FF79C4` return. Next
analyze the animated/fixed stones to the right and reach a stable
far-bank landing with real inputs and two independent full-cold
visual replays.

Checkpoint-only probes from the accepted X2540/Y121 stone narrow the
next jump. Immediate Right+Up reaches X2638/Y121 alive, then loses
`HSalive` at X2640/Y123 (Frame 55746); the bounded write trace points
to map collision, not a MOB. Eight extra neutral 500,000-cycle
intervals before jumping still die at X2640. A 100,000-cycle Right
offset likewise dies there; 150,000- and 200,000-cycle Right offsets
step off the narrow departure stone and die earlier at X2566. These
restore-based probes establish neither a far landing nor a visual
pass. Next inspect the live collision tile and jump timing at X2640,
then verify any successful crossing through two full DF0 cold starts
and active images.

**Stage-9 second-lava intermediate and far stones (2026-10-23):**
The X2640 death above is caused by the static deadly BACK-GID37 tile
at X2640-2671/Y144. A fixed GID17 step at X2608-2639/Y144 allows
a legitimate two-jump route. From X2540, Right+Up reaches X2616/Y99;
releasing Right and Up there lands and remains at X2616/Y121.
Rearming Right+Up from that step reaches the fixed GID191 stone
X2688/Y89; releasing both controls there and waiting ten 500,000-cycle
intervals remains alive. The durable
`Tools/a3000-celtic-heart-stage-nine-second-lava-far-stone-route.json`
passed two independent complete DF0 cold starts in
`tmp/stage9-second-lava-far-stone-full-a` and `-b`: Frame 55889,
`HSalive=FF`, zero non-ROM exceptions, identical hash
`A618893DB84CA366E3055E8141781F9619E30ABBBCDCF6DB6985B900085BC60B`.
Both active images show the hero on the fixed stone. This stone is
accepted; the rest of the second lava and Stage 9 remain open.
A further real Right+Up jump from X2688 lands sustainably at
X2772/Y57 on the next fixed GID191 platform. The durable
`Tools/a3000-celtic-heart-stage-nine-second-lava-upper-stone-route.json`
passed two independent complete DF0 cold starts in
`tmp/stage9-second-lava-upper-stone-full-a` and `-b`: Frame 56002,
`HSalive=FF`, zero non-ROM exceptions, identical hash
`6B63CCCF7FB8691F5A6FDA09965DBD4ED5581D50089429FC42CCEC25C634ED0B`.
Both active images show the hero on the raised fixed stone. This
landing is accepted; the second lava and Stage 9 remain open. Direct
Right from X2772 falls into lava, while immediate Right+Up reaches
X2852/Y40 and dies on the BACK-GID37 patch at X2832-2863/Y64.
Test a brake before the patch and the lower fixed step X2816/Y160.
The independently confirmed lower route brakes the
next jump earlier at X2806/Y8, releases Right and Up, and falls to
X2808/Y137 on the fixed X2816/Y160 step. Two short Right intervals
reach X2822/Y137; Right+Up travels below the deadly X2832-2863/Y64
patch and brakes stably on fixed GID191 X2928/Y121 through Frame
56234. The durable
`Tools/a3000-celtic-heart-stage-nine-second-lava-undertrap-stone-route.json`
passed two independent complete DF0 cold starts in
`tmp/stage9-second-lava-undertrap-stone-full-a` and `-b`:
`HSalive=FF`, zero non-ROM exceptions, identical hash
`9C70F57ECA2DB4DDE02893B3E07C42186A4C1BD7B60A80D824E560EDA83B1E58`.
Both active images show the hero on the fixed stone. This landing is
accepted, but the far bank and Stage 9 remain open. A direct
jump from X2928 dies in `hero_coll` against active MOB `$07FF7924`
(`MOtype=$13`, decimal 19) around X3004/Frame 56272; delaying the
jump changes the collision to another active MOB `$07FF7974` at
X3016/Frame 56362. Resolve those patrols with real input before
extending the full-cold route.

Checkpoint-only continuation now finds a grounded strike window for
the second Type19. Wait six neutral 500,000-cycle intervals on
X2928, jump Right+Up to the solid far wall and brake at X3020/Y121
on Frame 56322. Right-facing Fire while grounded changes MOB
`$07FF7974` from `MOtype=$13` to 0 at Frame 56350; it reaches
flags `$40` by Frame 56392. Release Fire and immediately jump
Right+Up over the still-active `$07FF7924`. Braking at X3098/Y103
lands sustainably on the solid far bank X3100/Y121 through Frame
56501. A longer Right hold dies against another MOB `$07FF78D4`
near X3144. The durable
`Tools/a3000-celtic-heart-stage-nine-second-lava-far-bank-route.json`
repeated the complete DF0 cold start independently in
`tmp/stage9-second-lava-far-bank-full-a` and `-b`. Both reached Frame
56501 at X3100/Y121 alive (`HSalive=FF`), with the struck Type19
`MOtype=00`/`MOflags=40`, zero non-ROM exceptions and the same hash
`8353DDDB4D978945697F2B629CEB2190B5DC679291A742A5194F7DED37132575`.
Both captured images visibly show active gameplay with the hero on
the fixed far bank. The second lava is accepted as crossed; Stage 9
is still open.

Further checkpoint-only work from X3100 identifies the next pair as
active `MOtype=$12` (decimal 18) at `$07FF78D4` and `$07FF7884`,
both at Y169. The first patrol moves right from X3198/Frame 56501;
the second returns left from X3288/Frame 56575. Right+Up reaches
X3248/Y185 alive, but an early or three-interval-delayed standing
Fire from there still dies on Frame 56605 with `hero_coll` pointing
to `$07FF7884` at X3256. An immediate rearmed jump dies still
earlier near X3266. Braking the first jump at X3142/Y71 instead
lands on a fixed intermediate ledge X3144/Y153 and stays alive
through Frame 56596. The durable
`Tools/a3000-celtic-heart-stage-nine-far-bank-upper-step-route.json`
passed two independent complete DF0 cold starts with `HSalive=FF`,
zero non-ROM exceptions, the identical hash
`06B0CD52670E689021022E42AE3447245F7469126595E694C2922603553DF7B0`,
and two inspected active images. This ledge landing is accepted.
A second jump from that ledge also meets
`$07FF7884`. A grounded Fire there starts the strike animation, but
the observed strike at X3160/Y157 did not kill the ghost near
X3160/Y169: the active strike's vertical collision line is Y161,
while the ghost's top collision line is Y169. Ground-level Fire at
Y185 may be marginal at the ghost's lower line; prior timing checks
did not kill it. A better landing or attack alignment is needed.
None of these checkpoint probes is a
passage beyond the ghost pair.

A later checkpoint-only timing probe found an avoidance route without
an attack: from X3144/Y153, briefly move right off the ledge; jump
Right+Up after X3166/Y171, release Up in the air at X3250/Y163,
retrigger Up immediately on landing X3260/Y185, and continue right.
A one-interval horizontal air brake before the first landing shifts
the second jump four pixels left and avoids the first Type18's
collision box. `Tools/a3000-celtic-heart-stage-nine-twin-ghost-bypass-route.json`
reaches X3344/Y185 alive and holds twelve neutral 500,000-cycle
intervals through Frame 56832, with both ghosts still active behind him.
Two independent complete DF0 cold starts in
`tmp/stage9-twin-ghost-bypass-full-a` and `-b` agree on HSalive=FF,
zero non-ROM exceptions and image hash
`3818C4392F6AAABFA36B02756AF80E2EAA0B2B9945BC0C40B4CB8A64B2529EE0`;
both active images were checked and their PNG SHA-256 values match.
This bypass to the next wall is accepted, but neither ghost was killed.
At X3344 the wall blocks direct Right, and waiting much longer than
the candidate route lets `$07FF7884` return and kill the hero at
Frame 56893. Right+Up from the lower floor reaches X3392/Y137 but
dies on a FRONT-map collision at Frame 56910. Retriggering Up from
the temporary higher X3338/Y153 landing reaches X3392/Y107 and
also hits the FRONT hazard. Delaying that last jump by 2-7 short
intervals did not pass the tile. Next isolate its live animation
phase or find a higher legitimate takeoff; do not treat the still
airborne figure as a passage.

Further checkpoint-only input probes found a higher, timed continuation
from the accepted X3144 ledge. Brake the third jump at X3372/Y103,
land on the fixed X3372/Y137 step, and wait until the decimal Type19
zombie `$07FF7834` turns right. Right+Up then reaches X3412/Y89;
release both controls for one 250,000-cycle interval and immediately
rearm Right+Up. This clears the FRONT tiles at X3392 and the zombie.
Release Up while falling, land X3554/Y185, and start the next jump
with three vertical Up intervals before pressing Right. That timing
passes two decimal Type18 ground ghosts. The durable route
`Tools/a3000-celtic-heart-stage-nine-twin-ghost-trap-crossing-route.json`
confirms X3772/Y185 alive through Frame 57173. Two independent full
DF0 cold starts in `tmp/stage9-twin-ghost-trap-crossing-full-a` and
`-b` agree on HSalive=FF, zero non-ROM exceptions, image hash
`4C55C4F9287B5BA6360CE7325BE3EB88F96B3D0E25C2999128327A73A522CBE4`
and identical PNG SHA-256; both active images were inspected. Count
the higher crossing as passed, with both ghosts bypassed and active.

The same checkpoint branch then jumps onto the X3808/Y137 wall step.
Standing right-facing Fire started at Frame 57272 kills the returning
decimal Type19 `$07FF7654` at Frame 57304; Type becomes 00 and flags
later 40. A Right+Up jump clears BACK-GID173 and the FRONT hazard at
X3840, landing X3872/Y137. A second jump reaches X3904/Y89; a
quickly rearmed third jump crosses the fleeing decimal Type20
`$07FF7604` but descends into a map collision at X3988/Y43. Static
level09.tmx/w2types.tmx places TYPE7 exit tiles at X4000–4047/Y64–111.
The map collision appears to be that exit, not a death. A checkpoint-only
continuation from the accepted earlier state waited, pressed Fire, and
visibly displayed the next title, `THE FOREST`, at Frame 59348; its
selected image hash was
`B0123BA71428F2EB9D23FD1F94FD16F65FB437D9A05352F60D38CD38FCD9D453`.
The durable complete route is
`Tools/a3000-celtic-heart-stage-nine-complete-stage-ten-title-route.json`.
Two independent complete DF0 cold starts in
`tmp/stage9-complete-stage10-title-full-a` and `-b` agree at Frame
59348 on image hash
`B0123BA71428F2EB9D23FD1F94FD16F65FB437D9A05352F60D38CD38FCD9D453`,
zero non-ROM exceptions and identical PNG SHA-256. Both cold images
were checked and visibly read `THE FOREST`. The hero's HSalive=00 at
the transition is intentional `exitHero` deactivation. **Stage 9 is
complete** on the reproduced next title. Stage 10 needs separate
gameplay activation, documented below.

The next title reads `THE FOREST / STAGE 1`. Its next PRESS BUTTON,
stage-title PRESS BUTTON and separate GET READY require three distinct
Fire presses across the transition. The durable route
`Tools/a3000-celtic-heart-stage-ten-active-movement-route.json` presses
all three, then moves the visible hero with real Right input from X16
to X44/Y185. Two independent complete DF0 cold starts in
`tmp/stage10-active-movement-full-a` and `-b` agree at Frame 60727 on
HSalive=FF, zero non-ROM exceptions and exact image hash
`55A83AB7998F391F10418C2FE4F8FB1E9A0853DF7F3D042EBE7B7E1379D02CA5`.
Both cold images were inspected and show active forest gameplay. Count
Stage 10 activation and movement as passed; Stage 10 itself remains open.
An additional checkpoint-only opening probe moves Right to X332/Y185
alive, then dies at X346 through `hero_coll` with MOB `$07FF80A4`.
Its MOtype byte `$1F` is decimal Type31 at Y185, moving left from
X379. Braking at X318 and pressing standing right-facing Fire kills
that MOB by Frame 60896; MOtype becomes 00 and flags later 40.
Holding the same Fire continuously is insufficient: a second decimal
Type31 at `$07FF8054` walks left from X400 and kills the stationary
hero at Frame 60967. Releasing Fire after the first kill and pressing
it again at Frame 60924 kills this second MOB by Frame 60959; both are
Type0/flags40 and the hero remains X319/Y185 alive through Frame
61044 after ten neutral 500,000-cycle intervals in
`tmp/stage10-double-type31-kill-stable.json`. This is a useful
prospective Stage 10 route, not a full-cold enemy-kill pass.
Continuing Right after those kills reaches X399/Y185 and is blocked
by a low wall; pressing Up there rises only briefly under the edge.
A Right+Up jump started earlier from X347 reaches the elevated
X417–459/Y137 step. MOB `$07FF8004` is another decimal Type31 at
Y137 and collides with direct Right at X461/Frame 61115. Braking on
the step at X426/Y137 and striking right kills it by Frame 61142;
ten neutral intervals hold the hero alive to Frame 61255 with all
three Type31 MOBs Type0/flags40. The durable route
`Tools/a3000-celtic-heart-stage-ten-opening-three-mobs-route.json`
passed two independent full DF0 cold starts in
`tmp/stage10-opening-three-mobs-full-a` and `-b`: X426/Y137,
HSalive=FF, all three Type0/flags40, zero non-ROM exceptions, exact
image hash
`58027EB5B75FA7B9F5D141EF0464013ECCCCFAB45EDA4005F334172EFB30D4C7`.
Both cold images show the active raised forest terrain and hero. Count
these three grounded Fire kills and the raised landing as passed.
Direct Right from the X426 ledge then dies almost immediately at
Frame 61257 against another decimal Type31 `$07FF7FB4`, which was
already at X439/Y137. The third Fire must be released early; after
five 500,000-cycle neutral intervals, a new standing right-facing
Fire at Frame 61220 kills this fourth MOB by Frame 61265. The
checkpoint-only `tmp/stage10-fourth-type31-kill-stable.json` holds
X426/Y137 alive through Frame 61360 with the fourth Type0/flags40.
The durable `Tools/a3000-celtic-heart-stage-ten-opening-four-mobs-route.json`
now passes two independent complete DF0 cold starts in
`tmp/stage10-opening-four-mobs-full-a` and `-b`: Frame 61360,
X426/Y137, HSalive=FF, all four Type31 MOBs Type0/flags40, zero
non-ROM exceptions and identical image hash
`9B90DD953C3A8C229ACECA48F7A89EFEEB6668BA51A97DFE18C349E943FF7F70`.
Both active cold images were inspected. Count this fourth grounded
Fire kill as passed; the remaining Stage 10 route is still open.
Right from that checkpoint crosses the raised step to X610/Y137 alive,
then descends toward a new active MOB `$07FF7EC4`: MOtype byte `$19`
is decimal Type25, moving left at Y185 from X704/Frame 61434 to
X664/Frame 61477. Direct Right dies X664/Frame 61479 via
`hero_coll`. An early jump from the raised step is the next concrete
Stage 10 input test; this is not a passed segment.
The short checkpoint-only jump from X610/Y137 clears this Type25 but
meets a second decimal Type25 `$07FF7E74` at X748/Y185. Releasing Up
before the first landing at X722/Y185 and immediately jumping again
passes both, reaching X842/Y185 alive at Frame 61568. Continued Right
dies X844/Frame 61569 against a further MOB `$07FF8324`. Identify
that MOB and a stable stopping/attack window before extending a full
route; no Type25 passage has yet been cold-replayed.
The next MOB `$07FF8324` is MOtype byte `$20`, decimal Type32, at
Y185 and moves left about two pixels per frame. Stationary Fire from
X814/Y185 does not stop it and the hero dies by Frame 61587. A
rearmed Right+Up jump from X814 passes over it alive to X948/Y185,
but continuing Right falls beyond the next ledge and dies around
X974/Y202. Checkpoint-only `tmp/stage10-edge-rejump-probe.json` now
rearms Up at X948 and reaches X1088/Y185 alive; another immediate
Right+Up from there reaches X1178/Y185. Beyond that, a decimal Type31
`$07FF7DD4` at X1232/Y185 kills direct Right at X1220. A third jump
from X1178 avoids it and reaches the raised X1244/Y137; standing
right-facing Fire kills the next decimal Type31 `$07FF7D84` by Frame
61818, and the hero holds X1244/Y137 alive through Frame 61881 with
that MOB Type0/flags40. These are checkpoint-only experiments, not
full-cold or visual passes. Right then stops at X1312/Y137 beneath a
higher step. A Right+Up jump reaches X1350/Y89 but meets decimal
Type25 `$07FF7C94` approaching from the right. Standing Fire there
did not kill it. A second jump from the stable X1404/Y41 upper ledge
passes over that Type25. The live Stage-10 `BgMap` pointer is
`$07C084C2` (map header `$07C084AA`, 256 columns x 16 rows). The
column-major words confirm solid tiles through X1472/Y112, then open
columns X1488–1631 above deadly `$48E4/$48EC` water; attempting to
continue the jump at X1553/Y152 dies by map contact. A separate
Type25 `$07FF7C44` patrols the upper ledge at Y41 and kills stationary
or right-moving approaches. Checkpoint images remain diagnostic only.

Braking the second jump at X1480/Y19 gives a sustainably alive landing
on the final fixed step X1480/Y89 (ten neutral intervals, Frame 62096).
Right+Up from there reaches X1570/Y98, where a moving platform catches
the hero; releasing Right carries him to X1615/Y98 at Frame 62184.
A rearmed Right+Up lands on the far bank X1668/Y89. Standing
right-facing Fire begun at Frame 62233 kills its approaching decimal
Type31 `$07FF7BF4` at Frame 62261. The durable
`Tools/a3000-celtic-heart-stage-ten-first-moving-platform-far-bank-route.json`
replays these real inputs from DF0 cold boot. Two independent complete
cold starts (`tmp/stage10-first-moving-platform-far-bank-full-a` and
`-b`) both reach Frame 62332, X1668/Y89, HSalive=FF, with the far-bank
MOB Type0/flags40, zero non-ROM exceptions and identical image hash
`F83648082326531618D3A6271FAE67CB13D71F7BFDD4FF9674B0EDB2C07E0D53`.
Both cold images show the hero on active forest terrain above the
far-bank Type31 corpse. The full route JSON validates. Count the two
Type25 and Type32 bypasses, the additional upper Type31 Fire kill,
the moving-platform crossing and the far-bank Type31 Fire kill as
passed. Stage 10 remains open. From X1668, Right reaches X1860/Y185;
ten neutral intervals keep the hero alive through Frame 62508. The
durable `Tools/a3000-celtic-heart-stage-ten-postbank-ground-route.json`
passes two independent complete DF0 cold starts in
`tmp/stage10-postbank-ground-full-a` and `-b`: X1860/Y185,
HSalive=FF, zero non-ROM exceptions, identical image hash
`A6A8C09C09406232F1F84D821A8A1D67FFCC5645B5ADF513B085E06CE9F8387B`.
Both active cold images show the living hero on the lower forest ground.
Count this descent and ground landing as passed. The next decimal
Type31 `$07FF7B04` patrols the lower ground and kills direct Right
at X2020; a Right+Up jump from X1992 clears that enemy but meets
another decimal Type31 `$07FF7A14` at X2126. A checkpoint-only
rearmed second Right+Up jump from X2084 clears both lower Type31
patrols, but its descent meets a third active MOB `$07FF7AB4` at
X2168/Y164. These exploratory jump controls did not pass the patrols.

A separate checkpoint-only timing probe at grounded X1992/Y185 resolves
the three-patrol collision without altering guest memory. The rightward
Type31 `$07FF7A64` turns back; initiating standing right-facing Fire at
Frame 62940 kills it by Frame 62986. Releasing and rearming Fire at
Frame 63000 kills the following `$07FF7AB4` by Frame 63032; another
release/rearm at Frame 63063 kills `$07FF7B04` by Frame 63092. The hero
remains X1992/Y185 with HSalive=FF and all three MOBs Type0/flags40
through Frame 63211 after ten neutral intervals in a checkpoint probe.
A direct two-jump timing control
cleared the first two patrols but collided with `$07FF7AB4` at
X2168/Frame 62662, while a late rearm cleared it and collided with
`$07FF7A64` at X2208/Frame 62683. These failures explain the need for
the grounded return-phase Fire sequence.

The checkpoint continuation advances right after those three kills.
Direct Right dies at X2120/Frame 63275 against decimal Type31
`$07FF7A14`; braking at X2084/Frame 63257 and starting grounded
right-facing Fire kills that MOB by Frame 63296. Continuing then
collides at X2196/Frame 63436 with decimal Type31 `$07FF79C4`.
Braking at X2140/Frame 63408, waiting eleven 250,000-chip-cycle
intervals for its return, and firing right kills it by Frame 63482.
The hero remains X2140/Y185 alive through Frame 63641 after ten
neutral intervals, with all five ground Type31 MOBs
`$07FF7A64/$07FF7AB4/$07FF7B04/$07FF7A14/$07FF79C4` Type0/flags40.
The durable
`Tools/a3000-celtic-heart-stage-ten-five-ground-type31-kills-route.json`
passes two independent complete DF0 cold starts in
`tmp/stage10-five-ground-type31-full-a` and `-b`: Frame 63641,
X2140/Y185, HSalive=FF, all five Type0/flags40, zero non-ROM
exceptions and identical image hash
`CFB3B7DA6F7601D76CEBC90F2ACC9DE1C1E092C030056A836FEEC755E40E7963`.
Both active cold images show the hero on forest ground beside the
tree and a defeated patrol. The route JSON validates. Count these
five grounded Fire defeats and the sustained passage to X2140 as
passed. Stage 10 remains open; continue from this living position.

Checkpoint-only continuation from X2140: Right reaches a low solid
wall and stops at X2272/Y185. The upper decimal Type25
`$07FF7974` patrols X2287–2352/Y153; immediate Right+Up dies
X2318/Frame 63745 through `hero_coll`. Waiting sixteen neutral
500,000-chip-cycle intervals, then jumping Right+Up, leaves that
patrol behind. A rearmed second jump climbs the next solid step;
braking at X2426 and releasing Right/Up lands stably X2428/Y121
through Frame 63996. Direct Right from that landing dies at X2436
via a map collision, while a newly pressed Right+Up crosses it.
Groundward travel to X2620/Y185 is alive only with a timely brake:
continued Right meets decimal Type31 `$07FF7834` at X2670, whereas
braking after 26 jump intervals keeps X2620 on fixed ground. A
grounded right-facing Fire begun at Frame 64119 defeats it by Frame
64154. Releasing and rearming Fire at Frame 64200 defeats the next
decimal Type31 `$07FF77E4` by Frame 64239. The hero remains alive
X2620/Y185 after ten neutral intervals through Frame 64334; both
new MOBs are Type0/flags40. These initial observations were
checkpoint-only; the full cold evidence follows.

Full-cold follow-up: `Tools/a3000-celtic-heart-stage-ten-upper-wall-landing-route.json`
now confirms X2428/Y121 at Frame 63996, HSalive=FF, zero non-ROM
exceptions and identical hash
`EC1FAF3DBF1B68DA3566499694EDA59C582992BA5C93C538F8E2CFEF1D05DD88`
in two independent full DF0 cold starts. Both active images show the
hero on the raised forest wall. The extended
`Tools/a3000-celtic-heart-stage-ten-upper-trap-twin-ground-route.json`
confirms a real Right+Up trap crossing and two more grounded Fire
defeats: decimal Type31 `$07FF7834` and `$07FF77E4` are both
Type0/flags40, while the hero remains alive X2620/Y185 at Frame
64334. Both independent full cold starts have zero non-ROM exceptions,
identical hash
`FC0F1D4C2E87714DA2BA62B40ACC373B9631303D6EEE0DDCCF0B85495AE1DC8C`
and reviewed active forest images. Both permanent route JSONs validate.
Count the raised wall, trap crossing and twin Fire kills as passed;
Stage 10 remains open.

Further checkpoint-only inputs from X2620/Y185 locate a map trap at
X2838: plain Right clears the hero's HSalive at Frame 64443 with a map
collision (`A5=$001658`). A Right+Up started at X2766 passes its active
phase. Continuing to X2854/Y185, releasing Up for 250,000 chip cycles,
then rearming Right+Up clears the active decimal Type32
`$07FF8284` on the lower ground. Braking at X3008/Y185 keeps the hero
alive for ten neutral 500,000-cycle intervals through Frame 64671;
the Type32 remains active. `tmp/stage10-far-type32-full-candidate.json`
validates. Two independent full DF0 cold starts in
`tmp/stage10-far-type32-full-a` and `-b` confirm Frame 64671,
X3008/Y185, HSalive=FF, zero non-ROM exceptions, identical hash
`505039100E2147345C41BD6A58EB8396F4D9A4CA3D3BF913C070D1A5A8860B2B`
and both reviewed active forest images. The permanent
`Tools/a3000-celtic-heart-stage-ten-far-type32-bypass-route.json`
validates with that end hash. Count the trap and Type32 bypass as
passed; the Type32 remains active and Stage 10 remains open. Next:
find a real-input passage over the low wall near X3008, then independently
repeat full DF0 cold starts and inspect active images before extending
the durable route.

Checkpoint continuation after X3008 — 2026-10-24: immediate Right+Up
meets decimal Type31 `$07FF7744` at X3048/Frame 64699. A neutral
phase watch shows it turning at X3024/Frame 64735 and then walking
right; the lower Type32 moves away to the left. Twenty-five neutral
500,000-chip-cycle intervals, followed by a Right+Up climb, reach
X3176/Y153. A standing Fire at Frame 64939 defeats returning Type31
`$07FF76F4`, and releasing/rearming Fire at Frame 64988 defeats
`$07FF76A4`. Another Type31 `$07FF7794` arrives before a third
stationary strike can finish. A short Right+Up, braked after four
250,000-cycle intervals, passes that active patrol and lands X3204/Y153.
Waiting for grounded control before Fire at Frame 65083 defeats
the returning first Type31 `$07FF7744`. The hero stays alive through
Frame 65241 and ten neutral intervals; all three defeated patrols are
Type0/flags40, while `$07FF7794` remains active. Pressing Fire in
flight and landing farther right were negative controls. These are
initially checkpoint-only findings. Two independent full DF0 cold
replays in `tmp/stage10-upper-three-type31-full-a` and `-b` now
confirm Frame 65241, X3204/Y153, HSalive=FF, all three defeated
Type31s Type0/flags40, and zero non-ROM exceptions. Both reviewed
active forest images have the identical hash
`BDD9609F41DB5105FCC080900A814BDCB0925B062DFF9F0573A39A91BAD8C82F`.
`Tools/a3000-celtic-heart-stage-ten-upper-three-type31-passage-route.json`
validates with that hash and the explicit live-memory guard. Count
this wall climb, three Fire defeats and fourth-patrol bypass as passed;
Stage 10 remains open pending the full completion route below.

Further checkpoint-only inputs from X3204: Right stops at X3312/Y153.
A Right+Up climbs to a sustained X3396/Y121, and another Right+Up
clears deadly FRONT-GID647 at X3440/Y112. Decimal Type28 `$07FF8144`
then follows the hero; waiting on X3564 or at the X3584 obstacle ends
in `hero_coll`. Releasing Up briefly and jumping again reaches
X3628/Y121 on the near bank. The existing source archive's
`level10.tmx`, `w3types.tmx` and `w3monster.tmx` identify the next
gap's moving platform as decimal Type24 `$07FF81E4` and its far-bank
projectile as decimal Type32 `$07FF8194`.

A braked Right+Up catches the Type24 platform at Y130. Jumping from
X3695/Frame 65783 clears the approaching Type32 and reaches the far
bank X3810/Y121/Frame 65840; waiting one more 500,000-cycle interval
before that jump instead collides with the projectile during ascent.
Do not stop at its far-bank spawn: waiting at X3812 dies at Frame
65877. Continuing Right, releasing/rearming Up near X3910, and
jumping toward BACK-TYPE7 at X4016–4063 enters the exit at
X4002/Y135/Frame 65936. The HSalive write comes from the map exit,
and the checkpoint image after 20 million neutral chip cycles visibly
shows LEVEL COMPLETE. A real Fire and another 20 million chip cycles
then show THE FOREST STAGE 2 / CODE MTTT at Frame 66529.

Full cold confirmation — 2026-10-24: both complete independent DF0
replays in `tmp/stage10-complete-stage11-title-full-a` and `-b` finish
successfully. Both reviewed completion images visibly show LEVEL COMPLETE
at Frame 66241, identical hash
`25914A75A97289C2C03803847C4ADC9CC33E02B344490304CE5723ACA4888C36`.
Both reviewed following title images show THE FOREST STAGE 2 / CODE MTTT
at Frame 66529, identical hash
`841464E315189298B09B54BB03C6AC7587908A472F657857C89D62BDCEFBF0C4`.
Neither full run observes a non-ROM exception. Count Stage 10 complete;
Stages 1–10 are now accepted. The permanent
`Tools/a3000-celtic-heart-stage-ten-complete-stage-eleven-title-route.json`
validates and includes both image guards plus the earlier living three-kill
RAM/image guard. This title is completion evidence, not a Stage-11 gameplay pass.

A short checkpoint continuation waits 40 million chip cycles on that title,
releases Fire, holds it for 500,000 cycles and releases it again. After
20 million cycles the reviewed image shows GET READY. A second Fire
activates play; an additional release/rearm and a brief Right move reach
X44/Y185, HSalive=FF at Frame 67778 in that checkpoint probe, with a visible
hero. Its hash is not accepted as a full-cold reference. The validated
`tmp/stage11-active-movement-full-candidate.json` appends those actual input
edges to the accepted full route and checks the living final RAM. Two new
independent full DF0 cold replays run with the current A1000-bus development
core in `tmp/stage11-active-movement-full-a` and `-b` (sessions 98076/94707),
capturing GET READY and final movement once each. Next: inspect both final
RAM states, matching hashes, zero non-ROM exceptions and both active images
before promoting a Stage-11 gameplay route. Stage 11 remains open.

### Optional Zorro III RAM bring-up — 2026-10-22

A development-only single Zorro III memory board can now be attached to a
32-bit desktop model through `AmigaModel.WithDevelopmentZorroIIIRam` or the
GameRunner scenario field `zorroIIIRamMiB` (8, 16 or 32). It presents native
Autoconfig at `$FF000000`, accepts the guest's 32-bit base assignment and
maps a distinct, cacheable RAM array there. Reset returns the card to
Autoconfig, power cycle clears its RAM, and a version-18 system savestate
checks the installed card size and restores its configuration and contents.
The previous model selector remains unchanged. The nibble/register layout
follows the [Commodore A4000/A3000 Hardware Developer Notes v1.11](https://amiga.net.au/files/Tech_Amiga/Commodore_A4000-A3000_Hardware_Developer_Notes_v1.11_ocr.pdf).

Unit tests cover 8/16/32 MiB native configuration, 32-bit non-aliased
read/write boundaries, cacheability, reset, shut-up, restore and rejection
on a 24-bit A500. A real A3000/Kickstart 3.1 cold boot with an 8 MiB board
reached the normal visible disk-insert screen at frame 422, hash
`4D01745C0CFA0218661EAA0C88D915FBDD3C7BC5943102362DC327D56E5B3FF7`,
with the guest assigning `$40000000`, 814 board reads, 62 writes and zero
non-ROM exceptions. An independent A4000/030/Kickstart 3.1 cold boot also
assigned `$40000000`, performed 794 board reads and 62 writes, reached its
normal visible disk-insert screen at frame 422, hash
`9192DD4A048E00810E72F976C7B658AA50B8051C10439AAB5283DF9DD1BD0733`,
with zero non-ROM exceptions. Brief 16/32 MiB A3000 boots also assigned
the same 32-bit base and accessed the card, but are not long-run or OS
acceptance. These are native guest Autoconfig checks, not installed-OS
memory enumeration or multi-card/IRQ/DMA/timing conformance. The earlier
compatible `$E80000` experiment produced a red expansion diagnostic and
was discarded; only the native path is retained.

The requested optional 68881/68882, independent A4000/040 CPU/MMU/FPU,
and A1000 profile are still absent. The unmodified A1000 needs its own
bootstrap ROM and writable control store rather than an A500 identity.
The decoder described below now has explicit CPU/bus development wiring,
but is not a complete motherboard or selectable profile. The
local ROM directory has Kickstart 1.x images but no separate A1000 bootstrap
image, so a native cold-boot acceptance test cannot yet be claimed. The
[Commodore A1000 component-level repair manual](https://amiga.net.au/files/Tech_Amiga/Commodore_Amiga_Computer_Model_1000_Assembly_and_Component_Level_Repair.pdf)
documents the physical bootstrap ROM and WCS hardware. Next:
exercise the opt-in card with installed AmigaOS on a disposable private HDF
copy and verify guest memory enumeration, then implement multi-card Zorro III
arbitration. Independently implement and test the A1000 bootstrap/WCS path,
external FPU instruction state/exceptions, and a distinct 68040 core before
making those models available.

### A1000 bootstrap/WCS decoder component — 2026-10-24

`A1000ControlStore` now models the separate upper-memory decoder:
an explicitly supplied 8 KiB logical or 64 KiB physical bootstrap image
is visible in `$F80000–$FBFFFF`, while the independent 256 KiB WCS
can be filled at `$FC0000–$FFFFFF`. A write anywhere in the bootstrap
aperture hides it and locks the WCS; the lower range then reads the
WCS mirror. The triggering write does not overwrite that mirror.
RESET reopens the decoder while retaining WCS contents; power cycle
also clears the volatile WCS. This behavior was cross-checked against
the original [MAME A1000 decoder and reset implementation](https://github.com/mamedev/mame/blob/master/src/mame/amiga/amiga.cpp)
and [WinUAE A1000 WCS handlers](https://github.com/tonioni/WinUAE/blob/master/memory.cpp).
No emulator source code or proprietary ROM bytes were copied.

Three focused regressions pass: complete WCS load/mirror/protection
boundaries and bootstrap ownership; reset versus power-loss behavior;
and locked/writable state restore, truncated payload rejection, invalid
latch rejection and bootstrap-identity mismatch without changing live
memory. Only synthetic bootstrap bytes are used. The project builds;
the NuGet vulnerability service was unavailable in this environment,
producing NU1900 warnings without build errors.

The subsequent CPU/bus integration below supersedes the initial standalone
limit. A native disk-loaded Kickstart boot and complete profile remain open;
the separate bootstrap image is still missing locally. No A1000 entry was
added to the normal machine selector.

### A1000 decoder CPU/bus integration — 2026-10-24

`AmigaSystem.LoadDevelopmentA1000Bootstrap` explicitly attaches the decoder
to an unexpanded 256/512 KiB OCS/68000 test board. It replaces the ordinary
ROM decoder for high-window reads, reset vectors, byte/word/long reads and
side-effect-free CPU planning peeks. CIA-A still controls the independent
low-memory overlay. WCS writes reach its separate RAM only while unlocked;
the high-window bootstrap write closes the latch. Loading an ordinary
Kickstart detaches the development decoder. Incompatible ECS/AGA/expanded
boards reject attachment before changing ROM state.

A synthetic 68000 guest now loads WCS vectors and executable code through
real `MOVE` instructions, releases the CIA overlay, writes a Chip RAM
trampoline, closes the WCS latch from that trampoline and jumps into the
loaded code. That code writes decimal 42 into physical Chip RAM and stops.
Both direct instruction stepping and the normal chip-cycle/deferred-bus
path pass on 256 and 512 KiB test boards. Warm reset exposes the bootstrap
again while retaining WCS; power cycle clears it.

The development board also receives the privileged CPU `RESET` output,
ordered after the opcode fetch and outside instruction planning. It resets
external board devices and reopens WCS without resetting CPU registers or
fetching CPU vectors. A stalled opcode fetch and a suspended savestate retain
that ordering. A user-mode `RESET` takes privilege vector 8 and leaves the
decoder untouched. Existing normal boards retain their previous RESET path.
The external reset behavior was checked against the primary
[MAME Amiga reset implementation](https://github.com/mamedev/mame/blob/master/src/mame/amiga/amiga_m.cpp).

Development decoder machine states use version 19 and include the bootstrap
SHA-256, latch and WCS before bus RAM. Missing/different decoder or bootstrap
identity is rejected before changing live CPU/RAM. Ordinary boards continue
writing version 18, preserving their released state layout. This remains an
explicit development API on the existing OCS test board, not a public A1000
machine identity or UI selection.

All six focused WCS/guest/reset/restore regressions pass. The full .NET
suite passes **375/375**, and the current GameRunner preflight suite passes
**28/28**. Builds succeed with the existing NU1900 warning because the NuGet
vulnerability endpoint is unavailable. Current-core real cold boots also
reach their reviewed normal disk-insert screens with zero non-ROM exceptions:
A500/Kickstart 1.3 at frame 422, hash
`6928024658CAE55B754ADBA5C62BCCB1952F98114BF398227A575C01BCD4A8C8`,
and A3000/Kickstart 3.1 with the opt-in 8 MiB Zorro III board at frame 422,
unchanged reference hash
`4D01745C0CFA0218661EAA0C88D915FBDD3C7BC5943102362DC327D56E5B3FF7`.
These are existing-board boot regressions, not an A1000 boot claim.
Compact evidence is in `tmp/a1000-bus-full-regression-final.log` and
`tmp/a1000-bus-native-boot-regression-output`; neither test used an installed
HDF. Next: finish native A1000 board identity and PAL decoding, then boot
an actual user-supplied bootstrap and disk-loaded Kickstart before offering
that model. Optional FPU and distinct 68040 remain unimplemented.
