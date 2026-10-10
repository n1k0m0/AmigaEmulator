# Paula audio and output filters

Settings / Sound provides OFF, A500 (AUTO LED), A1200 (AUTO LED) and LED
(ALWAYS ON). Selections now round-trip independently. Existing preferences are
preserved; OFF is the clearest output. A500 and A1200 obey the game's CIA-A
active-low /LED pin, including its direction register. LED explicitly forces the
extra filter on. Switching the software LED keeps filter histories warm.

The output circuit is approximated with stereo bilinear first-order stages.
A500 has fixed stages at 6.2 and 20 kHz; A1200 bypasses those stages. The LED
path adds three 7 kHz stages. The frequencies and circuit distinction were
cross-checked against the primary [WinUAE audio implementation](https://github.com/tonioni/WinUAE/blob/master/audio.cpp).
This is an independent filter implementation; it does not reproduce calibrated
analog measurements or component tolerances. No treble boost or loudness
normalization is applied.

Previously A500 mapped to the same mode as forced LED, using a single pole
with alpha 0.06 at 48 kHz (approximately 473 Hz cutoff). A1200 mapped to another
overly strong pole, and explicit LED was saved back as A500. Those mappings and
coefficients were the main excessive host filtering. The corrected A500 path
with LED off retains over 84% of the amplitude of a 4 kHz tone.

## Paula fixes

AUDDAT/DMA holding words are now separate from the word feeding the DAC. Early
prefetch no longer replaces the low byte of the word already playing. The four
signed 8-bit channels retain their hardware stereo routing: 0/3 left, 1/2 right.
AUDPER zero now means 65536 clocks, and the 48 kHz phase accumulator uses the
machine's PAL/NTSC chip clock.

Each host sample integrates the held DAC levels over its fractional chip-clock
interval. Integration happens at DAC transitions and output boundaries, keeping
the chip loop inexpensive and reducing aliases from rapid sample changes.
This is area averaging, not a full sinc/BLEP reconstruction. New savestates
preserve both words and the complete partial sample. Older states remain readable
and reconstruct their missing partial area from the saved DAC level; a live
restore clears any newer integration history before reading a legacy state.
Old states cannot recover a low byte that the old emulator already overwrote.

Network audio remains unfiltered 48 kHz stereo PCM/ADPCM. AudioChunk header flag
bit 0 carries the game's LED state; a switch flushes the current chunk before
the next state begins. Each client's own filter uses that per-chunk state,
rather than the retained local Amiga's CIA. Older senders default to LED bypass.
Entering/leaving a remote session resets local filter history. The PCM codec,
AMIGAR framing, TLS and relay encryption are unchanged.

## Verification, 2026-10-10

15 new audio regressions cover bypass, independent stereo paths, DC gain,
measured filter response at 44.1/48/96 kHz, LED switching/control, all four
settings, manual and DMA prefetch, signed bytes, period zero, PAL/NTSC output
counts, ultrasonic transition averaging, exact mid-sample restore and restoring
older states into an already-running machine. LAN and relay tests additionally
verify audio LED on/off flags. The full suite passes **294/294**, with no skipped
tests; Release builds have no warnings or errors.

Fresh evidence is in
`%TEMP%/AmigaAudio-2dd393b5345b47a689fb8b2378dbd697`:

- A paired 12-second Siedler intro continuation produces 576,000 stereo frames.
  Before/after PC, SR, D0, A7 and presented image SHA-256 agree. The recorded
  software LED is off throughout. Against the previous A500 output, spectral
  energy increases by approximately 13 dB in 2–4 kHz and 14 dB in 4–8 kHz;
  the corrected recording has no clipped samples. This is a signal comparison,
  not a completed subjective listening assessment.
- Four alternating before/after runs use the same state, 42,562,740 chip clocks
  and identical final CPU/image states. Median core capture runtime is
  4.0371 s before and 4.0086 s after, within measurement variation. Output filter
  processing/storage takes approximately 5.7 ms for the complete 12-second clip.
- An 8-second continuation of the Siedler mission-selection checkpoint preserves
  its final CPU/image state; music is disabled in that particular checkpoint.
  An additional older user Siedler save also loads and advances for 8 seconds
  with matching before/after CPU/image state and nonzero sound.
- Original source states retain their SHA-256. Replay media is read-only;
  recordings and logs stay outside the repository. User filter settings, ROMs,
  saves and disk images were not changed.

## Repeatable recordings

From the solution directory, use your own floppy-only savestate:

```powershell
dotnet build Tools/AudioRunner/AudioRunner.csproj -c Release -p:Platform=x64
dotnet Tools/AudioRunner/bin/x64/Release/net10.0-windows7.0/AudioRunner.dll C:/path/game.amigasav 12 C:/fresh/audio-review
```

The output directory must not exist. AudioRunner exports raw, reconstructed
legacy A500 and all four corrected filter WAVs from the same sample stream,
plus source/image hashes, CPU state, audio levels, LED counts and runtimes in
report.json. It mounts restored floppies read-only and refuses HDF sessions.
The script is a reproducible diagnostic, not a claim of compatibility for every
game or a complete Paula state machine. Remaining audio work includes ADKCON
period/volume modulation, fuller DMA/interrupt state-machine accuracy,
band-limited reconstruction and longer real-time listening/underrun tests.
