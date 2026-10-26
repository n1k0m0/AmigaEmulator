// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
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
            yield return Test("Zorro III 8/16/32 MB native Autoconfig, 32-bit RAM, reset and restore", TestZorroIII);
            yield return Test("Zorro III slot chain isolates nibble reads, assignment, shut-up and RAM", TestZorroIIISlots);
            yield return Test("Zorro III multi-card restore preserves partial configuration and guards layout", TestZorroIIISlotState);
            yield return Test("A4000 IDE uses DD2020, automatic interrupt acknowledgement and distinct board state", TestA4000Wiring);
            yield return Test("A4000 bus transfers preserve multi-sector writes, partial restore and read-only errors", TestA4000Transfer);
        }
        private static ChipTestCase Test(string name, Action run) => new ChipTestCase("Storage/Expansion", name,
            "ATA PIO; A600 Gayle; Amiga RDB/Autoconfig hardware interfaces", run);

        private static void TestZorroIII()
        {
            bool rejected = false;
            try { AmigaModel.Create(AmigaMachineModel.A500, AmigaRamProfile.Chip512K).WithDevelopmentZorroIIIRam(16 * 1024 * 1024); }
            catch (InvalidOperationException) { rejected = true; }
            Check.True(rejected, "A 24-bit A500 cannot host a Zorro III board.");

            foreach (int size in new[] { 8 * 1024 * 1024, 16 * 1024 * 1024, 32 * 1024 * 1024 })
            {
                var model = AmigaModel.Create(AmigaMachineModel.A3000, AmigaRamProfile.Chip2MbFast4Mb)
                    .WithDevelopmentZorroIIIRam(size);
                Check.Equal(size, model.WithDevelopmentProcessor(AmigaCpuModel.M68030, 25000000).ZorroIIIRamBytes,
                    "Development CPU clone retains the optional expansion board.");
                using var system = new AmigaSystem(model);
                var bus = system.Bus;
                Check.Equal(AmigaAddressRegion.ZorroIIIConfig, bus.GetRegion(0xFF000000), "Zorro III native config aperture.");
                Check.Equal((byte)0xA0, bus.ReadByte(0xFF000000), "Zorro III type and size upper nibble.");
                Check.Equal((byte)(size == 32 * 1024 * 1024 ? 0x10 : 0x00), bus.ReadByte(0xFF000100), "Size low nibble.");
                Check.Equal((byte)(size == 8 * 1024 * 1024 ? 0x6F : 0x4F), bus.ReadByte(0xFF000008), "Memory flags are complemented.");
                Check.Equal((byte)(size == 8 * 1024 * 1024 ? 0x5F : 0xFF), bus.ReadByte(0xFF000108),
                    "8 MiB declares its explicit sub-size; larger boards link their full physical size.");
                uint baseAddress = size == 32 * 1024 * 1024 ? 0x12000000u : 0x10000000u;
                bus.WriteByte(0xFF000048, (byte)(baseAddress >> 16));
                bus.WriteByte(0xFF000044, (byte)(baseAddress >> 24));
                Check.True(bus.ZorroIII.Configured, "High-byte write completes Zorro III configuration.");
                Check.Equal(AmigaAddressRegion.Unmapped, bus.GetRegion(0xFF000000), "Configured card releases config space.");
                Check.Equal(AmigaAddressRegion.ZorroIIIRam, bus.GetRegion(baseAddress), "32-bit board base is mapped.");
                Check.Equal(AmigaAddressRegion.ZorroIIIRam, bus.GetRegion(baseAddress + (uint)size - 1), "Last RAM byte is mapped.");
                Check.Equal(AmigaAddressRegion.Unmapped, bus.GetRegion(baseAddress + (uint)size), "No RAM alias beyond board.");
                bus.WriteLong(baseAddress + 0x100u, 0x12345678u);
                Check.Equal(0x12345678u, bus.ReadLong(baseAddress + 0x100u), "32-bit expansion RAM transfer.");
                Check.True(bus.IsInstructionCacheable(baseAddress + 0x100u) && bus.IsDataCacheable(baseAddress + 0x100u), "Zorro III RAM is cacheable.");
                using (var state = new MemoryStream())
                {
                    system.SaveState(new BinaryWriter(state));
                    bus.WriteLong(baseAddress + 0x100u, 0);
                    bus.ResetExpansionDevices();
                    state.Position = 0; system.LoadState(new BinaryReader(state));
                    Check.Equal(baseAddress, bus.ZorroIII.BaseAddress, "Restore retains board assignment.");
                    Check.Equal(0x12345678u, bus.ReadLong(baseAddress + 0x100u), "Restore retains expansion RAM.");
                }
                bus.ResetExpansionDevices();
                Check.Equal(AmigaAddressRegion.ZorroIIIConfig, bus.GetRegion(0xFF000000), "Reset returns card to Autoconfig.");
                Check.Equal(AmigaAddressRegion.Unmapped, bus.GetRegion(baseAddress), "Reset unmaps prior base.");
                bus.WriteWord(0xFF000044, (ushort)(baseAddress >> 16));
                Check.Equal(baseAddress, bus.ZorroIII.BaseAddress, "Word assignment sets both high and low address bytes.");
                bus.ResetExpansionDevices();
                bus.WriteByte(0xFF00004C, 0);
                Check.True(bus.ZorroIII.Configured && bus.ZorroIII.BaseAddress == 0, "Shut-up hides the board until reset.");
            }
        }
        private static AmigaModel ZorroIIISlotModel(params int[] sizes) =>
            AmigaModel.Create(AmigaMachineModel.A3000, AmigaRamProfile.Chip2MbFast4Mb)
                .WithDevelopmentZorroIIIRamBoards(sizes);

        private static void TestZorroIIISlots()
        {
            const int mib = 1024 * 1024;
            int[] sizes = { 8 * mib, 16 * mib, 32 * mib, 8 * mib };
            var model = ZorroIIISlotModel(sizes); sizes[0] = 32 * mib;
            Check.Equal(8 * mib, model.ZorroIIIRamBoardBytes[0], "Caller cannot mutate the attached slot layout.");
            Check.Equal(4, model.WithDevelopmentProcessor(AmigaCpuModel.M68030, 25000000)
                .WithDevelopmentChipset(AmigaChipset.Ecs).WithDevelopmentIde(AmigaIdeModel.Gayle)
                .WithDevelopmentDesktopResources().ZorroIIIRamBoardBytes.Count, "Development clones retain every slot.");
            using var system = new AmigaSystem(model); var bus = system.Bus;
            var cards = bus.ZorroIIIExpansion.Cards;
            Check.Equal(4, cards.Count, "Four RAM boards are installed.");
            Check.Equal((byte)0x6F, bus.ReadByte(0xFF000008), "Only first 8 MiB board presents its ROM.");
            Check.Equal(0L, cards[1].ConfigReads, "Later slot cannot see config reads before CFGIN.");
            bus.WriteByte(0xFF000048, 0); bus.WriteByte(0xFF000044, 0x40);
            Check.Equal((byte)0x4F, bus.ReadByte(0xFF000008), "Assignment enables the second slot.");
            bus.WriteWord(0xFF000044, 0x4100);
            Check.Equal((byte)0x10, bus.ReadByte(0xFF000100), "Third 32 MiB size nibble now drives config.");
            bus.WriteByte(0xFF00004C, 0);
            Check.True(cards[2].Configured && cards[2].BaseAddress == 0, "Shut-up passes the chain without mapping RAM.");
            bus.WriteWord(0xFF000044, 0x4280);
            Check.Equal(AmigaAddressRegion.Unmapped, bus.GetRegion(0xFF000000), "Last assignment releases Autoconfig.");
            uint[] bases = { 0x40000000, 0x41000000, 0x42800000 };
            for (int i = 0; i < bases.Length; i++) bus.WriteLong(bases[i] + 0x104, (uint)(0x12340000 + i));
            for (int i = 0; i < bases.Length; i++)
            {
                Check.Equal((uint)(0x12340000 + i), bus.ReadLong(bases[i] + 0x104), "Cards do not alias one another.");
                Check.True(bus.IsDataCacheable(bases[i] + 0x104), "Each assigned RAM range is cacheable.");
            }
            bus.ResetExpansionDevices();
            Check.Equal((byte)0x6F, bus.ReadByte(0xFF000008), "Reset starts again at the first slot.");
            Check.Equal(AmigaAddressRegion.Unmapped, bus.GetRegion(bases[1]), "Reset unmaps later cards too.");
            bus.WriteWord(0xFF000044, 0x4000); bus.WriteWord(0xFF000044, 0x4100);
            Check.Equal(0x12340001u, bus.ReadLong(0x41000104), "Warm reset retains each card's RAM.");
            bus.ZorroIIIExpansion.ClearRam();
            Check.Equal(0u, bus.ReadLong(0x41000104), "Power-loss clearing reaches later cards.");
            foreach (int[] invalid in new[] { Array.Empty<int>(), new[] { 8 * mib, 4 * mib }, new[] { 8 * mib, 8 * mib, 8 * mib, 8 * mib, 8 * mib } })
            {
                bool rejected = false; try { ZorroIIISlotModel(invalid); } catch (ArgumentException) { rejected = true; }
                Check.True(rejected, "Unsupported slot layouts are rejected.");
            }
        }

        private static void TestZorroIIISlotState()
        {
            const int mib = 1024 * 1024;
            using var system = new AmigaSystem(ZorroIIISlotModel(16 * mib, 8 * mib)); var bus = system.Bus;
            bus.WriteWord(0xFF000044, 0x4000); bus.WriteLong(0x40000100, 0x12345678);
            bus.WriteByte(0xFF000048, 0x80); // Second board is waiting for the high address byte.
            using var state = new MemoryStream(); system.SaveState(new BinaryWriter(state));
            Check.Equal(20, BitConverter.ToInt32(state.ToArray(), 0), "Only multi-card states use the new layout version.");
            bus.WriteByte(0xFF000044, 0x41); bus.WriteLong(0x41800100, 0x99);
            state.Position = 0; system.LoadState(new BinaryReader(state));
            Check.True(bus.ZorroIII.Configured && !bus.ZorroIIIExpansion.Cards[1].Configured, "Restore resumes the config chain.");
            Check.Equal(0x12345678u, bus.ReadLong(0x40000100), "Earlier card's contents restore independently.");
            bus.WriteByte(0xFF000044, 0x42);
            Check.Equal(0x42800000u, bus.ZorroIIIExpansion.Cards[1].BaseAddress, "Partial assignment latch survives restore.");
            using var mismatch = new AmigaSystem(ZorroIIISlotModel(8 * mib, 16 * mib));
            mismatch.Bus.WriteLong(0x200, 0xFACECAFE); state.Position = 0; bool rejected = false;
            try { mismatch.LoadState(new BinaryReader(state)); } catch (InvalidDataException) { rejected = true; }
            Check.True(rejected, "Equal total RAM with different slot sizes is rejected.");
            Check.Equal(0xFACECAFEu, mismatch.Bus.ReadLong(0x200), "Layout rejection precedes changes to live machine RAM.");
            bus.WriteLong(0x42800100, 0xABCD1234);
            using var expansion = new MemoryStream(); bus.ZorroIIIExpansion.SaveState(new BinaryWriter(expansion));
            byte[] truncated = expansion.ToArray(); Array.Resize(ref truncated, truncated.Length - 1);
            rejected = false;
            try { bus.ZorroIIIExpansion.LoadState(new BinaryReader(new MemoryStream(truncated))); } catch (EndOfStreamException) { rejected = true; }
            Check.True(rejected, "Truncated later-card data is rejected.");
            Check.Equal(0xABCD1234u, bus.ReadLong(0x42800100), "Rejected expansion restore preserves all current cards.");
        }

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
