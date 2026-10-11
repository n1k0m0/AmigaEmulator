# Local game/model compatibility audit — 2026-10-09

The requested preceding version was committed and pushed as `464ce54` on
`origin/main`. The full-library follow-up is separate work in progress.
**Executing a scenario is not a gameplay, graphics or compatibility pass.**

Latest checkpoint (2026-10-11): actual 020/030 caches and corrected LOF are
implemented; 332 tests pass. Fresh bounded Tinyus input/restore coverage
now includes all eight boards. The native desktop Pinball source failure is
traced to a 24-bit tagged loader pointer; a separate repaired copy now passes
native A3000/A4000 Partyland gameplay. See the dated follow-up below and
MachineModels.md for exact evidence; complete-library acceptance remains open.

## Coverage and profiles

`Tools/game-media-catalog.json` assigns all 50 local ADFs to 30 named games
and the SELECTOR utility. The formerly unidentified `PDREMS21/22/23` set is
positively identified as Pinball Fantasies by both native table-selection screens.
SELECTOR is excluded from game acceptance. The default matrix therefore has
30 disk-set entries × eight selectable boards = **240 cases**, covering 49
game ADFs. The stable scenario ID remains `PDREMS21`; the catalog records the
actual display name.

| Model | ROM | Matrix RAM |
| --- | --- | --- |
| A500 | KICK13.ROM | 512 KiB Chip + 512 KiB Slow; Siedler/Cannon Fodder retain their sources' 1 MiB Chip preference |
| A500+ | KICK20.ROM | 2 MiB Chip |
| A600 | KICK31.ROM | 2 MiB Chip |
| A2000 OCS | KICK13.ROM | 512 KiB Chip + 512 KiB Slow |
| A2000 ECS | KICK31.ROM | 2 MiB Chip |
| A1200 | KICK31-A1200.ROM | 2 MiB Chip + 4 MiB Fast |
| A3000 | KICK31-A3000.ROM | 2 MiB Chip + 4 MiB motherboard Fast |
| A4000/030 | KICK31-A4000.ROM | 2 MiB Chip + 4 MiB motherboard Fast |

This covers all exposed model variants with native ROMs and selected supported
RAM layouts. It is not an exhaustive sweep of every RAM/ROM combination or a
claim of compatibility with unimplemented A4000/040 hardware. A2000 OCS cannot
use the A500's 1 MiB Chip profile; an initial generated case mistakenly requested
it and is explicitly superseded by `a2000ocs-siedler-valid`.

The script refuses existing output directories, checks all selected boot/data
disks, records SHA-256 media inventories, and warns about uncovered images.
GameRunner validates board/RAM pairs and input actions before starting a sweep.
Its `--validate` mode creates no output or emulator instances. Success writes
`executed-unreviewed`; actual model metadata comes from the running machine,
including savestate continuations. The matrix no longer appends a universal
Fire press after restore: in some games it opens a pause/menu screen rather
than exercising gameplay. Title-specific scripts control Fire.

## Confirmed emulator fix: missing Boxx 4 playfield

Before the fix, the A500 showed its level but ECS and native A3000 cases showed
only the blue raster background and status strip. Longer waiting did not repair
this. Inspection found the same nonzero image words in the guest's bitplanes;
the data was present rather than lost by a blit or disk read.

The game's Copper opens the display, later moves DIWSTOP behind the current
beam, and writes DIWHIGH at line 64. Our renderer used the instantaneous
`top <= y < bottom` rectangle and incorrectly discarded an already-open
vertical window. Agnus now samples vertical start/stop comparators at physical
scanline boundaries. A passed stop cannot retroactively close the latch;
fixed PAL vertical blank resets it, and coincident start/stop gives stop
priority. The latch survives new snapshots. Old snapshots clear this new state
instead of inheriting a previous machine's open window, then converge when
the following field reaches the comparators.

This is an independent register/timing correction, without game-name detection
or forced bitplane enables. Register definitions are in the Commodore
[Hardware Reference Manual](https://amigadev.elowar.com/read/ADCD_2.1/Hardware_Manual_guide/node002E.html).
The retained-window and stop-priority interpretation was cross-checked against
the independently maintained [WinUAE implementation](https://github.com/tonioni/WinUAE/blob/master/custom.cpp).
Sub-line comparator timing and programmable blanking remain outside this fix.

The regression fails on the old core and passes on ECS/AGA. It covers a stop
moved behind the beam, restore while open, vertical-blank closure, reopening,
a future stop, coincident comparators and legacy-state loading into a used
machine. The standard suite plus local Siedler frame/menu/mouse regression has
**235 passes, zero failures/skips**. Turrican III and both North & South GoldenADF
checks also pass. The optional Amiga Test Kit fixture is absent and cannot count
as a pass. Boxx cold boots and input/restore continuations are retained separately
from the pre-fix captures; the full final-core sweep is still being reviewed.

## Corrections to earlier gameplay evidence

Tinyus's newer-Kickstart/68000 black phase is **not established as a permanent
hang**. On the fresh A600 state, waiting another 6 + 20 million chip clocks
restores visible enemies without another Fire or restore. A paired replay
with/without restore retains identical complete CPU registers, Chip RAM and
frame hashes. Restore was therefore not shown to cause that transition.
Further Fire/movement can produce another blank phase on A500+/A600; the
intermittent behavior remains open. The game intentionally writes BPLCON0
`$0200` during the dark phase. Do not force planes on to hide it. The Tinyus
scenario now retains that early evidence and adds 30 million clocks without
input before the later control check.

The first native A600 Siedler capture was gray because its loader had not
finished. An additional 120 million clocks reaches a blinking Disk-2 request.
Its blank phase was initially mistaken for missing menu contents. The text is
present in the other blink phase; this was an incomplete test sequence.
After supplying Disk 2, confirming with left mouse and later clicking through
the animation, all six larger-memory profiles reach the symbol screen, main
menu and mission playfield. A500/A2000 OCS reach the playfield directly through
their smaller-memory path. All eight respond to field selection and restore.
`model-gameplay-variants.json` retains the additional disk sequence for the six
profiles. All eight fresh cold boots of the corrected source sequences now
reach the mission field. Continuing those fresh states with the construction
menu builds the first castle on all eight boards, followed by live game frames
and the complete-state restore check. The common source sequence retains it.
This verifies the tested Disk-2 transition without reproducing a host crash; it
does not close the user's earlier, exact intro-disk swap report without its state.
The A500 Siedler RAM preference also differs from the initial blanket profile.

Generic Space/Fire scripts can pause games or leave them in menus. Actual
per-title continuation work has reached Speedball 2 Practice with movement,
fire and restore; Lemmings level loading; Civilization input selection and
intro; North & South language/main menu; Lotus track/race setup; and several
multi-disk requests. These stages must not be promoted to complete gameplay
passes without the remaining input and visual checks.

## Durable evidence and remaining acceptance

The cold matrix has completed all **240 cases**. Its one configuration failure
(`A2000Ocs-Siedler`, unsupported 1 MiB Chip layout) is retained and superseded
by the successful supported-layout replacement. This gives execution evidence
for all 240 intended combinations, **not 240 compatibility passes**. The old
baseline was stopped after 210 completed cases once superseded by this sweep.
All 50 original ADF SHA-256 hashes were checked again: none changed or vanished.

### Second emulator fix: Fred's Journey AGA raster corruption

Both A1200 and A4000/030 cold captures showed extensive noisy rows in the actual
level; the ECS profiles displayed it correctly. FMODE was zero, and reducing
Fast RAM did not provide an acceptable fix. The 16-bit AGA path now shares the
existing legacy complete-group latch/pointer progression; 32/64-bit transfers
retain their separate wide-group implementation. The same saved game states
now render cleanly with movement and restore. Two reviewed frame-hash replays
fail on the preceding core and pass on the corrected core. All 235 standard
and local Siedler regressions still pass, including AGA 8-plane and wide DMA.
All full CPU debug states in paired old/new replays match; the corrected
pixels differ. Native AGA Fred/Boxx cold boots were repeated on the final core.
All 60 A1200/A4000 library states were replayed with input and restore on the
new core and their contact sheets reviewed. Unfinished menus/loaders remain
unfinished; this replay is not a substitute for entering their gameplay.

The developer explicitly lists ECS/AGA support for
[Fred's Journey](https://www.retroguru.com/freds-journey/), so this corruption
must not be dismissed as an unsupported AGA game.

### Speedball 2: verified disk-version CPU restriction

The tested `Speedball 2.adf` has SHA-256
`80BB65BC35FA00D50DEB8859C33ECFD47F9DC178F62F2FCC89A0392C8E951627`.

Practice, direction/fire input and restore are visible on all five 68000
profiles. The same continuation breaks on A1200, A3000 and A4000/030. Copylock
trace processing itself reaches the same final checksum/data registers on
A500 and A1200; the initial suspicion of a missing format-two trace frame was
incorrect (that frame already exists and is tested).

The next Line-F call uses a disk-loader handler at `$8518` containing
`ADDQ #2,A7; ADDQ.L #2,(A7); RTS` (`544F 5497 4E75`). It consumes a six-byte
68000 exception frame and leaves the 020/030 format/vector word on the stack.
The later return then reaches invalid code. Paired instruction traces show
the A500 returning to `$836C` and the A1200 subsequently executing at `$000002`.
This is a concrete incompatibility of this local disk version with 020/030;
do not shorten real CPU frames or silently change the selected processor.
A compatible patched disk/WHDLoad version needs its own acceptance test.
The [WHDLoad maintainer's notes](https://whdload.de/games/Speedball2.html)
also document distinct original/budget versions and AGA-fix history; they do
not certify this local ADF.

### Reviewed stages by model

These are the furthest directly reviewed stages, not a list of certified
games. `L` = actual level/arena/race visible with scripted inputs; `I` = intro,
title or attract sequence; `M` = trainer/input/selection menu; `D` = disk
request; `W` = loader/black/gray phase still requiring diagnosis; `X` = the
verified local-media processor restriction (Speedball 2 or Moonstone). `*` marks an additional open graphics or
transition concern. A visible level is not proof of every control, restart,
disk swap, subsequent level or long-term compatibility.

| Disk set | A500 | A500+ | A600 | A2000 OCS | A2000 ECS | A1200 | A3000 | A4000/030 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Alien Breed SE92 | L | L | L | L | L | L | L | L |
| Black Strawberry Cake | L | L | L | L | L | L | L | L |
| Boxx 4 | L | L | L | L | L | L | L | L |
| Cannon Fodder | L | L | L | W | L | L | L | L |
| Celtic Heart | L | L | L | L | L | L | L | L |
| Civilization | L(d) | L(d) | L(d) | L(d) | L(d) | L(d) | L(d) | L(d) |
| Desert Strike | D | M | M | M | M | M | M | M |
| Fire and Ice | L | L | L | L | L | L | L | L |
| Fred's Journey | L | L | L | L | L | L | L | L |
| Hostages | L | L | L | L | L | L | L | L |
| Lotus | L | W | W | L | W | W | W | W |
| Maniac Mansion | L | L | L | L | L | L | L | L |
| Mega-lo-Mania | I | W | W | W | W | W | W | W |
| Moonstone | L | L | L | L | L | X | X | X |
| North & South | L | W | W | L | W | L | W | L |
| Oh No! More Lemmings | L | L | L | L | L | L | L | L |
| Pinball Fantasies | L | L | L | L | L | L | W | W |
| Die Siedler | L | L | L | L | L | L | L | L |
| Solid Gold | L | L | L | L | L | L | L | L |
| Speedball 2 | L | L | L | L | L | X | X | X |
| Sqrxz | L | L | L | L | L | L | L | L |
| Sqrxz 2 | L* | L* | L* | L* | L* | L* | L* | L* |
| Sqrxz 3 | L | L | L | L | L | L | L | L |
| Sqrxz 4 | L | L | L | L | L | I | I | I |
| Tiny Bobble | L | L | L | L | L | L | L | L |
| Tinyus | L | W* | W* | L | W* | L | L | L |
| Trap Runner | L | L | L | L | L | L | L | L |
| Turrican III | L | L | L | L | L | L | L | L |
| Wrong Way Driver | L | L | L | L | L | L | L | L |
| Zak McKracken | L(d) | L(d) | L(d) | L(d) | L(d) | L(d) | L(d) | L(d) |

`L(d)` requires the reviewed data-drive layout before boot: Civilization uses
Civ1–Civ4 in DF0–DF3; Zak uses Zak1 in DF0 and Zak2 in DF1. Civilization has
map/setup/city inputs on all eight native models. Zak's eight native models reach the bedroom
and move the character with mouse clicks. These do not certify single-drive
swaps or every later resource/mission. See the newest evidence below.

The Siedler row includes reviewed continuations of the fresh native cold boots,
with the requested Disk 2 on the six larger-memory profiles. Replays show menu,
mission terrain, mouse selection and construction of the first castle; this
is not a completed mission or a long populated-settlement stress test.
The separate A500 wood-frame/menu/mouse golden save also passes.
Lotus's identical continuation
reaches racing on A500/A2000 OCS and a guest-programmed planes-off phase on the
other profiles. That distinction remains open; it is not automatically a
renderer fault or established hardware incompatibility. Space in the old
generic scripts pauses Maniac Mansion/Zak and is not a valid universal input.
Alien's first Disk-2 swap was premature and invalid. Respecting the later request,
confirming with joystick Fire and allowing the lengthy data load reaches its
title, then the level with movement, shooting and full restore on all eight
native boards. All eight fresh source cold replays are completed and reviewed. Lemmings now shows spawned
figures, an advancing timer, mouse input and restore on all eight boards, both
from the original menu checkpoints and from fresh cold boots of the updated
source sequence. Maniac Mansion now has controlled Dave walking and complete-state
restore on all eight boards. The corrected source and model-specific character
selection/Start actions pass eight fresh native cold boots. Turrican III also
passes eight fresh source cold boots into its level, with Disk 2 in DF1, Fire,
continued frames and input/restore. Neither result is a complete playthrough.
Hostages reaches its mission scene on all eight native boards, including two
additional menu/input continuations on A600/A3000. Eight fresh source cold boots
now confirm the mission scene and full-state restore. Some short runs reach the
scorecard after player death; this is not mission completion. Each run has one
intentional DIV0 protection exception.

Fire and Ice now reaches its first icy level on all eight native boards. Sixteen
paired continuations use identical total cycles: Left+Fire versus no input.
Reviewed frames show camera/world movement, coyote/projectiles and different
pixel hashes on every board; all restores pass. The main-thread BRA loop at
$005020 is normal while game interrupts continue, not evidence of a hang. Its
TRAPV decryption exceptions are also intentional. The full sequence is retained
in the source; all eight fresh cold replays now reach the level with input and
full-state restore. Evidence: `fireice-cold-verified/`.

North & South reaches controlled campaign/unit selection on the two OCS boards,
A1200 and A4000/030. The native A3000 map was a stale display: a Line-F exception
at `$02030406` already occurred at frame 5734, before the input continuation.
A subsequent click/continuation returns to the publisher/language screens. It
must not count as gameplay. An instruction ring traces the music command dispatch
using a Chip-memory table to that invalid target; the cause of the data/address
is still open. The three 68000/ECS native-ROM cases also fault while loading:
A600/A2000 ECS at `$030406`, and A500+ at `$030408`.

Cold profile controls and subsequent input/restore prove campaign/unit selection
on A600/A2000 ECS with Kickstart 3.1 and 2 MiB Chip + 2 MiB Fast. A500+ succeeds
with that memory layout and Kickstart 3.1; its native 2.0/2 MiB Fast control still
fails. Kickstart 1.3 controls also reach the campaign on A600/A500+. Chip-only
Kickstart 3.1 fails even on A500 OCS, while its 512 KiB Chip + 512 KiB Slow control
works. These results isolate a ROM/memory interaction, without establishing its
underlying cause or silently changing the selected CPU. Explicit additional
profiles are retained in `Tools/game-compatibility-profiles.json`; the optional
`-IncludeCompatibilityProfiles` adds them alongside the 240 default cases
(244 total), preserving the failing defaults. Eleven fresh source cold replays
confirm the four failing native cases and the seven working native/alternate
campaign paths. A separate fresh A3000 Kickstart-1.3 case reaches the campaign,
responds to unit input and passes full-state restore without instruction faults.
This diagnostic flat-ROM mode retains the 68030 and is not a SuperKickstart
bootstrap or installed-OS/SCSI certification. Thus all eight boards have a
reviewed campaign path with four explicit compatibility profiles; the native
ROM/memory failures remain open. The generated 244-case manifest passes
preflight, which is not 244 gameplay passes.

Cannon Fodder's Crystal intro responds to Return and the later trainer screen
to left-click. The tested A500+ / A600 / A2000 ECS and A500 1-MiB-Chip configurations
reach the game's cast intro. The 512-KiB-Chip A500 continuation instead reaches
an unmapped `$080000` Line-F fault. A500 with 512-KiB Chip/512-KiB Slow/2-MiB
Fast reaches the cast intro; the corresponding A2000 OCS run remains black.
Five subsequent Disk-2 mission attempts clear the request but stay black, without
new instruction exceptions. The Sensi filesystem is present on all three disks;
Disk 3 contains startup/mission graphics. Alternative Disk-3 and three-drive
controls are being checked. Actual mission entry remains open. Desert Strike's requested-disk phase remains unresolved;
its disk headers identify EOA0/EOA1/EOA2 and are preserved. No waiting phase is
automatically classified as emulator corruption or permanent incompatibility.

## Complete-state restore audit

The runner now compares serialized CPU state and serialized bus state, including
Chip/Slow/Fast memory, ROM overlay and floppy state, as well as beam timing and
presented pixels. All **240** current game/model continuation cases pass this
stronger round-trip after settling one million chip clocks with the current core.
Lemmings uses its fresh level states and the two classic Siedler cases use their
playfield states. This is a restore result, not proof that all 240 states contain
playable games. Both Fred AGA pixel goldens also pass with these stronger checks.
Invalid numeric controller bits fail preflight; supported combined buttons pass.

Fresh outputs, runner binaries, manifests, inventory hashes, fault traces,
step logs, captures and continuation saves are under:

`%APPDATA%/AmigaEmulator/diagnostics/AllGamesModels-20261009-bdfaa5f7/`

- `native/`: initial 248-case run (includes eight SELECTOR utility cases).
- `final-native/`: 240-case final-core run; the invalid A2000 OCS Siedler RAM
  case is retained as a failure, and its valid replacement is separate.
- `a2000ocs-siedler-valid/`: supported-layout replacement.
- `boxx4-copper/`, `boxx4-inspect.txt`: register/data isolation.
- `boxx4-fixed/`, `boxx4-cold/`: targeted post-fix input/restore and cold boots.
- `advance*`, `freeware-restart/`: reviewed title/menu/disk continuations.
- `aga-replay/`, `aga-cold-final/`, `freds-golden-comprehensive/`: post-fix AGA
  controls, fresh Boxx/Fred levels and the Fred pixel references.
- `speedball-loader-a500.txt`, `speedball-loader-a1200.txt`: the disk version's
  incompatible six-byte exception-frame handling.
- `lotus-all-models/`: the reviewed two-model race / six-model planes-off split.
- `lemmings-models/`, `lemmings-cold-verified/`: eight-model live levels and
  eight fresh cold boots with corrected mouse/menu/pause sequences.
- `siedler-required-disk/`, `siedler-left-transition/`, `siedler-finish-games/`,
  `siedler-classic-gameplay/`: requested-disk, symbol, menu and playfield evidence.
- `siedler-cold-verified/`, `siedler-cold-construct/`: eight fresh source cold
  boots and eight first-castle constructions/complete-state restores.
- `maniac-native-gameplay/`, `maniac-ready-prepare/`, `maniac-ready-grid/`:
  character and Start input corrections, opening scenes and player verb panels.
- `northsouth-models/`: eight requested menu/GO sequences; five campaign maps
  and three still-unresolved guest loader faults.
- `maniac-controlled-all/`, `maniac-a600-reclick/`: eight-board walking input.
- `maniac-turrican-cold-verified/`: 16 fresh source cold boots into both games.
- `hostages-missions/`, `hostages-menu-input/`, `hostages-cold-verified/`:
  mission/input continuations and eight fresh source cold boots.
- `northsouth-profile-controls/`, `northsouth-memory-controls/`,
  `northsouth-plus-rom-controls/`, `northsouth-compatible-controls/`:
  native ROM/RAM isolation and three explicit compatible configurations.
- `northsouth-source-native-and-compatible/`: combined cold/input source replay.
- `northsouth-fault-ring.txt`, `exception-observation-controls/`: A3000 stale-map
  correction and positive/negative controls for recording non-ROM exceptions.
- `canfod-return-controls/`, `canfod-confirm-trainer-*`, `canfod-intro-continue/`:
  intro/trainer transition and RAM controls; later mission evidence below.
- `trainer-game-transitions/`, `trainer-followthrough/`, `disk-left-confirm/`:
  corrected confirmation buttons; A500 Fire and Ice icy level.
- `restore-matrix/`: 240 complete CPU/bus/beam/pixel round-trips.
- `tinyus-investigation/`, `restore-probe.log`: recovery and determinism checks.
- `vertical-window-before.log`, `regression-complete.log`: failing-before and
  full passing regressions.

All 50 original ADF SHA-256 hashes were rechecked unchanged on 2026-10-10.
Original ADFs are read-only, and original ROMs, saves, HDFs and user settings
are preserved. No additional commit/push was performed for the follow-up.

Next steps: replay the corrected menu/disk sequences on all boards, prove controlled
gameplay after restore, and isolate any remaining defects with failing fixtures.
In particular, Tinyus intermittent transitions, Sqrxz text against references,
the Pinball Fantasies disk/table loader, and the older generic scripts remain open.
The audit and the approved overall hardware roadmap are **not complete**.

### Further review evidence (continuation)

Within the existing diagnostics root: `hostages-cold-verified/` (eight fresh
missions), `northsouth-source-native-and-compatible/` (eleven fresh runs),
`northsouth-a3000-profile-controls/` (the Kickstart-1.3 case only),
`fireice-native-level/` and `fireice-input-pairs/` (eight levels and sixteen
matched controls), and `canfod-disk2-mission/` (five unresolved transitions).

The official Speedball 2 WHDLoad patch/runtime was installed on a separate new
32-MiB test HDF, then copied independently for A1200/A3000/A4000-030. All three
attempts fail before gameplay (WHDLoad exception/black screen/SAD). CPU/FPU/MMU
feature probes must be distinguished from the later fatal instruction faults.
This does not certify the patch or identify the root cause: the local cracked ADF,
slave selection/protected-track handling and CPU exception behavior need further
controls. User HDFs, original game disks and settings were not modified. Evidence:
`whdload-patches/`, including `speedball2-patch-boot.json` and its three outputs.

The next Cannon Fodder control succeeds with Disk 1 in DF0, Disk 2 in DF1 and
Disk 3 in DF2: all eight model continuations ran, and reviewed Mission-1 terrain
with squad/mouse input is present on A500 (1-MiB Chip), A500+, A600, A2000 ECS,
A3000 and A4000/030. A1200 with Fast RAM and A2000 OCS/Fast remain black, not
accepted. Early-data-drive cold controls and AGA Chip-only controls are running.
No native CPU was replaced. Evidence: `canfod-disk3-controls/`,
`canfod-three-drives-mission/`, `canfod-all-models-drives/`.

Tiny Bobble reaches Round 1 with player/enemy/bubble sprites and input/restore
on all eight native boards in `freeware-level-controls/`. The corrected source
flow passes eight fresh native-board cold replays, all with input/restore.
Evidence: `tinybobble-cold-verified/`. Sqrxz 4 still
ends at title/story in these controls and needs its start/restart flow resolved;
intermediate old 68000 gameplay evidence remains distinguished from the new menu
endpoints. All restores passed.

Lotus has a sustained level-2 interrupt loop on the native A1200 checkpoint: the
keyboard handler reads CIA-A ICR with serial bit clear and branches past its
PORTS acknowledgement. A Kickstart-1.3 A500+ cold control reaches the split-screen
race with steering/acceleration/restore. Its A1200 counterpart still fails,
including illegal instruction faults. This narrows a ROM/CPU interaction but does
not identify the root cause or certify the untested alternate boards. Evidence:
`lotus-a1200-wait-loop.txt`, `lotus-a1200-registers.txt`, `lotus-kick13-controls/`.

The identical WHDLoad Speedball fatal Line-F at $07FFBC also occurs in a fresh
A600/68000 control; it is not specific to the 020/030 CPU. Patched-media/slave
compatibility remains unproven. Multiple-drive controls for Alien Breed, Desert
Strike and Zak still show their explicit disk request; the Cannon workaround
is not assumed to apply universally. Evidence: `multi-drive-open-games/`.

Cannon Fodder reaches Mission 1 with squad inputs and exact restore in all
nine completed source cold cases except A2000 OCS: seven native model profiles
and the separate A1200 Chip-only profile pass their visual review. The native
A1200 Fast-RAM cold run also succeeds; its earlier black warm continuation is
not a hardware incompatibility proof. The source now retains three data drives
and the preferred A500 one-MiB Chip setup. Five optional profiles generate 245
cases, including the still-valid additional A1200 Chip-only control. A500
512-KiB-Chip/Fast and A2000 OCS controls remain unresolved; an impossible
one-MiB-Chip A2000 OCS profile is not offered. Evidence:
`cannon-source-native-and-compatible/`, `canfod-aga-chip-only/`,
`canfod-ocs-early-drives/`, `review/cannon-pinball-complete.png`.

Pinball Fantasies is positively identified: Disk 2 selects Partyland / Speed
Devils, Disk 3 selects Billion Dollar Gameshow / Stones and Bones. Both requested
data-disk swaps in DF0 reach their menus on A1200. Five classic boards and the
native A1200 reach Partyland; F1 selects the table and F1 again starts one player.
Down charges the plunger and both Shift keys control the flippers. The full
reviewed flow is retained in the source manifest. Native A3000 and A4000/030
with motherboard Fast both fault at $200002/$FFFF while entering the table,
then reboot to the ROM display; they are not gameplay passes. Fresh Chip-only
cold controls are running, retaining native CPU, chipset and ROM. Evidence:
`pinball-all-models/`, `pinball-controlled-fixed/`, `pdrems-course-controls/`.

Pinball's dot-matrix text exposed a shared bitplane DMA bug: alternating
zero-plane lines reloaded stale pointer registers instead of retaining the
completed fetch/modulo address. Resuming from zero planes also incorrectly
forced a raster-split reload. Both are fixed without adding a memory alias or
changing CPU timing. The independent OCS/ECS/AGA FMODE 0/1/3 regression fails
against the preceding assembly and passes with the fix. The reviewed real-game
frame hash is `4815C167FB6E6946DA18EF0CC22F541FA5101FBEF7AD61445D665AE2F90DC50C`;
the identical input/state replay fails that golden before the fix and passes
afterward, including full restore. Serialized CPU hash, complete 2-MiB Chip-RAM
hash and beam position are identical between the paired runs. All 235 standard
regressions, both Fred AGA pixel goldens and the Siedler wood-frame/mouse golden
pass. Evidence: `pinball-golden-before/`, `pinball-golden-fixed/`,
`pinball-paired-full-state-hashes.json`, `regression-pinball-fixed.log`,
`pinball-blank-rows-before-final-fixture.log`, `pinball-blank-rows-fixed.log`.
[Commodore HRM chapter 3](https://www.theflatnet.de/pub/cbm/amiga/AmigaDevDocs/hard_3.html)
describes pointer increments and the fetch-line modulo.

The older A500 Alien Breed save renders and responds in its level, while the
older Moonstone save remains a loading phase and cannot count as gameplay.
Keeping Moonstone Disk 1 in DF0 and adding Disks 2/3 in DF1/DF2 reaches its main
menu after the long intro. Debounced Down/Down selects Practice; the A500 replay
shows a live two-knight fight, directional/sword inputs and exact restore.
There is no insert-Disk-B prompt at that checkpoint. The same full Practice
sequence is now running across all eight native boards. Evidence:
`moonstone-long-disk1-intro/`, `moonstone-practice-debounced/`,
`moonstone-all-models-practice/`.

Lotus cold Kickstart-1.3 controls reach races on A500+, A600 and A2000 ECS while
retaining their native 68000/ECS. The A1200/A3000/A4000 native-CPU Kickstart-1.3
controls still fail; no CPU substitution is accepted. Native newer-ROM classic
profiles and the advanced boards remain unresolved. Evidence:
`lotus-kick13-controls/`, `lotus-other-kick13/`.

Moonstone's eight completed native Practice continuations confirm live fights
and directional/sword control on all five 68000 boards. The three 020/030 boards
fail in the same local CSL crack stub: `$041A: MOVEC CACR,D0` runs with saved
SR `$0000`, after installing only an illegal-opcode handler at vector 4. 68000
correctly enters that handler at `$042A`; 020/030 correctly raises privilege
vector 8, still pointing to the ROM. The resulting ROM recovery is not gameplay.
`X` here applies to this precise cracked set's processor probe, not to retail
Moonstone or other patched versions. A new independent fixture covers both
vector choices, unchanged destination, fault PC, saved user SR and the real
six/eight-byte frame for all five implemented CPU variants. No CPU substitution
or incorrect exception semantics are introduced to hide the cracked loader.
The portable full Practice flow is retained in the source; five fresh 68000
source cold runs are next. Evidence: `moonstone-all-models-practice/`,
`moonstone-cpu-probe-68000.txt`, `moonstone-processor-probe-regression.log`,
`review/moonstone-pinball-chip-controls.png`.

The two native desktop Pinball Chip-only cold controls also fail before gameplay;
removing Fast memory is not an accepted solution. Their earlier privilege fault
at `$CB34/$CD14: MOVE #imm,SR` must be examined independently of the native
Fast-memory `$200002` jump. Six classic/A1200 source cold replays are in progress.
Lotus's three reviewed 68000/ECS Kickstart-1.3 profiles now bring preflight to
248 valid cases; preflight does not count as gameplay. The final optional-ADF
regression invocation passes Turrican III and both North & South checks; its
fourth AmigaTestKit fixture fails because that external ADF is absent. This is
a missing test dependency, not counted as a hardware pass.

### Final-core cold controls and disk-read investigation — 2026-10-10

All six Pinball source cold starts have completed and been reviewed: Partyland,
plunger/flippers, moving ball and full-state restore on the five 68000 boards
and A1200. All five Moonstone source cold starts likewise show live Practice
combat, movement/sword control and exact restore. Evidence:
`pinball-source-cold-fixed/`, `moonstone-source-cold/`,
`review/final-cold-and-disk-flows.png`. The local-media advanced-board restrictions
and unresolved desktop Pinball faults above remain visible.

With the added independent user-mode MOVEC regression, the standard suite now
has **236 passes, zero failures/skips** (`regression-final-game-cpu-probes.log`).
All **240** library/model continuations also complete full CPU/bus/beam/pixel
restore with the Pinball DMA fix (`restore-matrix-pinball-fixed/`). These are
structural restore checks, not 240 gameplay passes. All 50 original ADF hashes
were rechecked unchanged (`original-media-recheck-final.json`).

Civilization now reaches the main menu on all eight native models. Its A500
world-birth animation advances through galaxy, volcano, planet and settlements
when allowed 2.1 billion additional chip clocks, eventually requesting Disk 4.
It is not a permanent graphics-task hang. After the requested Disk-4 swap,
AmigaDOS displays `Disk corrupt - task held`; Retry controls and an explicit
empty-drive interval do not resolve that state. Independent host OFS unpacking
reads all 69 files (444,988 bytes); all four source disks' root/bitmap checksums
are valid. The requester therefore does not establish damaged source media.
An independent guest DOS read fixture is being validated before drawing a
hardware conclusion. Evidence: `civ-all-native-input/`, `civ-world-birth-long/`,
`civ-requested-disk4/`, `civ-disk4-retry/`, `civ-disk4-eject-gap/`,
`civ4-independent-ofs-validation/`, `civ-ofs-root-bitmap-validation.json`.

GameRunner now supports validated `eject` actions for DF0..DF3, so a real
empty-drive interval can be exercised before inserting the requested disk.
An out-of-range drive is rejected in preflight before execution. No guest
filesystem or CPU semantics have been changed to bypass Civilization's requester.

All eight Alien Breed native continuations now reach the real shuttle/deck level,
movement, shooting and exact restore. The sequence keeps Disk 1 until its actual
Disk-2 request, acknowledges with joystick Fire and permits the long data load.
The complete flow is now in the source manifest; eight fresh source cold runs
are underway. All five Speedball 2 source cold runs are also reviewed in Practice
with directional/ball input and restore on the fixed core. Evidence:
`alien-actual-request/`, `speedball-source-current-run/`,
`review/alien-eight-and-speedball-five.png`. The three Speedball loader
restrictions remain unchanged.

The independently encoded 68000 DOS read probe now reads `CIV4:adscreen.pix`
successfully under both Kickstart 1.3 and 3.1, producing the host-verified
64-byte checksum `9EF884A5`. The 1.3/3.1 direct-boot fixture modifies only a new
external Disk-4 copy; the 3.1 HDF fixture uses a new external HDF and the unchanged
data disk. The source file's Hunk structure and readback hash were independently
validated. Fresh startup files must use Amiga LF line endings; earlier CRLF
fixture failures were test-setup failures, not guest filesystem defects.
Generic disk/file reading is therefore not disproved by the game's requester.
Removing duplicate data drives before the original A500 game's Disk-4 swap
still reaches the corrupt-disk requester. Its specific file/cache/swap path
remains open. Evidence: `civ4-direct-probe/`, `civ-dos-hdf-late-insert/`,
`civ-disk4-single-drive/`, `civ-corrupt-trace-v2.log`.

All 240 library/model states now also pass **warm-reset restore and power-cycle
restore**, checking serialized CPU, bus/RAM/media, beam and presented pixels
against the saved state. The runner's `resetBeforeRestore` option preserves
the existing strict equality checks (`reset-restore-matrix-valid/`). This proves
state recovery across reset, not game startup or completion of each title after
reset. Native A600/A1200 Civilization world creation and a 32-field-per-board
Speedball scrolling review were subsequently completed as described below.

### Completed cold, scrolling and filesystem controls — 2026-10-10

All eight fresh source Alien Breed replays now reach the shuttle/deck level,
directional/shooting input and exact restore, not just a loaded title screen.
Evidence: `alien-source-cold-run/`, `review/alien-source-cold-run-final.png`.
All five classic-board Speedball runs also complete 32 consecutive fields of
directional/shooting scrolling. Every one of the 160 fields retains source
`128,44+640x256`, produces a distinct frame hash and records zero non-ROM CPU
exceptions. Manual review shows stable court/HUD boundaries and moving players
and ball. This tests the reported jump/row defect over that sequence, not every
match or the incompatible 020/030 media. Evidence:
`speedball-fixed-scroll-fields/`, `speedball-consecutive-field-geometry.json`,
`review/speedball-consecutive-fields.png`.

The independent guest DOS probe also reads the entire 12,644-byte Civilization
`adscreen.pix` under Kickstart 1.3 and 3.1, matching host checksum `8C6EB102`.
Independent host validation checks all four disks' OFS data chains (header owner,
sequence, size, next-block link and checksum): 307 files, 5,064 data blocks,
zero errors. These checks do not clear the game's specific swap failure.
Native A600/A1200 world creation reaches the real Disk-4 request; a delayed
acknowledgment still leaves that request visible, while A500 still displays its
corrupt-disk requester. A corrected DOS-library-vector trace identifies the
A500's pending `Lock("CIV4:nuke1.pix", SHARED_LOCK)` as the operation to examine
next. An earlier trace matched unrelated libraries' vectors and is explicitly
discarded. Evidence: `civ4-full-read-probe/`,
`civ-independent-ofs-data-chain-validation.json`, `civ-native-new-world/`,
`civ-disk4-delayed-ack/`, `civ-a500-dos-vector-trace.log`.

The source manifests, including the full Alien Breed and Speedball cold flows,
pass a fresh **248-case preflight** (`all-248-cold-flow-final-preflight/`).
The library audit remains open; no further commit/push has been performed.

### Moonstone separate native-CPU compatible disk — 2026-10-10

The CSL loader's installed vector-4 handler intentionally abandons its exception
frame without RTE, continuing in Supervisor mode. A fresh external Disk-1 copy
replaces its user-mode MOVEC with `ILLEGAL; NOP`, invoking that existing handler
on all implemented CPU families. CPU instruction/exception behavior is unchanged.
Only four instruction bytes and the owning OFS data-block checksum are modified;
Disk 2/3 and the original Disk 1 remain unchanged.

All three native 020/030 boards now complete **fresh cold Practice/combat,
directional/sword input and exact full-state restore** with that separate copy.
This establishes a tested compatibility option; the table's `X` still describes
the unchanged original CSL set, not the compatible copy or retail releases.
Evidence: `moonstone-illegal-trap-control/`,
`review/moonstone-illegal-trap-control-final.png`,
`moonstone-illegal-trap-patch.json`.

`Tools/prepare_moonstone_020_disk.py` reproduces the exact reviewed patched hash
`fba2aa85a63922b4549e0f7d2883e2df6571a9f3ebab21448d5de2767d056665` from source
hash `a5742db6ee76af830f374421d41ba6ed234486c07bac3b6c50a6845cfc13ca52`.
It checks the loader signature, OFS owner/header/payload and checksums, rejects
other images and refuses existing destinations. Its negative controls and
original-media hash check pass. The matrix's explicit `-MoonstoneCompatibleBoot`
option adds three separate cases, retaining native originals and using the copy
again at the later Disk-1 insertion. All three generated sequences/settings/media
match the reviewed cold controls; the corrected **251-case preflight** passes.
Evidence: `moonstone-reproducible-tool-validation.json`,
`all-251-reviewed-moonstone-corrected-preflight/`. The first optional manifest
was superseded because it reinserted the original Disk 1; it is not accepted.

The independent guest now also locks and reads the exact failing game filename
`CIV4:nuke1.pix` (5,974 bytes) under Kickstart 1.3 and 3.1, matching padded big-endian
checksum `3EFE4AE7`. The game's swap/cache path remains unresolved despite this
control (`civ-nuke-lock-read-run/`). A single-drive A500 world-creation/swap control
is the next step; the older A500 world-birth state had mounted all four disks.

### Keyboard handshake correction and continuing swap controls — 2026-10-10

The single-drive A500 world-creation control reaches the real Disk-4 request and
then the same corrupt-disk requester. Duplicate data drives are therefore not a
sufficient explanation (`civ-single-drive-world-swap-run/`). A separate DOS probe
boots from a new read-only HDF, locks `CIV1:birth0.pix`, swaps DF0 to the unchanged
Disk 4 and locks/reads `CIV4:nuke1.pix`. With the system Retry shortcut it completes
the full read with checksum `3EFE4AE7` (`civ-dos-hdf-swap-run/`,
`civ-swap-retry-keyboard-fixed-run/A600-DOS-swap-retry/`). The game's own native
A600/A1200 request still remains open; generic file/swap success is not game success.

This Retry test exposed a separate input defect: a queued keyboard byte could
start immediately after the CIA ICR read, before the keyboard ISR selected serial
output for the KDAT handshake. That direction change aborted the following byte,
losing modifier/key releases and causing sustained key repeats. The independent
modifier/down/up fixture fails the old core (SDR expected 51, actual 151), and
passes the corrected core. CIA retains a completed output-to-input handshake
sequence; the keyboard waits for it before releasing its next queued byte. Both
states survive restore, including pulses between E-clock samples, and missing
legacy fields are cleared rather than inherited. The prior encoding self-test
now supplies the actual handshake instead of assuming that ICR read is enough.
This follows the Commodore [keyboard communications specification](https://www.theflatnet.de/pub/cbm/amiga/AmigaDevDocs/hard_g.html).
Electrical pulse-duration checking, full serial-bit timing and timeout/resync
remain separate hardware work; this correction addresses byte ordering/loss.

The standard suite now passes **237/237, zero failures/skips**
(`regression-keyboard-handshake.log`). The same OS Retry sequence previously
produced continuous `V` text and now shows the probe result without a held key.
Evidence: `keyboard-handshake-before.log`, `keyboard-handshake-fixed-input.log`,
`civ-swap-retry-controls-run/`, `civ-swap-retry-keyboard-fixed-run/`.
All 50 original ADF hashes were rechecked unchanged after the Moonstone copy
tool (`original-media-recheck-moonstone.json`). Full 240-case reset/restore
revalidation with the keyboard correction is underway; 16 fresh Tinyus/Cannon
native input flows are prepared next. Prior cold-game evidence above belongs
to the graphics-fixed core before this keyboard change and is not silently
relabeled as a new-core cold pass.

Mega lo Mania's longer A500/A1200 and A600/Kickstart-1.3 controls reach intro/title
content but do not establish gameplay. A500 left-only continuation also ends in
an unbounded copy loop at `$7872` with pointers outside configured RAM; the
Disk-2 control does not repair it. A1200 eventually recovers through the ROM
after user-mode MOVE SR privilege faults. These are investigation leads,
not yet certified media restrictions or accepted compatibility profiles.
Evidence: `mega-long-intro-controls/`, `mega-left-only-run/`,
`mega-post-intro-disk2-run/`. The native library audit remains unfinished.

### New-core reset checks and Tinyus compatibility option — 2026-10-10

All **240** model/library states pass warm-reset and power-cycle full-state
restore again with the keyboard-handshake correction, zero failures
(`reset-restore-keyboard-fixed/`). All eight native Tinyus cold sequences also
complete exact restore and are visually reviewed on this core: the five OCS/
020/030 configurations reach the level, while the three newer-ROM 68000/ECS
configurations still end in a software-programmed blank phase. The input fix
does not close that transition issue (`keyboard-native-cold-controls/`).

Keeping each of those three ECS boards, native 68000 and 2-MiB Chip RAM, but
selecting Kickstart 1.3, now passes fresh cold Tinyus gameplay. All three also
pass five directional/shooting intervals, the Continue screen, a credit-based
restart back into a moving/shooting level and exact restore, with zero non-ROM
exceptions. The final restart PNGs and raw ARGB hashes are independently checked
identical across the three boards; repeated identical image previews can appear
blank in the tool UI, which is not a guest-rendering failure.
These are separate reviewed ROM configurations, not an automatic ROM/CPU
substitution or a claim that the newer-ROM problem is fixed. Evidence:
`tinyus-kick13-keyboard-controls/`, `tinyus-kick13-input-controls/`,
`tinyus-kick13-restart-controls/`. The three alternatives are retained in
`Tools/game-compatibility-profiles.json`.

The full selection now contains 240 native cases plus 11 explicit ROM/RAM
alternatives (**251**), or **254** when the three compatible Moonstone-disk cases
are explicitly enabled. The three genuinely fresh single-drive Civilization
world/swap replays complete but retain the Disk-4 problem. All eight fresh native
Cannon Fodder sequences have also completed and been visually reviewed on the
keyboard-fixed core: seven reach Mission 1 with inputs/restore; A2000 OCS still
recovers through the ROM after its non-ROM Line-F fault. These results retain
the native CPU/RAM configuration (`civ-keyboard-cold-world-run/`,
`keyboard-native-cold-controls/`, `review/keyboard-native-cold-controls.png`).

### Writable floppy persistence correction — 2026-10-10

An independent DOS guest opens `SYS:writecheck` in MODE_NEWFILE, writes the 64
bytes `00..3F`, closes it and reports checksum `E1F20210`. Initially the guest
reported success while an independent host filesystem tool found no file.
Tracing the actual OS MFM words showed that sector decoding works: 66 sectors
were decoded, and explicitly flushing the drive yielded the exact payload.
The failure was disk lifecycle persistence, not the MFM decoder. Disk debug
counts for `selectedDrive:none` must not be interpreted as total drive writes.

Three independent lifecycle fixtures fail the preceding core: replacing DF2
loses its pending data, system disposal omits all four drives, and savestate
file reattachment clears an incomplete MFM write. The correction flushes the
outgoing disk before replacement (retaining it if flushing throws), flushes all
drives on system disposal/menu persistence, serializes the ADF pending-write
flag and retains the saved buffer/partial sector when reattaching the same path.
No savestate format-version change is needed for this named-field addition;
legacy images default to no pending writes. The file-restore fixture verifies
the saved payload instead of a later mutation and completes the partial sector
exactly once. Evidence: `floppy-lifecycle-before.log`,
`regression-floppy-lifecycle-fixed.log` (**240/240, zero failures/skips**).

Fresh writable **external copies** boot this actual DOS write probe on all eight
native models/ROMs, power-cycle and exactly restore CPU/bus/beam/pixels, then
dispose the system. All eight show `PROBE PASS`; independent host filesystem
reads then match every byte `00..3F`. This proves guest-to-host persistence,
rather than accepting only the guest's buffered success message. Evidence:
`floppy-write-fixed-controls/`, `floppy-write-fixed-host-readback.json`,
`review/floppy-write-fixed-all-models.png`. The 240 warm-reset/power-cycle full
restore cases also pass again on this core (`reset-restore-floppy-fixed/`).
All 50 original ADF hashes remain unchanged
(`original-media-recheck-floppy-fixed.json`). HDF writes remain external;
restored pending ADF writes are subsequently persisted to their writable copy.

Civilization's three new writable Disk-4 copies do not fix its request/cache
path: A500 still shows the corrupt-disk requester; native A600/A1200 remain at
the Disk-4 prompt and issue no write DMA. The independent DOS file/swap controls
still pass. Evidence: `civ-writable-data-controls/`,
`review/civ-keyboard-writable-copies.png`. It remains an open game issue.

A **254-case cold library sweep** was prepared and preflight checked
on the persistence-fixed core, including the explicit ROM/RAM alternatives and
separate Moonstone copy, initially in four disjoint workers:
`all-254-floppy-fixed-cold/`, `all-254-floppy-fixed-cold-workers.txt`.
Its outputs require visual/input review; it is not yet a library pass. Next:
review these cold results, retain any changed failures, and trace Civilization's
filesystem error before requester rendering. No further commit/push has occurred.

## Motor latch, external drive identification and restore order (2026-10-10)

The persistence-fixed sweep retained 47 completed cases, then continued its
remaining cases in eight workers. A further 126 completed before those workers
were stopped for the next core correction. These 173 captures remain baseline
evidence, **not current-core gameplay passes**; interrupted cases are not counted.
`previous-cold-sweep-retained.json` records the boundary.

The motor incorrectly followed changes of the shared MOTOR line while a drive
was already selected. It now samples that line only when SELECT becomes active,
as specified in [Commodore HRM Appendix H](https://www.theflatnet.de/pub/cbm/amiga/AmigaDevDocs/hard_h.html).
External drives also lacked their standard DD identification on motor-off RDY.
Media insertion attaches the physical drive; disk ejection leaves it attached.
The DD identifier is independent of spin-up and readable media. HD drive IDs,
electrical setup/hold timing and complete drive reset behavior remain unimplemented.

Independent motor and 32-bit identification fixtures fail on the preceding core
(`floppy-id-before.log`) and pass after correction. An additional full-system
fixture exposed an ordering fault: restoring the bus before the CIAs applied
old SELECT/MOTOR values to the restored drive, generating a false select edge.
Bus loading now defers line refresh until both CIAs are restored. This fixture
fails before and passes after (`motor-restore-before.log`, `motor-restore-fixed.log`).
The final standard suite passes **243/243**
(`regression-floppy-id-restore-fixed.log`). All **240** warm-reset/power-cycle
full CPU/bus/beam/pixel restore cases pass again
(`reset-restore-floppy-id-final/`). Siedler's wooden-frame/menu regression and
Pinball's exact DMD golden frame also pass on this final core
(`siedler-floppy-id-final-golden.log`, `pinball-dmd-floppy-id-final-golden/`).

Real native Kickstart boot controls attach Disk 4 to DF3 before the first CPU
wait, then read the complete `CIV4:nuke1.pix` file (5974 bytes, CRC `3EFE4AE7`).
All eight models pass with zero non-ROM exceptions; their register checks are
recorded in `floppy-id-native-boot-proof.json` and their screens in
`review/floppy-id-native-boot-all-models.png`. These cold controls use the
motor/ID-fixed core immediately before the additional restore-order correction.
The same A500/A600/A1200 cold controls on the preceding core remain at the
missing-CIV4 requester despite inserted DF3 media
(`floppy-id-native-before-run/`, `review/floppy-id-before-three.png`). This proves
real DOS drive registration and file reading, not Civilization gameplay.

Civilization's early K13 filesystem trace identifies invalid block number
`260F801C` before GUI error rendering. Its lookup uses block 915, which is
Disk 1's root hash entry for this filename. On Disk 4, block 915 is valid pixel
data containing that value; its actual filename hash chain starts at block 946.
The live DOS list nevertheless contains a real CIV4 volume with the correct
Disk-4 date, rather than an alias to CIV1. The precise stale-cache/lookup cause
remains open. This is not evidence that the source ADF is corrupt. Diagnostic
scripts/dumps include `civ-dos-fatal-trace*`, `civ-root-hash-chain-investigate.py`
and `civ-dos-volumes.py`.

The minimal boot-volume/swap control reaches its actual request before swapping:
native K31 A600/A1200 read the file successfully; K13 A500 reports a task-held
software error. Longer boot timing supersedes the premature first probe.
Evidence: `civ-boot-volume-swap-probe-v2-run/` and
`civ-boot-volume-swap-probe-motor-fixed-run/`. The game-specific single-drive
swap failure remains open after the motor correction.

A fresh final-core **254-case cold sweep** now runs in eight disjoint workers:
`all-254-floppy-id-restore-fixed-cold/` (manifests, logs and `workers.txt`).
Its captures still require gameplay/input review. Separate three-model
Civilization controls attach all original read-only disks before boot and
visually select Earth Game with two Down presses; an unverified E hotkey had
actually selected New Game. `civ-earth-menu-verified-run/` preserves the menu
selection and subsequent loading; no playable map has yet been accepted.
Next: review this loading path and all final-core cold captures, then fix the
remaining transitions with reproducible state/CPU evidence. No further commit
or push has occurred; full library acceptance and the hardware roadmap are open.

## Multi-drive menu and new Civilization/Zak gameplay evidence (2026-10-10)

Civilization's verified Earth selection, full evolution wait, default setup and
first-city inputs now reach the real map, city naming and Rome's city view or
management screen. A500/A600/A1200 cold-to-exact-state continuation chains have
zero non-ROM exceptions and pass the full power-cycle restore checks:
`civ-three-native-gameplay-evidence.json`, `civ-earth-setup-run/`,
`civ-earth-city-run/`, `civ-city-followthrough-run/`. The dark city transition
needed further data loading; it is not accepted as a frozen renderer.
The merged source flow is now freshly cold-tested on all eight native boards
under `civ-reviewed-four-drive-final-cold/`, with four disjoint workers in its
`workers.txt`. A500, A500+, A600 and A2000 OCS have completed and their actual
city screens are reviewed; the other four were still running at this checkpoint.
The A1200's earlier native cold/restore lineage also proves the marked stage.
The table does not pre-accept the pending models. Single-drive K13 stale-root
lookup remains open. The minimal DOS probe reproduces the same invalid block
`260F801C`; a bounded trace directly observes the old root hash read at `$5E6C`
in ROM lookup code. Evidence: `civ-boot-volume-swap-k13-lookup*`,
`civ-boot-volume-swap-k13-word-lookup*`, `civ-boot-volume-swap-k13-root-stop*`.

Zak's new preboot DF1 controls keep the boot disk in DF0 throughout its lengthy
intro. Native A500 and A1200 reach the bedroom, respond to the same mouse
point/click and visibly walk from its center to the left side, then exactly
power-cycle/restore. Zero non-ROM exceptions are observed. Evidence:
`zak-keep-boot-run/`, `zak-room-input-run/`,
`review/zak-room-input-run-latest.png`. The earlier control that replaced Zak1
while still needed produces a legitimate missing-Zak1 request and is not a
gameplay pass. A separate A500 1 MiB Chip diagnostic remains in the dream/intro;
it does not justify changing the native RAM profile. The corrected native
512 KiB Chip + 512 KiB Slow case already reaches the room.
The source now preserves DF0, mounts Zak2 in DF1 before boot and retains the
mouse-walk check. Fresh all-eight cold confirmation uses two disjoint workers
under `zak-reviewed-two-drive-final-cold/` (`workers.txt`); pending cases are
not pre-accepted.

The normal emulator's MEDIA menu now selects DF0–DF3 with Left/Right or Enter
on TARGET DRIVE, and inserts/ejects only that selected disk. First insertion
of a new external drive prompts a reset so Kickstart can enumerate it.
The already-existing DF1/DF2/DF3 settings are no longer erased by saving DF0.
External disks mount before startup, retain protection on a model change, and
remain available across reset. Restoring a state derives the boot disk from
the actual DF0 image, rather than mistaking last-data-disk metadata for DF0.
Unavailable configured paths are retained until explicit eject or state restore.

An isolated offscreen smoke harness exercises the actual menu methods without
a window constructor, native handle, user settings store or original media.
It verifies all four stored paths, DF3-metadata/DF0 separation, external startup
mounts, unavailable-path retention, isolated eject settings, selection cycling
and scrolling headers; the rendered menu is visually reviewed.
Evidence: `four-drive-menu-smoke.log`,
`four-drive-menu-smoke-media/media-menu.png`. The standard suite still passes
243/243 after these menu changes (`regression-four-drive-menu-final.log`).

The broad 254-case final-core run predates the two newly extended scenario
flows. Its eight menu/intro cases for each of Civ1 and Zak1 are superseded by
the separately generated all-eight runs above; its remaining 238 cases retain
the same source/core. Keep this distinction when assembling final acceptance.
At this checkpoint the library and both dedicated sweeps are running. Next:
finish/review all cold cases, compare changed captures and address the remaining
Desert Strike, Mega-lo-Mania, advanced Sqrxz4/Pinball and newer-ROM transitions.
No further commit/push; complete library acceptance remains open.

## Eight-board data-drive gameplay confirmation (2026-10-10)

The merged-source cold runs now finish on all eight native boards for both
Civilization and Zak. Original CPU, ROM and RAM profiles are retained. All
sixteen cases have zero observed non-ROM exceptions and pass exact CPU,
bus, beam and framebuffer restore assertions. Civilization shows Rome's map,
city view or management screen after native setup/city-name inputs. Zak's
actor visibly walks from the bedroom center to its left after the same native
mouse movement and click. Reviewed contacts:
`review/civ-reviewed-four-drive-final-cold-latest.png` and
`review/zak-reviewed-two-drive-final-cold-latest.png`; individual evidence:
`civ-zak-eight-native-gameplay-reviewed.json`.

All sixteen gameplay checkpoints additionally pass both warm reset and full
power-cycle restore, with exact CPU/bus/beam/pixel assertions and zero observed
non-ROM exceptions (`civ-zak-gameplay-reset-proof-run/`). Both data-drive
conditions remain mandatory. Single-drive K13 stale-root handling, later game
resources and full missions are not certified by these bounded tests.
The updated complete 254-case source manifest validates
(`all-254-reviewed-four-drive-sources-preflight/`). The broad final-core sweep
had 244/254 raw cases at this checkpoint; its sixteen older Civ/Zak flows are
superseded by these gameplay runs. Raw execution is not game acceptance.
All fifty original ADF hashes are unchanged
(`original-media-recheck-four-drive-games.json`).

Two fresh low-Chip Cannon controls both remain black: native A500 with
512 KiB Chip + 512 KiB Slow, and A2000 OCS with the same Chip/Slow plus
2 MiB Zorro II Fast (`cannon-ocs-ram-controls/`). Extra Fast RAM does not
resolve this copy's failure. A Chip-RAM dependency is observed, but no hardware
exemption or new RAM profile is accepted without tracing the actual failure.
Next: finish and review the library sweep, then cold-test Desert Strike and
Mega-lo-Mania with data drives attached before boot. No further commit/push;
the full library and hardware roadmap remain unfinished.

## Completed sweep, keyboard clock and changed cold cases (2026-10-10)

All 254 cases of `all-254-floppy-id-restore-fixed-cold/` now execute without
harness errors. All 240 canonical native last captures have been visually
reviewed in `review/final-canonical-{model}.png`, using the sixteen separately
confirmed Civ/Zak gameplay runs instead of the older menu/intro flows.
`final-canonical-capture-index.json` retains the actual result/capture paths.
This is complete execution/capture coverage, not 254 compatibility passes.
Known waits, local processor restrictions and graphics defects remain visible.

The A500+ Pinball cold case regresses with the keyboard-handshake correction,
although the identical source and native K20 ROM reach Partyland on the older
Pinball core. A same-board/CPU/RAM K31 control also reaches the live table.
The table's A500+ result is therefore withdrawn to W pending a fix. A trace of
the actual native K20 menu proves that F1 down/up are received and correctly
acknowledged; the subsequent depacker runs outside its output buffer. The
keyboard backlog at the final black screen is a consequence of that hang, not
proof that F1 itself lacked acknowledgement. Evidence:
`pinball-prior-core-final-source/`, `pinball-keyboard-core-final-source/`,
`pinball-current-k31-control/`, `pinball-menu-keyboard-wire-trace.log`.
Compare the first packed data at depacker entry next; do not weaken handshake
semantics or fake exception frames to make this game pass.

The keyboard's serial bit clock was independently too fast: eight CIA E clocks
completed an entire keycode instead of approximately 60 microseconds per bit.
The keyboard now supplies 43 E clocks per bit; generic CIA input retains its
original explicit one-clock default. Queued byte periods, partial bit countdown
and legacy pending transfers survive restore. Independent timing/queue fixtures
fail on the old keyboard path and pass on the new one; the full standard suite
passes 245/245 (`regression-keyboard-bitclock-final2.log`). This clock fix alone
does not resolve the K20 Pinball depacker failure. It implements sampled bit
spacing, not full electrical KCLK/KDAT waveforms, minimum acknowledgement pulse
measurement or the 143 ms resync protocol. Reference:
[Commodore HRM Appendix G](https://www.theflatnet.de/pub/cbm/amiga/AmigaDevDocs/hard_g.html).

The completed broad run also exposes two Moonstone K31 menu inputs that were
too short to move the selection reliably. Two 28-frame holds repeat into the
Select Knight row; two approximately seven-frame holds select Practice exactly.
Native A600/A2000 ECS then show combat and sword/movement control and pass
power-cycle restore (`moonstone-seven-frame-menu-run/`). The source uses these
bounded holds; fresh eight-board/native-copy and three separately patched-copy
confirmation plus all Tinyus ROM controls are running as 22 disjoint cases in
`moon-tinyus-bitclock-cold/`. No CPU, RAM or original media is replaced.

Desert Strike's first click was before the crack intro finished. A late click
advances native A500/A1200 into story and loading, where the preboot data drives
are actually scanned (`preboot-mega-desert-run/`, `late-intro-controls-run/`,
`desert-story-followthrough-run/`). This is not mission entry. Mega's preboot
data drive and late clicks still do not resolve its post-intro failure.
Continue the packed-data comparison and review the new Moon/Tinyus cold cases.
No further commit/push; complete compatibility and the hardware roadmap remain
unfinished.

## Physical INDEX/track correction and final-core recheck (2026-10-10)

The paired K20 menu comparison now establishes the Pinball failure. Loading an
older-core menu into the current core reaches Partyland. Both menu states load
the same first 87,600-byte PP20 packet (SHA-256
`587B4D9A53DE371719D81EAFF4E674F051BCFB726D1FCF7FA7F31E9E4023454E`),
and its 310,376-byte unpacked output is identical. The failing second packet
skips exactly ten sectors at logical track 39: its bytes after offset 46,774
match the original ADF 5,120 bytes too far ahead. This corrupts the PP20 footer.
Evidence: `pinball-{current,prior}-second-decrunch.*`,
`compare-pinball-second-packed.py`, `locate-pinball-second-data.py`.

The custom loader arms DMA just after physical INDEX. The emulated 6,400-word
stream wrapped after 691,200 chip clocks while INDEX repeated every 709,379.
Its sector origin therefore drifted through the independent INDEX pulses; a
few clocks of startup timing determined whether sector 10's sync was caught.
This is a shared floppy bug, not a keyboard exception or ROM workaround.
The synthesized ADF sectors now wait in gap flux after the encoded track ends,
and restart at the physical INDEX edge. Head/side changes keep angular phase;
stopped drives reset both clocks together. The existing modeled data cadence,
200 ms PAL index period, encoded sector layout and CPU semantics are retained.
This does not implement a bitcell PLL, arbitrary raw/IPF tracks or NTSC-specific
drive timing. Reference: [Commodore INDEX interface](https://www.theflatnet.de/pub/cbm/amiga/AmigaDevDocs/hard_h.html).

Three independent fixtures fail on the old core (INDEX origin drifts; head/
restore state retains the drift; INDEX-gated DMA starts at sector 1), and pass
after correction. The standard suite passes **248/248**. The failing K20 menu
now loads the exact good second packet, including its correct PP20 footer:
230,844 bytes, SHA-256
`FA212FA1707DEAC2171D57E60B1D1A30CACD85B686480ABC15DB4BE0C0150735`.
Evidence: `floppy-index-final-old-core-proof.log`,
`regression-index-final.log`, `pinball-index-current-track39.*`.
Siedler frame/menu alignment and the exact Pinball DMD two-field golden hash
still pass (`siedler-index-fixed-golden.log`, `pinball-dmd-index-fixed-golden/`).
All eight fresh native Pinball cold starts now finish and are visually reviewed
(`pinball-index-fixed-cold-run/`, `review/pinball-index-fixed-cold-final.png`).
All five 68000 boards, including native A500+/K20, and native A1200 reach
Partyland with native player/plunger/flipper inputs and exact restore. All six
gameplay checkpoints additionally pass exact warm-reset and power-cycle
restores (`pinball-index-gameplay-reset-run/`). The K20 result is reinstated to
L. Native A3000/A4000 still return to their Kickstart insert-disk screen after
the loader fault; they remain W. Individual acceptance and limits are recorded
in `pinball-index-native-cold-review.json`.

The previous bitclock core's 22 Moonstone/Tinyus cold cases are complete and
reviewed (`moon-tinyus-bitclock-reviewed.json`). Five original 68000 Moonstone
cases and three separate compatible 020/030-copy cases show combat. Eight
accepted Tinyus native/K13 profiles show gameplay; the three newer-ROM 68000
black phases remain open. All sixteen accepted gameplay states additionally
pass warm-reset and full power-cycle exact CPU/bus/beam/pixel restore
(`moon-tinyus-bitclock-reset-run/`). These do not establish long-session
compatibility, and they predate the physical INDEX change.

The entire corrected 254-case source manifest validates. A new disjoint
eight-worker cold sweep is running under `all-254-index-fixed-cold/` with the
frozen `runner-index-fixed` binaries and their hashes in
`index-fixed-runtime-provenance.json`. This sweep includes the final Civ/Zak
data-drive sources and corrected Moonstone menu holds. Earlier raw results
remain retained; they cannot replace final-core cold coverage after a shared
disk change. Inspect `workers.txt`, individual `result.json` and capture/input
evidence before accepting cases. No further commit/push; full acceptance is
still open.

The same final core advances both native A500/A1200 Desert Strike loading
checkpoints into the aircraft-carrier/helicopter sequence after another
500 million clocks, with zero new non-ROM exceptions (`desert-index-loading-run/`).
The captures are coherent and reviewed, but this is still a cinematic rather
than proven mission gameplay. Fire, left click and Return controls finish on
both models and retain this animated carrier screen
(`desert-carrier-input-run/`, `review/desert-carrier-input-latest.png`);
mission entry remains open. All fifty original ADF hashes remain unchanged
(`original-media-recheck-index-fixed.json`).


## Cache-core follow-up (2026-10-11)

Evidence root: `%APPDATA%/AmigaEmulator/diagnostics/RoadmapSelected-20261011-a7ac3b55b5e3/`.
This follow-up does not promote unreviewed matrix executions to passes.

Tinyus has fresh native cold/input continuations on A500, A500+, A600,
A2000 OCS/ECS, A1200, A3000 and A4000/030. The seven larger-script controls
through frame 4187 and the A2000 OCS source flow through frame 2691 show
movement/fire/enemies and exact reset/restore, with zero observed non-ROM
synchronous exceptions. Newer-ROM 68000 dark phases are finite in these cases;
unchanged HEAD A500+/A600 controls have the same transition images/registers.
No new fix for a permanent Tinyus hang is claimed; later levels remain untested.

Siedler's repaired K13 Compatible castle checkpoint continues with mouse
movement, coherent wood/menu and exact power-cycle restore. The real frontend
paint/fullscreen code passes 24 mode/aspect/fill combinations with unchanged
viewport dimensions. This is bounded automated rendering evidence, not a
reproduction of every previously reported host configuration.

Native A3000/A4000 Pinball again reaches the course/table menus but fails when
Partyland loads. The A3000 trace catches the reader using A3=$80080000 from
its tagged destination field at frame 11219; 87,634 writes go to unpopulated
high space. The actual Chip RAM module remains empty. Its relocation loop
calls $80010 at frame 12606 and runs through zero instructions to $200002.
Full 32-bit processors preserve the flagged address; 68000/68EC020 discard
the upper byte. This matches the [WHDLoad author's documented compatibility
problem](https://www.whdload.de/docs/en/howto.html), without justifying a hardware
RAM alias. The original source copy is not 32-bit clean here. Chip-only A3000
also fails. A separate validated loader adaptation remains open; no modified
media has been accepted or original overwritten. Evidence: `pinball-before-load/`,
`pinball-target-watch/`, `pinball-first-entry-watch/`, `pinball-unmapped-watch/`,
`pinball-chip-only-control/`.

A separate diagnostic clears the two tagged A3 values at reader PCs $2C50
and $2D1E. The ordinary native core then loads Partyland, responds to ball/
flipper input and preserves exact power-cycle restore through frame 13926
with zero observed non-ROM faults (`pinball-diagnostic-pointer-review/`).
This is causal evidence from a modified guest state, not a released repair:
the original ADF is unchanged and a separate media adaptation is still required.

GameRunner now rejects unknown fields instead of silently ignoring them.
Earlier Desert controls used `code` rather than `key`, so their alleged Return
press was key zero. Correct Return/Space/Fire continuations on A500/A1200 still
show the carrier cinematic; mission entry is not accepted. Zak's source mouse
action now uses `x`/`y`, replacing ignored `dx`/`dy`. Seven preflight regressions
pass, and all 80 direct source cases plus the generated 24-case subset validate.
Desert, Mega lo Mania, Cannon Fodder A2000 OCS and broader matrix acceptance
remain open. Unsupported SHRES/interlace/040/FPU/Zorro III hardware is not
made compatible by these game observations.

## Separate Pinball repair and final TT-overlap core — 2026-10-11

Checkpoint `1bbc12e` was committed/pushed at the user's request. Subsequent
work remains local. See MachineModels.md's **Post-commit Pinball loader and
overlapping TT fix** section for the exact code, hashes, rejected controls and
evidence. The original-media table above is intentionally unchanged.

`Tools/prepare_pinball_32bit_disk.py` creates a separate exact-hash Disk 1 that
clears the loader's relative-address flag before publishing its transfer pointer.
Native A3000/A4000 cold Partyland launch/flipper/restore now passes with this
copy, and A500/A1200 cold controls pass too. On the final core, both desktops
also pass power-cycle restore/continued input. A real patched-disk A3000 cold
menu checkpoint is used for separately reviewed Speed Devils, Billion Dollar
Gameshow and Stones 'n' Bones loading, one-player/launch/flipper and power-cycle
restore. All three show their own artwork and observe zero new non-ROM faults.
A4000's other three tables and longer sessions remain open.

The retained early additional-table/quit attempts stayed on Partyland and are
not accepted. Their misleading scenario names and zero exits cannot establish
table coverage. Pause/quit needs further held-key observation.
Accepted evidence is in `pinball-native-copy-cold/`,
`pinball-copy-legacy-controls/`, `pinball-final-core-restore/`,
`pinball-copy-before-table/`, `pinball-remaining-tables-valid-menu/` and
`pinball-loader-final-reviewed.json` under the current evidence root.

The matrix accepts explicit `-PinballCompatibleBoot`, preserving original
cases, and optional `-PinballTables` for all four cold sequences with their
correct disk and F-key. Both the complete 256-case default combined-copy
manifest and 262-case four-table manifest validate. This is preflight, not
complete-library gameplay acceptance. All fifty original ADFs and the original
installed AmigaOS HDF remain unchanged. The prepared user-selectable copy is
under `%USERPROFILE%/Documents/AmigaEmulator/Kompatibel/Pinball Fantasies/`.

The 68030 TT-overlap CI correction has a failing-before independent MMU fixture,
real instruction/data-cache fixtures, 322 passing core tests and native A3000
HDF boot/golden/reset/restore verification. This preserves the existing state
format and does not complete ATC replacement, timing or MMU fault restart.

Desert Strike left/right click, map/movement and Shift/Escape controls still
show the carrier. A CIA/game-variable probe confirms Fire reaches the guest's
own button state. The stalled script/menu transition remains unresolved;
Mega lo Mania/Cannon and the remaining hardware/model acceptance work stay open.

## Keyboard recovery and bounded mission follow-up — 2026-10-11

The latest local core passes 328 tests (zero failures/skips), nine strict-runner
checks and a warning-free Release build. Missing keyboard acknowledgement now
recovers with the HRM's slow bit synchronization, F9 and replay; startup and
partial/legacy savestate recovery have independent failing-before fixtures.
See MachineModels.md's **Keyboard recovery, Desert flight and remaining Pinball
tables** section for protocol details and retained evidence.

All four Pinball tables now have reviewed launch/flipper/power-cycle-restore
continuations from real repaired-disk cold menus on both A3000 and A4000.
A4000 Speed Devils reaches frame 12712, Billion Dollar Gameshow and Stones 'n'
Bones frame 15095, without new non-ROM faults. Fresh Partyland cold starts on
both desktops pass again on the keyboard-recovery core at frame 16332. The
original-media table remains unchanged: the separate loader copy is explicitly
selected, not substituted. Pause/quit is still open; normal Esc/Y now reaches
a write-enable prompt, but private writable copies have not established the
return to the menu. Longer sessions and separate cold starts for every table
remain open.

Desert Strike now has reviewed A500/A1200 lift-off, flight/fire, F10 map, map
EXIT and exact power-cycle restore. Earlier carrier captures were already a
playable starting state awaiting joystick Down; they did not prove a permanent
cinematic hang. A second defect did block queued keyboard events after a lost
initialization handshake. Recovery drains the seven-event legacy queue and
the map visibly opens, without forced guest flags or registers.

Fresh native-ROM cold prefixes reach the campaign menu at frame 24971. Exact
menu continuations on both boards reach flight/map at 32737 and exit-map/flight
restore at 32833; only the intentional startup CPU probes remain. The expanded
`DESERTS1` source actions reproduce these segmented reviewed paths, with data
disks mounted read-only before boot. The other six profiles, land combat,
objectives and campaigns remain unaccepted. A regenerated 262-case full matrix
validates but has not been executed/reviewed in full.

A paired idle-path control preserves identical complete CPU/bus/beam/pixel
states, with old/new times differing by about 0.4%; no FPS gain is claimed.
Native A3000 HDF boot/reset/golden/restore passes again. All fifty original
ADFs and the installed OS HDF are unchanged. Further game/model and hardware
acceptance remains open; this does not complete the full roadmap.

## Corrected live-ball review, disk tab and full Desert model coverage — 2026-10-11

Latest local evidence:
`%APPDATA%/AmigaEmulator/diagnostics/RoadmapContinuation-20261011-074437-f843c655/`.
The final core passes 331 tests without failures/skips, ten runner guards and
warning-free Release builds. Menu write protection now changes PROT without
ejecting DF0, discarding partial/dirty writes or resetting pending insertion.
Three failing-before tests exercise the actual menu and all four drive ports.

Correction: the earlier two-million-clock Pinball Down hold did not prove
a launched ball. Those cold checkpoints establish table loading/input/restore.
With a full 15-million-clock pull and release, all four tables now have actual
live-mode-1, flipper, visible frozen pause, exact power-cycle restore and Esc/Y
quit-requester continuations on native A3000/A4000. The same corrected suffix
is in the source. Eight cases have no new non-ROM faults and matching per-table
pixels across desktops (`pinball-eight-reviewed.json`). Late pause controls
after a drained ball are retained and rejected. This is segmented cold/suffix
evidence rather than a new uninterrupted eight-case cold execution.

A3000 Partyland also returns to the actual selection menu after cancelling
score saving, inserting the requested main disk and then course Disk 2.
Clearing the write-protect tab on a private checkpoint clears the requester;
persistent scores are still unaccepted. An actual DMA-start buffer contains
valid header-track-0 sectors while the physical head is on cylinder 1/side 1,
so the canonical ADF guard rejects them. See MachineModels.md's latest section
for the trace and next investigation. Original media is unchanged; the separate
32-bit loader copy is still explicitly selected.

Fresh uninterrupted native cold Desert flows on A500+, A600, both A2000s,
A3000 and A4000/030 now pass lift-off, flight/fire, F10 map, mouse EXIT,
cold-reset/exact restore and continued input at frame 32881. Only the expected
startup CPU probes occur. Six final-runtime continuations pass again at 32963
with zero new faults (`desert-six-reviewed.json`, `desert-six-final-tab-core/`).
Combined with preceding A500/A1200 segmented tests, bounded flight/map coverage
now spans all eight boards; land combat, objectives and campaigns remain open.

The 262-case source-derived matrix validates, without claiming complete
execution/review. All fifty original ADFs and installed OS HDF retain their
preserved hashes. Pinball scores, Mega/Cannon transitions, longer sessions and
remaining hardware gates keep the roadmap open.

## Separate Pinball score-writer repair — 2026-10-11

The preceding version is pushed as `19c95ce`; the following repair is local.
The physical head reaches requested track 3 correctly. This disk set's encoder
instead writes scratch D2, cleared during tab calibration, into its MFM headers.
The six-byte optional `--repair-scores` addition to
`Tools/prepare_pinball_32bit_disk.py` takes the descriptor's requested track.
The original-media cases, loader-only variant and canonical ADF track guard
are preserved; raw/custom-track support remains incomplete.

The new exact result hash is
`74307adc5f44748e078ab7782628de0482c1e45dda1781e5a7327a53779e23b2`.
The explicit matrix option accepts this or the previous loader-only hash.
No game/ROM/OS media is distributed. All four tables on native A3000/A4000
now have real cold-loaded, private-media continuations through live mode 1,
flippers, frozen pause, exact cold restore and quit. Each writes eleven correct
track-3 sectors; independently decoded checksums and all payload bytes agree
with the flushed ADF. Eight write continuations have zero new guest faults.

The source's post-F1 wait is increased from three to 30 million clocks: six
initial attempts were still in the start/chute sequence and are excluded from
live acceptance. The revised first-launch flow passes on both desktops.
A3000 Partyland also returns to its actual menu and a fresh native cold boot
reloads the saved disk/table, with the written score/name records matching
guest RAM. This covers preserving existing records; a naturally earned new
named highscore, other boards and longer sessions remain open.

Evidence is under
`%APPDATA%/AmigaEmulator/diagnostics/PinballScore-20261011-081849-963ca87a/`:
`eight-native-writes-reviewed.json`, `settled-first-launch/`,
`persisted-exit-menu/`, `readback-cold/`, `readback-table-ram/`.
The 332-test suite and ten runner guards pass; exact image/preservation guards
also pass. Disk-3 menu returns on both desktops, A4000 Partyland's menu return
and all four exact menu restores pass. `final-pinball-review.json` records these,
per-table identical desktop pixels and the 96-byte score-table readback at `$200`.
The new user set is in `Kompatibel/Pinball Fantasies (32bit und Scores)/`;
both original media and the older loader-only set are preserved.
The final 262-case manifest validates; this remains preflight. Duplicate
uncovered inventory rows for explicitly selected compatibility copies are fixed
and checked for old/new Pinball and Moonstone. See MachineModels.md for causal
traces, scope and next steps.
