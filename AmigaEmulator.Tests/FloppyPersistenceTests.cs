using System;
using System.Collections.Generic;
using System.IO;
using AmigaEmulator.Core;
using AmigaEmulator.Core.Media;
using AmigaEmulator.Core.SaveStates;

namespace AmigaEmulator.Tests
{
    internal static class FloppyPersistenceTests
    {
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return new ChipTestCase("Storage", "floppy replacement persists the outgoing disk", "Writable ADF lifecycle", TestReplacement);
            yield return new ChipTestCase("Storage", "system disposal persists all four floppy drives", "Writable ADF lifecycle", TestDisposal);
            yield return new ChipTestCase("SaveState", "floppy file restore retains dirty data and a partial write", "Writable ADF savestate", TestFileRestore);
        }

        private static string NewDisk()
        {
            string path = Path.Combine(Path.GetTempPath(), "amiga-floppy-persistence-" + Guid.NewGuid().ToString("N") + ".adf");
            File.WriteAllBytes(path, new byte[AdfImage.StandardSize]);
            return path;
        }

        private static byte[] Payload(byte value)
        {
            var bytes = new byte[AdfImage.SectorSize];
            Array.Fill(bytes, value);
            return bytes;
        }

        private static void CheckSector(string path, int sector, byte expected)
        {
            byte[] data = File.ReadAllBytes(path);
            for (int i = 0; i < AdfImage.SectorSize; i++)
                Check.Equal(expected, data[sector * AdfImage.SectorSize + i], "Persisted sector byte " + i);
        }

        private static void TestReplacement()
        {
            string oldPath = NewDisk(), newPath = NewDisk();
            try
            {
                var controller = new FloppyController();
                controller.InsertDrive(2, AdfImage.Load(oldPath), false);
                controller.Drive2.Image.WriteSector(0, 0, 3, Payload(0x5A), 0);
                controller.InsertDrive(2, AdfImage.Load(newPath), true);
                CheckSector(oldPath, 3, 0x5A);
                CheckSector(newPath, 3, 0);
                Check.True(!controller.Drive2.HasDisk, "Persistence must precede the physical insertion interval.");
                for (int cycle = 0; cycle < FloppyDrive.MediaChangeChipCycles; cycle++) controller.Drive2.Tick();
                Check.True(controller.Drive2.WriteProtected, "Replacement must keep its requested protection after insertion.");
            }
            finally { File.Delete(oldPath); File.Delete(newPath); }
        }

        private static void TestDisposal()
        {
            var paths = new string[4];
            try
            {
                using (var system = new AmigaSystem(AmigaModel.A500Pal, AmigaAccuracyOptions.Compatible.Clone()))
                    for (int d = 0; d < paths.Length; d++)
                    {
                        paths[d] = NewDisk();
                        system.Bus.FloppyController.InsertDrive(d, AdfImage.Load(paths[d]), false);
                        system.Bus.FloppyController.GetDrive(d).Image.WriteSector(0, 0, d, Payload((byte)(0x30 + d)), 0);
                    }
                for (int d = 0; d < paths.Length; d++) CheckSector(paths[d], d, (byte)(0x30 + d));
            }
            finally { foreach (string path in paths) if (path != null) File.Delete(path); }
        }

        private static void TestFileRestore()
        {
            string path = NewDisk(), save = path + ".amigasav";
            try
            {
                using var system = new AmigaSystem(AmigaModel.A500Pal, AmigaAccuracyOptions.Compatible.Clone());
                var drive = system.Bus.FloppyController.Drive1;
                system.Bus.FloppyController.InsertDrive(1, AdfImage.Load(path), false);
                // Select DF1, side 0, motor on; keep CIA and drive mechanics consistent for restoration.
                system.Bus.WriteByte(0xBFD300, 0xFF);
                system.Bus.WriteByte(0xBFD100, 0x6F);
                system.RunChipCycles(18 * 312 * 227 + 1);
                Check.True(drive.Ready, "DF1 must be ready before the partial write.");
                drive.Image.WriteSector(0, 0, 2, Payload(0x52), 0);
                ushort[] words = AdfMfmEncoder.EncodeSectorWords(Payload(0x73), 0, 3, 8);
                for (int i = 0; i < 300; i++) Check.True(drive.TryCaptureWrittenMfmWord(words[i]), "Partial write must be accepted.");
                SaveStateFile.Write(save, system, MountedMediaInfo.ForFloppy(1, Path.GetFileName(path), path, false),
                    AmigaKickstart.Kickstart13, AmigaRamProfile.Chip512K, AmigaAccuracyProfile.Compatible,
                    system.PresentedFrameBuffer.Pixels, system.PresentedFrameBuffer.Width, system.PresentedFrameBuffer.Height);
                drive.Image.WriteSector(0, 0, 2, Payload(0x99), 0);
                SaveStateFile.Load(save, system);
                drive = system.Bus.FloppyController.Drive1;
                Check.Equal(300, drive.CapturedWriteWordCount, "File restore must retain the in-flight MFM sector.");
                Check.True(drive.Image.IsDirty, "File restore must retain pending writes.");
                Check.True(!drive.WriteProtected, "File restore must retain writable media.");
                for (int i = 300; i < words.Length; i++) Check.True(drive.TryCaptureWrittenMfmWord(words[i]), "Restored write must continue.");
                Check.Equal(1, drive.DecodedWriteSectorCount, "Restored sector must finish decoding exactly once.");
                drive.FlushImage();
                CheckSector(path, 2, 0x52);
                CheckSector(path, 3, 0x73);
            }
            finally { File.Delete(save); File.Delete(path); }
        }
    }
}
