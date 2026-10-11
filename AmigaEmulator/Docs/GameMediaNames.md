# Local game-media naming

The local collection uses `Game Title (disk N).adf`, including `(disk 1)` for
single-disk games. For example, `Die Siedler (disk 1).adf`,
`Cannon Fodder (disk 2).adf` and `Sqrxz 2 (disk 1).adf`. Publisher/year/crack
tags and shortened download filenames are removed from host filenames only;
AmigaDOS volume names and image contents are unchanged. No game images or
ROMs are distributed with the repository.

`Tools/game-media-catalog.json` records the current filenames and readable
`displayName` for each title. Scenario IDs such as `Canfod-1`, `Civ1` and
`PDREMS21` remain stable so compatibility profiles and reviewed evidence still
identify the same cases. All current source scenarios, download destinations
and local ADF goldens use the new filenames.

The repaired Siedler Intro is the primary `Die Siedler (disk 1).adf`.
Its damaged source is preserved separately as
`Originale/Die Siedler (disk 1).adf`. The catalog's `archivedMedia` distinguishes
this source from primary media. The short disk-change diagnostic and the
previous Kickstart-2.04 golden intentionally use the archived original; the
Kickstart-1.3 gameplay golden uses the repaired primary. Both are still
byte-identical to the images reviewed before renaming.

## Completed local migration (2026-10-11)

- Renamed all 51 collection ADFs: 50 in the main media folder, one archived
  original. Verified every image's SHA-256 before and after; no image changed.
- Renamed 10 user save directories and updated 22 savestate headers and
  serialized ADF host paths. A typed serializer walk changed only media-name
  and path strings; embedded disk data, screenshots, timestamps and guest
  state were preserved. Every migrated state loads with the same CPU,
  canonicalized bus, beam and framebuffer as its pre-migration baseline.
- Updated startup DF0–DF3 references while preserving all other settings.
- Preflight validates 259 generated native/compatibility/utility cases and
  all three Siedler manifests against the renamed files. This checks paths
  and configurations; it is not a new gameplay/model compatibility sweep.
- Release build: zero warnings/errors. Local North & South ADF goldens verify
  the renamed source's intro and mouse-driven main-menu load.

The original savestates/settings, path map, edit offsets and state hashes are
preserved under
`%APPDATA%/AmigaEmulator/diagnostics/MediaRename-20261011-294157ec31fc/`.
Historical diagnostic captures and their logs remain as collected and can
still mention former paths; consult `plan.json` in that directory when
continuing an older diagnostic. ROMs, HDFs and AmigaOS installation media
were not renamed.
