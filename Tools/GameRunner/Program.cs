// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using AmigaEmulator.Core;
using AmigaEmulator.Core.Input;
using AmigaEmulator.Core.Media;
using AmigaEmulator.Core.SaveStates;

// Deterministic input/disk scenarios. A screenshot is evidence for review, never an automatic gameplay pass.
if (args.Length < 2)
{
    Console.Error.WriteLine("GameRunner <scenario.json> <output-directory|--validate> [case-name]");
    return 2;
}
List<Scenario> scenarios;
try
{
    // A misspelled key/button must not silently become key zero/default Fire.
    scenarios = JsonSerializer.Deserialize<List<Scenario>>(File.ReadAllText(args[0]),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow });
}
catch (Exception ex) when (ex is JsonException || ex is IOException || ex is UnauthorizedAccessException)
{ Console.Error.WriteLine($"INVALID scenario file: {ex.Message}"); return 1; }
if (scenarios == null || scenarios.Count == 0 || scenarios.Any(s => s == null))
{ Console.Error.WriteLine("INVALID scenario file: expected a nonempty list without null entries."); return 1; }
var selected = scenarios.Where(s => args.Length < 3 || s.Name == args[2]).ToList();
if (selected.Count == 0) { Console.Error.WriteLine("Requested case was not found."); return 2; }
bool validateOnly = args[1] == "--validate";
if (selected.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != selected.Count)
{ Console.Error.WriteLine("INVALID selection: duplicate scenario names would replace evidence."); return 1; }
// Validate the complete selection before starting a potentially long sweep.
// Validation never creates images, mounts disks or mutates a running machine.
foreach (var scenario in selected)
{
    try { Validate(scenario); }
    catch (Exception ex) { Console.Error.WriteLine($"INVALID {scenario.Name}: {ex.Message}"); return 1; }
}
if (validateOnly) { Console.WriteLine($"VALID {selected.Count} scenarios."); return 0; }
Directory.CreateDirectory(args[1]);
int failures = 0;
foreach (var scenario in selected)
{
    try
    {
        string actualModel = Run(scenario, args[1]);
        WriteResult(scenario, "executed-unreviewed", null, actualModel);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {scenario.Name}: {ex}"); failures++;
        WriteResult(scenario, "failed", ex.ToString(), null);
    }
}
return failures == 0 ? 0 : 1;

static void Validate(Scenario scenario)
{
    static void Name(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value is "." or "..")
            throw new ArgumentException("Scenario/checkpoint name must be a valid filename.");
    }
    Name(scenario.Name);
    if (scenario.MaximumNonRomExceptions.HasValue &&
        (scenario.MaximumNonRomExceptions.Value < 0 || !scenario.TraceExceptions))
        throw new ArgumentException("maximumNonRomExceptions requires traceExceptions and a nonnegative limit.");
    var metadata = scenario.Save == null ? null : SaveStateFile.ReadMetadata(Environment.ExpandEnvironmentVariables(scenario.Save));
    var machine = metadata?.MachineModel ?? Enum.Parse<AmigaMachineModel>(scenario.Model ?? "A500");
    var ram = metadata?.RamProfile ?? (scenario.Ram == null ? AmigaModel.GetDefaultRamProfile(machine) : Enum.Parse<AmigaRamProfile>(scenario.Ram));
    _ = AmigaModel.Create(machine, ram); // Reject unsupported board/RAM pairs rather than normalize them silently.
    string mediaRoot = Environment.ExpandEnvironmentVariables(scenario.MediaRoot ?? "%USERPROFILE%/Documents/AmigaEmulator");
    void Disk(string filename, int drive)
    {
        if (drive < 0 || drive > 3) throw new ArgumentOutOfRangeException(nameof(drive));
        if (string.IsNullOrWhiteSpace(filename) || !File.Exists(Path.GetFullPath(Path.Combine(mediaRoot, filename))))
            throw new FileNotFoundException("ADF missing: " + filename);
    }
    if (scenario.Boot != null) Disk(scenario.Boot, 0);
    if (metadata == null && scenario.KickstartRom != null && !File.Exists(Environment.ExpandEnvironmentVariables(scenario.KickstartRom)))
        throw new FileNotFoundException("Kickstart ROM missing: " + scenario.KickstartRom);
    foreach (var action in scenario.Actions ?? [])
    {
        if (action.Cycles < 0) throw new ArgumentOutOfRangeException(nameof(action.Cycles));
        if (action.Label != null) Name(action.Label);
        switch (action.Type)
        {
            case "wait": case "mouse": case "reset": case "restore": break;
            case "key":
                if (!action.Key.HasValue || action.Key < 0 || action.Key > 0x7F || (action.Modifiers ?? []).Any(key => key < 0 || key > 0x7F))
                    throw new ArgumentOutOfRangeException(nameof(action.Key));
                break;
            case "click": case "joystick":
                var buttons = Enum.Parse<AmigaJoystickButtons>(action.Button ?? "Fire", true);
                const AmigaJoystickButtons supportedButtons = AmigaJoystickButtons.Up | AmigaJoystickButtons.Down |
                    AmigaJoystickButtons.Left | AmigaJoystickButtons.Right | AmigaJoystickButtons.Fire |
                    AmigaJoystickButtons.Second | AmigaJoystickButtons.Third;
                if ((buttons & ~supportedButtons) != 0)
                    throw new ArgumentException("Unsupported controller button bits.");
                break;
            case "disk": Disk(action.Disk, action.Drive); break;
            case "eject":
                if (action.Drive < 0 || action.Drive > 3) throw new ArgumentOutOfRangeException(nameof(action.Drive));
                break;
            case "checkpoint": Name(action.Label ?? "checkpoint"); break;
            default: throw new ArgumentException("Unknown action " + action.Type);
        }
    }
}

void WriteResult(Scenario scenario, string status, string error, string actualModel)
{
    // Never call a successfully executed input script a compatibility pass.
    // Reuse only Run's validated directory, including for failure diagnostics.
    if (string.IsNullOrWhiteSpace(scenario.Name) || scenario.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || scenario.Name is "." or "..") return;
    string directory = Path.Combine(args[1], scenario.Name);
    Directory.CreateDirectory(directory);
    File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new
    {
        name = scenario.Name, model = actualModel, requestedModel = scenario.Model,
        boot = scenario.Boot, save = scenario.Save,
        status, error, review = "pending", gameplay = "requires-visual-and-input-review"
    }, new JsonSerializerOptions { WriteIndented = true }));
}

static byte[] StateHash(Action<BinaryWriter> save)
{
    using var bytes = new MemoryStream();
    using (var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, true)) save(writer);
    return SHA256.HashData(bytes.GetBuffer().AsSpan(0, checked((int)bytes.Length)));
}

static string Run(Scenario scenario, string output)
{
    if (string.IsNullOrWhiteSpace(scenario.Name) || scenario.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || scenario.Name is "." or "..")
        throw new ArgumentException("Scenario name must be a valid directory name.");
    string directory = Path.Combine(output, scenario.Name);
    Directory.CreateDirectory(directory);
    string mediaRoot = Environment.ExpandEnvironmentVariables(scenario.MediaRoot ?? "%USERPROFILE%/Documents/AmigaEmulator");
    string save = scenario.Save == null ? null : Environment.ExpandEnvironmentVariables(scenario.Save);
    var saveMetadata = save != null ? SaveStateFile.ReadMetadata(save) : null;
    var machine = saveMetadata != null ? saveMetadata.MachineModel : Enum.Parse<AmigaMachineModel>(scenario.Model ?? "A500");
    var ram = saveMetadata != null ? saveMetadata.RamProfile : scenario.Ram != null
        ? Enum.Parse<AmigaRamProfile>(scenario.Ram) : AmigaModel.GetDefaultRamProfile(machine);
    var model = AmigaModel.Create(machine, ram);
    var accuracy = (scenario.Strict ? AmigaAccuracyOptions.Compatible : AmigaAccuracyOptions.Fast).Clone();
    using var system = new AmigaSystem(model, accuracy);
    using var exceptionTrace = scenario.TraceExceptions
        ? new StreamWriter(Path.Combine(directory, "exceptions.jsonl")) { AutoFlush = true } : null;
    int exceptions = 0;
    int nonRomExceptions = 0;
    var nonRomExceptionVectors = new Dictionary<int, int>();
    object firstNonRomException = null;
    if (exceptionTrace != null) system.Cpu.ExceptionEntered += (vector, pc, sr, opcodePc, opcode, target) =>
    {
        // Interrupts are normal gameplay activity. Keep synchronous CPU faults/traps.
        if (vector >= 24) return;
        exceptions++;
        // Count all instruction addresses outside Kickstart, including 32-bit
        // motherboard RAM. A stale playfield can otherwise hide a guest fault
        // among normal ROM Supervisor calls. Software protection may deliberately
        // use exceptions, so these observations are evidence, not pass/fail rules.
        if (opcodePc < 0xF80000 || opcodePc >= 0x1000000)
        {
            nonRomExceptions++;
            nonRomExceptionVectors[vector] = nonRomExceptionVectors.GetValueOrDefault(vector) + 1;
            firstNonRomException ??= new { frame = system.FrameCounter, vector,
                opcodePc = opcodePc.ToString("X6"), opcode = opcode.ToString("X4"), target = target.ToString("X6") };
        }
        exceptionTrace.WriteLine(JsonSerializer.Serialize(new { frame = system.FrameCounter,
            vector, pc = pc.ToString("X6"), sr = sr.ToString("X4"),
            opcodePc = opcodePc.ToString("X6"), opcode = opcode.ToString("X4"), target = target.ToString("X6") }));
    };
    using var ideTrace = scenario.TraceIde ? new StreamWriter(Path.Combine(directory, "ide-trace.txt")) { AutoFlush = true } : null;
    using var copperTrace = scenario.TraceCopper ? new StreamWriter(Path.Combine(directory, "copper.jsonl")) : null;
    if (copperTrace != null) system.Agnus.Copper.InstructionExecuted += entry =>
        copperTrace.WriteLine(JsonSerializer.Serialize(entry));
    using var copperBuildTrace = scenario.TraceCopperBuild ? new StreamWriter(Path.Combine(directory, "copper-build.jsonl")) : null;
    if (copperBuildTrace != null) system.Bus.WriteObserved += (address, size, value, region) =>
    {
        if (region != AmigaEmulator.Core.Memory.AmigaAddressRegion.ChipRam || address < 2 ||
            size == 1 && (address & 1) == 0) return;
        uint wordAddress = address & ~1u;
        ushort word = system.Bus.PeekWord(wordAddress);
        if (word == 0x100 || system.Bus.PeekWord(wordAddress - 2) == 0x100)
            copperBuildTrace.WriteLine(JsonSerializer.Serialize(new { frame = system.FrameCounter, address = wordAddress,
                value = word, pc = system.Cpu.PC, cpu = system.GetCpuDebugInfo() }));
    };
    if (ideTrace != null) system.Bus.WriteObserved += (address, size, value, region) =>
    { if (region == AmigaEmulator.Core.Memory.AmigaAddressRegion.GayleIde || address >= 0xE80000 && address < 0xE90000) ideTrace.WriteLine($"PC={system.Cpu.PC:X6} W{size} {address:X6}={value:X8}"); };
    if (ideTrace != null) system.Bus.ReadObserved += (address, size, value, region) =>
    { if (address >= 0xE80000 && address < 0xE90000) ideTrace.WriteLine($"PC={system.Cpu.PC:X6} R{size} {address:X6}={value:X8}"); };
    var mounted = MountedMediaInfo.None;
    var kickstart = save == null ? scenario.KickstartRom == null ? AmigaKickstart.Kickstart13 : AmigaKickstart.Custom : saveMetadata.Kickstart;
    if (save != null)
    {
        var metadata = SaveStateFile.ReadMetadata(save);
        SaveStateFile.Load(save, system);
        mounted = metadata.MountedMedia ?? MountedMediaInfo.None;
        // Permit isolated replay against a copy instead of writing the user's
        // live disk recorded in the savestate.
        if (scenario.HardDisk != null)
            system.Bus.HardDisk.Mount(Environment.ExpandEnvironmentVariables(scenario.HardDisk), scenario.HardDiskReadOnly);
    }
    else
    {
        byte[] rom;
        bool loaded = scenario.KickstartRom == null
            ? KickstartRomResolver.TryLoadDefaultKickstart(out rom, out _)
            : KickstartRomResolver.TryLoadKickstart(Environment.ExpandEnvironmentVariables(scenario.KickstartRom), out rom, out _);
        if (!loaded) throw new FileNotFoundException("Kickstart ROM missing");
        string hardDisk = scenario.HardDisk == null ? null : Environment.ExpandEnvironmentVariables(scenario.HardDisk);
        if (scenario.CreateHardDiskMiB > 0) HardDiskImage.Create(hardDisk, scenario.CreateHardDiskMiB);
        if (hardDisk != null) system.Bus.HardDisk.Mount(hardDisk, scenario.HardDiskReadOnly);
        if (scenario.PcmciaSramMiB != 0) system.Bus.Pcmcia.InsertSram(scenario.PcmciaSramMiB * 1024 * 1024);
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
                var modifiers = action.Modifiers ?? [];
                if (!action.Key.HasValue || action.Key < 0 || action.Key > 0x7F || modifiers.Any(key => key < 0 || key > 0x7F))
                    throw new ArgumentOutOfRangeException(nameof(action.Key), "Amiga raw keys must be 0..127.");
                foreach (int modifier in modifiers)
                {
                    system.Input.KeyboardKeyDown((byte)modifier);
                    system.RunChipCycles(120_000);
                }
                system.Input.KeyboardKeyDown((byte)action.Key.Value);
                system.RunChipCycles(action.Cycles > 0 ? action.Cycles : 400_000);
                system.Input.KeyboardKeyUp((byte)action.Key.Value);
                for (int i = modifiers.Count - 1; i >= 0; i--)
                    system.Input.KeyboardKeyUp((byte)modifiers[i]);
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
            case "eject":
                system.Bus.FloppyController.EjectDrive(action.Drive);
                if (mounted.DriveIndex == action.Drive) mounted = MountedMediaInfo.None;
                break;
            case "reset": system.Reset(action.Cold); break;
            case "checkpoint": SaveCheckpoint(action.Label ?? $"step-{step:00}"); break;
            case "restore":
                using (var state = new MemoryStream())
                {
                    var beforeHash = SHA256.HashData(MemoryMarshal.AsBytes(system.PresentedFrameBuffer.Pixels.AsSpan()));
                    var beforeCpuHash = StateHash(system.Cpu.SaveState);
                    var beforeBusHash = StateHash(system.Bus.SaveState);
                    var beforeBeam = system.GetBeamDebugInfo();
                    using (var writer = new BinaryWriter(state, System.Text.Encoding.UTF8, true)) system.SaveState(writer);
                    system.RunChipCycles(model.ChipCyclesPerLine * model.LinesPerFrame);
                    if (action.ResetBeforeRestore) system.Reset(action.Cold);
                    state.Position = 0;
                    using var reader = new BinaryReader(state);
                    system.LoadState(reader);
                    if (!beforeCpuHash.AsSpan().SequenceEqual(StateHash(system.Cpu.SaveState)))
                        throw new InvalidOperationException("Restore changed the serialized CPU state.");
                    if (!beforeBusHash.AsSpan().SequenceEqual(StateHash(system.Bus.SaveState)))
                        throw new InvalidOperationException("Restore changed memory, ROM overlay or floppy state.");
                    if (system.GetBeamDebugInfo() != beforeBeam || !beforeHash.AsSpan().SequenceEqual(
                        SHA256.HashData(MemoryMarshal.AsBytes(system.PresentedFrameBuffer.Pixels.AsSpan()))))
                        throw new InvalidOperationException("Restore changed the saved beam or presented framebuffer.");
                }
                break;
            default: throw new ArgumentException($"Unknown action {action.Type}");
        }
        Capture(action.Label ?? action.Type, timer.Elapsed.TotalSeconds);
        if (scenario.MaximumNonRomExceptions.HasValue && nonRomExceptions > scenario.MaximumNonRomExceptions.Value)
            throw new InvalidOperationException($"Guest CPU faults exceeded the allowed limit after {action.Label ?? action.Type}: {nonRomExceptions} (first: {JsonSerializer.Serialize(firstNonRomException)}).");
        if (action.ExpectedFrameHash != null && !string.Equals(action.ExpectedFrameHash,
            Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(system.PresentedFrameBuffer.Pixels.AsSpan()))),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Framebuffer differs from the reviewed reference after {action.Label ?? action.Type}.");
        if (system.Bus.HardDisk.ReadSectors < action.MinimumHardDiskReads)
            throw new InvalidOperationException($"Expected at least {action.MinimumHardDiskReads} HDF sector reads after {action.Label ?? action.Type}.");
    }
    if (scenario.ExpectPcmciaRam && !GuestMemory().Any(m => m.Lower >= 0x600000 && m.Lower < 0x600400 && m.Upper == 0x600000 + system.Bus.Pcmcia.Size))
        throw new InvalidOperationException("Guest Exec did not register the inserted PCMCIA SRAM card.");
    SaveCheckpoint("checkpoint");
    Console.WriteLine($"DONE {scenario.Name} frame={system.FrameCounter} PC={system.Cpu.PC:X6}");
    return machine.ToString();

    void SaveCheckpoint(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name is "." or "..")
            throw new ArgumentException("Checkpoint label must be a valid filename.");
        SaveStateFile.Write(Path.Combine(directory, name + ".amigasav"), system, mounted,
            kickstart, ram, scenario.Strict ? AmigaAccuracyProfile.Compatible : AmigaAccuracyProfile.Fast,
            system.PresentedFrameBuffer.Pixels, system.PresentedFrameBuffer.Width, system.PresentedFrameBuffer.Height);
    }

    void Mount(string disk, int drive)
    {
        string path = Path.GetFullPath(Path.Combine(mediaRoot, disk));
        system.Bus.FloppyController.InsertDrive(drive, AdfImage.Load(path), scenario.FloppyReadOnly);
        mounted = MountedMediaInfo.ForFloppy(drive, Path.GetFileName(path), path, scenario.FloppyReadOnly);
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
            presentation = system.GetPresentationDebugInfo(), cpu = system.GetCpuDebugInfo(), cacheControl = system.Cpu.CacheControl.ToString("X8"), stopped = system.Cpu.IsStopped, exceptions,
            exceptionObservations = scenario.TraceExceptions ? new { nonRomExceptions, nonRomExceptionVectors, firstNonRomException } : null,
            disk = system.Bus.FloppyController.GetDebugInfo(), hardDisk = new { path = system.Bus.HardDisk.Image?.Path, reads = system.Bus.HardDisk.ReadSectors, writes = system.Bus.HardDisk.WrittenSectors },
            diskDma = system.Paula.GetDiskDebugInfo(), audio = system.Paula.GetAudioDebugInfo(), input = new { port0 = system.Input.Port0Mode.ToString(),
                port1 = system.Input.Port1Mode.ToString(), joy0 = system.Input.ReadJoyDat(AmigaControlPort.Port0).ToString("X4") },
            fastRam = new { configured = model.HasDesktopResources ? model.FastRamBytes != 0 : system.Bus.AutoConfig.Configured,
                address = model.HasDesktopResources ? model.MotherboardFastRamStart : system.Bus.AutoConfig.BaseAddress },
            pcmcia = new { size = system.Bus.Pcmcia.Size, status = system.Bus.PeekByte(0xDA8000), changes = system.Bus.PeekByte(0xDA9000),
                guestMemory = scenario.ExpectPcmciaRam ? GuestMemory() : null }, accuracy = accuracy.Name, imagePath, gameplay = "requires-visual-review" }));
        log.Flush();
    }

    List<GuestMemoryRegion> GuestMemory()
    {
        var result = new List<GuestMemoryRegion>();
        uint exec = system.Bus.PeekLong(4);
        if (exec < 0x400 || exec > 0x9FFF00) return result;
        uint header = system.Bus.PeekLong(exec + 322); // ExecBase.MemList, shared by the classic 68000 ROMs.
        var visited = new HashSet<uint>();
        while (header >= 0x400 && header <= 0x9FFFE0 && visited.Add(header) && result.Count < 32)
        {
            uint next = system.Bus.PeekLong(header);
            if (next == 0) break; // List's tail sentinel is not a MemHeader.
            result.Add(new GuestMemoryRegion(system.Bus.PeekLong(header + 20), system.Bus.PeekLong(header + 24)));
            header = next;
        }
        return result;
    }
}

record GuestMemoryRegion(uint Lower, uint Upper);

class Scenario
{
    public string Name { get; set; }
    public string MediaRoot { get; set; }
    public string Boot { get; set; }
    public string Save { get; set; }
    public string Ram { get; set; }
    public string Model { get; set; }
    public bool Strict { get; set; }
    public string KickstartRom { get; set; }
    public string HardDisk { get; set; }
    public bool HardDiskReadOnly { get; set; } = true;
    public bool FloppyReadOnly { get; set; } = true;
    public int CreateHardDiskMiB { get; set; }
    public int PcmciaSramMiB { get; set; }
    public bool ExpectPcmciaRam { get; set; }
    public bool TraceIde { get; set; }
    public bool TraceExceptions { get; set; }
    public int? MaximumNonRomExceptions { get; set; }
    public bool TraceCopper { get; set; }
    public bool TraceCopperBuild { get; set; }
    public List<ScenarioAction> Actions { get; set; }
}
class ScenarioAction
{
    public string Type { get; set; }
    public string Label { get; set; }
    public int Cycles { get; set; }
    public int? Key { get; set; }
    public List<int> Modifiers { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public string Button { get; set; }
    public string Disk { get; set; }
    public int Drive { get; set; }
    public bool Cold { get; set; }
    public bool ResetBeforeRestore { get; set; }
    public string ExpectedFrameHash { get; set; }
    public long MinimumHardDiskReads { get; set; }
}
