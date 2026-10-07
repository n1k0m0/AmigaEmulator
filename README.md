# AmigaEmulator

AmigaEmulator is a C#/.NET Amiga 500 emulator with a WinForms and SharpPixels
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
- Motorola 68000 core with growing opcode and regression test coverage.
- Kickstart ROM resolver and ROM selection through the overlay UI.
- Real Kickstart 1.3 insert-disk path through ROM code, Copper, Blitter,
  bitplane DMA, Chip RAM, and Denise output.
- Standard 880 KiB AmigaDOS ADF loading and writable-sector DMA with explicit flush.
- Paula disk and audio path with MFM track streams, disk activity status, and
  audio output.
- CIA 8520 basics, keyboard input, mouse input, and joystick input.
- F12 save state overlay.
- SharpPixels video output with scaling and post-processing filters.
- Automated tests for CPU, Blitter, Copper, CIA, input, disk, selected golden
  ADF cases, and optional vAmigaTS cases.

Still incomplete:

- Not every game is glitch-free yet.
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
the corresponding local files are available.

## Repository Layout

```text
AmigaEmulator/
|-- AmigaEmulator.sln
|-- AmigaEmulator/          Main application and emulator core
|   |-- Audio/              Paula audio output and resampling
|   |-- Cia/                CIA 8520 emulation
|   |-- Core/               System model, settings, clock, ROM resolver
|   |-- Cpu/                Motorola 68000 core
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
- Scale2x
- Scale3x
- HQ2x
- HQ3x
- Clean4

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
- Mouse emulation
- CIA/input regression self-tests

Input behavior is still an area of active improvement because many games depend
on very specific CIA, joystick, and mouse behavior.

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

## License

This project is licensed under the Apache License 2.0. See `LICENSE`.

Kickstart ROMs, Workbench disks, and game/ADF files are not covered by this
repository license and must be provided separately by the user.
