# Scripted gameplay review

Run from the solution directory with the local Kickstart ROM and your own ADFs:

```powershell
dotnet build Tools/GameRunner/GameRunner.csproj -c Release -p:Platform=x64
dotnet Tools/GameRunner/bin/x64/Release/net10.0-windows7.0/GameRunner.dll Tools/gameplay-scenarios.json ../tmp/gameplay-review Alien-Breed
```

Omit the final case name to run the whole manifest. A missing case returns exit
code 2; an exception returns 1. Successful execution returns 0 **only for the
script execution**, never a gameplay or graphics pass. Review the captured
screenshots. Cracker screens, menus, credits and disk requests are not gameplay.

Each case creates numbered PNGs, `steps.jsonl` and a continuation
`checkpoint.amigasav`. Log entries include raw CPU/drive diagnostics, frame
number, presentation bounds, pixel hash and `gameplay: requires-visual-review`.
Disk images are mounted read-only. Output filenames are replaced on reruns;
use a fresh output directory to preserve earlier evidence.
Step logs also include Paula DMA counters/queue state and input-port modes and
JOY0DAT, to distinguish media/loader failures from pointer/input failures.

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
- `click`: mouse-port button, default `button: Fire` (left mouse).
- `joystick`: joystick-port button, default Fire; Up/Down/Left/Right are also
  available. `cycles` optionally changes the hold duration.
- `disk`: `disk` ADF filename and optional `drive` index (default DF0).
- `reset`: warm reset by default, `cold: true` for a power cycle.
- `restore`: save the current core state, advance one frame, restore and check
  that the captured framebuffer hash is unchanged. The HDF is not rolled back.
- `checkpoint`: write a continuation savestate named `<label>.amigasav` at an
  intermediate step, without advancing the guest; the label must be a filename.

Every action accepts a `label` for screenshots. `strict` does not imply that
software is compatible; both profiles may require title-specific investigation.
Actions can also specify `expectedFrameHash` (SHA-256 of presented ARGB pixels)
and `minimumHardDiskReads`. A mismatch fails the case after writing its screenshot.
The native-ROM HDF cases assert the manually reviewed Initial CLI image and at
least 14 sector reads, so a black screen or an idle CPU cannot count as a pass.
Hashes require the same formatted test volume; adapt them after reviewing a
different HDF. Gameplay screenshots still require visual review.
The supplied manifest is an initial smoke sweep. Real gameplay requires
appropriate disk swaps and menu selections, as demonstrated in the review report.

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
Environment variables are expanded in ROM, HDF and media-root paths.

`traceCopper: true` records executed Copper instructions in `copper.jsonl`.
`traceCopperBuild: true` records CPU-created BPLCON0 instruction words and
following data words in Chip RAM, including byte-split bus stores. These are
optional diagnostics; keep them off for throughput measurements.

Generate the eight-model sweep using `Tools/run-model-gameplay-matrix.ps1` with
`-OutputDirectory` set to a fresh external directory. `-Games` selects existing
manifest templates and `-Models` selects boards. `-ManifestOnly` writes the cases
without running them. A successful process exit still requires visual review;
newer-Kickstart/68000 Tinyus currently remains an open case.
