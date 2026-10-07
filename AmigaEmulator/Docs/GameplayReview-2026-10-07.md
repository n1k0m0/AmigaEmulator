# Gameplay/performance review — 2026-10-07/08

The sweep covers 18 local boot entries, including the SELECTOR utility and a
compilation. **All games reaching glitch-free gameplay is not yet established.**
Evidence is in [tmp/gameplay-optimization](../../../tmp/gameplay-optimization).

## Retained changes

- Correct BPLCON1 scroll mapping: odd planes use the low nibble, even the high.
- Hires DMA/shifter lead-in fixes periodically missing font bits; Civilization
  and AmigaDOS text are legible. A beam-ordered regression covers the defect.
- HAM holds the playfield color beneath sprites, including at span boundaries.
- Manual AUDxDAT audio survives ticks with DMA disabled. Audio DMA enable
  transitions are processed only when the mask changes.
- CIA serial output now completes at half the Timer-A underflow rate, buffers
  the next SDR write, and defers keyboard input while output mode is active.
  This releases Moonstone's serial-completion wait. Active output is preserved
  in new savestates; legacy output-mode snapshots infer an in-flight transfer
  from SDR/timer state, since they contained no output bookkeeping.
- Lowres plane/sprite samples are shared within doubled pixel pairs. Empty spans
  use a fill, and redundant presentation-buffer clearing is removed.
- Profiling now executes line tails and completed-frame presentation like normal
  execution, instead of measuring an incomplete rendering path.
- Compatible retains contention without continuous CPU/DMA trace collection.
  New settings default to Compatible; existing settings and savestate profiles
  remain respected. Fast is still selectable.
- Scripted input/disk review captures screenshots and diagnostics. Visible title
  screens are labeled `visible-unverified`, never an automatic gameplay pass.

Register mapping follows the [Commodore register summary](https://www.theflatnet.de/pub/cbm/amiga/AmigaDevDocs/hard_a.html).
Serial behavior follows [Commodore's CIA description](https://www.theflatnet.de/pub/cbm/amiga/AmigaDevDocs/hard_f.html).

## Reviewed stages and remaining work

Gameplay seen means a visually inspected checkpoint, not every-level or
long-session certification. Existing-save verification is separate from a full
cold-boot walkthrough. All test ADFs were mounted read-only.

| Entry | Furthest reviewed stage | Remaining work |
| --- | --- | --- |
| Alien Breed SE92 | Gameplay from boot after Disk 2 and start-menu input | Longer play and level transitions |
| Cannon Fodder | Crack/title and later full-screen disk-loader text; Return advances the crack intro | Loader, menus, first mission |
| Civilization | Input selection, intro, main menu; legible hires text | World setup, first playable turn |
| Desert Strike | Crack/start screen | Loader and mission start |
| Fire and Ice | Actual first level after data-disk swap and a further Fire input | Longer play, controls, next level |
| Hostages | Team selection | Start and inspect mission |
| Lotus | Actual split-screen race | Longer race and controls |
| Maniac Mansion | Gameplay from boot after kid selection/opening sequence; existing-save gameplay also checked | Longer play and transitions |
| Mega lo Mania | Compatible advances into animated introduction | Start playable island |
| Moonstone | Disk B accepted; serial wait resolved; a resumed later stage displays corrupt graphics | Subsequent load/display fault, playable battle |
| North & South | Language selection and main menu | Play selection, map/battle |
| Oh No More Lemmings | Main menu in Compatible | Scripted selection, playable level |
| PDREMS21 | Compilation menu/course-disk request | Intended game and matching media |
| SELECTOR | Cortex Floppy Emulator utility, Floppy error | Utility-specific investigation; not a game |
| Die Siedler | Intro and copy-protection symbol query from boot; gameplay from existing local save | Symbol query/new-map path, longer gameplay |
| Speedball 2 | Credits and Loading Game; blank output after a later load transition (PC 220005, ADDRESS ERROR) | Diagnose failed game transition and reach a match |
| Turrican III | Disk 2 accepted; title/intro | Playable level |
| Zak McKracken | Opening story and request for Disk 2; swapping Zak2.adf and Return leaves the request visible | Diagnose disk/input acceptance, finish intro and inspect gameplay |

Confirmed playfield evidence, under the evidence directory:

- Alien Breed: `play/Alien-Breed/03-wait.png`.
- Lotus: latest screenshot in `final-play/LOTUS`.
- Fire and Ice: `serial-games/Fire-and-Ice/04-wait.png`.
- Maniac Mansion from boot: `maniac-final/ManiacM1/01-wait.png`.
- Maniac Mansion existing save: `extra/Saved-ManiacM1/01-wait.png`.
- Siedler existing save: `compare-final/Siedler-strict/01-wait.png`.

`review-contact.png`, `final-contact.png`, per-case PNGs and `steps.jsonl` show
intermediate stages. Diagnostic folders also contain a rejected graphics
experiment; those screenshots are not retained-version examples.

## Validation

- Release solution/GameRunner builds: zero warnings/errors.
- Standard suite: 118 passed, no failures/skips.
- Local Siedler gameplay/cursor/frame check with strict contention: 1 passed.
- vAmigaTS smoke/reference-property checks with strict contention: 9 passed.
- Golden ADF boot/input smoke checks: 4 passed.
- **Total: 132 passed.** External smoke checks do not certify full reference suites.
- Same Siedler save, 1,000,000 chip clocks: final pixels have the same SHA-256
  hash as before, in both Fast and Compatible.
- CIA regressions cover exact serial completion timing, buffered output,
  input-direction gating, stopped-clock behavior, reset, and new/legacy state load.
- Missing GameRunner case returns 2. PowerShell matrix syntax and Git whitespace
  checks pass. Runner success means script execution, not gameplay success.

The eight-slot lowres experiment for up to four planes was rejected: it corrupted
Siedler's command panel and regressed VPOS smoke. The compact compatibility path
remains. Mid-line pointer/plane-enable handling and hardware-slot reads remain
open core work; see `A500ChipAuditPlan.md`.

## Performance

Before binaries were captured at the start of this review, after the earlier
frame/pointer fixes. Both versions use the same Siedler gameplay save, 1 MB Chip
RAM, 500 frames, two-frame warmup and three alternating before/after rounds per
profile. No gameplay jobs ran during measurement.

| Profile | Before FPS | After FPS | Median change |
| --- | --- | --- | --- |
| Fast | 83.94 / 83.58 / 83.46 | 99.82 / 98.47 / 97.37 | 83.58 → 98.47, +17.8% |
| Compatible | 70.11 / 79.36 / 79.37 | 99.19 / 107.31 / 109.08 | 79.36 → 107.31, +35.2% |

These measure headless emulation throughput, not promised display FPS for all
games. Host/JIT variability is visible, but every paired round improved. These
are the final retained-version measurements, including the CIA serial fix.
Normal PAL playback remains near 50 Hz. Raw results: `retained-benchmark.csv`
and `retained-benchmark-*.log`. Earlier preliminary runs remain separately saved.

The final GUI playback check used a Siedler gameplay checkpoint with Compatible:
60 seconds, six samples (first skipped as warmup), exactly 50 FPS and 100% host
speed in every retained sample. GDI and USER handle counts did not grow; private
memory grew 0.24 MB and working set 0.22 MB. No early exit. This short run verifies
real-time playback, not the absence of a long-session memory leak. Raw evidence:
`gui-retained/gui_stability.csv` and `gui-retained/gui_stability_summary.txt`.
