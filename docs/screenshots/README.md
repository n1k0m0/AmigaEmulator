# Screenshot gallery

These images document the emulator's tested output and current frontend.
They contain no ROM binaries, game disks, OS disks, hard disk images or saves.
Click the thumbnails in the main README to open the complete PNGs.

## Captures

| Image | Capture context |
| --- | --- |
| `kickstart-13.png` | A500, Kickstart 1.3, 512 KiB Chip + 512 KiB Slow RAM; real ROM execution to the insert-disk screen. |
| `main-menu.png`, `settings-menu.png`, `hardware-menu.png`, `graphics-menu.png`, `network-menu.png`, `hard-disk-menu.png` | Current frontend renderer at 1000×720, isolated A500 profile, no mounted media, no network connection, no passwords. The network menu shows configuration, not an active session. |
| `workbench-desktop.png`, `workbench-shell-clock.png` | AmigaOS 3.1.4 installed from user-provided disks on an emulated A4000/030 HDF; desktop and Shell/Clock acceptance captures from 2026-10-09. |
| `the-settlers.png` | A500+, Kickstart 2.0; construction result after the reviewed intro/disk-change/gameplay flow, 2026-10-11. |
| `mega-lo-mania.png` | A500, Kickstart 1.3; live island after input and continuation, 2026-10-11. |
| `cannon-fodder.png` | A500, Kickstart 1.3; mission one after movement/fire, pause, reset and cold restore; three data disks mounted, 2026-10-11. |
| `pinball-fantasies.png` | A3000, native Kickstart 3.1 and the reviewed compatibility loader copy; Partyland with flipper input, 2026-10-11. |
| `desert-strike.png` | A2000 ECS, native Kickstart 3.1; flight after opening and leaving the map, 2026-10-11. |
| `speedball.png` | A500; continued match from the raster/restore/input verification, 2026-10-09. |

The menu/Kickstart views are rendered directly through the release frontend's
`OnPaint` code without a desktop capture or fabricated UI. Game and Workbench
images come from real machine validation runs. Their 704×288 captured pixels
are displayed as 704×576 with nearest-neighbor row duplication to correct the
non-square pixel aspect. No scene content was retouched. PNGs omit private
paths and embedded metadata.

These are individual tested scenes. They do not establish complete campaign,
hardware or timing compatibility. See the [model roadmap](../../AmigaEmulator/Docs/MachineModels.md)
and [game compatibility report](../../AmigaEmulator/Docs/GameModelCompatibility-2026-10-09.md)
for configurations, evidence and remaining issues.

## Rights

Emulator frontend code is licensed under Apache 2.0. Game graphics, Kickstart
and AmigaOS/Workbench artwork remain the property of their respective owners;
the source-code license does not apply to that artwork. These screenshots
illustrate the emulator, and none of the underlying ROMs or media is bundled.
