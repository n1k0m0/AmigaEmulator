using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AmigaEmulator.Core;
using AmigaEmulator.Core.Media;
using AmigaEmulator.Core.SaveStates;

namespace AmigaEmulator.Tests
{
    internal static class FloppyMediaChangeTests
    {
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return new ChipTestCase("Paula/Disk", "media change exposes an empty drive before replacement on all four ports", "Guest disk-change polling and physical media removal", TestReplacement);
            yield return new ChipTestCase("Paula/Disk", "media change retains elapsed eject time and cancels pending insertion", "Physical eject/insert lifecycle", TestEject);
            yield return new ChipTestCase("SaveState", "media change resumes the pending insertion after file restore", "In-flight disk replacement and read-only host media restoration", TestRestore);
            yield return new ChipTestCase("SaveState", "legacy floppy states have no pending media change", "Backward-compatible drive state fields", TestLegacy);
        }

        private static string NewDisk()
        {
            string path = Path.Combine(Path.GetTempPath(), "amiga-media-change-" + Guid.NewGuid().ToString("N") + ".adf");
            File.WriteAllBytes(path, new byte[AdfImage.StandardSize]);
            return path;
        }

        private static void Advance(FloppyDrive drive, int cycles)
        {
            for (int i = 0; i < cycles; i++) drive.Tick();
        }

        private static void TestReplacement()
        {
            string oldPath = NewDisk(), newPath = NewDisk();
            try
            {
                for (int port = 0; port < 4; port++)
                {
                    var controller = new FloppyController();
                    var drive = controller.GetDrive(port);
                    controller.InsertDrive(port, AdfImage.Load(oldPath), true);
                    Check.True(drive.HasDisk, "An initial boot insertion must be immediately present.");
                    drive.ApplySelection(true, true);
                    drive.Step(24); drive.SetSide(1);
                    Advance(drive, 18 * 312 * 227);
                    Check.True(drive.Ready && drive.CanReadFlux, "The outgoing disk must be readable.");
                    controller.InsertDrive(port, AdfImage.Load(newPath), false);
                    Check.True(drive.Selected && drive.MotorOn, "Replacement must preserve the drive control lines.");
                    Check.Equal(24, drive.Track, "Replacement must preserve the head position.");
                    Check.Equal(1, drive.Side, "Replacement must preserve the side.");
                    Check.True(!drive.HasDisk && !drive.Ready && !drive.CanReadFlux, "An atomic replacement must not remain readable to the guest.");
                    byte status = controller.BuildCiaAStatusBits();
                    Check.True((status & FloppyController.CiaADiskChangeBit) == 0, "The empty interval must assert CHNG.");
                    Check.True((status & FloppyController.CiaAReadyBit) != 0, "The empty interval must deassert media RDY.");
                    Check.Equal((ushort)0xAAAA, drive.ReadNextMfmWord(), "Queued media must not deliver flux.");
                    Check.True(!drive.TryCaptureWrittenMfmWord(0x4489), "Queued writable media must not accept DMA writes.");
                    drive.ApplyControlLines(true, true, false, true);
                    drive.ApplyControlLines(true, true, true, true);
                    Check.True(drive.DiskChanged, "Stepping without physical media must not clear CHNG.");
                    Advance(drive, FloppyDrive.MediaChangeChipCycles - 1);
                    Check.True(!drive.HasDisk, "The replacement must remain absent for the entire interval.");
                    drive.Tick();
                    Check.True(drive.HasDisk && drive.DiskChanged && !drive.Ready, "Insertion must precede motor-ready spin-up.");
                    Advance(drive, 18 * 312 * 227);
                    Check.True(drive.Ready && drive.CanReadFlux, "Inserted media must become readable.");
                    drive.ApplyControlLines(true, true, false, false);
                    drive.ApplyControlLines(true, true, true, false);
                    Check.True(!drive.DiskChanged, "A real step after insertion must clear CHNG.");
                    Check.True(!drive.WriteProtected, "The requested incoming write protection must survive the interval.");
                }
            }
            finally { File.Delete(oldPath); File.Delete(newPath); }
        }

        private static void TestEject()
        {
            string path = NewDisk();
            try
            {
                var drive = new FloppyDrive();
                drive.Insert(AdfImage.Load(path)); drive.Eject();
                int elapsed = FloppyDrive.MediaChangeChipCycles / 3;
                Advance(drive, elapsed);
                drive.Insert(AdfImage.Load(path));
                Advance(drive, FloppyDrive.MediaChangeChipCycles - elapsed - 1);
                Check.True(!drive.HasDisk, "Eject time must count toward the minimum absent interval.");
                drive.Tick();
                Check.True(drive.HasDisk, "Explicit eject must not incur a second full delay on insertion.");
                drive.Insert(AdfImage.Load(path)); drive.Eject();
                Advance(drive, FloppyDrive.MediaChangeChipCycles + 1);
                Check.True(drive.Image == null && !drive.HasDisk, "Ejecting a pending replacement must cancel it.");
                drive.Insert(AdfImage.Load(path));
                Check.True(drive.HasDisk, "A drive already empty for the full interval can accept media immediately.");
            }
            finally { File.Delete(path); }
        }

        private static void TestRestore()
        {
            string oldPath = NewDisk(), newPath = NewDisk(), save = newPath + ".amigasav";
            try
            {
                using var system = new AmigaSystem(AmigaModel.A500Pal, AmigaAccuracyOptions.Fast.Clone());
                var controller = system.Bus.FloppyController;
                controller.InsertDrive(0, AdfImage.Load(oldPath), true);
                controller.InsertDrive(0, AdfImage.Load(newPath), true);
                int elapsed = FloppyDrive.MediaChangeChipCycles / 3;
                Advance(controller.Drive0, elapsed);
                SaveStateFile.Write(save, system, MountedMediaInfo.ForFloppy(0, Path.GetFileName(newPath), newPath, true),
                    AmigaKickstart.Kickstart13, AmigaRamProfile.Chip512K, AmigaAccuracyProfile.Fast,
                    system.PresentedFrameBuffer.Pixels, system.PresentedFrameBuffer.Width, system.PresentedFrameBuffer.Height);
                Advance(controller.Drive0, FloppyDrive.MediaChangeChipCycles);
                SaveStateFile.Load(save, system);
                var restored = system.Bus.FloppyController.Drive0;
                Check.True(!restored.HasDisk && restored.Image != null, "A file restore must retain the selected but not yet inserted disk.");
                Advance(restored, FloppyDrive.MediaChangeChipCycles - elapsed - 1);
                Check.True(!restored.HasDisk, "RestoreMedia must not skip the saved remaining interval.");
                restored.Tick();
                Check.True(restored.HasDisk, "Restored insertion must finish on the original emulated cycle.");
                Check.Equal(newPath, restored.Image.Path, "Restore must retain the incoming disk.");
            }
            finally { File.Delete(oldPath); File.Delete(newPath); File.Delete(save); }
        }

        private static void TestLegacy()
        {
            string path = NewDisk();
            try
            {
                var source = new FloppyDrive(); source.Insert(AdfImage.Load(path));
                using var bytes = new MemoryStream();
                using (var writer = new BinaryWriter(bytes, Encoding.UTF8, true))
                    StateSerializer.WriteObjectFields(writer, source, "_mediaChangeChipCycles", "_driveAttached");
                bytes.Position = 0;
                var restored = new FloppyDrive();
                using (var reader = new BinaryReader(bytes, Encoding.UTF8, true)) StateSerializer.ReadObjectFields(reader, restored);
                Check.True(restored.HasDisk, "A legacy mounted disk must not acquire a replacement delay.");
                restored.Eject();
                Check.True(restored.HasPhysicalDrive, "Ejecting a legacy disk must preserve the inferred physical drive attachment.");
            }
            finally { File.Delete(path); }
        }
    }
}
