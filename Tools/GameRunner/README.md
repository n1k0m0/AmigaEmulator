# Scripted gameplay review

Mega lo Mania's source now covers the intro, Scarlet selection, seven allocated
men, a valid first island sector, live play, P and cold restore. The A4000 variant
targets another buildable sector in its first island. The original A500/A2000
OCS route uses 512 KiB Chip + 512 KiB Slow; CPU Chip-RAM aliases are essential to
its cache probe. The newer-ROM 68000 profiles additionally need 2 MiB Fast to
keep live Exec structures outside the game's fixed-address program. These are
explicit compatibility profiles, preserving the failing bare-Chip native cases.

For the reviewed local Disk 1 on 020/030, `prepare_megalomania_020_disk.py` creates
a **new** disk: its unique IMP literal changes user MOVE SR to MOVE CCR and its
packed checksum is repaired. The original, instruction privileges and 68000
decoder are preserved. This copy is **not for 68000**. No media is distributed.
The exact input/output hashes and exclusive output creation are checked.
Pass `-IncludeCompatibilityProfiles -MegaLoManiaCompatibleBoot <new-disk.adf>`
to the matrix generator to add explicit A1200/A3000/A4000/030 copy cases.

Cannon Fodder's source now tests the actual first mission, movement/fire, P,
cold restore and a further movement order, with all three original disks in
DF0/DF1/DF2. It uses 512 KiB Chip + 512 KiB Slow on A500 and A2000 OCS.
Pause precedes the shorter fire/advance orders: the previous long suffix killed
the squad, and a static retry screen cannot prove a pause. The DF0-only route is
still unaccepted: it can load behind a black palette and needs Disk 1 again;
a later mission attempt faults. Use the explicit data-drive setup for this
reviewed flow, and keep single-drive/later-campaign failures as separate gates.
The loader deliberately uses CPU detection and trace exceptions; the bounded
exception guard allows that reviewed prefix. Disk requesters, black frames and
successful script execution alone remain insufficient acceptance evidence.
`allowedNonRomExceptionVectors: [4, 5, 9]` also rejects any new guest bus/address
fault even when its count stays below the protection loader's total budget.
The optional list requires tracing and distinct observed vectors 2 through 23;
an empty list rejects every observed non-ROM fault. Interrupts and software
TRAP vectors 24 and above are not counted by this guard.

Run from the solution directory with the local Kickstart ROM and your own ADFs:

The source manifests now expect `Game Title (disk N).adf`, including single-disk
games. See `AmigaEmulator/Docs/GameMediaNames.md` for the completed local rename,
preserved originals and savestate migration.

```powershell
dotnet build Tools/GameRunner/GameRunner.csproj -c Release -p:Platform=x64
dotnet Tools/GameRunner/bin/x64/Release/net10.0-windows7.0/GameRunner.dll Tools/gameplay-scenarios.json ../tmp/gameplay-review Alien-Breed
```

Omit the final case name to run the whole manifest. A missing case returns exit
code 2; an exception returns 1. Successful execution returns 0 **only for the
script execution**, never a gameplay or graphics pass. Review the captured
screenshots. Cracker screens, menus, credits and disk requests are not gameplay.
Use `GameRunner <scenario.json> --validate [case-name]` for a read-only
preflight of model/RAM layouts, referenced disks, keys and button names.
Invalid selections return 1 before any cases start; a missing case returns 2.
Normal runs perform the same preflight before creating output directories.
Unknown JSON fields, missing raw `key` values and duplicate case names are
rejected. Use `key`/`button`, not `code`/`buttons`: older runners silently
ignored those spellings and could send key zero or default Fire instead.
Run `python Tools/test_game_runner.py` after building to check these guards
without ROMs, guest execution or disk creation.

`Tools/run-purchased-os-validation.ps1` prepares fresh copies of an installed
OS HDF for all eight current boards, using the user's matching unsplit Kickstart
46.143 ROM. A500/A500+/A600/A2000 OCS/ECS share one ROM family; A1200,
A3000 and A4000 require their individual board ROMs. Mismatches are rejected
before any HDF copy or output directory is created. Invoke separate runs for
different ROM families.
Pass `-SourceHardDisk`, `-KickstartRom` and a new `-OutputDirectory`; optional
`-Models` selects a subset and `-ManifestOnly` stops after preparing private
copies/JSON. The script exercises Shell, Version, ShowConfig, Clock, file
writes, warm/cold resets and exact restore. Both first and second Type results
are retained separately for review. The initial first-read concern was a
misread small capture; a DOS probe and revised five-board cold tests confirm
the content already appears on the first command.
Review screenshots and independently read the test HDF before acceptance.
No ROM, OS file or installed HDF is included in Git. A4000 uses the implemented
030 profile; this validation does not enable the unfinished 040 hardware.

Each case creates numbered PNGs, `steps.jsonl` and a continuation
`checkpoint.amigasav`. Log entries include raw CPU/drive diagnostics, frame
number, presentation bounds, pixel hash and `gameplay: requires-visual-review`.
Disk images are mounted read-only by default. Use a fresh private copy for any
scenario that disables write protection. A replacement exposes a two-second emulated
empty-drive interval before the new medium becomes readable, including when
confirmation is clicked immediately. An explicit eject's elapsed empty time
counts toward that interval. Output filenames are replaced on reruns;
use a fresh output directory to preserve earlier evidence.
Step logs also include Paula DMA counters/queue state and input-port modes and
JOY0DAT and Paula audio state, to distinguish media/loader failures from pointer/input failures.
`cacheControl` records CACR so OS command output can be checked against the
actually enabled instruction/data caches rather than only their requested state.
Each completed case also writes `result.json`: `executed-unreviewed` means
only that the script ran; `failed` includes the exception. The actual model is
recorded from the running machine, including for savestate continuations.

A case has `name`, `boot` (ADF filename), optional `mediaRoot`, `model`, `ram`, `strict`
and `actions`. Default media root is `%USERPROFILE%/Documents/AmigaEmulator`;
`model` accepts `A500` (default), `A500Plus`, `A600`, `A2000Ocs`, `A2000Ecs`,
`A1200`, `A3000` or `A4000Ec030`.
RAM defaults to `Chip512KSlow512K` for A500/A2000Ocs, `Chip1Mb` for older ECS
models, `Chip2Mb` for A1200 and `Chip2MbFast4Mb` for A3000/A4000Ec030.
The three newer profiles require 2 MiB Chip RAM and accept optional 2/4/8 MiB Fast.
A2000Ocs additionally accepts `Chip512KSlow512KFast2Mb`, `Chip512KSlow512KFast4Mb`
and `Chip512KSlow512KFast8Mb`. Set `strict: true` for Compatible contention
without tracing. `save` (absolute savestate path) can replace `boot`; RAM is then
read from the savestate together with the machine model. The checkpoint records the selected accuracy profile.

Actions:

- `wait`: `cycles` chip clocks (about 3.55 million per emulated PAL second).
- `key`: `key` Amiga raw keycode; `cycles` optional hold duration. Space is 64,
  Return 68, F1 80. Release is followed by 400,000 chip clocks.
  Optional `modifiers` holds raw modifier keys around the main key, then releases
  them in reverse order. For example, `key: 18, modifiers: [103]` sends right
  Amiga + E to open Workbench's Execute Command dialog.
- `mouse`: relative `x`/`y` movement, split into small steps to avoid byte-counter
  wrap ambiguity. Coordinates refer to mouse counters, not screenshot pixels.
- `key-down` / `key-up`: send one raw `key` edge and advance exactly `cycles`
  chip clocks (zero by default). No automatic release or post-release delay is
  added. Use separate edges for each modifier; `modifiers` is rejected here.
  These actions allow paused-state captures and explicitly held chords. The
  step log's `queuedKeyboardEvents` helps detect events waiting for guest acknowledgement.
- `click`: mouse-port button, default `button: Fire` (left mouse), `Second`
  for right mouse. Button flags can be combined, e.g. `Fire, Second`.
- `joystick`: joystick-port button, default Fire; Up/Down/Left/Right are also
  available. `cycles` optionally changes the hold duration.
- `disk`: `disk` ADF filename and optional `drive` index (default DF0).
- `write-protect`: explicit `writeProtected: true`/`false`, optional `drive`
  (default DF0), and `cycles` (zero by default). Changes the existing medium's
  tab without ejecting, reloading, or clearing a partial write. Use private
  copies for writable tests; loading a savestate retains its recorded media.
- `eject`: remove media from `drive` (0..3, default DF0). Use a following
  `wait` before `disk` to test a real empty-drive interval during a swap.
- `reset`: warm reset by default, `cold: true` for a power cycle.
- `restore`: save the current core state, advance one frame, restore and compare
  serialized CPU and bus state (including Chip/Slow/Fast RAM, ROM overlay and
  floppies), beam timing and the presented framebuffer. The HDF is not rolled back.
  `resetBeforeRestore: true` resets the machine before loading that saved state;
  combine with `cold: true` to verify restore after a power cycle. The same
  CPU/memory/beam/pixel equality checks apply.
- `checkpoint`: write a continuation savestate named `<label>.amigasav` at an
  intermediate step, without advancing the guest; the label must be a filename.

The `DESERTS1` source flow now mounts the user's data disks in DF1/DF2 before
boot, advances the crack/title/story/menu sequence, starts the campaign with
the left mouse button, lifts off with joystick Down, flies/fires, opens the map
with F10 (raw 89), closes it and checks a power-cycle restore. Its cold boot
prefix and mission continuation are reviewed on A500/A1200; other generated
models still require actual execution and review. The original five-step flow
stopped at the crack screen. Two initial non-ROM CPU-detection exceptions are
allowed; a zero exit or valid manifest alone is still not a gameplay pass.
See MachineModels.md's keyboard recovery follow-up for bounded acceptance and
the separately reviewed map-to-flight continuation.

The optional scenario `maximumNonRomExceptions` requires `traceExceptions: true`;
its limit applies to observed synchronous exceptions outside Kickstart. When
exceeded, the action's PNG/log is written and the scenario fails. Use only for
reviewed paths: some software deliberately generates such exceptions.

`Tools/siedler-intro-swap-scenarios.json` exercises the actual Intro Disk 1 ->
requested Disk 2 -> requested Boot Disk 3 path on A500 / 2 MiB Chip + 8 MiB Fast,
with both Fast and Compatible timing and a restore during pending insertion.
It rejects guest CPU faults instead of allowing a frozen black frame to count
as successful script execution. These shorter cases deliberately retain the
original Disk 1 and stop at the subsequent game-loader Disk-2 request. That
image has damaged filesystem allocation metadata: its delayed Kickstart-1.3
Validator reply can cause an Exec AN_FreeTwice alert during the later handoff.
The non-ROM exception guard alone cannot detect software alerts.
Use a fresh output and your own PDX disks:

```powershell
dotnet Tools/GameRunner/bin/x64/Release/net10.0-windows7.0/GameRunner.dll Tools/siedler-intro-swap-scenarios.json C:/fresh/siedler-intro Siedler-Intro-Fast
```

For the full Kickstart-1.3 route, first create a new repaired Disk 1 with
`Tools/prepare_siedler_intro_disk.py`. It accepts only the reviewed PDX image
(source SHA-256 `ce24f8af192683c6b143bb9c4d6d3e3da707dc79541da99398e62ad11fe38de0`),
fixes the root/bitmap metadata and checksums, and refuses to overwrite any file.
Only four 32-bit metadata words change; no boot/game code changes. No ADFs or
ROMs are distributed. Place the new ADF alongside your original three disks:

```powershell
$media = Join-Path $env:USERPROFILE 'Documents/AmigaEmulator'
python Tools/prepare_siedler_intro_disk.py "$media/Originale/Die Siedler (disk 1).adf" "$media/Die Siedler (disk 1).adf"
dotnet Tools/GameRunner/bin/x64/Release/net10.0-windows7.0/GameRunner.dll Tools/siedler-kick13-gameplay-scenario.json C:/fresh/siedler-kick13
```

The full scenarios use your `%APPDATA%/AmigaEmulator/roms/KICK13.ROM`, A500,
2 MiB Chip + 8 MiB Fast, Fast and Compatible timing, all requested disk changes,
the symbol/menu inputs, mission start, castle construction and exact state restore. Reviewed framebuffer
hashes reject frozen loader/alert screens as well as guest CPU faults. See the
filesystem-repair evidence in `AmigaEmulator/Docs/MachineModels.md`.

Every action accepts a `label` for screenshots. `strict` does not imply that
software is compatible; both profiles may require title-specific investigation.
Actions can also specify `expectedFrameHash` (SHA-256 of presented ARGB pixels)
and `minimumHardDiskReads`. A mismatch fails the case after writing its screenshot.
The native-ROM HDF cases assert the manually reviewed Initial CLI image and at
least 14 sector reads, so a black screen or an idle CPU cannot count as a pass.
Hashes require the same formatted test volume; adapt them after reviewing a
different HDF. Gameplay screenshots still require visual review.
Several legacy cases remain initial smoke sequences. Lemmings, Siedler,
Maniac Mansion, Hostages, Fire and Ice, Tiny Bobble and Turrican III have reviewed menu/level/input
sequences, cold-booted on all eight native-ROM boards. Alien Breed also passes
its actual Disk-2 request, long load and controlled level on all eight boards.
Speedball 2 passes cold Practice on all five 68000 boards; a subsequent scrolling
check reviews 32 consecutive fields per board, with fixed presentation geometry
and no non-ROM exceptions. Its current local disk's 020/030 loader remains incompatible.
Cannon Fodder also has
reviewed cold gameplay on seven native boards (A2000 OCS remains unresolved).
Pinball Fantasies previously passed its cold Partyland/plunger/flipper sequence on the five
68000 boards and A1200; Moonstone passes cold Practice/combat on the five
68000 boards. Their current advanced-board failures are recorded separately.
Maniac Mansion and
Zak omit the old universal Space press, which pauses them.
Other titles still require appropriate disk swaps and menu selections, as
documented in the compatibility review report; a completed script is not a pass.

Floppy mounts default to read-only. `floppyReadOnly: false` explicitly enables
writes for subsequent boot/disk mounts; use only new disposable ADF copies for
write/save controls. Loading a saved machine keeps its already-mounted media's
saved protection flags until an explicit `disk` action replaces them. Writable
ADF buffers are persisted on replacement/ejection and system disposal. Savestate
file loading retains the saved disk buffer, its pending-write flag and partially
received MFM sector; pending writes can subsequently be flushed to that copy.
An HDF's already-issued writes remain external and are not rolled back.

For HDD boot cases, omit `boot` and provide `hardDisk` (absolute whole-disk HDF
path) and `kickstartRom` (absolute ROM path matching the selected motherboard).
A3000 uses SCSI target 0; A4000Ec030 uses DD2020 IDE. `hardDiskReadOnly`
defaults to true; set false only for a disposable writable test image.
When replaying a `save`, an explicit `hardDisk` overrides its saved HDF mount
after loading; use this to replay mouse/input tests against a separate copy.
`createHardDiskMiB` optionally creates a new RDB/DH0 image before mounting it
(refuses existing files; the partition must still be formatted/installed).
`traceIde: true` adds `ide-trace.txt` with controller writes and Autoconfig
accesses. Step diagnostics include HDF sector counters and Fast-RAM assignment.

`pcmciaSramMiB` inserts a 1/2/4 MiB SRAM card on A600/A1200. It conflicts with
8 MiB Fast RAM. `expectPcmciaRam: true` checks guest Exec memory registration.
`traceExceptions: true` records synchronous CPU exceptions for investigation.
Step diagnostics also count instructions outside the Kickstart ROM range and
retain their first exception, including addresses above the 24-bit bus range.
These observations require review: software protection can deliberately use
exceptions. A visible image alone does not prove that the guest still runs.
Environment variables are expanded in ROM, HDF and media-root paths.

`traceCopper: true` records executed Copper instructions in `copper.jsonl`.
`traceCopperBuild: true` records CPU-created BPLCON0 instruction words and
following data words in Chip RAM, including byte-split bus stores. These are
optional diagnostics; keep them off for throughput measurements.

Generate the eight-model sweep using `Tools/run-model-gameplay-matrix.ps1` with
`-OutputDirectory` set to a fresh external directory. `-Games` selects existing
manifest templates and `-Models` selects boards. `-ManifestOnly` writes the cases
without running them. A successful process exit still requires visual review;
newer-Kickstart/68000 Tinyus currently remains an open case. Longer waits can
restore its visible playfield, but further input can trigger another blank phase.

The larger local-library sweep is `Tools/run-all-games-model-matrix.ps1`.
It combines all three game manifests and checks boot disks and native ROMs.
`Tools/model-gameplay-variants.json` inserts reviewed model-specific steps at
a unique action label. Siedler's larger-memory profiles request Disk 2 before
the symbol/menu stage; the low-memory A500/A2000 OCS path proceeds directly.
Maniac's character selection and Hostages' mission inputs also retain their
reviewed model-specific continuations.
`Tools/game-media-catalog.json` groups all 50 currently known ADFs into 30
game/disk-set entries and the SELECTOR utility. Utilities are excluded unless
`-IncludeUtilities` is specified. The default sweep generates 240 scenarios.
The inventory records SHA-256 hashes, disk-set membership and uncovered media;
an uncovered image is never silently reported as tested. A500 retains the
Siedler/Cannon Fodder templates' preferred one-MiB Chip profiles; A2000 OCS uses its supported
512-KiB Chip/512-KiB Slow layout. Every run requires a fresh
external output directory. Use `-Games` and `-Models` for targeted reruns.
`-IncludeCompatibilityProfiles` additionally uses
`Tools/game-compatibility-profiles.json`, currently adding four North & South
ROM/RAM cases, one Cannon Fodder A1200 Chip-only case, three Lotus and three
Tinyus 68000/ECS Kickstart-1.3 cases (251 total). The Tinyus alternatives pass
cold gameplay, directional/shooting input, Continue/restart and exact restore
on the keyboard-handshake-fixed core. Its newer-ROM transitions remain open.
They retain the native cases and selected CPUs.
The A3000 Kickstart 1.3 case is a direct diagnostic ROM configuration; it does
not test the real A3000 SuperKickstart bootstrap or installed-OS/SCSI support.
These are test profiles, not a declaration that every game is compatible with
every ROM, RAM expansion or CPU.

The reviewed local Moonstone CSL Disk 1 uses user-mode MOVEC to enter its
installed illegal-opcode handler. That works on 68000 but takes privilege vector
8 on 020/030. `python Tools/prepare_moonstone_020_disk.py <source-adf> <new-adf>`
creates a separate compatible copy using ILLEGAL/NOP and recalculates the owning
OFS block checksum. It accepts only the exact reviewed SHA-256/signature and
refuses an existing destination; it supplies no media. Original disks and CPU
exception semantics are preserved. Disk 2/3 remain unchanged.
Fresh cold Practice/combat/input/restore has been reviewed on A1200, A3000 and
A4000Ec030 with that copy. Pass `-IncludeCompatibilityProfiles
-MoonstoneCompatibleBoot <new-adf>` to the library matrix to retain the original
native cases and add these three separate cases (254 total for the full sweep).
The generated case keeps the compatible copy when Disk 1 is reinserted and
records its hash in the inventory. Without that argument, coverage remains 251.

The exact reviewed Pinball Fantasies PDII loader retains bit 31 in a relative
transfer pointer, causing an unmapped write on native A3000/A4000 CPUs.
`python Tools/prepare_pinball_32bit_disk.py <source-disk1> <new-disk1>` creates
a separate copy that strips the tag before adding the relative base. It checks
the full source SHA-256, the 38-byte loader window and the reviewed output hash,
and refuses existing destinations. Disk 2/3 and the emulated address bus are
unchanged. Cold Partyland/input/restore passes on native A3000/A4000 with this
copy, with A500/A1200 cold controls and final-core desktop power-cycle restore.
Original media still has its documented processor restriction.
Use `-IncludeCompatibilityProfiles -PinballCompatibleBoot <new-disk1>` to add
separate Partyland cases for those two desktop boards, retaining their originals.
`-PinballTables Partyland,SpeedDevils,BillionDollarGameshow,StonesNBones` selects
all four tables, with the correct course disk and F1/F2 selection in each cold
case. The eight optional sequences pass preflight; execution and capture review
are still required for every selected board/table. With both compatibility disks,
the full default sweep has 256 cases, or 262 with all four Pinball tables.

Attach external floppy disks with `disk` actions before the first boot `wait`
when testing DOS volumes on DF1–DF3. Kickstart enumerates physical drives during
startup; inserting the first disk later also attaches a drive and can require
a reset or a guest Mount command. Ejecting media retains that drive's physical
attachment. The emulated external drive identifies as standard 3.5-inch DD
even when empty; identification does not make an empty disk readable. HD drives
are not implemented.

The reviewed Civilization source starts an Earth game with Civilization disks 1–4 attached
to DF0–DF3 before boot, waits for evolution/setup, names Rome and continues into
the city screen. The Zak source keeps disk 1 in DF0, puts disk 2 in preboot DF1,
allows the full intro and checks bedroom mouse walking. These replace earlier
menu/intro-only scripts; compatibility still needs capture/input review on
each model. The normal MEDIA menu now exposes all four drive targets. After
first attaching an external drive during a session, reset for DOS enumeration.

The final physical-INDEX core has fresh reviewed Partyland gameplay/input/restore
on all five 68000 boards and A1200, including native A500+/Kickstart 2.0. All six
gameplay checkpoints also pass warm-reset and power-cycle exact restore. The
generated ADF sector origin now stays aligned with its physical INDEX rather
than wrapping on an independent clock; this fixes a ten-sector loader skip.
The modeled DD data cadence and 200 ms PAL INDEX period are retained by padding
the unused tail with gap flux. This is not raw/IPF or bitcell-PLL support.
The unchanged native A3000/A4000 Pinball source limitation, broader Tinyus
sessions and other open cases remain in Docs/GameModelCompatibility-2026-10-09.md.
The full-library cold/input/capture review remains in progress.

2026-10-11 review correction: the earlier two-million-clock Pinball plunger
hold did not establish a launched ball. Table artwork and an injected key alone
are insufficient. The source now holds Down for 15 million chip clocks, then
waits one million, sends separate left/right flipper inputs, captures live
play, presses/releases P, checks a held pause, power-cycles/restores and sends
Esc/Y to the write-enable requester. The revised suffix has reviewed native
A3000/A4000 continuations for all four tables, with live mode, frozen pause
pixels and no new non-ROM faults. Their cold loading prefixes are retained
separately; this is not a newly executed uninterrupted eight-case cold run.
The controls follow the [original game manual](https://www.lemonamiga.com/doc/pinball-fantasies/1217).

For Pinball's return path, cancel saving with the mouse, supply the requested
main disk, then a course disk; the first requester after cancellation is not
the selection menu. An A3000 Partyland continuation has reviewed evidence all
the way back to that menu. Persistent highscore writes remain unaccepted.
See MachineModels.md for the private-DMA trace and current physical-track/header
mismatch. Original media and raw/custom-format compatibility remain separate.
