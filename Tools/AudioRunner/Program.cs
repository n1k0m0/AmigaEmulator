using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AmigaEmulator.Core;
using AmigaEmulator.Core.Audio;
using AmigaEmulator.Core.SaveStates;

if (args.Length != 3)
{
    Console.Error.WriteLine("AudioRunner <savestate> <seconds (1..60)> <fresh-output-directory>");
    return 2;
}
string state = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[2]);
double seconds = double.Parse(args[1], CultureInfo.InvariantCulture);
if (seconds < 1 || seconds > 60 || !double.IsFinite(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
if (Directory.Exists(output)) throw new IOException("Use a fresh output directory to preserve earlier recordings.");
var metadata = SaveStateFile.ReadMetadata(state);
using var system = new AmigaSystem(AmigaModel.Create(metadata.MachineModel, metadata.RamProfile), AmigaAccuracyOptions.Fast.Clone());
SaveStateFile.Load(state, system);
// A diagnostic replay must never write back into the user's mounted media.
for (int drive = 0; drive < 4; drive++)
{
    var floppy = system.Bus.FloppyController.GetDrive(drive);
    if (floppy.Image != null) floppy.RestoreMedia(floppy.Image, true);
}
if (system.Bus.HardDisk.Image != null) throw new InvalidOperationException("Use a floppy-only state; mounted user HDFs are not replayed by this tool.");
var frames = new List<float>((int)(seconds * 48000 * 2));
var ledStates = new List<bool>((int)(seconds * 48000));
int ledSamples = 0;
system.Paula.AudioSampleGenerated += (left, right) =>
{
    frames.Add(left); frames.Add(right);
    bool led = system.CiaA.AudioFilterLedEnabled;
    ledStates.Add(led);
    if (led) ledSamples++;
};
int cycles = (int)Math.Round(seconds * system.Model.ChipClockHz);
var timer = Stopwatch.StartNew();
system.RunChipCycles(cycles);
timer.Stop();
Directory.CreateDirectory(output);
WriteWave(Path.Combine(output, "raw.wav"), frames);
var legacy = new List<float>(frames.Count);
var filteringSeconds = new Dictionary<string, double>();
var filterTimer = Stopwatch.StartNew();
float l = 0, r = 0;
for (int i = 0; i < frames.Count; i += 2)
{
    l += (frames[i] - l) * 0.06f; r += (frames[i + 1] - r) * 0.06f;
    legacy.Add(l); legacy.Add(r);
}
filterTimer.Stop(); filteringSeconds["legacy-a500"] = filterTimer.Elapsed.TotalSeconds;
WriteWave(Path.Combine(output, "legacy-a500.wav"), legacy);
foreach (AmigaPaulaFilter mode in Enum.GetValues<AmigaPaulaFilter>())
{
    var filter = new PaulaOutputFilter { Mode = mode };
    var filtered = new List<float>(frames.Count);
    filterTimer.Restart();
    for (int i = 0; i < frames.Count; i += 2)
    {
        float left = frames[i], right = frames[i + 1];
        filter.Process(ref left, ref right, ledStates[i / 2]);
        filtered.Add(left); filtered.Add(right);
    }
    filterTimer.Stop(); filteringSeconds[mode.ToString()] = filterTimer.Elapsed.TotalSeconds;
    WriteWave(Path.Combine(output, mode.ToString().ToLowerInvariant() + ".wav"), filtered);
}
string imageHash = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(system.PresentedFrameBuffer.Pixels.AsSpan())));
var report = new
{
    source = state, sourceSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(state))),
    model = metadata.MachineModel.ToString(), cycles, elapsedSeconds = timer.Elapsed.TotalSeconds,
    samples = frames.Count / 2, ledSamples, pc = system.Cpu.PC, sr = system.Cpu.SR, d0 = system.Cpu.D0, a7 = system.Cpu.A7,
    imageSha256 = imageHash,
    rms = Math.Sqrt(frames.Sum(value => (double)value * value) / frames.Count),
    peak = frames.Max(value => Math.Abs(value)), filteringSeconds, audio = system.Paula.GetAudioDebugInfo()
};
string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(Path.Combine(output, "report.json"), json, new UTF8Encoding(false));
Console.WriteLine(json);
return 0;

static void WriteWave(string path, List<float> frames)
{
    using var writer = new BinaryWriter(File.Create(path));
    writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + frames.Count * 2);
    writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((ushort)1); writer.Write((ushort)2);
    writer.Write(48000); writer.Write(48000 * 4); writer.Write((ushort)4); writer.Write((ushort)16);
    writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(frames.Count * 2);
    foreach (float sample in frames) writer.Write((short)Math.Clamp(Math.Round(sample * 32767), -32768, 32767));
}
