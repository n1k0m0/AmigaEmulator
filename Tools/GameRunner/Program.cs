using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using AmigaEmulator.Core;
using AmigaEmulator.Core.Input;
using AmigaEmulator.Core.Media;
using AmigaEmulator.Core.SaveStates;

// Deterministic input/disk scenarios. A screenshot is evidence for review, never an automatic gameplay pass.
if (args.Length < 2)
{
    Console.Error.WriteLine("GameRunner <scenario.json> <output-directory> [case-name]");
    return 2;
}
var scenarios = JsonSerializer.Deserialize<List<Scenario>>(File.ReadAllText(args[0]),
    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
if (scenarios == null || scenarios.Count == 0) throw new ArgumentException("No scenarios found.");
var selected = scenarios.Where(s => args.Length < 3 || s.Name == args[2]).ToList();
if (selected.Count == 0) { Console.Error.WriteLine("Requested case was not found."); return 2; }
Directory.CreateDirectory(args[1]);
int failures = 0;
foreach (var scenario in selected)
{
    try { Run(scenario, args[1]); }
    catch (Exception ex) { Console.Error.WriteLine($"FAIL {scenario.Name}: {ex}"); failures++; }
}
return failures == 0 ? 0 : 1;

static void Run(Scenario scenario, string output)
{
    if (string.IsNullOrWhiteSpace(scenario.Name) || scenario.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || scenario.Name is "." or "..")
        throw new ArgumentException("Scenario name must be a valid directory name.");
    string directory = Path.Combine(output, scenario.Name);
    Directory.CreateDirectory(directory);
    string mediaRoot = Environment.ExpandEnvironmentVariables(scenario.MediaRoot ?? "%USERPROFILE%/Documents/AmigaEmulator");
    string save = scenario.Save == null ? null : Environment.ExpandEnvironmentVariables(scenario.Save);
    var ram = save != null ? SaveStateFile.ReadMetadata(save).RamProfile : Enum.Parse<AmigaRamProfile>(scenario.Ram ?? "Chip512KSlow512K");
    var model = AmigaModel.ForRamProfile(ram);
    var accuracy = (scenario.Strict ? AmigaAccuracyOptions.Compatible : AmigaAccuracyOptions.Fast).Clone();
    using var system = new AmigaSystem(model, accuracy);
    using var exceptionTrace = scenario.TraceExceptions
        ? new StreamWriter(Path.Combine(directory, "exceptions.jsonl")) { AutoFlush = true } : null;
    int exceptions = 0;
    if (exceptionTrace != null) system.Cpu.ExceptionEntered += (vector, pc, sr, opcodePc, opcode, target) =>
    {
        // Interrupts are normal gameplay activity. Keep synchronous CPU faults/traps.
        if (vector >= 24) return;
        exceptions++;
        exceptionTrace.WriteLine(JsonSerializer.Serialize(new { frame = system.FrameCounter,
            vector, pc = pc.ToString("X6"), sr = sr.ToString("X4"),
            opcodePc = opcodePc.ToString("X6"), opcode = opcode.ToString("X4"), target = target.ToString("X6") }));
    };
    using var ideTrace = scenario.TraceIde ? new StreamWriter(Path.Combine(directory, "ide-trace.txt")) { AutoFlush = true } : null;
    if (ideTrace != null) system.Bus.WriteObserved += (address, size, value, region) =>
    { if (region == AmigaEmulator.Core.Memory.AmigaAddressRegion.GayleIde || address >= 0xE80000 && address < 0xE90000) ideTrace.WriteLine($"PC={system.Cpu.PC:X6} W{size} {address:X6}={value:X8}"); };
    if (ideTrace != null) system.Bus.ReadObserved += (address, size, value, region) =>
    { if (address >= 0xE80000 && address < 0xE90000) ideTrace.WriteLine($"PC={system.Cpu.PC:X6} R{size} {address:X6}={value:X8}"); };
    var mounted = MountedMediaInfo.None;
    var kickstart = save == null ? AmigaKickstart.Kickstart13 : SaveStateFile.ReadMetadata(save).Kickstart;
    if (save != null)
    {
        var metadata = SaveStateFile.ReadMetadata(save);
        SaveStateFile.Load(save, system);
        mounted = metadata.MountedMedia ?? MountedMediaInfo.None;
    }
    else
    {
        byte[] rom;
        bool loaded = scenario.KickstartRom == null
            ? KickstartRomResolver.TryLoadDefaultKickstart(out rom, out _)
            : KickstartRomResolver.TryLoadKickstart(scenario.KickstartRom, out rom, out _);
        if (!loaded) throw new FileNotFoundException("Kickstart ROM missing");
        if (scenario.CreateHardDiskMiB > 0) HardDiskImage.Create(scenario.HardDisk, scenario.CreateHardDiskMiB);
        if (scenario.HardDisk != null) system.Bus.HardDisk.Mount(scenario.HardDisk, scenario.HardDiskReadOnly);
        system.LoadKickstart(rom);
        if (scenario.Boot != null) Mount(scenario.Boot, 0);
    }
    using var log = new StreamWriter(Path.Combine(directory, "steps.jsonl"));
    int step = 0;
    Capture("loaded", 0);
    foreach (var action in scenario.Actions ?? [])
    {
        var timer = Stopwatch.StartNew();
        switch (action.Type)
        {
            case "wait": system.RunChipCycles(action.Cycles); break;
            case "key":
                system.Input.KeyboardKeyDown((byte)action.Key);
                system.RunChipCycles(action.Cycles > 0 ? action.Cycles : 400_000);
                system.Input.KeyboardKeyUp((byte)action.Key);
                system.RunChipCycles(400_000);
                break;
            case "mouse":
                int slices = Math.Max(1, (Math.Max(Math.Abs(action.X), Math.Abs(action.Y)) + 7) / 8);
                int previousX = 0, previousY = 0;
                for (int i = 1; i <= slices; i++)
                {
                    int x = action.X * i / slices, y = action.Y * i / slices;
                    system.Input.MoveMouse(AmigaControlPort.Port0, x - previousX, y - previousY);
                    system.RunChipCycles(120_000);
                    previousX = x; previousY = y;
                }
                break;
            case "click":
            case "joystick":
                var button = Enum.Parse<AmigaJoystickButtons>(action.Button ?? "Fire", true);
                var port = action.Type == "click" ? AmigaControlPort.Port0 : AmigaControlPort.Port1;
                system.Input.SetJoystickButton(port, button, true);
                system.RunChipCycles(action.Cycles > 0 ? action.Cycles : 400_000);
                system.Input.SetJoystickButton(port, button, false);
                system.RunChipCycles(400_000);
                break;
            case "disk": Mount(action.Disk, action.Drive); break;
            default: throw new ArgumentException($"Unknown action {action.Type}");
        }
        Capture(action.Label ?? action.Type, timer.Elapsed.TotalSeconds);
    }
    SaveStateFile.Write(Path.Combine(directory, "checkpoint.amigasav"), system, mounted,
        kickstart, ram, scenario.Strict ? AmigaAccuracyProfile.Compatible : AmigaAccuracyProfile.Fast, system.PresentedFrameBuffer.Pixels, 704, 288);
    Console.WriteLine($"DONE {scenario.Name} frame={system.FrameCounter} PC={system.Cpu.PC:X6}");

    void Mount(string disk, int drive)
    {
        string path = Path.GetFullPath(Path.Combine(mediaRoot, disk));
        system.Bus.FloppyController.InsertDrive(drive, AdfImage.Load(path), true);
        mounted = MountedMediaInfo.ForFloppy(drive, Path.GetFileName(path), path, true);
    }
    void Capture(string label, double seconds)
    {
        var frame = system.PresentedFrameBuffer;
        string imagePath = Path.Combine(directory, $"{step++:00}-{label}.png");
        using var bitmap = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, frame.Width, frame.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int[] pixels = new int[frame.Pixels.Length];
            Buffer.BlockCopy(frame.Pixels, 0, pixels, 0, pixels.Length * 4);
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally { bitmap.UnlockBits(data); }
        bitmap.Save(imagePath, ImageFormat.Png);
        var bytes = MemoryMarshal.AsBytes(frame.Pixels.AsSpan());
        log.WriteLine(JsonSerializer.Serialize(new { label, seconds, frame = system.FrameCounter,
            pc = system.Cpu.PC.ToString("X6"), hash = Convert.ToHexString(SHA256.HashData(bytes)),
            presentation = system.GetPresentationDebugInfo(), cpu = system.GetCpuDebugInfo(), stopped = system.Cpu.IsStopped, exceptions,
            disk = system.Bus.FloppyController.GetDebugInfo(), hardDisk = new { path = system.Bus.HardDisk.Image?.Path, reads = system.Bus.HardDisk.ReadSectors, writes = system.Bus.HardDisk.WrittenSectors },
            fastRam = new { configured = system.Bus.AutoConfig.Configured, address = system.Bus.AutoConfig.BaseAddress }, accuracy = accuracy.Name, imagePath, gameplay = "requires-visual-review" }));
        log.Flush();
    }
}

class Scenario
{
    public string Name { get; set; }
    public string MediaRoot { get; set; }
    public string Boot { get; set; }
    public string Save { get; set; }
    public string Ram { get; set; }
    public bool Strict { get; set; }
    public string KickstartRom { get; set; }
    public string HardDisk { get; set; }
    public bool HardDiskReadOnly { get; set; } = true;
    public int CreateHardDiskMiB { get; set; }
    public bool TraceIde { get; set; }
    public bool TraceExceptions { get; set; }
    public List<ScenarioAction> Actions { get; set; }
}
class ScenarioAction
{
    public string Type { get; set; }
    public string Label { get; set; }
    public int Cycles { get; set; }
    public int Key { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public string Button { get; set; }
    public string Disk { get; set; }
    public int Drive { get; set; }
}
