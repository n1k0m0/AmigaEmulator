# Ten additional Amiga games — 2026-10-08

The previous changes were already committed at `0ea9fd9`; the requested push
confirmed that `origin/main` was up to date before this review started.

Ten additional freely downloadable developer releases were installed in
`%USERPROFILE%/Documents/AmigaEmulator`. These are additional to Solid Gold,
Celtic Heart and Sqrxz from the earlier review. Each extracted ADF is 901120
bytes. The repository contains neither the games nor Kickstart ROMs.
[Download provenance](../../Tools/ten-new-games-downloads.json) records the
release page, exact download URL, extracted ADF hash and size. Fred's Journey
was extracted from the author's ZIP and Sqrxz 2 from its gzip download.

## Retained core fixes

- Copper instruction fetches were scheduled every four chip memory cycles.
  Two-word MOVE instructions therefore took eight cycles instead of four.
  Scheduling every second cycle restores the missing bandwidth for repeated
  sprite positions and per-line palette changes. This restores Trap Runner's
  distant mountains and Fred's Journey's full-width gradient sky.
- SPRxPOS preserves an armed horizontal sprite comparator; SPRxCTL disarms it.
  SPRxDATA arms it, including manual Copper output outside the DMA vertical
  interval. SPRxDATB updates the second data latch without arming it.
- Sprite DMA activation is separate from horizontal output. A late Copper POS
  write with VSTART zero must not start an idle dummy channel reading unrelated
  chip memory. The activation state is saved, with a migration for older states.
- A loaded zero control pair remains a known terminator. Subsequent POS updates
  cannot accidentally reuse an older register-map CTL value.
- Odd-channel ATT selects the attached pair's palette even when the odd sprite
  is transparent, vertically inactive or parked at another horizontal position.

Hardware references: [Copper instruction timing](https://amigadev.elowar.com/read/ADCD_2.1/Hardware_Manual_guide/node0049.html),
[sprite comparator](https://amigadev.elowar.com/read/ADCD_2.1/Hardware_Manual_guide/node00C8.html),
[attached sprite colors](https://amigadev.elowar.com/read/ADCD_2.1/Hardware_Manual_guide/node00C6.html).
Celtic Heart's published `sprites.asm` / `PlxSpriteField` provided a concrete
horizontal reuse example. The earlier POS-only experiment remains rejected;
the combined fix here addresses the additional DMA, palette and Copper causes.

GameRunner now optionally records synchronous CPU exceptions to
`exceptions.jsonl`. Interrupts are omitted. Kickstart's deliberate privilege
exception during supervisor transitions is normal, and is not classified as
a game crash. Every cold boot also records the same handled illegal-instruction
exception at ROM `FC0564`, opcode `4E7B` (MOVEC), targeting ROM `FC0582` at
frame 53. This is consistent with the ROM's CPU capability probe on a 68000;
no bus/address/illegal/line-A/line-F exception was observed in game code in the
reviewed sequences. Screenshots and changing hashes alone never certify gameplay.

## Reproduction

From the solution directory, with a locally supplied Kickstart 1.3 ROM:

```powershell
dotnet build Tools/GameRunner/GameRunner.csproj -c Release -p:Platform=x64 -p:OutDir=C:/temp/amiga-game-runner/
dotnet C:/temp/amiga-game-runner/GameRunner.dll Tools/ten-new-games-scenarios.json C:/temp/amiga-ten-games/
```

The scenarios boot each ADF from reset with 512 KB Chip + 512 KB Slow RAM and
the Compatible arbitration profile, pass title/story/level prompts, and issue
movement plus jump/fire inputs. No HDD or RAM expansion is required for this
batch. A case name can be supplied as the third argument. The mounted ADFs are
read-only. Saved checkpoints, input-step screenshots and CPU/display/disk logs
are emitted outside the repository.

Boxx 4 and Tiny Bobble have long initial decompression phases. Advancing CPU
state and compressed source/destination pointers confirmed these were active
loaders rather than hangs. Extra RAM did not solve Boxx 4's loading delay;
waiting for the loader did. The scenarios deliberately allow substantial time
for loading, and are not measurements of the shortest possible boot time.

## Scope of validation

| Game | Reviewed gameplay | Remaining observation |
| --- | --- | --- |
| Trap Runner | First forest level; rabbit moves and jumps | Occasional parallax seams and overscan edge artifacts |
| Black Strawberry Cake | Volcano stage 1; movement and jump | Parallax seams remain in the background |
| Sqrxz 2 | First level; start prompt dismissed, movement and jump | Edge/scrolling artifacts; additional Fire during gameplay pauses |
| Sqrxz 3 | First level; movement, enemy collision and respawn | Edge/scrolling artifacts |
| Sqrxz 4 | First level after all story pages; movement, jump and collision | HUD/edge artifacts |
| Fred's Journey | First level; movement and jump | Some left-edge sky clipping remains despite restored gradient |
| Boxx 4 | First level outside the castle; movement and jump | Incorrect tiles near the top and scrolling edges |
| Tinyus | First space stage; movement and shooting | Bottom status/power-up strip remains visually incorrect |
| Wrong Way Driver | Traffic stage; Down/Up changes the player's lane | Short run; scrolling edges still need reference comparison |
| Tiny Bobble | Round 1; movement, jump and bubbles | Short run; bottom/right presentation needs reference comparison |

The final cold scenarios are `cold-final` for Trap Runner, Black Strawberry
Cake, Sqrxz 3, Fred's Journey, Boxx 4 and Wrong Way Driver;
`cold-confirmed` contains the revised Sqrxz 2 and Sqrxz 4 sequences;
`cold-confirmed-final` contains the final Tinyus and Tiny Bobble cold runs.
The original `cold-final` Sqrxz 4 capture was still a story page, and the
original Sqrxz 2 sequence had not dismissed the level prompt. Those are not
accepted gameplay checkpoints. The checked-in scenarios contain the corrected
sequences. Tinyus uses an earlier input window to avoid losing ships during
long idle waits before the movement/shooting check.
Tiny Bobble requires an additional Fire press to enter the player-controlled
round from the attract sequence. The earlier round-like capture did not prove
player control. The corrected sequence shows the green player moving right,
jumping and firing a visible bubble, and is the accepted gameplay example.

The review verifies entry into the first level and short controlled input runs.
It does not certify complete playthroughs, every level, audio fidelity or GUI
frame pacing. Remaining edge, scrolling and HUD artifacts are recorded below;
these releases must not be described as universally glitch-free.

Standard Release build: zero warnings/errors; **130 tests pass**, zero failures
or skips. The expanded run passes **144 tests**, including four GoldenADF
checks, the strict Siedler wooden-frame/menu regression and nine vAmigaTS smoke
checks. The sprite tests cover horizontal reuse, unchanged CTL disarming, DATB
non-arming, manual DATA before VSTART, parked attached partners, and idle DMA
state through save/restore. The scheduler regression checks both instruction
fetch opportunities and the intervening Blitter opportunity.

The existing compact lowres bitplane fetch compatibility path is retained.
A full slot-map experiment did not improve the reviewed scenes and conflicted
with current timing regressions, so it was reverted. The retained Copper
bandwidth correction does not imply complete cycle-accurate OCS arbitration.

Local evidence is under `../tmp/ten-games-review` relative to the solution
directory: `downloads.json`, `boot`, `advance`, `level`, `slow-load`,
`copper-verify`, `cold-final`, `cold-confirmed`, exception traces and test logs.
`sprite-verify*` and `dma-verify` are intermediate experiments, not final passes.
