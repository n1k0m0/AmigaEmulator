// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using AmigaEmulator.Core;
using AmigaEmulator.Core.Media;

if (args.Length < 3)
{
    Console.Error.WriteLine("CpuBringup <M68EC020|M68020|M68EC030|M68030> <ROM path> <new output directory> [chip cycles=40000000] [read-only HDF or empty] [Ecs|Aga] [Gayle|A4000] [CPU Hz=14187580] [desktop]");
    return 2;
}
var processor = Enum.Parse<AmigaCpuModel>(args[0], true);
string output = Path.GetFullPath(args[2]);
if (Directory.Exists(output)) throw new IOException("Use a fresh diagnostic output directory.");
Directory.CreateDirectory(output);
var board = AmigaModel.Create(AmigaMachineModel.A600, AmigaRamProfile.Chip2MbFast4Mb)
    .WithDevelopmentProcessor(processor, args.Length > 7 ? int.Parse(args[7]) : 14187580);
if (args.Length > 5) board = board.WithDevelopmentChipset(Enum.Parse<AmigaChipset>(args[5], true));
if (args.Length > 6) board = board.WithDevelopmentIde(Enum.Parse<AmigaIdeModel>(args[6], true));
if (args.Length > 8 && args[8] == "desktop") board = board.WithDevelopmentDesktopResources();
using var system = new AmigaSystem(board, AmigaAccuracyOptions.Compatible);
system.LoadKickstart(File.ReadAllBytes(Environment.ExpandEnvironmentVariables(args[1])));
if (args.Length > 4 && !string.IsNullOrWhiteSpace(args[4])) system.Bus.HardDisk.Mount(Path.GetFullPath(args[4]), true);
system.Reset(true);
long cycles = args.Length > 3 ? long.Parse(args[3]) : 40_000_000;
var exceptions = new Dictionary<(int vector, uint pc, ushort opcode), int>();
system.Cpu.ExceptionEntered += (vector, pc, sr, instructionPc, opcode, target) =>
{
    var key = (vector, instructionPc, opcode);
    if (exceptions.TryGetValue(key, out int count)) exceptions[key] = count + 1;
    else if (exceptions.Count < 4096) exceptions[key] = 1;
};
long done = 0;
while (done < cycles)
{
    int step = (int)Math.Min(cycles - done, 2_000_000);
    system.RunChipCycles(step); done += step;
    Console.WriteLine($"cycles={done} PC={system.Cpu.PC:X8} VBR={system.Cpu.VectorBase:X8} Exec={system.Bus.PeekLong(4):X8} SR={system.Cpu.SR:X4}");
}
Capture("boot");
using (var state = new MemoryStream())
{
    system.SaveState(new BinaryWriter(state)); state.Position = 0;
    system.LoadState(new BinaryReader(state));
}
system.RunChipCycles(2_000_000); Capture("restored");
File.WriteAllLines(Path.Combine(output, "exceptions.txt"), exceptions.OrderByDescending(e => e.Value)
    .Select(e => $"vector={e.Key.vector} pc={e.Key.pc:X8} opcode={e.Key.opcode:X4} count={e.Value}"));
Console.WriteLine(system.Cpu.GetDebugInfo());
foreach (uint offset in new uint[] { 0x08E, 0x090, 0x092, 0x094, 0x100, 0x102, 0x104, 0x106, 0x10C, 0x1E4, 0x1FC })
    Console.WriteLine($"CUSTOM {offset:X3}={system.Agnus.CustomRegisters.ReadWord(offset):X4}");
return 0;

void Capture(string label)
{
    var frame = system.PresentedFrameBuffer;
    using var bitmap = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb);
    var data = bitmap.LockBits(new Rectangle(0, 0, frame.Width, frame.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
    try
    {
        int[] pixels = Array.ConvertAll(frame.Pixels, p => unchecked((int)p));
        for (int y = 0; y < frame.Height; y++) Marshal.Copy(pixels, y * frame.Width, data.Scan0 + y * data.Stride, frame.Width);
    }
    finally { bitmap.UnlockBits(data); }
    bitmap.Save(Path.Combine(output, label + ".png"), ImageFormat.Png);
    Console.WriteLine($"{label} frame hash={Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(frame.Pixels.AsSpan())))}");
}
