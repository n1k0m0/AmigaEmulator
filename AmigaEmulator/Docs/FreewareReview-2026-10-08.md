# Freeware gameplay review — 2026-10-08

Three developer-published Amiga disk images were downloaded to
`%USERPROFILE%/Documents/AmigaEmulator`. Disk images, Kickstart ROMs and downloaded
game source archives are not included in this repository. Each ADF is 901120 bytes.

| Image | Official download page | SHA-256 |
| --- | --- | --- |
| `SolidGold.adf` | https://night.owl.de/index.php?id=23 | `95D468FF6A6DB6F22BBACDDFB0A1A0BF8B47C06D5B258FABBD09B4376611E997` |
| `CelticHeart.adf` | https://night.owl.de/index.php?id=9 | `1B47C714D1C6A93742BAFEECB8807B36A230C52892B407CB72AB4178FB67FFCB` |
| `Sqrxz.adf` | https://www.sqrxz.de/sqrxz/ | `AC50DD0F7B9A549835867041852948702523ED11449CD11BF4B49518B38D42CE` |

Direct downloads used:

- https://server.owl.de/%7Efrank/SolidGold.adf
- https://server.owl.de/%7Efrank/CelticHeart.adf
- https://www.retroguru.com/sqrxz/sqrxz-v.latest-amigaocs.adf (OCS v1.3 at review time)

## Retained fix

Solid Gold entered an illegal-instruction exception at `C2091E`, opcode `C109`
(`ABCD -(A1),-(A0)`), while formatting the menu score. Execution subsequently
entered Kickstart's crash/debug path instead of the menu. The game's published
`menu.asm` uses four ABCD instructions to add a decimal score before printing it.

The CPU now implements ABCD, SBCD and NBCD, including byte register preservation,
predecrement memory operands, A7's two-byte decrement, aliased address registers,
extend/carry and cumulative Z. Instruction timing estimates are included. These
are general CPU implementations without game-specific PC or ROM patches.

After this fix, Solid Gold starts and Sqrxz no longer reaches its green raster
error loop during boot. Celtic Heart already reached its menu before the fix.

## Observed gameplay

All runs use the user's Kickstart 1.3 ROM, 512 KB Chip + 512 KB Slow RAM,
Compatible accuracy, read-only DF0 media and joystick input on port 1.

| Game | Verified | Remaining limitations |
| --- | --- | --- |
| Solid Gold | Cold boot, main menu, World 1 / Level 1, dismissing GET READY, movement and jumping with screen scrolling | No full campaign or long-session verification; occasional sprite/HUD edge artifacts remain visible during scrolling |
| Celtic Heart | Cold boot, menu, story, Snow Mountains Stage 1, dismissing GET READY, movement and jumping | Right-edge sprite strips and incomplete parallax remain; not a graphics-clean pass |
| Sqrxz | Cold boot, menu, story and first level; character reacts to movement/jump input; pause can be dismissed | Left-edge graphics garbage and sprite/text anomalies remain; not a graphics-clean pass |

Fire is required at several distinct prompts. Direction input while GET READY is
displayed does not demonstrate gameplay. Screenshots before and after a timed
input were visually reviewed; a changing hash or successful runner exit alone
is not treated as a gameplay pass.

## Reproduction and evidence

`Tools/freeware-gameplay-scenarios.json` contains the cold-boot input sequences.
With an isolated Release GameRunner build and the verified images installed:

```powershell
dotnet <GameRunner-build>/GameRunner.dll Tools/freeware-gameplay-scenarios.json <output-directory>
```

The runner captures every step, CPU/display/disk diagnostics in `steps.jsonl`
and a final checkpoint. Local evidence is outside Git at
`../tmp/homebrew-review` relative to the solution root:

- `repro/SolidGold/07-get-ready.png`, `10-move-right.png`, `11-jump-right.png`.
- `celtic-final/CelticHeart/02-level.png`, `03-move-right.png`, `04-jump-right.png`.
- `repro-final/CelticHeart/11-level.png`, `12-move-right.png`, `13-jump-right.png`:
  final cold-boot script including the second GET READY button press.
- `repro/Sqrxz/05-wait.png`, `07-level.png`, `08-move-right.png`, `09-jump-right.png`.
- `final-play/Sqrxz`: pause dismissal and subsequent input.
- `solid-boot.txt`: original illegal opcode trace; `sqrxz-boot.txt`: boot trace after the CPU fix.
- `downloads.json`: exact source URLs, image sizes and hashes.

## Graphics investigation — rejected experiment

Follow-up: the combined Copper bandwidth, sprite DMA activation, comparator,
and attachment-palette correction is now retained. See
[the ten-game follow-up](TenGamesReview-2026-10-08.md). The rejected experiment
below records the earlier POS-only attempt and does not describe the new core.

Celtic Heart's published `sprites.asm` / `PlxSpriteField` moves the same sprite
channels repeatedly across one raster line with Copper SPRxPOS writes. The
current sprite path suppresses latched data when that position changes. The
hardware comparator should remain armed after a POS write; CTL disables it and
DATA arms it (DATB does not). Reference:
https://amigadev.elowar.com/read/ADCD_2.1/Hardware_Manual_guide/node00C8.html
and https://amigadev.elowar.com/read/ADCD_2.1/Hardware_Manual_guide/node003F.html.

An isolated comparator correction revealed further repeated corrupt sprite data
across the Celtic Heart screen. It was **reverted**, including its test changes.
Evidence remains in `sprite-verify` and is not a retained-version example.
The sprite DMA activation/control-word state and horizontal reuse need to be
fixed together before changing this compatibility path. The existing
"sprite POS write does not move stale line data" regression should be revisited
against the comparator rules as part of that work; its assertion is not a
complete hardware specification.

## Validation

- Retained Release build: zero warnings/errors.
- Standard test suite: **128 passed, zero failures/skips**.
- Decimal tests cover all valid BCD byte pairs, both X and initial Z states,
  synchronous Step and deferred Tick execution with denied bus cycles.
- NBCD valid bytes are checked through Step and Tick; memory tests cover all
  source/destination predecrement register combinations and a four-byte carry
  chain used by Solid Gold.
- The implemented-family opcode matrix now includes decimal instructions.
- Invalid BCD digit behavior and undefined N/V flags are not certified by the
  decimal oracle tests.
- No new FPS benchmark or graphical UI playback certification was performed.
