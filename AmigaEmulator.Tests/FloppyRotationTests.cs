// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AmigaEmulator.Core.Media;
using AmigaEmulator.Core.SaveStates;
using AmigaEmulator.Core.CustomChips;

namespace AmigaEmulator.Tests
{
    internal static class FloppyRotationTests
    {
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return new ChipTestCase("Paula/Disk", "floppy INDEX retains the same physical MFM origin across revolutions", "Commodore HRM appendix H: one INDEX pulse per physical revolution", TestIndexOrigin);
            yield return new ChipTestCase("SaveState", "floppy INDEX phase survives head changes and restore", "Disk angular position belongs to the rotating medium", TestIndexRestore);
            yield return new ChipTestCase("Paula/Disk", "INDEX-gated DMA retains all eleven ordered ADF sectors", "INDEX and MFM flux must represent the same physical revolution", TestIndexDma);
        }

        private static void Revolution(FloppyDrive drive)
        {
            int ticks = 0;
            do { drive.Tick(); ticks++; } while (!drive.IndexPulsePending && ticks <= 709379);
            Check.True(drive.IndexPulsePending, "A selected DD drive must produce a physical INDEX pulse.");
            Check.Equal(0, drive.StreamWordPosition, "INDEX must retain the synthesized track's gap origin, not drift through its sectors.");
            Check.Equal((ushort)0xAAAA, drive.ReadNextMfmWord(), "The synthesized track starts at its gap.");
        }

        private static void TestIndexOrigin()
        {
            string path = Path.Combine(Path.GetTempPath(), "amiga-index-" + Guid.NewGuid().ToString("N") + ".adf");
            File.WriteAllBytes(path, new byte[AdfImage.StandardSize]);
            try
            {
                var drive = new FloppyDrive(); drive.Insert(AdfImage.Load(path)); drive.ApplySelection(true, true);
                for (int revolution = 0; revolution < 4; revolution++) Revolution(drive);
                // The encoded sectors must not wrap early into the next revolution.
                for (int i = 0; i < 700000; i++) drive.Tick();
                Check.Equal((ushort)0xAAAA, drive.ReadNextMfmWord(), "After the encoded track ends, the remaining rotation must be gap flux.");
                Check.True(drive.StreamWordPosition >= drive.StreamWordCount, "Encoded sectors must wait for physical INDEX before wrapping.");
                Revolution(drive);
            }
            finally { File.Delete(path); }
        }

        private static void TestIndexRestore()
        {
            string path = Path.Combine(Path.GetTempPath(), "amiga-index-save-" + Guid.NewGuid().ToString("N") + ".adf");
            File.WriteAllBytes(path, new byte[AdfImage.StandardSize]);
            try
            {
                var drive = new FloppyDrive(); drive.Insert(AdfImage.Load(path)); drive.ApplySelection(true, true);
                for (int i = 0; i < 705001; i++) drive.Tick();
                drive.Step(19); drive.SetSide(1);
                using var stream = new MemoryStream();
                using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
                StateSerializer.WriteObjectFields(writer, drive); writer.Flush(); stream.Position = 0;
                var restored = new FloppyDrive();
                using var reader = new BinaryReader(stream, Encoding.UTF8, true);
                StateSerializer.ReadObjectFields(reader, restored);
                for (int i = 0; i < 120000; i++)
                {
                    drive.Tick(); restored.Tick();
                    Check.Equal(drive.IndexPulsePending, restored.IndexPulsePending, "Restored INDEX edge changed.");
                    Check.Equal(drive.StreamWordPosition, restored.StreamWordPosition, "Restored physical MFM position changed.");
                    Check.Equal(drive.ReadNextMfmWord(), restored.ReadNextMfmWord(), "Restored flux changed.");
                    if (drive.IndexPulsePending) Check.Equal(0, drive.StreamWordPosition, "A head change must not move INDEX through the track.");
                }
            }
            finally { File.Delete(path); }
        }

        private static void TestIndexDma()
        {
            string path = Path.Combine(Path.GetTempPath(), "amiga-index-dma-" + Guid.NewGuid().ToString("N") + ".adf");
            var bytes = new byte[AdfImage.StandardSize];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i * 37 + i / 512);
            File.WriteAllBytes(path, bytes);
            try
            {
                var controller = new FloppyController(); controller.InsertDrive0(AdfImage.Load(path));
                controller.Drive0.ApplySelection(true, true);
                for (int i = 0; i < 4 * 709379; i++) controller.Tick();
                var captured = new List<ushort>(); var paula = new Paula();
                paula.AttachDisk(controller, (_, word) => captured.Add(word), null, _ => { }, () => 0x0210, () => 0x0400);
                paula.NotifyCustomRegisterWordWrite(0x024, 0x9960);
                paula.NotifyCustomRegisterWordWrite(0x024, 0x9960);
                for (int i = 0; i < 800000 && paula.DiskDmaActive; i++) { controller.Tick(); paula.Tick(true, false); }
                Check.True(!paula.DiskDmaActive, "The INDEX-gated read must finish.");
                // WORDSYNC consumes the first sync; reconstruct only that known prefix.
                var words = new List<ushort> { 0xAAAA, 0xAAAA, 0x4489 }; words.AddRange(captured);
                for (int sector = 0; sector < 11; sector++)
                {
                    Check.True(AdfMfmEncoder.TryDecodeSectorWords(words, sector * 544, out var decoded), "Invalid INDEX-gated sector " + sector);
                    Check.Equal(sector, decoded.Sector, "An INDEX read skipped or reordered a sector.");
                    for (int i = 0; i < 512; i++) Check.Equal(bytes[sector * 512 + i], decoded.Data[i], "INDEX-gated DMA changed disk data.");
                }
            }
            finally { File.Delete(path); }
        }
    }
}
