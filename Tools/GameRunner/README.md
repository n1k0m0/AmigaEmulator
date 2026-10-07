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

A case has `name`, `boot` (ADF filename), optional `mediaRoot`, `ram`, `strict`
and `actions`. Default media root is `%USERPROFILE%/Documents/AmigaEmulator`;
RAM defaults to `Chip512KSlow512K`. Set `strict: true` for Compatible contention
without tracing. `save` (absolute savestate path) can replace `boot`; RAM is then
read from the savestate. The checkpoint records the selected accuracy profile.

Actions:

- `wait`: `cycles` chip clocks (about 3.55 million per emulated PAL second).
- `key`: `key` Amiga raw keycode; `cycles` optional hold duration. Space is 64,
  Return 68, F1 80. Release is followed by 400,000 chip clocks.
- `mouse`: relative `x`/`y` movement, split into small steps to avoid byte-counter
  wrap ambiguity. Coordinates refer to mouse counters, not screenshot pixels.
- `click`: mouse-port button, default `button: Fire` (left mouse).
- `joystick`: joystick-port button, default Fire; Up/Down/Left/Right are also
  available. `cycles` optionally changes the hold duration.
- `disk`: `disk` ADF filename and optional `drive` index (default DF0).

Every action accepts a `label` for screenshots. `strict` does not imply that
software is compatible; both profiles may require title-specific investigation.
The supplied manifest is an initial smoke sweep. Real gameplay requires
appropriate disk swaps and menu selections, as demonstrated in the review report.
