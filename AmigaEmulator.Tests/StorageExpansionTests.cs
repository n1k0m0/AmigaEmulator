using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using AmigaEmulator.Core;
using AmigaEmulator.Core.Cpu;
using AmigaEmulator.Core.Media;
using AmigaEmulator.Core.Memory;

namespace AmigaEmulator.Tests
{
    internal static class StorageExpansionTests
    {
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return Test("new HDF has a valid bootable RDB partition and never overwrites", TestRdb);
            yield return Test("ATA identify exposes geometry and capacity in ATA byte order", TestIdentify);
            yield return Test("PIO CHS/LBA reads and writes persist across multi-sector transfers", TestTransfer);
            yield return Test("read-only, range errors, absent slave, interrupt mask and soft reset", TestErrorsAndIrq);
            yield return Test("deferred 68000 longword FIFO reads preserve distinct words", TestCpuFifo);
            yield return Test("savestate resumes partial ATA transfer and does not snapshot the host disk", TestState);
            yield return Test("Zorro II 2/4/8 MB Autoconfig assignment registers non-aliased RAM", TestAutoConfig);
            yield return Test("A4000 IDE uses DD2020, automatic interrupt acknowledgement and distinct board state", TestA4000Wiring);
            yield return Test("A4000 bus transfers preserve multi-sector writes, partial restore and read-only errors", TestA4000Transfer);
        }
        private static ChipTestCase Test(string name, Action run) => new ChipTestCase("Storage/Expansion", name,
            "ATA PIO; A600 Gayle; Amiga RDB/Autoconfig hardware interfaces", run);
        private static void TestA4000Transfer()
        {
            using var fixture = new Fixture();
            using var system = new AmigaSystem(AmigaModel.Create(AmigaMachineModel.A4000Ec030, AmigaRamProfile.Chip2MbFast4Mb));
            var bus = system.Bus; bus.HardDisk.Mount(fixture.Path, false);
            void Command(byte opcode)
            {
                bus.WriteByte(0xDD202Au, 2); // two sectors
                bus.WriteByte(0xDD202Eu, 100); bus.WriteByte(0xDD2032u, 0); bus.WriteByte(0xDD2036u, 0);
                bus.WriteByte(0xDD203Au, 0xE0); bus.WriteByte(0xDD203Eu, opcode);
            }
            Command(0x30);
            for (int i = 0; i < 123; i++) bus.WriteWord(0xDD2020, (ushort)(i ^ 0xA5A5));
            using (var state = new MemoryStream())
            {
                system.SaveState(new BinaryWriter(state));
                bus.WriteWord(0xDD2020, 0xDEAD); state.Position = 0; system.LoadState(new BinaryReader(state));
            }
            for (int i = 123; i < 512; i++) bus.WriteWord(0xDD2020, (ushort)(i ^ 0xA5A5));
            Check.Equal(2L, bus.HardDisk.WrittenSectors, "Both sectors committed through motherboard data port.");
            Command(0x20);
            for (int i = 0; i < 512; i++) Check.Equal((ushort)(i ^ 0xA5A5), bus.ReadWord(0xDD2020), "Big-endian FIFO word and sector ordering.");
            Check.Equal(2L, bus.HardDisk.ReadSectors, "Both sectors read through actual A4000 bus wiring.");
            bus.HardDisk.Mount(fixture.Path, true); Command(0x30);
            Check.True((bus.ReadByte(0xDD203E) & 1) != 0, "Read-only image reports ATA error.");
            Check.Equal((byte)4, bus.ReadByte(0xDD2026), "ATA command aborted without host writes.");
        }
        private sealed class Fixture : IDisposable
        {
            private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AmigaStorage-" + Guid.NewGuid().ToString("N"));
            public string Path { get; }
            public Fixture() { Directory.CreateDirectory(_directory); Path = System.IO.Path.Combine(_directory, "disk.hdf"); HardDiskImage.Create(Path, 16); }
            public void Dispose() => Directory.Delete(_directory, true);
        }

        private static void TestA4000Wiring()
        {
            using var fixture = new Fixture();
            var board = AmigaModel.Create(AmigaMachineModel.A600, AmigaRamProfile.Chip2MbFast4Mb)
                .WithDevelopmentProcessor(AmigaCpuModel.M68020, 14187580)
                .WithDevelopmentChipset(AmigaChipset.Aga).WithDevelopmentIde(AmigaIdeModel.A4000);
            using var system = new AmigaSystem(board); var bus = system.Bus; var ide = bus.HardDisk;
            ide.Mount(fixture.Path, true);
            Check.Equal(AmigaAddressRegion.Unmapped, bus.GetRegion(0xDA2020), "A4000 does not shadow the Gayle aperture.");
            Check.Equal(AmigaAddressRegion.Unmapped, bus.GetRegion(0xDE1000), "A4000 has no serial Gayle ID register.");
            Check.Equal(AmigaAddressRegion.GayleIde, bus.GetRegion(0xDD2020), "A4000 motherboard IDE aperture.");
            bus.WriteByte(0xDD203E, 0xEC);
            Check.True(ide.InterruptActive, "A4000 has no Gayle interrupt enable gate.");
            Check.Equal((byte)0x80, bus.PeekByte(0xDD3020), "A4000 interrupt status.");
            Check.Equal((byte)0x58, bus.ReadByte(0xDD303A), "Alternate status at base plus 101A.");
            Check.True(ide.InterruptActive, "Alternate status does not acknowledge.");
            Check.Equal((byte)0x58, bus.PeekByte(0xDD203E), "Peek retains status and IRQ.");
            bus.ReadByte(0xDD203E); Check.True(!ide.InterruptActive, "Task-file status acknowledges automatically.");
            Check.Equal((ushort)0x4000, bus.ReadWord(0xDD2020), "Word read consumes one IDENTIFY FIFO word.");
            using (var state = new MemoryStream())
            {
                system.SaveState(new BinaryWriter(state));
                ushort next = bus.ReadWord(0xDD2020);
                state.Position = 0; system.LoadState(new BinaryReader(state));
                Check.Equal(next, bus.ReadWord(0xDD2020), "Board wiring and pending FIFO retain their position.");
                using var wrong = new AmigaSystem(board.WithDevelopmentIde(AmigaIdeModel.Gayle));
                state.Position = 0; bool rejected = false;
                try { wrong.LoadState(new BinaryReader(state)); } catch (InvalidDataException) { rejected = true; }
                Check.True(rejected, "Different IDE wiring is rejected before loading RAM or opening disk images.");
            }
            for (int i = 2; i < 256; i++) bus.ReadWord(0xDD2020);
            bus.WriteByte(0xDD303A, 2); bus.WriteByte(0xDD203E, 0xEC);
            Check.True(!ide.InterruptActive, "ATA nIEN masks the A4000 interrupt.");
            bus.WriteByte(0xDD303A, 0); Check.True(ide.InterruptActive, "Unmask restores the pending interrupt.");
            bus.WriteByte(0xDD303A, 4); bus.WriteByte(0xDD303A, 0);
            Check.Equal((byte)0x50, bus.ReadByte(0xDD203E), "Soft reset restores ready status.");
            ide.Eject(); Check.Equal((byte)0xFF, bus.ReadByte(0xDD203E), "Empty drive cannot identify a phantom disk.");
        }
        private static uint Word(byte[] block, int index) => BinaryPrimitives.ReadUInt32BigEndian(block.AsSpan(index * 4, 4));
        private static void TestRdb()
        {
            using var fixture = new Fixture();
            using var image = new HardDiskImage(fixture.Path, true);
            var block = new byte[512]; image.ReadSector(0, block);
            Check.Equal(0x5244534Bu, Word(block, 0), "RDB ID");
            Check.Equal(1u, Word(block, 7), "PART pointer");
            Check.Equal((uint)image.Cylinders, Word(block, 16), "RDB geometry");
            uint checksum = 0; for (int i = 0; i < Word(block, 1); i++) checksum = unchecked(checksum + Word(block, i));
            Check.Equal(0u, checksum, "RDB checksum"); image.ReadSector(1, block);
            Check.Equal(0x50415254u, Word(block, 0), "PART ID"); Check.Equal(1u, Word(block, 5), "Boot flag");
            Check.Equal(0x444F5301u, Word(block, 48), "FFS DOS type");
            checksum = 0; for (int i = 0; i < Word(block, 1); i++) checksum = unchecked(checksum + Word(block, i));
            Check.Equal(0u, checksum, "PART checksum");
            bool refused = false; try { HardDiskImage.Create(fixture.Path, 16); } catch (IOException) { refused = true; }
            Check.True(refused, "Creation must refuse existing files");
        }
        private static void TestIdentify()
        {
            using var fixture = new Fixture(); using var ide = new GayleIdeController(); ide.Mount(fixture.Path, true);
            ide.WriteByte(0xDA201C, 0xEC);
            var bytes = new byte[512];
            for (int i = 0; i < 256; i++) BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(i * 2, 2), ide.ReadData());
            Check.Equal((ushort)16, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6, 2)), "Heads in identify");
            Check.Equal((ushort)63, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(12, 2)), "Sectors/track");
            Check.Equal((uint)ide.Image.SectorCount, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(120, 4)), "LBA capacity");
            Check.Equal((byte)0x50, ide.ReadByte(0xDA201C), "DRQ clears at end of identify");
        }
        private static void Address(GayleIdeController ide, long sector, int count, bool lba = true)
        {
            ide.WriteByte(0xDA2018, (byte)(lba ? 0xE0 | ((sector >> 24) & 15) : 0xA0 | ((sector / 63) % 16)));
            ide.WriteByte(0xDA2008, (byte)count);
            ide.WriteByte(0xDA200C, (byte)(lba ? sector : sector % 63 + 1));
            long cylinder = sector / (16 * 63);
            ide.WriteByte(0xDA2010, (byte)(lba ? sector >> 8 : cylinder));
            ide.WriteByte(0xDA2014, (byte)(lba ? sector >> 16 : cylinder >> 8));
        }
        private static void TestTransfer()
        {
            using var fixture = new Fixture();
            using (var ide = new GayleIdeController())
            {
                ide.Mount(fixture.Path, false); Address(ide, 1007, 2); ide.WriteByte(0xDA201C, 0x30);
                for (int i = 0; i < 512; i++) ide.WriteData((ushort)(0x1234 ^ i));
                ide.Flush(); Check.Equal(2L, ide.WrittenSectors, "Two write sectors");
                Address(ide, 1007, 2, false); ide.WriteByte(0xDA201C, 0x20);
                for (int i = 0; i < 512; i++) Check.Equal((ushort)(0x1234 ^ i), ide.ReadData(), "CHS read across track/head boundary");
                Address(ide, 2000, 0); ide.WriteByte(0xDA201C, 0x20);
                for (int i = 0; i < 256 * 256; i++) ide.ReadData();
                Check.Equal((byte)0x50, ide.ReadByte(0xDA201C), "Count zero transfers 256 sectors");
            }
            using var image = new HardDiskImage(fixture.Path, true); var block = new byte[512]; image.ReadSector(1008, block);
            Check.Equal((ushort)(0x1234 ^ 256), BinaryPrimitives.ReadUInt16BigEndian(block), "Host image persisted");
        }
        private static void TestErrorsAndIrq()
        {
            using var fixture = new Fixture(); using var ide = new GayleIdeController(); ide.Mount(fixture.Path, true);
            ide.WriteByte(0xDAA000, 0x80); ide.WriteByte(0xDA201C, 0xEC);
            Check.True(ide.InterruptActive, "Identify IRQ"); ide.ReadByte(0xDA3018);
            Check.True(ide.InterruptActive, "Alternate status preserves IRQ"); ide.ReadByte(0xDA201C);
            Check.True(!ide.InterruptActive, "Status acknowledges IRQ");
            ide.WriteByte(0xDA3018, 4); ide.WriteByte(0xDA3018, 0);
            Check.Equal((byte)0x80, ide.ReadByte(0xDAA000), "ATA reset preserves Gayle interrupt mask");
            Address(ide, 0, 1); ide.WriteByte(0xDA201C, 0x30);
            Check.True((ide.ReadByte(0xDA201C) & 1) != 0, "Write protect returns ATA error");
            Address(ide, ide.Image.SectorCount, 1); ide.WriteByte(0xDA201C, 0x20);
            Check.Equal((byte)0x10, ide.ReadByte(0xDA2004), "Out of bounds is IDNF");
            ide.WriteByte(0xDA2018, 0xB0); ide.WriteByte(0xDA201C, 0xEC);
            Check.Equal((byte)0, ide.ReadByte(0xDA201C), "No phantom slave");
        }
        private static void TestCpuFifo()
        {
            using var fixture = new Fixture(); using var system = new AmigaSystem(AmigaModel.A500Pal);
            system.Bus.RomOverlayAtZero = false;
            // MOVE.L (A0),D0 reads two successive words from aliased ATA data registers.
            system.Bus.WriteLong(0, 0x70000); system.Bus.WriteLong(4, 0x100); system.Bus.WriteWord(0x100, 0x2010); system.Bus.WriteWord(0x102, 0x4E71);
            system.Cpu.Reset(); system.Cpu.SetA(0, 0xDA2000); system.Bus.HardDisk.Mount(fixture.Path, true);
            Address(system.Bus.HardDisk, 0, 1); system.Bus.HardDisk.WriteByte(0xDA201C, 0x20);
            Check.Equal((ushort)0x5244, system.Bus.PeekWord(0xDA2000), "Debugger peek");
            Check.Equal((ushort)0x5244, system.Bus.PeekWord(0xDA2000), "Debugger peek is repeatable");
            system.Cpu.Tick(true); for (int i = 0; i < 30; i++) system.Cpu.Tick(true);
            Check.Equal(0x5244534Bu, system.Cpu.GetD(0), "Deferred long reads distinct FIFO words");
            Check.Equal((ushort)0, system.Bus.ReadWord(0xDA2000), "Exactly two words retired");
        }
        private static void TestState()
        {
            using var fixture = new Fixture(); using var ide = new GayleIdeController(); ide.Mount(fixture.Path, false);
            Address(ide, 100, 1); ide.WriteByte(0xDA201C, 0x30);
            for (int i = 0; i < 100; i++) ide.WriteData((ushort)i);
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
            ide.SaveState(writer); for (int i = 100; i < 256; i++) ide.WriteData(0xFFFF);
            stream.Position = 0; using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true); ide.LoadState(reader);
            for (int i = 100; i < 256; i++) ide.WriteData((ushort)i);
            Address(ide, 100, 1); ide.WriteByte(0xDA201C, 0x20);
            for (int i = 0; i < 256; i++) Check.Equal((ushort)i, ide.ReadData(), "Resumed partial sector");
        }
        private static void TestAutoConfig()
        {
            foreach (var profile in new[] { AmigaRamProfile.Chip2MbFast2Mb, AmigaRamProfile.Chip2MbFast4Mb, AmigaRamProfile.Chip2MbFast8Mb })
            {
                using var system = new AmigaSystem(AmigaModel.ForRamProfile(profile)); system.Bus.RomOverlayAtZero = false;
                byte type = (byte)((system.Bus.ReadByte(0xE80000) & 0xF0) | (system.Bus.ReadByte(0xE80002) >> 4));
                Check.True((type & 0xE0) == 0xE0, "Zorro II RAM board");
                Check.Equal((byte)0, system.Bus.ReadByte(0xE80040), "Uninverted board control register");
                byte manufacturerHigh = (byte)~((system.Bus.ReadByte(0xE80010) & 0xF0) | (system.Bus.ReadByte(0xE80012) >> 4));
                byte manufacturerLow = (byte)~((system.Bus.ReadByte(0xE80014) & 0xF0) | (system.Bus.ReadByte(0xE80016) >> 4));
                Check.Equal((ushort)2011, (ushort)((manufacturerHigh << 8) | manufacturerLow), "Valid manufacturer ID for expansion.library");
                system.Bus.WriteByte(0xE8004A, 0); system.Bus.WriteByte(0xE80048, 0x20);
                Check.True(system.Bus.AutoConfig.Configured, "Board assigned");
                Check.Equal((byte)0xFF, system.Bus.ReadByte(0xE80000), "No second phantom board");
                system.Bus.WriteLong(0x200000, 0x12345678);
                uint end = 0x200000u + (uint)system.Model.FastRamBytes;
                system.Bus.WriteLong(end - 4, 0x90ABCDEF);
                Check.Equal(0x12345678u, system.Bus.ReadLong(0x200000), "Fast RAM start");
                Check.Equal(0x90ABCDEFu, system.Bus.ReadLong(end - 4), "Fast RAM end");
                Check.Equal(0xFFFFFFFFu, system.Bus.ReadLong(end), "No wrapping beyond installed RAM");
                Check.True(!system.Bus.RequiresChipBus(0x200000, 4), "Expansion avoids chip DMA contention");
                system.Reset(true); Check.True(!system.Bus.AutoConfig.Configured, "Autoconfig returns after reset");
            }
        }
    }
}
