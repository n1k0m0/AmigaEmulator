# Audio capture

Build and run from the solution directory:

```powershell
dotnet build Tools/AudioRunner/AudioRunner.csproj -c Release -p:Platform=x64
dotnet Tools/AudioRunner/bin/x64/Release/net10.0-windows7.0/AudioRunner.dll C:/path/game.amigasav 12 C:/fresh/audio-review
```

Use a floppy-only savestate and a fresh output directory. Duration is 1–60
emulated seconds. Restored floppies are write protected; HDF sessions are refused.
The tool records native 48 kHz stereo sound without Windows playback, then
exports raw, legacy A500 and OFF/A500/A1200/LED WAVs from identical source samples.
It does not normalize volume. report.json retains the source hash, machine,
chip clocks, CPU registers, presented image hash, levels, LED samples and timings.

ElapsedSeconds measures the core run plus raw sample capture. FilteringSeconds
measures offline filtering and result storage, excluding WAV file writes. These
are diagnostic timings, not a measurement of live Windows audio latency. WAV
recordings can contain copyrighted game music and remain in the external output
directory; they must not be committed or distributed with the emulator.

See [Audio.md](../../AmigaEmulator/Docs/Audio.md) for fixes, evidence and remaining
limitations.
