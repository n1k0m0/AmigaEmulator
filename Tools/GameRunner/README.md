# Scripted gameplay review

For isolated 32-bit expansion experiments, an A3000 or A4000/030 scenario
may set `"zorroIIIRamMiB": 8`, `16`, or `32`. This attaches one development
Zorro III RAM board through native Autoconfig; it is a cold-boot option and
is not an installed-OS or general Zorro III conformance claim. The step JSON
reports its assigned address and observed RAM accesses.

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
covered separately by `Tools/cannon-df0-cold-gameplay-scenario.json` on native
A500/Kickstart 1.3 with 512 KiB Chip + 512 KiB Slow. The reviewed source needs
boot-only graphics after its Disk-2 request: the tested DF0 sequence is Disk
1 -> 2 -> 1 -> 2 -> 1 -> 2, including the mission loader. Simply leaving Disk 2
inserted stalls behind the game's completed fade. The script supplies these
media round trips, checks the reviewed jungle/pause/restore hashes and sends a
movement order inside the playfield, with both Jools and Jops alive. No game
bytes, palettes or CPU exception semantics are modified. Keep Disk 1 in DF0 and
data disks in DF1/DF2 for the simpler reviewed setup. Single-drive requester
visibility, other boards' cold DF0 routes and later campaigns remain separate
gates. The earlier exact DF0 address exception is fixed by PAL field timing;
do not reclassify the missing-media black scene as that exception.
The same DF0 script now follows the first mission long enough to observe a
two-soldier loss, the game's fade back to its Mission 1 briefing and a real
retry from that briefing. It asserts the reviewed briefing and resumed jungle
frames; the brief black fade is recorded as a transition, never a gameplay
pass. A paired no-right-button control reaches the same fade and briefing, so
that transition is not attributed to the mouse button. Completing Mission 1
and entering later missions still needs a separate gameplay test.
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
Pass `--no-final-checkpoint` after the output directory or case name for a
space-conscious sweep. It keeps screenshots and `steps.jsonl` but omits the
automatic final `.amigasav` in every case. Explicit `checkpoint` actions still
write their named states. Use the default when a continuation save is needed.
For a replay whose screenshots were already visually reviewed, add
`--no-images` to suppress PNGs while retaining frame, native and interlaced
hashes, presence guards, CPU/input diagnostics and `steps.jsonl`. Image path
fields are null in that mode. It can be combined with `--no-final-checkpoint`;
do not use it for first-time visual acceptance of a scene.
For a targeted first visual review, use one or more `--image-label=LABEL`
options instead: only actions with those exact labels write logical and
available native/interlaced PNGs. Every action still records hashes and
guards. Unknown labels fail before any run or output directory is created;
`loaded` selects the initial capture. This option cannot be combined with `--no-images` and helps avoid
large diagnostic image trees or repeated full guest replays.
For selected scenes' full Denise raster before monitor cropping, combine
`--image-label=LABEL --raw-frame`. It adds `-raw.png`, `-raw-sprites.png`
and `-raw-playfield.png` beside the selected presented image, including
pixels outside the 704x288 viewport. Use them to locate whether a missing
edge is already absent in the source raster or one transient component;
the raw images may contain an unfinished current beam frame.
Any action can require an exact four-digit hexadecimal `expectedBplcon0`
to verify the active bitplane mode at that checkpoint. This catches a
ScreenMode selection or Use-click that leaves the prior mode running even
when an image still looks plausible.
When the completed frame contains native ECS/AGA SuperHires samples, each
capture also writes a `-native-shres.png` at twice the logical horizontal
resolution and records `nativeImagePath` and `nativeHash` in `steps.jsonl`.
An action may require an exact 64-digit `expectedNativeFrameHash`; the guard
fails if that frame has no native SuperHires output. The ordinary screenshot
and `expectedFrameHash` remain the established logical-resolution checks.
Use `GameRunner <scenario.json> --validate [case-name]` for a read-only
preflight of model/RAM layouts, referenced disks, keys and button names.
For a narrowly scoped guest RAM write diagnosis, set
`traceMemoryWriteAddresses` to 1–8 RAM byte addresses. The runner writes
`memory-writes.jsonl` with the frame, current CPU PC, A5/A7, four stack
longwords, write size/value and resulting watched byte. Add a
`trace-memory-start` action immediately before
the suspect interval and `trace-memory-stop` afterward to avoid a long trace.
This records bus writes; the PC is the emulator's current PC when the bus
observer runs, and may need disassembly context to identify the instruction.
Guest code may change A5 before the watched write; do not assume it still
points at the object that initiated an earlier collision.
Invalid selections return 1 before any cases start; a missing case returns 2.
Normal runs perform the same preflight before creating output directories.
Unknown JSON fields, missing raw `key` values and duplicate case names are
rejected. Use `key`/`button`, not `code`/`buttons`: older runners silently
ignored those spellings and could send key zero or default Fire instead.
Run `python Tools/test_game_runner.py` after building to check these guards
without ROMs, guest execution or disk creation.

The original SHRES guest diagnostic checks native 35-ns capture after a ROM
boot on all six supported ECS/AGA boards. A500+ and A1200 also check cold
restore and reset. Run from the solution directory with your own compatible
Kickstart ROMs in `%APPDATA%/AmigaEmulator/roms`:

```powershell
python Tools/GuestDiagnostics/build_shres_check_adf.py tmp/native-shres-check.adf
dotnet Tools/GameRunner/bin/x64/Release/net10.0-windows7.0/GameRunner.dll Tools/shres-guest-diagnostic-scenarios.json tmp/shres-review --no-final-checkpoint
```

The generator requires a fresh output filename. The scenario asserts exact
native hashes after boot, cold restore and a separate cold reset; inspect the
companion PNGs as well. This validates a guest boot block, not a full
Workbench SHRES application or an independent hardware reference image.

For the real A1200/AmigaOS 3.1.4 PAL SHRES+LACE Workbench path, use
`Tools/amigaos314-a1200-shres-lace-scenario.json`. It expects the user's
matching A1200 Kickstart 3.1.4 ROM at
`%APPDATA%/AmigaEmulator/roms/KICK314-A1200.ROM` and a **private copy** of
the installed OS HDF at `tmp/os-a1200-test.hdf`; the scenario opens that copy
read-only. It guards native and logical desktop, System and Prefs window,
cold restore and reset captures. It also guards three successive real Prefs
scroll positions (scrollbar drag and two arrow clicks), an exact cold restore
of the final scrolled image, and a separate reset to the original desktop.
`Tools/amigaos314-a1200-lace-exit-probe.json` uses the same private HDF and
ROM to enter PAL Super-High Res Laced, reopen ScreenMode Preferences, and
return to non-LACE PAL Super-High Res. The modal Use button requires the
pointer to land inside its small hit area. The probe guards the final logical
and 1408x288 native frames at two successive captures and after cold restore.
`Tools/amigaos314-a4000-lace-exit-probe.json` exercises the matching A4000/030
Kickstart 3.1.4 route against the same read-only private HDF copy and guards
its own logical/native images across LACE entry, non-LACE exit and restore.
Both probes use `expectNativeInterlacedFrame` to require a complete woven
native pair while LACE is active and its absence after the switch.
For mode-exit checks also use `expectInterlacedFrame`: native pairs exist
only for SuperHires, while logical field pairs also reveal Low Res or High
Res LACE. A false native-pair guard alone cannot prove LACE is off.
Inspect the full
`-native-interlaced.png` image while LACE is active; `-native-shres.png` is
only one 288-line field and is not a complete interlaced picture.
Each `steps.jsonl` capture also records `displayRegisters`: stored BPLCON0,
DIW/DDF, first two bitplane pointers and modulos. These are snapshots at
the action boundary; a Copper or CPU write later in the field may change
them, so use a timed trace before attributing a whole frame to one value.
Set scenario `traceDisplayControl` to true to write `display-control.jsonl`
with CPU/Copper display-register writes, frame, beam and current state.
Optional `trace-display-start` and `trace-display-stop` actions limit the
trace to the transition being investigated.

The same original generator can program PAL LACE and alternate COLOR00 by
polling the guest VPOSR long-field flop. The runner writes a separate 704x576
`-interlaced.png` and, for SHRES, a 1408x576 `-native-interlaced.png`.
The scenario guards both complete field pairs with
`expectedInterlacedFrameHash` and `expectedNativeInterlacedFrameHash`:

```powershell
python Tools/GuestDiagnostics/build_shres_check_adf.py tmp/native-lace-check.adf --lace
dotnet Tools/GameRunner/bin/x64/Release/net10.0-windows7.0/GameRunner.dll Tools/lace-guest-diagnostic-scenarios.json tmp/lace-review --no-final-checkpoint
```

Inspect the alternating red/blue scanlines in the A500+, A600, A2000 ECS,
A3000 and A1200 captures. Each case guards the complete field pair after
boot, cold restore and cold reset.
This validates the guest-to-runner field path. The frontend selects the
woven source after both fields arrive; a real application and independent
hardware comparison remain separate checks.

The original `--mixed-lace` diagnostic runs a Copper list that resets both
bitplane pointers, draws two-plane lores above raster $80, then switches to
SHRES for the lower part of the same interlaced field:

```powershell
python Tools/GuestDiagnostics/build_shres_check_adf.py tmp/native-mixed-lace-check.adf --mixed-lace
dotnet Tools/GameRunner/bin/x64/Release/net10.0-windows7.0/GameRunner.dll Tools/mixed-lace-guest-diagnostic-scenarios.json tmp/mixed-lace-review --no-final-checkpoint
```

The A500+ ECS and A1200 AGA scenarios guard the logical and native woven
images through cold restore and reset. In each native image, the upper
playfield's color runs span four 35 ns samples while the lower SHRES
playfield alternates every sample. The mode boundary remains visible at
the same raster on both boards.

The installed A4000 AmigaOS 3.1.4 image also has a read-only PAL High Res
Laced acceptance script. Prepare a fresh private copy of your installed HDF
as `tmp/os-lace-test.hdf`, retain your matching 46.143 ROM at the path in the
scenario, then run:

```powershell
dotnet Tools/GameRunner/bin/x64/Release/net10.0-windows7.0/GameRunner.dll Tools/amigaos314-a4000-lace-scenario.json tmp/os-lace-review --no-final-checkpoint
```

It opens Workbench ScreenMode Preferences, selects PAL High Res Laced,
confirms the screen, moves the pointer, opens Execute Command, then cold
resets back to the ordinary desktop. The hash guards cover the real laced
Workbench and dialog plus the restored non-laced desktop. The source HDF is
never mounted by the scenario; remove the private copy and captures after
review. Only the user's own ROM and OS files can run this scenario.

`Tools/amigaos314-a4000-laced-clock.json` continues the same read-only
PAL High Res Laced Workbench route through `Run SYS:Utilities/Clock`, closes
the covering CLI Output Window, observes the analog clock twice, then closes
Clock and returns to Workbench. Run it with `--no-final-checkpoint` and
`--image-label=clock-after-output-gadget --image-label=clock-late
--image-label=workbench-after-clock`. The script checks the interlaced mode,
zero non-ROM exceptions and zero HDF writes; visually review the captures
because the live clock face changes and has no fixed image hash.

The same private HDF and ROM can exercise Workbench's PAL Super-High Res
Laced mode through the native 1408x576 capture path:

```powershell
dotnet Tools/GameRunner/bin/x64/Release/net10.0-windows7.0/GameRunner.dll Tools/amigaos314-a4000-shres-lace-scenario.json tmp/os-shres-lace-review --no-final-checkpoint
```

This scenario checks both the woven logical and native images, moves the
pointer, opens and cancels Execute Command, double-clicks System, drags its
window, cold-restores that window, then opens and scrolls the populated Prefs
drawer three times. A second cold restore verifies the scrolled image; a cold
reset verifies the ordinary desktop. Inspect the native interlaced PNGs as
well as the hash guards.

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
number, presentation bounds, pixel hash, `nonBlackPixels` and
`gameplay: requires-visual-review`. An action can set `minimumNonBlackPixels`
to reject an all-black stall at a scene where output should be visible (e.g.
`1000` after mission loading). The allowed range is 0..202752; zero disables
the guard. This condition cannot establish gameplay on its own: menus,
error screens and game-over output still need visual/input review.
For a known visible failure scene, an action may specify
`forbiddenFrameHashes: ["<64-character SHA-256>"]`. The runner rejects a
matching framebuffer after recording its diagnostic PNG and step log. Hashes
must be complete hexadecimal SHA-256 values; invalid values fail preflight
before outputs or disk creation. This catches reviewed exact loss/menu frames
without claiming that all other frames are playable. Prefer
`expectedFrameHash` when a scene has a stable exact gameplay reference.
Malformed expected hashes are rejected during preflight before any guest run
or output directory is created.
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
  `mouseStepCycles` optionally sets the chip clocks between steps; zero or
  omission preserves the established 120,000 clocks per step. Use the same
  pacing in input and control branches when targeting a moving sprite. Very
  short pacing can leave a guest cursor short of the final raw JOY0 counter
  because the game polls movement over time; inspect the actual cursor capture.
- `mouse-button-down` / `mouse-button-up`: hold or release exactly one mouse
  button (`Fire`, `Second` or `Third`) on port 0 and advance exactly `cycles`
  chip clocks (zero by default). Separate edges permit a held right-button
  aim followed by a left-button grenade press; release both buttons explicitly.
  Each capture records `input.ciaAPortA` and `input.potInp` as hexadecimal
  guest-visible register values, so a held-button chord can be checked without
  inferring it from a screenshot. An action can assert these readings with
  `expectedCiaAPortA` (two hex digits) and `expectedPotInp` (four hex digits);
  the runner retains the capture and fails the scenario if either differs.
- `key-down` / `key-up`: send one raw `key` edge and advance exactly `cycles`
  chip clocks (zero by default). No automatic release or post-release delay is
  added. Use separate edges for each modifier; `modifiers` is rejected here.
  These actions allow paused-state captures and explicitly held chords. The
  step log's `queuedKeyboardEvents` helps detect events waiting for guest acknowledgement.
- `click`: mouse-port button, default `button: Fire` (left mouse), `Second`
  for right mouse. Button flags can be combined, e.g. `Fire, Second`.
- `joystick`: joystick-port button, default Fire; Up/Down/Left/Right are also
  available. `cycles` optionally changes the hold duration. This legacy pulse
  always advances another 400,000 chip clocks after releasing its buttons.
- `joystick-down` / `joystick-up`: press or release explicit port-1 button
  flags (including chords such as `Up,Right`) and advance exactly `cycles`
  chip clocks, zero by default. Other held flags remain pressed. Use these
  edges with `wait` to sample a continuous jump or time a separate Fire press
  without the `joystick` pulse's implicit released interval; release every
  held flag explicitly before the end of the scenario. Each step logs `joy1`
  (the guest-visible JOY1DAT register); `expectedJoy1` accepts four hex digits
  to assert the actual direction held at a sampled edge or wait.
- `memoryWatchAddresses`: optional scenario-level list of up to 32 distinct
  RAM byte addresses below `$A00000` or in a desktop model's motherboard Fast
  RAM (decimal JSON integers). Every step logs
  their six-digit addresses and two-digit values in `memoryWatch`. These
  read-only peeks help correlate guest life, damage and animation state with
  reviewed frames without creating large memory dumps; the caller must still
  establish what each guest address means.
- `expectedMemoryBytes`: optional per-action object mapping decimal RAM byte
  addresses to two-digit hex values, for example
  `"expectedMemoryBytes": { "134182154": "FF" }`. It accepts up to 32
  inspectable addresses using the same RAM bounds as `memoryWatchAddresses`
  and fails the scenario when the guest byte differs. Use it to guard a
  reviewed live-gameplay checkpoint against a visually similar death frame;
  guest addresses remain specific to the loaded program and its state.
- `memory-snapshot`: explicit read-only RAM capture after optional `cycles`.
  Set decimal `memoryStart` and `memoryLength` (1..65536); only inspectable
  low RAM or the configured desktop motherboard Fast RAM is accepted. The
  action writes one named `.bin` beside `steps.jsonl`, allowing a bounded
  before/after comparison without dumping memory at every step. Store these
  diagnostic files under the ignored `tmp` directory and remove them after
  analysis where permitted.
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

`Tools/amigaos314-a600-lace-exit-probe.json` boots a private, read-only
AmigaOS 3.1.4 HDF on A600 with the user's purchased, unsplit/unswapped
Kickstart 3.1.4 A500-family ROM installed locally as
`%APPDATA%/AmigaEmulator/roms/KICK314-A500.ROM`. It opens ScreenMode, enters
PAL Super-High Res Laced, switches back to non-LACE, then checks persistence
and cold restore. The guarded run requires reviewed logical/native hashes and
the presence or absence of a complete native interlaced field pair at each
result. The ROM and HDF are private test prerequisites, not repository assets.
`Tools/amigaos314-a600-laced-system-window.json` continues the laced A600
desktop through a real System drawer open, drag and cold restore; reviewed
native interlaced hashes guard the visible window state.
`Tools/amigaos314-a2000-ecs-lace-exit-probe.json` runs the same real
ScreenMode entry/exit workflow on A2000 ECS with the private A500-family
Kickstart and read-only test HDF. Its independent replay guards the reviewed
logical/native hashes and the woven-frame presence across cold restore.
`Tools/amigaos314-a2000-ecs-laced-system-window.json` adds System drawer
opening, window drag and cold restore while the real A2000 Workbench remains
in PAL Super-High Res Laced mode.
`Tools/amigaos314-a500-system-window.json` boots the purchased 3.1.4
Workbench on the A500 profile with the existing private read-only HDF, opens
the System drawer and verifies its exact image through cold restore. The A500
profile uses a synthetic hard-disk expansion; this is not a stock A500
hard-disk configuration.
`Tools/amigaos314-a500plus-lace-exit-probe.json` and
`Tools/amigaos314-a500plus-laced-system-window.json` cover real A500+
Workbench ScreenMode entry/exit, System drawer drag and cold restore. They
use the same private 3.1.4 ROM family and read-only HDF; image hashes are
guarded separately for this model profile.

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
