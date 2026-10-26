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
using AmigaEmulator.Core.Memory;
using AmigaEmulator.Core.SaveStates;

// Deterministic input/disk scenarios. A screenshot is evidence for review, never an automatic gameplay pass.
if (args.Length < 2)
{
    Console.Error.WriteLine("GameRunner <scenario.json> <output-directory|--validate> [case-name] [--no-final-checkpoint] [--no-images|--image-label=LABEL ... [--raw-frame]]");
    return 2;
}
string[] extraArgs = args.Skip(2).ToArray();
if (extraArgs.Any(arg => arg.StartsWith("--", StringComparison.Ordinal) &&
    arg is not ("--no-final-checkpoint" or "--no-images" or "--raw-frame") && !arg.StartsWith("--image-label=", StringComparison.Ordinal)) ||
    (extraArgs.Contains("--no-images", StringComparer.Ordinal) &&
     extraArgs.Any(arg => arg.StartsWith("--image-label=", StringComparison.Ordinal))) ||
    (extraArgs.Contains("--raw-frame", StringComparer.Ordinal) &&
     !extraArgs.Any(arg => arg.StartsWith("--image-label=", StringComparison.Ordinal))) ||
    extraArgs.Any(arg => arg.StartsWith("--image-label=", StringComparison.Ordinal) &&
        (arg.Length == "--image-label=".Length ||
         arg["--image-label=".Length..].IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)) ||
    extraArgs.Count(arg => !arg.StartsWith("--", StringComparison.Ordinal)) > 1)
{
    Console.Error.WriteLine("INVALID command line: expected at most one case name, --no-final-checkpoint, --no-images or one or more --image-label=LABEL values, with optional --raw-frame only for selected image labels.");
    return 2;
}
bool writeFinalCheckpoint = !extraArgs.Contains("--no-final-checkpoint", StringComparer.Ordinal);
bool writeImages = !extraArgs.Contains("--no-images", StringComparer.Ordinal);
bool writeRawFrame = extraArgs.Contains("--raw-frame", StringComparer.Ordinal);
string[] imageLabels = extraArgs.Where(arg => arg.StartsWith("--image-label=", StringComparison.Ordinal))
    .Select(arg => arg["--image-label=".Length..]).ToArray();
string selectedCase = extraArgs.FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal));
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
var selected = scenarios.Where(s => selectedCase == null || s.Name == selectedCase).ToList();
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
if (imageLabels.Length != 0)
{
    var availableLabels = selected.SelectMany(s => (s.Actions ?? []).Select(a => a.Label ?? a.Type))
        .Append("loaded").ToHashSet(StringComparer.Ordinal);
    var missingLabels = imageLabels.Distinct(StringComparer.Ordinal)
        .Where(label => !availableLabels.Contains(label)).ToArray();
    if (missingLabels.Length != 0)
    {
        Console.Error.WriteLine("INVALID image label(s): " + string.Join(", ", missingLabels));
        return 2;
    }
}
if (validateOnly) { Console.WriteLine($"VALID {selected.Count} scenarios."); return 0; }
Directory.CreateDirectory(args[1]);
int failures = 0;
foreach (var scenario in selected)
{
    try
    {
        string actualModel = Run(scenario, args[1], writeFinalCheckpoint, writeImages, imageLabels, writeRawFrame);
        WriteResult(scenario, "executed-unreviewed", null, actualModel);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {scenario.Name}: {ex}"); failures++;
        WriteResult(scenario, "failed", ex.ToString(), null);
    }
}
return failures == 0 ? 0 : 1;

static AmigaModel AttachDevelopmentZorroIII(AmigaModel model, Scenario scenario, bool saved)
{
    if (scenario.ZorroIIIRamMiB == 0 && scenario.ZorroIIIRamBoardsMiB == null) return model;
    if (saved) throw new ArgumentException("Zorro III development RAM requires a cold boot, not a saved checkpoint.");
    if (scenario.ZorroIIIRamMiB != 0 && scenario.ZorroIIIRamBoardsMiB != null)
        throw new ArgumentException("Use either zorroIIIRamMiB or zorroIIIRamBoardsMiB, not both.");
    var sizes = scenario.ZorroIIIRamBoardsMiB ?? new List<int> { scenario.ZorroIIIRamMiB };
    return model.WithDevelopmentZorroIIIRamBoards(sizes.Select(size => checked(size * 1024 * 1024)).ToArray());
}

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
    if (scenario.AllowedNonRomExceptionVectors is { } allowed &&
        (!scenario.TraceExceptions || allowed.Any(vector => vector < 2 || vector >= 24) ||
         allowed.Distinct().Count() != allowed.Count))
        throw new ArgumentException("allowedNonRomExceptionVectors requires traceExceptions and distinct observed vectors 2..23.");
    var metadata = scenario.Save == null ? null : SaveStateFile.ReadMetadata(Environment.ExpandEnvironmentVariables(scenario.Save));
    var machine = metadata?.MachineModel ?? Enum.Parse<AmigaMachineModel>(scenario.Model ?? "A500");
    var ram = metadata?.RamProfile ?? (scenario.Ram == null ? AmigaModel.GetDefaultRamProfile(machine) : Enum.Parse<AmigaRamProfile>(scenario.Ram));
    var model = AmigaModel.Create(machine, ram); // Reject unsupported board/RAM pairs rather than normalize them silently.
    model = AttachDevelopmentZorroIII(model, scenario, metadata != null);
    bool InspectableRam(uint start, int length) =>
        (start < 0xA00000 && (ulong)start + (uint)length <= 0xA00000) ||
        (model.SlowRamBytes > 0 && start >= MemoryMap.SlowRamStart &&
         (ulong)start + (uint)length <= (ulong)MemoryMap.SlowRamStart + (uint)model.SlowRamBytes) ||
        (model.HasDesktopResources && model.FastRamBytes > 0 &&
         start >= model.MotherboardFastRamStart && (ulong)start + (uint)length <= 0x08000000);
    if (scenario.MemoryWatchAddresses is { } watched &&
        (watched.Count > 32 || watched.Any(address => !InspectableRam(address, 1)) ||
         watched.Distinct().Count() != watched.Count))
        throw new ArgumentException("memoryWatchAddresses requires at most 32 distinct inspectable RAM addresses.");
    if (scenario.TraceMemoryWriteAddresses is { } traced &&
        (traced.Count is < 1 or > 16 || traced.Any(address => !InspectableRam(address, 1)) ||
         traced.Distinct().Count() != traced.Count))
        throw new ArgumentException("traceMemoryWriteAddresses requires 1..16 distinct inspectable RAM addresses.");
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
        if (action.MouseStepCycles < 0) throw new ArgumentOutOfRangeException(nameof(action.MouseStepCycles));
        if (action.MouseStepCycles != 0 && action.Type != "mouse")
            throw new ArgumentException("mouseStepCycles is only valid for mouse actions.");
        if (action.MinimumNonBlackPixels < 0 || action.MinimumNonBlackPixels >
            AmigaSystem.PresentationViewportWidth * AmigaSystem.PresentationViewportHeight)
            throw new ArgumentOutOfRangeException(nameof(action.MinimumNonBlackPixels));
        if (action.ExpectedFrameHash is { } frameHash &&
            (frameHash.Length != 64 || !frameHash.All(Uri.IsHexDigit)))
            throw new ArgumentException("expectedFrameHash requires a 64-character SHA-256 hex value.");
        if (action.ForbiddenFrameHashes?.Any(hash => hash is null || hash.Length != 64 ||
            !hash.All(Uri.IsHexDigit)) == true)
            throw new ArgumentException("forbiddenFrameHashes requires 64-character SHA-256 hex values.");
        if (action.ExpectedNativeFrameHash is { } nativeHash &&
            (nativeHash.Length != 64 || !nativeHash.All(Uri.IsHexDigit)))
            throw new ArgumentException("expectedNativeFrameHash requires a 64-character SHA-256 hex value.");
        if (action.ExpectedInterlacedFrameHash is { } interlacedHash &&
            (interlacedHash.Length != 64 || !interlacedHash.All(Uri.IsHexDigit)))
            throw new ArgumentException("expectedInterlacedFrameHash requires a 64-character SHA-256 hex value.");
        if (action.ExpectInterlacedFrame == false && action.ExpectedInterlacedFrameHash != null)
            throw new ArgumentException("An interlaced hash cannot be required when expectInterlacedFrame is false.");
        if (action.ExpectedNativeInterlacedFrameHash is { } nativeInterlacedHash &&
            (nativeInterlacedHash.Length != 64 || !nativeInterlacedHash.All(Uri.IsHexDigit)))
            throw new ArgumentException("expectedNativeInterlacedFrameHash requires a 64-character SHA-256 hex value.");
        if (action.ExpectNativeInterlacedFrame == false && action.ExpectedNativeInterlacedFrameHash != null)
            throw new ArgumentException("A native interlaced hash cannot be required when expectNativeInterlacedFrame is false.");
        if (action.ExpectedCiaAPortA is { } cia && (cia.Length != 2 || !cia.All(Uri.IsHexDigit)) ||
            action.ExpectedPotInp is { } pot && (pot.Length != 4 || !pot.All(Uri.IsHexDigit)))
            throw new ArgumentException("Expected input registers require two-digit CIA-A and four-digit POTINP hex values.");
        if (action.ExpectedJoy1 is { } joy1 && (joy1.Length != 4 || !joy1.All(Uri.IsHexDigit)))
            throw new ArgumentException("expectedJoy1 requires a four-digit JOY1DAT hex value.");
        if (action.ExpectedMemoryBytes is { } expectedMemory &&
            (expectedMemory.Count > 32 || expectedMemory.Any(entry =>
                !InspectableRam(entry.Key, 1) || entry.Value is null ||
                entry.Value.Length != 2 || !entry.Value.All(Uri.IsHexDigit))))
            throw new ArgumentException("expectedMemoryBytes requires at most 32 inspectable RAM addresses with two-digit hex byte values.");
        if (action.ExpectedBplcon0 is { } bplcon0 &&
            (bplcon0.Length != 4 || !bplcon0.All(Uri.IsHexDigit)))
            throw new ArgumentException("expectedBplcon0 requires a four-digit hexadecimal register value.");
        if (action.Type != "memory-snapshot" && (action.MemoryStart.HasValue || action.MemoryLength != 0))
            throw new ArgumentException("memoryStart/memoryLength require a memory-snapshot action.");
        if (action.Label != null) Name(action.Label);
        switch (action.Type)
        {
            case "wait": case "mouse": case "reset": case "restore": break;
            case "memory-snapshot":
                if (!action.MemoryStart.HasValue || action.MemoryLength is < 1 or > 65536 ||
                    !InspectableRam(action.MemoryStart.Value, action.MemoryLength))
                    throw new ArgumentException("memory-snapshot requires an inspectable RAM range of 1..65536 bytes.");
                break;
            case "trace-display-start": case "trace-display-stop":
                if (!scenario.TraceDisplayControl)
                    throw new ArgumentException("Display trace actions require traceDisplayControl.");
                break;
            case "trace-memory-start": case "trace-memory-stop":
                if (scenario.TraceMemoryWriteAddresses is not { Count: > 0 })
                    throw new ArgumentException("Memory trace actions require traceMemoryWriteAddresses.");
                break;
            case "key": case "key-down": case "key-up":
                if (!action.Key.HasValue || action.Key < 0 || action.Key > 0x7F || (action.Modifiers ?? []).Any(key => key < 0 || key > 0x7F))
                    throw new ArgumentOutOfRangeException(nameof(action.Key));
                if (action.Type != "key" && (action.Modifiers?.Count ?? 0) != 0)
                    throw new ArgumentException("Held-key actions use separate key-down/key-up events for modifiers.");
                break;
            case "click": case "joystick": case "joystick-down": case "joystick-up":
                if ((action.Type is "joystick-down" or "joystick-up") && string.IsNullOrWhiteSpace(action.Button))
                    throw new ArgumentException("Held joystick actions require an explicit button.");
                var buttons = Enum.Parse<AmigaJoystickButtons>(action.Button ?? "Fire", true);
                const AmigaJoystickButtons supportedButtons = AmigaJoystickButtons.Up | AmigaJoystickButtons.Down |
                    AmigaJoystickButtons.Left | AmigaJoystickButtons.Right | AmigaJoystickButtons.Fire |
                    AmigaJoystickButtons.Second | AmigaJoystickButtons.Third;
                if ((buttons & ~supportedButtons) != 0 ||
                    ((action.Type is "joystick-down" or "joystick-up") && buttons == 0))
                    throw new ArgumentException("Unsupported controller button bits.");
                break;
            case "mouse-button-down": case "mouse-button-up":
                var mouseButton = Enum.Parse<AmigaJoystickButtons>(action.Button ?? "Fire", true);
                if (mouseButton is not (AmigaJoystickButtons.Fire or AmigaJoystickButtons.Second or AmigaJoystickButtons.Third))
                    throw new ArgumentException("Mouse button edges require one of Fire, Second or Third.");
                break;
            case "disk": Disk(action.Disk, action.Drive); break;
            case "eject":
                if (action.Drive < 0 || action.Drive > 3) throw new ArgumentOutOfRangeException(nameof(action.Drive));
                break;
            case "write-protect":
                if (action.Drive < 0 || action.Drive > 3) throw new ArgumentOutOfRangeException(nameof(action.Drive));
                if (!action.WriteProtected.HasValue) throw new ArgumentException("write-protect requires an explicit writeProtected value.");
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

static string Run(Scenario scenario, string output, bool writeFinalCheckpoint, bool writeImages, string[] imageLabels,
    bool writeRawFrame)
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
    model = AttachDevelopmentZorroIII(model, scenario, saveMetadata != null);
    var accuracy = (scenario.Strict ? AmigaAccuracyOptions.Compatible : AmigaAccuracyOptions.Fast).Clone();
    using var system = new AmigaSystem(model, accuracy);
    using var exceptionTrace = scenario.TraceExceptions
        ? new StreamWriter(Path.Combine(directory, "exceptions.jsonl")) { AutoFlush = true } : null;
    int exceptions = 0;
    int nonRomExceptions = 0;
    var nonRomExceptionVectors = new Dictionary<int, int>();
    object firstNonRomException = null;
    object firstUnexpectedNonRomException = null;
    var allowedNonRomVectors = scenario.AllowedNonRomExceptionVectors?.ToHashSet();
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
            // A protection loader's hundreds of intentional trace traps must not
            // conceal a new address/bus fault below its total exception budget.
            if (allowedNonRomVectors != null && !allowedNonRomVectors.Contains(vector))
                firstUnexpectedNonRomException ??= new { frame = system.FrameCounter, vector,
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
    using var displayTrace = scenario.TraceDisplayControl
        ? new StreamWriter(Path.Combine(directory, "display-control.jsonl")) : null;
    using var memoryWriteTrace = scenario.TraceMemoryWriteAddresses is { Count: > 0 }
        ? new StreamWriter(Path.Combine(directory, "memory-writes.jsonl")) : null;
    bool memoryWriteTraceEnabled = memoryWriteTrace != null &&
        !(scenario.Actions?.Any(action => action.Type == "trace-memory-start") ?? false);
    if (memoryWriteTrace != null)
    {
        var traceAddresses = scenario.TraceMemoryWriteAddresses.ToHashSet();
        system.Bus.WriteObserved += (address, size, value, region) =>
        {
            if (!memoryWriteTraceEnabled || size < 1) return;
            foreach (uint watchedAddress in traceAddresses)
            {
                if ((ulong)watchedAddress < address || (ulong)watchedAddress >= (ulong)address + (uint)size) continue;
                memoryWriteTrace.WriteLine(JsonSerializer.Serialize(new {
                    frame = system.FrameCounter, pc = system.Cpu.PC.ToString("X6"),
                    a5 = system.Cpu.Snapshot.A[5].ToString("X6"),
                    a7 = system.Cpu.A7.ToString("X6"),
                    stack = Enumerable.Range(0, 4).Select(index =>
                        system.Bus.PeekLong(system.Cpu.A7 + (uint)(index * 4)).ToString("X8")),
                    address = address.ToString("X6"), size, value = value.ToString("X8"),
                    watchedAddress = watchedAddress.ToString("X6"),
                    watchedByte = system.Bus.PeekByte(watchedAddress).ToString("X2"), region = region.ToString()
                }));
            }
        };
    }
    bool displayTraceEnabled = scenario.TraceDisplayControl &&
        !(scenario.Actions?.Any(action => action.Type == "trace-display-start") ?? false);
    if (displayTrace != null)
    {
        var displayOffsets = new HashSet<uint> {
            0x08E, 0x090, 0x092, 0x094, 0x096, 0x0E0, 0x0E2, 0x0E4, 0x0E6,
            0x100, 0x108, 0x10A, 0x1E4
        };
        system.Bus.WriteObserved += (address, size, value, region) =>
        {
            uint offset = address & 0x1FE;
            if (!displayTraceEnabled || region != AmigaEmulator.Core.Memory.AmigaAddressRegion.CustomRegisters ||
                !displayOffsets.Contains(offset)) return;
            displayTrace.WriteLine(JsonSerializer.Serialize(new {
                source = "cpu", frame = system.FrameCounter, beam = system.GetBeamDebugInfo(),
                register = offset.ToString("X3"), size, value = value.ToString("X8"),
                state = system.GetDisplayRegisterDebugInfo()
            }));
        };
        system.Agnus.Copper.CustomRegisterWritten += (address, size) =>
        {
            uint offset = address & 0x1FE;
            if (!displayTraceEnabled || !displayOffsets.Contains(offset)) return;
            displayTrace.WriteLine(JsonSerializer.Serialize(new {
                source = "copper", frame = system.FrameCounter, beam = system.GetBeamDebugInfo(),
                register = offset.ToString("X3"), size,
                state = system.GetDisplayRegisterDebugInfo()
            }));
        };
    }
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
            case "memory-snapshot":
                system.RunChipCycles(action.Cycles);
                var memory = new byte[action.MemoryLength];
                for (int i = 0; i < memory.Length; i++)
                    memory[i] = system.Bus.PeekByte(action.MemoryStart.Value + (uint)i);
                File.WriteAllBytes(Path.Combine(directory,
                    $"{step:00}-{action.Label ?? action.Type}-ram-{action.MemoryStart.Value:X8}-{action.MemoryLength}.bin"), memory);
                break;
            case "trace-display-start": displayTraceEnabled = true; break;
            case "trace-display-stop": displayTraceEnabled = false; break;
            case "trace-memory-start": memoryWriteTraceEnabled = true; break;
            case "trace-memory-stop": memoryWriteTraceEnabled = false; break;
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
            // Separate edges permit inspecting a paused guest before releasing P,
            // and chords whose keys must remain down across several actions.
            case "key-down": case "key-up":
                if (action.Type == "key-down") system.Input.KeyboardKeyDown((byte)action.Key.Value);
                else system.Input.KeyboardKeyUp((byte)action.Key.Value);
                system.RunChipCycles(action.Cycles);
                break;
            case "mouse":
                int slices = Math.Max(1, (Math.Max(Math.Abs(action.X), Math.Abs(action.Y)) + 7) / 8);
                int previousX = 0, previousY = 0;
                for (int i = 1; i <= slices; i++)
                {
                    int x = action.X * i / slices, y = action.Y * i / slices;
                    system.Input.MoveMouse(AmigaControlPort.Port0, x - previousX, y - previousY);
                    system.RunChipCycles(action.MouseStepCycles == 0 ? 120_000 : action.MouseStepCycles);
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
            // Exact controller edges allow a continuous hold across frame samples.
            // The legacy joystick action intentionally adds 400,000 released clocks.
            case "joystick-down": case "joystick-up":
                system.Input.SetJoystickButton(AmigaControlPort.Port1,
                    Enum.Parse<AmigaJoystickButtons>(action.Button, true), action.Type == "joystick-down");
                system.RunChipCycles(action.Cycles);
                break;
            case "mouse-button-down": case "mouse-button-up":
                system.Input.SetMouseButton(AmigaControlPort.Port0,
                    Enum.Parse<AmigaJoystickButtons>(action.Button ?? "Fire", true),
                    action.Type == "mouse-button-down");
                system.RunChipCycles(action.Cycles);
                break;
            case "disk": Mount(action.Disk, action.Drive); break;
            case "write-protect":
                system.Bus.FloppyController.SetDriveWriteProtection(action.Drive, action.WriteProtected.Value);
                system.Bus.RefreshFloppyControlLines();
                if (mounted.Kind == MountedMediaKind.Adf && mounted.DriveIndex == action.Drive)
                    mounted.IsWriteProtected = action.WriteProtected.Value;
                system.RunChipCycles(action.Cycles);
                break;
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
        var (frameHash, nonBlackPixels, nativeFrameHash, interlacedFrameHash, nativeInterlacedFrameHash) =
            Capture(action.Label ?? action.Type, timer.Elapsed.TotalSeconds);
        if (firstUnexpectedNonRomException != null)
            throw new InvalidOperationException($"Unexpected guest CPU fault after {action.Label ?? action.Type}: {JsonSerializer.Serialize(firstUnexpectedNonRomException)}.");
        if (scenario.MaximumNonRomExceptions.HasValue && nonRomExceptions > scenario.MaximumNonRomExceptions.Value)
            throw new InvalidOperationException($"Guest CPU faults exceeded the allowed limit after {action.Label ?? action.Type}: {nonRomExceptions} (first: {JsonSerializer.Serialize(firstNonRomException)}).");
        if (action.ExpectedFrameHash != null && !string.Equals(action.ExpectedFrameHash,
            frameHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Framebuffer differs from the reviewed reference after {action.Label ?? action.Type}.");
        if (action.ExpectedNativeFrameHash != null && !string.Equals(action.ExpectedNativeFrameHash,
            nativeFrameHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Native SHRES framebuffer differs after {action.Label ?? action.Type}: {nativeFrameHash ?? "no native frame"}.");
        if (action.ExpectedInterlacedFrameHash != null && !string.Equals(action.ExpectedInterlacedFrameHash,
            interlacedFrameHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Interlaced framebuffer differs after {action.Label ?? action.Type}: {interlacedFrameHash ?? "no complete field pair"}.");
        if (action.ExpectInterlacedFrame.HasValue &&
            action.ExpectInterlacedFrame.Value != (interlacedFrameHash != null))
            throw new InvalidOperationException($"Interlaced framebuffer presence differs after {action.Label ?? action.Type}.");
        if (action.ExpectedNativeInterlacedFrameHash != null && !string.Equals(action.ExpectedNativeInterlacedFrameHash,
            nativeInterlacedFrameHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Native interlaced framebuffer differs after {action.Label ?? action.Type}: {nativeInterlacedFrameHash ?? "no complete native field pair"}.");
        if (action.ExpectNativeInterlacedFrame.HasValue &&
            action.ExpectNativeInterlacedFrame.Value != (nativeInterlacedFrameHash != null))
            throw new InvalidOperationException($"Native interlaced framebuffer presence differs after {action.Label ?? action.Type}.");
        if (action.ExpectedBplcon0 != null && !system.GetDisplayRegisterDebugInfo().StartsWith(
            $"BPLCON0:{action.ExpectedBplcon0}", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"BPLCON0 differs after {action.Label ?? action.Type}: {system.GetDisplayRegisterDebugInfo()}.");
        if (action.ForbiddenFrameHashes?.Contains(frameHash, StringComparer.OrdinalIgnoreCase) == true)
            throw new InvalidOperationException($"Framebuffer matches a forbidden reviewed screen after {action.Label ?? action.Type}: {frameHash}.");
        if (action.ExpectedCiaAPortA != null && !string.Equals(action.ExpectedCiaAPortA,
            system.Input.GetCiaAPortAInputValue().ToString("X2"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"CIA-A input differs from the expected value after {action.Label ?? action.Type}.");
        if (action.ExpectedPotInp != null && !string.Equals(action.ExpectedPotInp,
            system.Input.ReadPotInp(0).ToString("X4"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"POTINP input differs from the expected value after {action.Label ?? action.Type}.");
        if (action.ExpectedJoy1 != null && !string.Equals(action.ExpectedJoy1,
            system.Input.ReadJoyDat(AmigaControlPort.Port1).ToString("X4"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"JOY1DAT differs from the expected value after {action.Label ?? action.Type}.");
        if (action.ExpectedMemoryBytes is { } expectedMemoryBytes)
            foreach (var (address, expected) in expectedMemoryBytes)
            {
                byte actual = system.Bus.PeekByte(address);
                if (!string.Equals(expected, actual.ToString("X2"), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"RAM byte ${address:X6} differs after {action.Label ?? action.Type}: expected ${expected}, actual ${actual:X2}.");
            }
        if (system.Bus.HardDisk.ReadSectors < action.MinimumHardDiskReads)
            throw new InvalidOperationException($"Expected at least {action.MinimumHardDiskReads} HDF sector reads after {action.Label ?? action.Type}.");
        if (action.MinimumNonBlackPixels > 0 && nonBlackPixels < action.MinimumNonBlackPixels)
            throw new InvalidOperationException($"Visible pixels fell below {action.MinimumNonBlackPixels} after {action.Label ?? action.Type}; black output is not gameplay evidence.");
    }
    if (scenario.ExpectPcmciaRam && !GuestMemory().Any(m => m.Lower >= 0x600000 && m.Lower < 0x600400 && m.Upper == 0x600000 + system.Bus.Pcmcia.Size))
        throw new InvalidOperationException("Guest Exec did not register the inserted PCMCIA SRAM card.");
    if (writeFinalCheckpoint) SaveCheckpoint("checkpoint");
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
    (string Hash, int NonBlackPixels, string NativeHash, string InterlacedHash, string NativeInterlacedHash)
        Capture(string label, double seconds)
    {
        var frame = system.PresentedFrameBuffer;
        int captureIndex = step++;
        bool captureImage = writeImages && (imageLabels.Length == 0 || imageLabels.Contains(label, StringComparer.Ordinal));
        string imagePath = captureImage ? Path.Combine(directory, $"{captureIndex:00}-{label}.png") : null;
        if (captureImage) SaveArgbPng(imagePath, frame.Pixels, frame.Width, frame.Height);
        if (captureImage && writeRawFrame)
        {
            var raw = system.FrameBuffer;
            SaveArgbPng(Path.Combine(directory, $"{captureIndex:00}-{label}-raw.png"),
                raw.Pixels, raw.Width, raw.Height);
            var spritePixels = new uint[raw.Pixels.Length];
            raw.CopySpritePixelsTo(spritePixels);
            SaveArgbPng(Path.Combine(directory, $"{captureIndex:00}-{label}-raw-sprites.png"),
                spritePixels, raw.Width, raw.Height);
            var playfieldPixels = new uint[raw.Pixels.Length];
            for (int pixel = 0; pixel < playfieldPixels.Length; pixel++)
                playfieldPixels[pixel] = raw.ReadPlayfieldPixel(pixel);
            SaveArgbPng(Path.Combine(directory, $"{captureIndex:00}-{label}-raw-playfield.png"),
                playfieldPixels, raw.Width, raw.Height);
        }
        string nativeImagePath = null;
        string nativeHash = null;
        if (frame.SuperHiresPlayfieldPixels != null)
        {
            uint[] native = new uint[frame.Pixels.Length * 2];
            frame.CopyCompositedSuperHiresTo(native);
            if (captureImage)
            {
                nativeImagePath = Path.Combine(directory, $"{captureIndex:00}-{label}-native-shres.png");
                SaveArgbPng(nativeImagePath, native, frame.Width * 2, frame.Height);
            }
            nativeHash = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(native.AsSpan())));
        }
        string interlacedImagePath = null;
        string interlacedHash = null;
        if (system.CompletedInterlacedFrameBuffer is { } interlaced)
        {
            if (captureImage)
            {
                interlacedImagePath = Path.Combine(directory, $"{captureIndex:00}-{label}-interlaced.png");
                SaveArgbPng(interlacedImagePath, interlaced.Pixels, interlaced.Width, interlaced.Height);
            }
            interlacedHash = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(interlaced.Pixels.AsSpan())));
        }
        string nativeInterlacedImagePath = null;
        string nativeInterlacedHash = null;
        if (system.CompletedNativeInterlacedFrameBuffer is { } nativeInterlaced)
        {
            if (captureImage)
            {
                nativeInterlacedImagePath = Path.Combine(directory, $"{captureIndex:00}-{label}-native-interlaced.png");
                SaveArgbPng(nativeInterlacedImagePath, nativeInterlaced.Pixels,
                    nativeInterlaced.Width, nativeInterlaced.Height);
            }
            nativeInterlacedHash = Convert.ToHexString(SHA256.HashData(
                MemoryMarshal.AsBytes(nativeInterlaced.Pixels.AsSpan())));
        }
        var bytes = MemoryMarshal.AsBytes(frame.Pixels.AsSpan());
        string frameHash = Convert.ToHexString(SHA256.HashData(bytes));
        int nonBlackPixels = frame.Pixels.Count(pixel => (pixel & 0xFFFFFFu) != 0);
        log.WriteLine(JsonSerializer.Serialize(new { label, seconds, frame = system.FrameCounter,
            pc = system.Cpu.PC.ToString("X6"), hash = frameHash,
            nonBlackPixels,
            presentation = system.GetPresentationDebugInfo(),
            displayRegisters = system.GetDisplayRegisterDebugInfo(),
            cpu = system.GetCpuDebugInfo(), cacheControl = system.Cpu.CacheControl.ToString("X8"), stopped = system.Cpu.IsStopped, exceptions,
            exceptionObservations = scenario.TraceExceptions ? new { nonRomExceptions, nonRomExceptionVectors, firstNonRomException } : null,
            disk = system.Bus.FloppyController.GetDebugInfo(), hardDisk = new { path = system.Bus.HardDisk.Image?.Path, reads = system.Bus.HardDisk.ReadSectors, writes = system.Bus.HardDisk.WrittenSectors },
            diskDma = system.Paula.GetDiskDebugInfo(), audio = system.Paula.GetAudioDebugInfo(), input = new { port0 = system.Input.Port0Mode.ToString(),
                port1 = system.Input.Port1Mode.ToString(), joy0 = system.Input.ReadJoyDat(AmigaControlPort.Port0).ToString("X4"),
                joy1 = system.Input.ReadJoyDat(AmigaControlPort.Port1).ToString("X4"),
                ciaAPortA = system.Input.GetCiaAPortAInputValue().ToString("X2"),
            potInp = system.Input.ReadPotInp(0).ToString("X4"),
                queuedKeyboardEvents = system.Input.QueuedKeyboardEvents },
            memoryWatch = scenario.MemoryWatchAddresses?.ToDictionary(
                address => address.ToString("X6"), address => system.Bus.PeekByte(address).ToString("X2")),
            fastRam = new { configured = model.HasDesktopResources ? model.FastRamBytes != 0 : system.Bus.AutoConfig.Configured,
                address = model.HasDesktopResources ? model.MotherboardFastRamStart : system.Bus.AutoConfig.BaseAddress },
            zorroIII = new { size = system.Bus.ZorroIII.Size, configured = system.Bus.ZorroIII.Configured,
                address = system.Bus.ZorroIII.BaseAddress, ramReads = system.Bus.ZorroIII.RamReads,
                ramWrites = system.Bus.ZorroIII.RamWrites,
                lastRamRead = system.Bus.ZorroIII.LastRamReadAddress,
                lastRamWrite = system.Bus.ZorroIII.LastRamWriteAddress,
                configReads = system.Bus.ZorroIII.ConfigReads,
                configWrites = system.Bus.ZorroIII.ConfigWrites,
                assignments = system.Bus.ZorroIII.Assignments,
                shutups = system.Bus.ZorroIII.Shutups,
                lastConfigWriteOffset = system.Bus.ZorroIII.LastConfigWriteOffset,
                lastConfigWriteValue = system.Bus.ZorroIII.LastConfigWriteValue,
                highWriteValue = system.Bus.ZorroIII.HighWriteValue,
                lowWriteValue = system.Bus.ZorroIII.LowWriteValue },
            zorroIIICards = system.Bus.ZorroIIIExpansion.Cards.Select((card, slot) => new {
                slot, size = card.Size, configured = card.Configured, address = card.BaseAddress,
                ramReads = card.RamReads, ramWrites = card.RamWrites, configReads = card.ConfigReads,
                configWrites = card.ConfigWrites, assignments = card.Assignments, shutups = card.Shutups }),
            zorroIIIGuestMemory = system.Model.ZorroIIIRamBytes != 0 ? GuestMemory() : null,
            pcmcia = new { size = system.Bus.Pcmcia.Size, status = system.Bus.PeekByte(0xDA8000), changes = system.Bus.PeekByte(0xDA9000),
                guestMemory = scenario.ExpectPcmciaRam ? GuestMemory() : null }, accuracy = accuracy.Name,
            imagePath, nativeImagePath, nativeHash, interlacedImagePath, interlacedHash,
            nativeInterlacedImagePath, nativeInterlacedHash,
            gameplay = "requires-visual-review" }));
        log.Flush();
        return (frameHash, nonBlackPixels, nativeHash, interlacedHash, nativeInterlacedHash);
    }

    static void SaveArgbPng(string path, uint[] pixels, int width, int height)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int[] upload = new int[pixels.Length];
            Buffer.BlockCopy(pixels, 0, upload, 0, pixels.Length * 4);
            Marshal.Copy(upload, 0, data.Scan0, upload.Length);
        }
        finally { bitmap.UnlockBits(data); }
        bitmap.Save(path, ImageFormat.Png);
    }

    List<GuestMemoryRegion> GuestMemory()
    {
        var result = new List<GuestMemoryRegion>();
        bool GuestRam(uint address, uint bytes)
        {
            if (address < 0x400 || bytes == 0 || (ulong)address + bytes > 0x100000000UL) return false;
            var region = system.Bus.GetRegion(address);
            return (region == AmigaAddressRegion.ChipRam || region == AmigaAddressRegion.SlowRam ||
                region == AmigaAddressRegion.FastRam || region == AmigaAddressRegion.ZorroIIIRam) &&
                system.Bus.GetRegion(address + bytes - 1) == region;
        }
        uint exec = system.Bus.PeekLong(4);
        if (!GuestRam(exec, 326)) return result;
        uint header = system.Bus.PeekLong(exec + 322); // ExecBase.MemList, including headers in native 32-bit RAM.
        var visited = new HashSet<uint>();
        while (GuestRam(header, 28) && visited.Add(header) && result.Count < 32)
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
    public int ZorroIIIRamMiB { get; set; }
    public List<int> ZorroIIIRamBoardsMiB { get; set; }
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
    public List<int> AllowedNonRomExceptionVectors { get; set; }
    public List<uint> MemoryWatchAddresses { get; set; }
    public List<uint> TraceMemoryWriteAddresses { get; set; }
    public bool TraceCopper { get; set; }
    public bool TraceDisplayControl { get; set; }
    public bool TraceCopperBuild { get; set; }
    public List<ScenarioAction> Actions { get; set; }
}
class ScenarioAction
{
    public string Type { get; set; }
    public string Label { get; set; }
    public int Cycles { get; set; }
    // Zero preserves the established 120,000-chip-clock pacing per <=8-count move.
    public int MouseStepCycles { get; set; }
    public int? Key { get; set; }
    public List<int> Modifiers { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public string Button { get; set; }
    public string Disk { get; set; }
    public int Drive { get; set; }
    public bool Cold { get; set; }
    public bool ResetBeforeRestore { get; set; }
    public bool? WriteProtected { get; set; }
    public string ExpectedFrameHash { get; set; }
    public string ExpectedNativeFrameHash { get; set; }
    public string ExpectedInterlacedFrameHash { get; set; }
    public bool? ExpectInterlacedFrame { get; set; }
    public string ExpectedNativeInterlacedFrameHash { get; set; }
    public bool? ExpectNativeInterlacedFrame { get; set; }
    public string ExpectedCiaAPortA { get; set; }
    public string ExpectedPotInp { get; set; }
    public string ExpectedJoy1 { get; set; }
    public Dictionary<uint, string> ExpectedMemoryBytes { get; set; }
    public string ExpectedBplcon0 { get; set; }
    public uint? MemoryStart { get; set; }
    public int MemoryLength { get; set; }
    // A reviewed loss/menu frame may contain many nonblack pixels; reject it explicitly.
    public List<string> ForbiddenFrameHashes { get; set; }
    public long MinimumHardDiskReads { get; set; }
    // An optional scene-specific guard; visible menu/error pixels still require review.
    public int MinimumNonBlackPixels { get; set; }
}
