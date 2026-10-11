// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using AmigaEmulator.Core;
using AmigaEmulator.Core.Media;

namespace AmigaEmulator.Tests;

internal static class FloppyWriteProtectionTests
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static IEnumerable<ChipTestCase> Build()
    {
        yield return new("Disk protection", "actual menu preserves the mounted disk, dirty sectors and partial write",
            "Changing the write-protect tab is not an eject/insert operation", () => Sta(Menu));
        yield return new("Disk protection", "actual menu updates a deferred insertion without resetting its deadline",
            "Protection chosen while DF0 is queued must apply to that same pending medium", () => Sta(Deferred));
        yield return new("Disk protection", "all four ports change PROT without CHNG and retain pending insertion through restore",
            "HRM disk sensing: write protection is independent of the media-change latch", Pending);
    }

    private static object Get(AmigaWindow window, string name) => typeof(AmigaWindow).GetField(name, Private).GetValue(window);
    private static void Set(AmigaWindow window, string name, object value) => typeof(AmigaWindow).GetField(name, Private).SetValue(window, value);
    private static void Call(AmigaWindow window, string name, params object[] arguments) => typeof(AmigaWindow).GetMethod(name, Private).Invoke(window, arguments);

    private static void Menu()
    {
        using var profile = new Profile();
        using var window = new AmigaWindow(AmigaModel.A500Pal, "Disk protection regression");
        try
        {
            var system = (AmigaSystem)Get(window, "_system");
            var drive = system.Bus.FloppyController.Drive0;
            AdfImage image = AdfImage.Load(profile.Disk);
            drive.Insert(image, false);
            system.Bus.WriteByte(0xBFD300, 0xFF);
            system.Bus.WriteByte(0xBFD100, 0x7F);
            system.Bus.WriteByte(0xBFD100, 0x71);
            system.Bus.WriteByte(0xBFD100, 0x70);
            system.Bus.WriteByte(0xBFD100, 0x71);
            drive.Step(23); drive.SetSide(1);
            for (int cycle = 0; cycle < 18 * 312 * 227 + 1234; cycle++) drive.Tick();
            byte[] payload = new byte[AdfImage.SectorSize]; Array.Fill(payload, (byte)0x73);
            ushort[] words = AdfMfmEncoder.EncodeSectorWords(payload, 49, 3, 8);
            for (int i = 0; i < 300; i++) Check.True(drive.TryCaptureWrittenMfmWord(words[i]), "A writable disk accepts the partial sector.");
            image.WriteSector(0, 0, 2, payload, 0);
            Set(window, "_mountedMedia", MountedMediaInfo.ForFloppy(0, "Private test disk", profile.Disk, false));
            Set(window, "_floppyWriteProtected", false);
            int phase = drive.StreamWordPosition, index = drive.IndexPulseCountdown;

            Call(window, "ToggleDiskWriteProtection");
            Check.True(drive.HasDisk && drive.Ready && drive.CanReadFlux, "Changing the tab must not eject or spin down the disk.");
            Check.True(ReferenceEquals(image, drive.Image) && image.IsDirty, "The same dirty in-memory image remains mounted.");
            Check.True(drive.WriteProtected && !drive.DiskChanged, "Only PROT changes; CHNG must remain deasserted.");
            Check.Equal(24, drive.Track, "The physical head stays on its track.");
            Check.Equal(1, drive.Side, "The selected side stays unchanged.");
            Check.Equal(phase, drive.StreamWordPosition, "Rotation phase remains unchanged.");
            Check.Equal(index, drive.IndexPulseCountdown, "INDEX keeps its deadline.");
            Check.Equal(300, drive.CapturedWriteWordCount, "A partial MFM write must not be discarded.");
            Check.True(((MountedMediaInfo)Get(window, "_mountedMedia")).IsWriteProtected, "Savestate media metadata follows the tab.");
            Check.Equal((byte)0, File.ReadAllBytes(profile.Disk)[2 * AdfImage.SectorSize], "The toggle does not force host persistence.");
            Check.True(!drive.TryCaptureWrittenMfmWord(words[300]), "Protected media rejects new write data.");

            Call(window, "ToggleDiskWriteProtection");
            Check.True(!drive.WriteProtected && !drive.DiskChanged && drive.Ready, "Unlocking also preserves the live drive.");
            for (int i = 300; i < words.Length; i++) Check.True(drive.TryCaptureWrittenMfmWord(words[i]), "The existing partial sector resumes.");
            Check.Equal(1, drive.DecodedWriteSectorCount, "The resumed MFM sector is decoded once.");
            drive.FlushImage();
            byte[] persisted = File.ReadAllBytes(profile.Disk);
            Check.Equal((byte)0x73, persisted[2 * AdfImage.SectorSize], "Previously dirty data survives both toggles.");
            Check.Equal((byte)0x73, persisted[(49 * 11 + 3) * AdfImage.SectorSize], "The resumed sector persists at its own track address.");
        }
        finally { Call(window, "OnFormClosed", new FormClosedEventArgs(CloseReason.None)); }
    }

    private static void Pending()
    {
        using var profile = new Profile();
        for (int port = 0; port < 4; port++)
        {
            var controller = new FloppyController();
            var drive = controller.GetDrive(port);
            controller.InsertDrive(port, AdfImage.Load(profile.Disk), true);
            drive.ApplyControlLines(true, false, false, true);
            drive.ApplyControlLines(true, false, true, true);
            Check.True((controller.BuildCiaAStatusBits() & FloppyController.CiaAWriteProtectBit) == 0, "Protected media asserts PROT.");
            controller.SetDriveWriteProtection(port, false);
            Check.True((controller.BuildCiaAStatusBits() & FloppyController.CiaAWriteProtectBit) != 0, "The selected drive releases PROT immediately.");
            Check.True(!drive.DiskChanged && drive.HasDisk, "Changing protection alone does not assert CHNG.");
            controller.InsertDrive(port, AdfImage.Load(profile.Disk), true);
            for (int cycle = 0; cycle < 500; cycle++) drive.Tick();
            controller.SetDriveWriteProtection(port, false);
            using var state = new MemoryStream();
            using (var writer = new BinaryWriter(state, System.Text.Encoding.UTF8, true))
                AmigaEmulator.Core.SaveStates.StateSerializer.WriteObjectFields(writer, drive);
            drive.Tick();
            state.Position = 0;
            using (var reader = new BinaryReader(state, System.Text.Encoding.UTF8, true))
                AmigaEmulator.Core.SaveStates.StateSerializer.ReadObjectFields(reader, drive);
            for (int cycle = 0; cycle < FloppyDrive.MediaChangeChipCycles - 501; cycle++) drive.Tick();
            Check.True(!drive.HasDisk, "Restored replacement retains the last absent cycle.");
            drive.Tick();
            Check.True(drive.HasDisk && !drive.WriteProtected && drive.DiskChanged, "The pending medium becomes present with its chosen PROT and real CHNG.");
        }
    }

    private static void Deferred()
    {
        using var profile = new Profile();
        using var window = new AmigaWindow(AmigaModel.A500Pal, "Queued disk protection regression");
        try
        {
            var media = MountedMediaInfo.ForFloppy(0, "Queued private disk", profile.Disk, true);
            Call(window, "ScheduleDeferredMediaMount", media);
            long deadline = (long)Get(window, "_deferredMediaMountFrame");
            Call(window, "ToggleDiskWriteProtection");
            var pending = (MountedMediaInfo)Get(window, "_deferredMediaMount");
            Check.True(pending != null && !pending.IsWriteProtected, "The queued disk retains the new protection choice.");
            Check.Equal(deadline, (long)Get(window, "_deferredMediaMountFrame"), "Changing protection does not restart the startup delay.");
            Check.Equal(profile.Disk, pending.HostPath, "The selected queued path is retained.");
            var mounted = (MountedMediaInfo)Get(window, "_mountedMedia");
            Check.True(mounted.Status == MountedMediaStatus.Loading && !mounted.IsWriteProtected, "The menu still reports a pending insertion.");
            Check.True(((AmigaSystem)Get(window, "_system")).Bus.FloppyController.Drive0.Image == null, "The disk is not inserted ahead of its deadline.");
        }
        finally { Call(window, "OnFormClosed", new FormClosedEventArgs(CloseReason.None)); }
    }

    private static void Sta(Action action)
    {
        Exception failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure != null) throw failure;
    }

    private sealed class Profile : IDisposable
    {
        private readonly string _previous = Environment.GetEnvironmentVariable("AMIGAEMULATOR_USER_DATA_DIRECTORY");
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "AmigaDiskProtectionTests", Guid.NewGuid().ToString("N"));
        public string Disk { get; }
        public Profile()
        {
            Directory.CreateDirectory(_directory);
            Disk = Path.Combine(_directory, "private-test.adf");
            File.WriteAllBytes(Disk, new byte[AdfImage.StandardSize]);
            Environment.SetEnvironmentVariable("AMIGAEMULATOR_USER_DATA_DIRECTORY", _directory);
            AmigaEmulatorSettingsStore.Save(new() { AudioEnabled = false });
        }
        public void Dispose() => Environment.SetEnvironmentVariable("AMIGAEMULATOR_USER_DATA_DIRECTORY", _previous);
    }
}
