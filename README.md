# AmigaEmulator

AmigaEmulator is a C#/.NET Amiga emulator with a WinForms and SharpPixels
frontend. Its structure follows the same general idea as the C64Emulator:
a small desktop application, a local SharpPixels rendering layer, in-emulator
overlay menus, persistent user settings, save states, and a dedicated test
runner for emulator regressions.

The project goal is not to draw a fake boot screen or use a shortcut renderer.
The target is a real Amiga 500 hardware path: Kickstart ROM, Motorola 68000,
Chip RAM, Agnus, Denise, Paula, CIA, disk DMA, Copper, Blitter, bitplanes,
sprites, audio, keyboard, mouse, and joystick input should all cooperate to
produce the final machine state and output.

## Current Status

This emulator is an active work-in-progress project.

Implemented or partially implemented:

- Amiga 500 PAL model with a 7.093790 MHz CPU timing base.
- A500+/A600, revision-specific A2000 OCS/ECS, A1200, A3000/030 and
  A4000/030 preview models. ECS paths
  include extended display windows, border blank, EHB control and blitter sizes;
  A600/A1200 include built-in IDE and PCMCIA SRAM. A3000 has SCSI/Super DMAC;
  A4000/030 has its own IDE wiring. Desktop models include motherboard Fast RAM
  and a Ricoh clock; A500+/A2000 include an MSM6242 battery clock. See
  [machine models and remaining milestones](AmigaEmulator/Docs/MachineModels.md).
- Motorola 68000 and extended 020/030 integer cores, including a 68030 MMU.
- AGA subset: 256-color palette, eight planes, HAM8, wide DMA and sprites.
- Kickstart ROM resolver and ROM selection through the overlay UI.
- Real Kickstart 1.3 insert-disk path through ROM code, Copper, Blitter,
  bitplane DMA, Chip RAM, and Denise output.
- Standard 880 KiB AmigaDOS ADF loading and writable-sector DMA with explicit flush.
- File-backed HDF hard disks through an optional A600 Gayle ATA PIO controller,
  including real Kickstart 3.1 boot to AmigaDOS without floppy media.
- Chip/Slow RAM profiles and 2/4/8 MiB Fast RAM with Zorro II Autoconfig.
- Paula disk and audio path with MFM track streams, disk activity status, and
  audio output.
- CIA 8520 basics, keyboard input, mouse input, and joystick input.
- F12 save state overlay.
- SharpPixels video output with scaling and post-processing filters.
- Automated tests for CPU, Blitter, Copper, CIA, input, disk, selected golden
  ADF cases, and optional vAmigaTS cases.

Still incomplete:

- Not every game is glitch-free yet.
- ECS/AGA previews do not yet cover all fine scrolling, superhires, interlace
  or programmable monitor timings. Caches, fault restart and RTC timing are
  approximate. Floating-point coprocessors and A4000/040 are not implemented.
- Matching Kickstart 3.1/40.68 boots the test HDF to Initial CLI on A1200,
  A3000 and A4000/030, including cold/warm restart and restore. A3000's WD
  register mirrors and selection/completion interrupts are corrected.
  A4000/030 AmigaOS 3.1.4 on a new HDF also passes basic desktop, Shell/Clock,
  cold/warm/restore and file-persistence checks. Other models and broader OS
  coverage remain open. Generic-ROM runs remain separate compatibility checks.
- A2000 motherboard profiles boot, but real Zorro storage/SCSI boards and
  Workbench installation still require implementation/verification.
- OCS DMA timing, sprite priority, Blitter/CPU arbitration, and loader edge
  cases need broader hardware/reference validation despite implemented slot
  scheduling, per-plane fetches and region-aware bus contention.
- Precompensation, ADKCON edge cases, and exact disk-loader behavior are not
  finished. Basic disk write DMA, sector decoding and explicit flush are tested.
- vAmigaTS support exists, but full byte-exact reference-frame comparison is
  not the final state yet.

The emulator runs real Amiga hardware paths and remains under active
compatibility and timing development. Current implementation details and
remaining work are tracked in `AmigaEmulator/Docs/A500ChipAuditPlan.md`.

## Target Machine

The primary target is:

- Commodore Amiga 500
- PAL OCS
- Kickstart 1.3
- 512 KiB Chip RAM base model plus selectable expansion profiles
  (the frontend currently defaults to 1 MiB Chip RAM for game loaders)
- DF0 as the first internal floppy drive

Additional RAM profiles and ROMs can be selected from the emulator menu when
the corresponding local files are available. **MACHINE → MODEL** switches
between A500, A500+, A600, A2000 OCS/ECS, A1200, A3000 and A4000/030 previews
and restarts the guest. RAM remains
separately selectable; older ECS models require 1/2 MiB Chip RAM without Slow RAM.
A1200/A3000/A4000 require 2 MiB Chip RAM, with optional 2/4/8 MiB Fast RAM.
A3000/A4000 use high motherboard Fast RAM instead of a Zorro II card.
The early A2000 OCS profile uses 512 KiB Chip RAM, optional 512 KiB motherboard
RAM at `$C00000`, and optional 2/4/8 MiB Zorro II Fast RAM. The ECS profile
also permits a 2 MiB Chip RAM Agnus upgrade. Switching revisions preserves
Fast RAM capacity while normalizing the motherboard RAM layout.
Model selection is persisted and recorded in new savestates. Old savestates
continue to load as A500.

## Hard disks and memory expansions

Open the overlay with Esc, then **HARD DISK**. Choose **NEW IMAGE SIZE**
(16–2048 MiB), then **CREATE IMAGE** and a new `.hdf` filename. This creates and
mounts a writable, whole-disk image with an RDB and bootable DH0 FFS partition.
The filesystem is initially unformatted: format DH0 and install your own Amiga
system from Workbench disks. Existing files are never overwritten by creation.
**MOUNT IMAGE** opens an existing raw whole-disk/RDB HDF; partition-only HDFs,
VHD/HDZ containers and host directories are not translated into drives.

Use **WRITE PROTECTION** to select read-only or read/write access. Completed
sector writes persist in the image; ejecting, reconfiguring, saving or closing
flushes it. Changing the controller/image/write mode restarts the guest machine.
The emulator operates on image files, not Windows physical disks.

Select a Kickstart for the actual motherboard and its controller (A600/A1200
Gayle IDE, A3000 SCSI or A4000 IDE). The optional legacy IDE expansion requires
an A600-compatible Kickstart (2.05 or 3.1, with the Gayle IDE `scsi.device`)
in the ROM menu. Ordinary A500 Kickstart 1.3 has no built-in driver for this
controller. **BOOT FROM HD** enables the persistent hard-disk boot preference,
ejects floppies for the current session and restarts. When enabled, startup does
not automatically restore DF0. Disable it to return to normal floppy startup;
floppies can still be inserted explicitly through MEDIA.

Select memory through **MACHINE → RAM** (also available in SETTINGS): 512 KiB
Chip, 512 KiB Chip + 512 KiB Slow, 1/2 MiB Chip, 1/2 MiB Chip + 2/4 MiB Fast,
or 2 MiB Chip + 8 MiB Fast. Changing a profile restarts the machine. Fast RAM
is exposed as a Zorro II expansion on older boards and A1200; A3000/A4000 use
motherboard RAM ending at `$08000000`.

Savestates preserve controller registers, partially transferred sectors and
the mounted image path/access mode. The HDF remains an external, mutable file;
loading a savestate does **not** rewind disk contents. Keep the image alongside
its saves and make a separate copy when a disk snapshot is needed. Old savestates
still load with the IDE controller disabled. See
[storage validation](AmigaEmulator/Docs/StorageExpansion.md) for tested scope.

## Repository Layout

```text
AmigaEmulator/
|-- AmigaEmulator.sln
|-- AmigaEmulator/          Main application and emulator core
|   |-- Audio/              Paula audio output and resampling
|   |-- Cia/                CIA 8520 emulation
|   |-- Core/               System model, settings, clock, ROM resolver
|   |-- Cpu/                Motorola 68000/020/030 integer cores and MMU
|   |-- CustomChips/        Agnus, Denise, Paula, Copper, Blitter, DMA
|   |-- DevTools/           Tracing and diagnostics helpers
|   |-- Docs/               Technical plans and test specifications
|   |-- Input/              Keyboard, mouse, and joystick input
|   |-- Media/              ADF, MFM, and floppy controller code
|   |-- Memory/             Chip/Slow/Fast RAM, bus, custom register map
|   |-- SaveStates/         Save state file format and slot handling
|   |-- Video/              Framebuffer, presenter, and filters
|   |-- AmigaWindow.cs      WinForms/SharpPixels host and overlay menus
|   `-- Program.cs          Application entry point and CLI diagnostics
|-- AmigaEmulator.Tests/    Custom test runner without external test framework
|-- SharpPixels/            Local SharpPixels renderer
`-- Tools/                  Helper scripts for matrix and vAmigaTS runs
```

## Requirements

- Windows x64
- A .NET SDK that supports `net10.0-windows7.0`
- Optional: Visual Studio with the .NET desktop workload
- A legally obtained Kickstart ROM
- Optional: legally obtained ADF disk images

ROMs, Workbench disks, and games are not included in this repository.

## ROMs and User Data

The emulator keeps user-provided and generated data outside the repository.

Kickstart ROM directory:

```text
%APPDATA%\AmigaEmulator\roms
```

Save state directory:

```text
%APPDATA%\AmigaEmulator\saves
```

Settings file:

```text
%APPDATA%\AmigaEmulator\settings.json
```

Default ADF media directory:

```text
%USERPROFILE%\Documents\AmigaEmulator
```

The media directory is created automatically on startup if it does not already
exist. The media browser remembers the last selected directory afterwards.

Common Kickstart file names that can be detected:

- `kick13.rom`
- `kickstart13.rom`
- `amiga-os-130.rom`
- `kick12.rom`
- `kick20.rom`

The ROM resolver checks file size, reset-vector structure, and known hashes.
Kickstart 1.3 is recommended for the main Amiga 500 target.

## Building

Release build:

```powershell
dotnet build .\AmigaEmulator.sln -c Release -p:Platform=x64
```

Run from the build output:

```powershell
.\AmigaEmulator\bin\x64\Release\AmigaEmulator.exe
```

Run directly through `dotnet run`:

```powershell
dotnet run --project .\AmigaEmulator\AmigaEmulator.csproj -c Release -p:Platform=x64
```

Normal emulator testing should use Release mode. Debug builds can become too
slow for meaningful cycle and frame-rate behavior.

## Controls

Global hotkeys:

| Key | Function |
| --- | --- |
| `F9` | Toggle turbo mode |
| `F10` | Open or close the main menu |
| `F11` | Toggle fullscreen/windowed mode |
| `F12` | Open the save state menu |

Overlay navigation:

- Arrow keys to move through menu entries
- `Enter` to open or select
- `Esc` to close or go back
- Mouse wheel to scroll long menus

The main menu contains submenus for media, ROMs, machine/RAM settings, input,
display, reset options, and save states.

## Video

`F10` > Display (or Settings) > `IMAGE SIZE` selects `KEEP ASPECT` or
`FILL SCREEN`. Fill stretches the cropped picture across the entire client area;
with `F11` fullscreen it fills the monitor, including the area normally occupied
by the taskbar. The setting is saved. All upscale modes keep the same displayed
bounds and mouse coordinates as the unscaled image. Leaving fullscreen restores
the previous window bounds and state.

The visible image is produced by the emulated Amiga framebuffer. Scaling and
filters are applied afterwards as monitor/TV-style post-processing, not as a
shortcut in the Amiga graphics path.

Host presentation uses DIW/DDF and per-line display metadata for its source
geometry. Black backgrounds and sparse top-edge artwork are preserved; frame
brightness does not automatically shift or crop the picture.

Available video options include:

- Sharp
- CRT
- TV
- No upscaling
- 2x/3x sharp (Scale2x/Scale3x neighbor rules)
- 4x sharp (two Scale2x passes)
- 2x/3x/4x smooth (pixel-center bilinear interpolation)
- 4x soft (sharp edges mixed with smooth interpolation)

Upscaling is applied by the CPU presentation renderer before the final display
stretch. Source crop boundaries clamp the neighbors, and buffers are reused.
The serialized `Hq2x`, `Hq3x`, `Hq4x` and `Xbrz` names remain for compatibility;
these smooth/soft options do not implement the HQx or xBRZ algorithms.

Managed failures during GUI frame updates or painting create a fresh
`%APPDATA%/AmigaEmulator/logs/host-error-*.txt` containing the original exception
and CPU, beam, floppy/DMA, input and display configuration. Reporting preserves
the existing exception handling and never writes guest media or savestates.
For a game-specific failure, create a new `F12` save immediately before the
trigger so the exact guest state can be replayed.

## Accuracy profiles

The frontend defaults to `Fast`, which disables tracing and relaxes chip-bus
contention. `Compatible` and `Strict` enable region-aware bus arbitration and
tracing. These profiles do not promise complete cycle accuracy. Saved user
profile choices are preserved.

## Audio

Paula produces the emulator's audio output. The overlay exposes audio filter
options:

- Off
- LED
- LowPass

The disk debug/status overlay includes a drive activity LED similar to the
original Amiga 500 drive LED.

## Input

Implemented input paths:

- Amiga keyboard through PC keyboard mapping
- Joystick emulation
- Connected Windows USB/HID gamepads, polled automatically with hot-plug detection
- Mouse emulation
- CIA/input regression self-tests

Input behavior is still an area of active improvement because many games depend
on very specific CIA, joystick, and mouse behavior.

Gamepads use the joystick port selected under `F10` > Input (normally Amiga
port 2). The Input menu shows the detected Windows controller name and port;
selecting Gamepad refreshes discovery. Directional axes and an eight-way POV
hat are supported with a centered deadzone. The first four physical buttons
are Fire; buttons 5 and 6 are the additional Amiga buttons. Keyboard joystick
input remains available alongside the controller. With dual joysticks, the
first controller drives port 2 and the second drives port 1. Gamepad inputs
are released on disconnect, focus loss and while an overlay menu is open;
loading a savestate clears saved physical-controller buttons.

The native reader uses Windows `joyGetPosEx` and `joyGetDevCapsW`, which also
cover generic USB controllers that do not expose XInput. The data layouts and
flags follow Microsoft's [JOYINFOEX](https://learn.microsoft.com/en-us/windows/win32/api/joystickapi/ns-joystickapi-joyinfoex)
and [JOYCAPSW](https://learn.microsoft.com/en-us/windows/win32/api/joystickapi/ns-joystickapi-joycapsw)
documentation. Only controllers exposed through Windows joystick drivers are
currently covered; custom button remapping is not yet available.

## Save States

Save states are managed through `F12` and stored under:

```text
%APPDATA%\AmigaEmulator\saves
```

Slots store machine state including CPU state, RAM, custom chips, disk/media
metadata, and preview data. Save states are especially useful for debugging and
compatibility testing, but they do not replace real timing fixes.

## Tests

The tests live in a dedicated console project and do not require an external
test framework.

Build first:

```powershell
dotnet build .\AmigaEmulator.sln -c Release -p:Platform=x64
```

Run the default test suite:

```powershell
dotnet .\AmigaEmulator.Tests\bin\x64\Release\AmigaEmulator.Tests.dll
```

List test cases:

```powershell
dotnet .\AmigaEmulator.Tests\bin\x64\Release\AmigaEmulator.Tests.dll --list
```

Run filtered tests:

```powershell
dotnet .\AmigaEmulator.Tests\bin\x64\Release\AmigaEmulator.Tests.dll --filter CPU
dotnet .\AmigaEmulator.Tests\bin\x64\Release\AmigaEmulator.Tests.dll --filter Denise
dotnet .\AmigaEmulator.Tests\bin\x64\Release\AmigaEmulator.Tests.dll --filter GoldenADF --include-adf
```

Run optional vAmigaTS smoke tests:

```powershell
dotnet .\AmigaEmulator.Tests\bin\x64\Release\AmigaEmulator.Tests.dll --include-vamigats --filter vAmigaTS
```

Run broader vAmigaTS discovery:

```powershell
dotnet .\AmigaEmulator.Tests\bin\x64\Release\AmigaEmulator.Tests.dll --vamigats-all --vamigats-limit 50 --filter vAmigaTS
```

More details:

```text
AmigaEmulator/Docs/TestSuite.md
AmigaEmulator/Docs/TestSpecs/README.md
```

## Diagnostics and Developer Modes

The application contains several CLI diagnostics for emulator development.
Examples:

```powershell
dotnet .\AmigaEmulator\bin\x64\Release\AmigaEmulator.dll --check-roms
dotnet .\AmigaEmulator\bin\x64\Release\AmigaEmulator.dll --self-test-cpu
dotnet .\AmigaEmulator\bin\x64\Release\AmigaEmulator.dll --self-test-blitter
dotnet .\AmigaEmulator\bin\x64\Release\AmigaEmulator.dll --self-test-copper
dotnet .\AmigaEmulator\bin\x64\Release\AmigaEmulator.dll --self-test-cia
dotnet .\AmigaEmulator\bin\x64\Release\AmigaEmulator.dll --self-test-disk
dotnet .\AmigaEmulator\bin\x64\Release\AmigaEmulator.dll --self-test-input
dotnet .\AmigaEmulator\bin\x64\Release\AmigaEmulator.dll --assert-kickstart-screen-path 120000000 --model a500
dotnet .\AmigaEmulator\bin\x64\Release\AmigaEmulator.dll --adf-test-matrix "%USERPROFILE%\Documents\AmigaEmulator" 30000000 .\tmp\adf-matrix --limit 20 --model a500
```

Helper scripts:

```text
Tools/run-game-matrix.ps1
Tools/run-vamigats-full.ps1
Tools/run-amiga-gui-stability.ps1
```

## Gameplay/performance review (2026-10-07)

New settings use Compatible chip-bus arbitration without continuous CPU/DMA
tracing; Fast remains selectable and existing saved profile choices are respected.
The local boot entries are not yet all certified playable. See [the reviewed
results and open cases](AmigaEmulator/Docs/GameplayReview-2026-10-07.md) and
[the scripted gameplay runner](Tools/GameRunner/README.md).

The current model roadmap, native-ROM checks and paired throughput measurements
are tracked in [MachineModels](AmigaEmulator/Docs/MachineModels.md#approved-roadmap-progress-2026-10-09).
The full roadmap is still in progress. The user-provided AmigaOS 3.1.4 files and
matching A4000 ROM now pass basic installed-OS checks; wider coverage remains open.

## License

This project is licensed under the Apache License 2.0. See `LICENSE`.

Kickstart ROMs, Workbench disks, and game/ADF files are not covered by this
repository license and must be provided separately by the user.
