// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using AmigaEmulator.Core;
using AmigaEmulator.Core.SaveStates;

if (args.Length < 1) { Console.Error.WriteLine("BenchmarkRunner <savestate> [frames=50] [samples=5] [warmupFrames=50]"); return 2; }
int Read(int index, int fallback) => args.Length > index ? int.Parse(args[index]) : fallback;
int frames = Read(1, 50), samples = Read(2, 5), warmup = Read(3, 50);
if (frames < 1 || frames > 100000 || samples < 1 || samples > 1000 || warmup < 0 || warmup > 100000)
    throw new ArgumentOutOfRangeException("Frame/sample counts exceed supported limits.");
var metadata = SaveStateFile.ReadMetadata(args[0]);
var model = AmigaModel.Create(metadata.MachineModel, metadata.RamProfile);
using var system = new AmigaSystem(model, AmigaAccuracyOptions.Compatible.Clone());
SaveStateFile.Load(args[0], system);
// Older files can retain CPU tracing independently of the system's profile.
// Explicitly apply the benchmark profile after restore on both builds.
system.SetAccuracyOptions(AmigaAccuracyOptions.Compatible.Clone());
int cycles = model.ChipCyclesPerLine * model.LinesPerFrame;
for (int i = 0; i < warmup; i++) system.RunChipCycles(cycles);
for (int sample = 0; sample < samples; sample++)
{
    var times = new double[frames];
    long allocated = GC.GetAllocatedBytesForCurrentThread();
    long start = Stopwatch.GetTimestamp(), previous = start;
    for (int frame = 0; frame < frames; frame++)
    {
        system.RunChipCycles(cycles);
        long now = Stopwatch.GetTimestamp();
        times[frame] = (now - previous) * 1000.0 / Stopwatch.Frequency;
        previous = now;
    }
    double seconds = (previous - start) / (double)Stopwatch.Frequency;
    allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
    Array.Sort(times);
    // Capture complete serialized CPU state outside the timed/allocation region;
    // equal registers alone can hide divergent bus retirement or cache entries.
    using var cpuState = new MemoryStream();
    system.Cpu.SaveState(new BinaryWriter(cpuState));
    Console.WriteLine(JsonSerializer.Serialize(new { sample, model = metadata.MachineModel.ToString(),
        ram = metadata.RamProfile.ToString(), profile = system.AccuracyOptions.Name, frames, seconds, fps = frames / seconds,
        medianFrameMs = times[frames / 2], p95FrameMs = times[Math.Max(0, (int)Math.Ceiling(frames * 0.95) - 1)],
        allocated, pc = system.Cpu.PC.ToString("X8"), cpu = system.GetCpuDebugInfo(),
        cpuStateHash = Convert.ToHexString(SHA256.HashData(cpuState.GetBuffer().AsSpan(0, (int)cpuState.Length))),
        hash = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(system.PresentedFrameBuffer.Pixels.AsSpan()))) }));
}
return 0;
