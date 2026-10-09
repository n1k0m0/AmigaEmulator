using System;
using System.Collections.Generic;
using System.IO;
using AmigaEmulator.Core;
using AmigaEmulator.Core.Media;
using AmigaEmulator.Core.SaveStates;

namespace AmigaEmulator.Tests
{
    internal static class A3000Tests
    {
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return Test("profile restores CPU/MMU, RAM, SCSI and Ricoh clock through public states", Profile);
            yield return Test("WD33C93 probe, absent targets, INQUIRY and READ CAPACITY use real register ports", Probe);
            yield return Test("SCSI multi-sector PIO, partial restore and sense errors preserve disk contents", Pio);
            yield return Test("Super DMAC respects chip slots, ROM overlay and high Fast RAM in both directions", Dma);
            yield return Test("low-level SCSI command/status/message phases and transfer-count continuation work", Phases);
            yield return Test("CIA-A uses the independent PAL PSU clock and restores its fractional phase", Tod);
            yield return Test("WD address/data byte mirrors share selection, IRQ acknowledgement and planning state", RegisterMirrors);
            yield return Test("selection completion precedes the target phase request and survives restore", SelectionService);
            yield return Test("combined completion reports bus disconnect after acknowledgement unless EDI suppresses it", CompletionDisconnect);
            yield return Test("controller states retain legacy layouts and reject invalid pending interrupts", StateVersions);
        }
        private static ChipTestCase Test(string name, Action run) => new("Models/A3000", name,
            "Western Digital WD33C93A register specification; Commodore Super DMAC interfaces", run);
        private static AmigaSystem Board() => new(AmigaModel.Create(AmigaMachineModel.A3000, AmigaRamProfile.Chip2MbFast4Mb));
        private static void W(AmigaSystem system, int register, params byte[] values)
        { system.Bus.WriteByte(0xDD0041, (byte)register); foreach (byte value in values) system.Bus.WriteByte(0xDD0043, value); }
        private static byte R(AmigaSystem system, int register)
        { system.Bus.WriteByte(0xDD0041, (byte)register); return system.Bus.ReadByte(0xDD0043); }
        private static void Count(AmigaSystem system, int count) => W(system, 0x12, (byte)(count >> 16), (byte)(count >> 8), (byte)count);
        private static void Command(AmigaSystem system, byte[] cdb, int count)
        {
            R(system, 0x17); // Acknowledge any previous command before issuing another.
            W(system, 3, cdb); W(system, 0x0F, 0); W(system, 0x10, 0); Count(system, count);
            W(system, 0x15, 0); W(system, 0x18, 9);
        }
        private static byte[] BlockCommand(bool write, int block, int sectors) => new byte[]
            { write ? (byte)0x2A : (byte)0x28, 0, (byte)(block >> 24), (byte)(block >> 16), (byte)(block >> 8), (byte)block, 0, (byte)(sectors >> 8), (byte)sectors, 0 };
        private static void Profile()
        {
            foreach (var ram in new[] { AmigaRamProfile.Chip2Mb, AmigaRamProfile.Chip2MbFast2Mb, AmigaRamProfile.Chip2MbFast4Mb, AmigaRamProfile.Chip2MbFast8Mb })
            {
                var model = AmigaModel.Create(AmigaMachineModel.A3000, ram);
                using var system = new AmigaSystem(model);
                Check.Equal(AmigaChipset.Ecs, model.Chipset, "A3000 chipset.");
                Check.Equal(AmigaCpuModel.M68030, model.Cpu, "Full 68030 with MMU.");
                Check.Equal(25000000, model.CpuClockHz, "25 MHz CPU.");
                Check.True(system.Cpu.MemoryManagementUnit != null && system.Bus.RicohClock.Enabled && !model.HasPcmcia, "Board-specific resources.");
                Check.Equal(AmigaIdeModel.A3000Scsi, system.Bus.HardDisk.Model, "SCSI instead of IDE.");
                Check.True(system.Bus.HardDisk.Enabled && !system.Bus.HardDisk.Maps(0xDD2020) && !system.Bus.HardDisk.Maps(0xDA2020), "Only SCSI aperture enabled.");
                system.Cpu.MemoryManagementUnit.SetTransparent(0, 0xAAFF8100);
                if (model.FastRamBytes > 0) system.Bus.WriteLong(model.MotherboardFastRamStart, 0xCAFEBABE);
                string path = Path.Combine(Path.GetTempPath(), "a3000-state-" + Guid.NewGuid().ToString("N") + ".amigasav");
                try
                {
                    SaveStateFile.Write(path, system, MountedMediaInfo.None, AmigaKickstart.Custom, ram, AmigaAccuracyProfile.Compatible, Array.Empty<uint>(), 0, 0);
                    Check.Equal(AmigaMachineModel.A3000, SaveStateFile.ReadMetadata(path).MachineModel, "Public model metadata.");
                    using var restored = new AmigaSystem(model); SaveStateFile.Load(path, restored);
                    Check.Equal(0xAAFF8100u, restored.Cpu.MemoryManagementUnit.Transparent0, "MMU extension survives restore.");
                    if (model.FastRamBytes > 0) Check.Equal(0xCAFEBABEu, restored.Bus.ReadLong(model.MotherboardFastRamStart), "High RAM survives restore.");
                }
                finally { File.Delete(path); }
            }
        }
        private sealed class Disk : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "a3000-disk-" + Guid.NewGuid().ToString("N") + ".hdf");
            public Disk() { HardDiskImage.Create(Path, 16); }
            public void Dispose() => File.Delete(Path);
        }
        private static void Tod()
        {
            using var system = Board();
            system.CiaA.WriteByte(0xA00, 0); system.CiaA.WriteByte(0x900, 0); system.CiaA.WriteByte(0x800, 0);
            system.RunChipCycles(70935);
            Check.Equal((byte)0, system.CiaA.ReadByte(0x800), "No tick at the earlier VSYNC edge.");
            using var state = new MemoryStream(); system.SaveState(new BinaryWriter(state));
            system.RunChipCycles(5); Check.Equal((byte)1, system.CiaA.ReadByte(0x800), "Independent 50 Hz edge.");
            state.Position = 0; system.LoadState(new BinaryReader(state));
            system.RunChipCycles(5); Check.Equal((byte)1, system.CiaA.ReadByte(0x800), "Restored clock phase.");
        }
        private static void Probe()
        {
            using var disk = new Disk(); using var system = Board();
            system.Bus.WriteWord(0xDD000A, 4); W(system, 0, 8); W(system, 0x18, 0);
            Check.True(system.Bus.HardDisk.InterruptActive, "Reset interrupt routed through DMAC gate.");
            W(system, 0x18, 9);
            Check.True((system.Bus.ReadByte(0xDD0041) & 0x40) != 0, "A command issued before acknowledging the interrupt is ignored.");
            system.Bus.WriteByte(0xDD0041, 0x17);
            Check.Equal((byte)1, system.Bus.PeekByte(0xDD0043), "Enhanced reset status.");
            Check.True(system.Bus.HardDisk.InterruptActive, "Debugger peek cannot acknowledge interrupt.");
            Check.Equal((byte)1, R(system, 0x17), "Read acknowledges reset.");
            Check.True(!system.Bus.HardDisk.InterruptActive, "Acknowledged IRQ falls.");
            Check.Equal((byte)0xFF, R(system, 0x1A), "An undefined WD33C93A register returns all ones.");
            Check.Equal((byte)0xFF, R(system, 0xFF), "Undefined selector does not alias OWN ID.");
            W(system, 0x15, 0); W(system, 0x18, 9); Check.Equal((byte)0x42, R(system, 0x17), "Empty target selection times out.");
            system.Bus.HardDisk.Mount(disk.Path, true);
            Command(system, new byte[] { 0x12, 0, 0, 0, 36, 0 }, 36);
            Check.True((system.Bus.ReadByte(0xDD0041) & 1) != 0, "Data buffer ready.");
            W(system, 0x19); var inquiry = new byte[36];
            for (int i = 0; i < inquiry.Length; i++) inquiry[i] = system.Bus.ReadByte(0xDD0043);
            Check.Equal((byte)2, inquiry[2], "SCSI-2 disk."); Check.Equal((byte)31, inquiry[4], "Correct INQUIRY additional length.");
            Check.Equal((byte)0x16, R(system, 0x17), "Combined command completed.");
            Command(system, new byte[] { 0x25, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, 8);
            W(system, 0x19); uint capacity = 0, blockSize = 0;
            for (int i = 0; i < 4; i++) capacity = capacity << 8 | system.Bus.ReadByte(0xDD0043);
            for (int i = 0; i < 4; i++) blockSize = blockSize << 8 | system.Bus.ReadByte(0xDD0043);
            Check.Equal((uint)(system.Bus.HardDisk.Image.SectorCount - 1), capacity, "Last logical block.");
            Check.Equal(512u, blockSize, "Sector length.");
        }

        private static void RegisterMirrors()
        {
            using var system = Board();
            system.Bus.WriteWord(0xDD000A, 4);
            foreach (uint selector in new uint[] { 0xDD0041, 0xDD0045, 0xDD0049, 0xDD004D })
            foreach (uint data in new uint[] { 0xDD0043, 0xDD0047, 0xDD004B, 0xDD004F })
            {
                system.Bus.WriteByte(selector, 0); system.Bus.WriteByte(data, 8);
                system.Bus.WriteByte(selector, 0x18); system.Bus.WriteByte(data, 0);
                Check.True(system.Bus.HardDisk.InterruptActive, "A reset issued through any byte mirror requests the same interrupt.");
                Check.True((system.Bus.PeekByte(selector) & 0x80) != 0, "Every SASR read returns auxiliary status.");
                system.Bus.WriteByte(selector, 0x17);
                Check.Equal((byte)1, system.Bus.PeekByte(data), "All data mirrors expose the enhanced reset status.");
                Check.True(system.Bus.HardDisk.InterruptActive, "A peek must not acknowledge the interrupt.");
                Check.Equal((byte)1, system.Bus.ReadByte(data), "A mirrored data-port read acknowledges reset status.");
                Check.True(!system.Bus.HardDisk.InterruptActive, "The shared interrupt drops after acknowledgement.");
                system.Bus.WriteByte(selector, 2); system.Bus.WriteByte(data, 0xA5);
                system.Bus.WriteByte(0xDD0049, 2);
                system.Bus.BeginInstructionPlanning();
                Check.Equal((byte)0xA5, system.Bus.PeekByte(data), "Planning follows the mirrored data selector.");
                system.Bus.EndInstructionPlanning();
                Check.Equal((byte)0xA5, system.Bus.ReadByte(0xDD0043), "Planning does not retire the selector increment.");
            }
        }
        private static void SelectionService()
        {
            using var disk = new Disk(); using var system = Board(); system.Bus.HardDisk.Mount(disk.Path, true);
            foreach (byte command in new byte[] { 6, 7 })
            {
                system.Bus.HardDisk.Scsi.Reset(); system.Bus.WriteWord(0xDD000A, 4);
                W(system, 0x15, 0); W(system, 0x18, command);
                system.Bus.TickStorageDma(false);
                Check.Equal((byte)0x11, R(system, 0x17), "Selection status cannot be overwritten by the first target REQ.");
                using var state = new MemoryStream(); system.SaveState(new BinaryWriter(state));
                system.Bus.TickStorageDma(false);
                Check.Equal(command == 6 ? (byte)0x8E : (byte)0x8A, R(system, 0x17), "Target requests message out with ATN, command otherwise.");
                state.Position = 0; system.LoadState(new BinaryReader(state));
                system.Bus.TickStorageDma(false);
                Check.True(system.Bus.HardDisk.InterruptActive, "Pending REQ survives a save between the two interrupts.");
                Check.Equal(command == 6 ? (byte)0x8E : (byte)0x8A, R(system, 0x17), "Restored phase request.");
            }
        }
        private static void CompletionDisconnect()
        {
            using var disk = new Disk(); using var system = Board(); system.Bus.HardDisk.Mount(disk.Path, true);
            foreach (byte control in new byte[] { 0, 8 })
            {
                system.Bus.HardDisk.Scsi.Reset(); system.Bus.WriteWord(0xDD000A, 4); W(system, 1, control);
                Command(system, new byte[] { 0, 0, 0, 0, 0, 0 }, 0);
                system.Bus.TickStorageDma(false);
                Check.Equal((byte)0x16, R(system, 0x17), "Completion remains readable before disconnect.");
                system.Bus.TickStorageDma(false);
                Check.Equal(control == 0, system.Bus.HardDisk.InterruptActive, "EDI combines command completion and disconnect into one interrupt.");
                if (control == 0) Check.Equal((byte)0x85, R(system, 0x17), "Target bus disconnect after completion.");
            }
        }
        private static void StateVersions()
        {
            using var system = Board(); W(system, 2, 0xA5);
            using var state = new MemoryStream(); system.Bus.HardDisk.Scsi.SaveState(new BinaryWriter(state));
            byte[] current = state.ToArray();
            foreach (int version in new[] { 1, 2, 3 })
            {
                byte[] bytes = current.AsSpan(0, current.Length - (3 - version)).ToArray();
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes, version);
                W(system, 2, 0x5A);
                using var legacy = new MemoryStream(bytes); system.Bus.HardDisk.Scsi.LoadState(new BinaryReader(legacy));
                Check.Equal((byte)0xA5, R(system, 2), "All supported state layouts preserve WD registers.");
                Check.Equal(legacy.Length, legacy.Position, "The loader consumes exactly the version's controller state.");
            }
            current[^1] = 0xFF;
            bool rejected = false;
            try { using var invalid = new MemoryStream(current); system.Bus.HardDisk.Scsi.LoadState(new BinaryReader(invalid)); }
            catch (InvalidDataException) { rejected = true; }
            Check.True(rejected, "An unknown queued interrupt is rejected before resuming the machine.");
        }
        private static void Pio()
        {
            using var disk = new Disk(); using var system = Board(); system.Bus.HardDisk.Mount(disk.Path, false);
            Command(system, BlockCommand(true, 100, 2), 1024); W(system, 0x19);
            for (int i = 0; i < 123; i++) system.Bus.WriteByte(0xDD0043, (byte)(i ^ 0xA5));
            using var state = new MemoryStream(); system.SaveState(new BinaryWriter(state));
            system.Bus.WriteByte(0xDD0043, 0xFF); state.Position = 0; system.LoadState(new BinaryReader(state));
            for (int i = 123; i < 1024; i++) system.Bus.WriteByte(0xDD0043, (byte)(i ^ 0xA5));
            Check.Equal(2L, system.Bus.HardDisk.WrittenSectors, "Both sectors committed.");
            Command(system, BlockCommand(false, 100, 2), 1024); W(system, 0x19);
            for (int i = 0; i < 1024; i++) Check.Equal((byte)(i ^ 0xA5), system.Bus.ReadByte(0xDD0043), "PIO sector and byte ordering.");
            Check.Equal(2L, system.Bus.HardDisk.ReadSectors, "Both sectors read.");
            system.Bus.HardDisk.Mount(disk.Path, true); Command(system, BlockCommand(true, 100, 1), 512);
            Check.Equal((byte)0x45, R(system, 0x17), "CHECK CONDITION for protected disk.");
            Check.Equal((byte)2, R(system, 0x0F), "Target status is CHECK CONDITION.");
            Command(system, new byte[] { 3, 0, 0, 0, 18, 0 }, 18); W(system, 0x19);
            var sense = new byte[18]; for (int i = 0; i < 18; i++) sense[i] = system.Bus.ReadByte(0xDD0043);
            Check.Equal((byte)7, sense[2], "DATA PROTECT sense key."); Check.Equal((byte)0x27, sense[12], "WRITE PROTECTED additional sense.");
            Command(system, BlockCommand(false, int.MaxValue, 1), 512);
            Check.Equal((byte)0x45, R(system, 0x17), "Out-of-range LBA fails as a guest SCSI error.");
        }
        private static void Dma()
        {
            using var disk = new Disk(); using var system = Board(); system.Bus.HardDisk.Mount(disk.Path, false);
            uint fast = system.Model.MotherboardFastRamStart;
            for (uint i = 0; i < 1024; i++) system.Bus.WriteByte(fast + i, (byte)(i ^ 0x5A));
            system.Bus.WriteLong(0xDD000C, fast); system.Bus.WriteWord(0xDD000A, 6); system.Bus.WriteWord(0xDD0012, 0);
            Command(system, BlockCommand(true, 100, 2), 1024);
            for (int i = 0; i < 512; i++) Check.True(!system.Bus.TickStorageDma(false), "Fast RAM DMA does not consume shared slots.");
            Check.Equal(2L, system.Bus.HardDisk.WrittenSectors, "DMA write committed both sectors.");
            system.Bus.WriteLong(0xDD000C, 0x800); system.Bus.WriteWord(0xDD000A, 4);
            Command(system, BlockCommand(false, 100, 2), 1024);
            Check.True(!system.Bus.TickStorageDma(false), "Chip DMA waits for an available slot.");
            Check.Equal(0x800u, system.Bus.HardDisk.Scsi.DmaAddress, "No transfer without bus grant.");
            for (int i = 0; i < 64; i++) Check.True(system.Bus.TickStorageDma(true), "Chip DMA consumes its grant.");
            using var state = new MemoryStream(); system.SaveState(new BinaryWriter(state));
            system.Bus.TickStorageDma(true); state.Position = 0; system.LoadState(new BinaryReader(state));
            for (int i = 64; i < 512; i++) system.Bus.TickStorageDma(true);
            for (uint i = 0; i < 1024; i++) Check.Equal((byte)(i ^ 0x5A), system.Bus.ChipRam.Read(0x800 + i), "DMA bypasses CPU ROM overlay and restores partial transfer.");
            Check.Equal(0xC00u, system.Bus.HardDisk.Scsi.DmaAddress, "Final incremented address.");
        }
        private static void Phases()
        {
            using var disk = new Disk(); using var system = Board(); system.Bus.HardDisk.Mount(disk.Path, true);
            W(system, 0x15, 0); W(system, 0x18, 7); Check.Equal((byte)0x11, R(system, 0x17), "Selection completed.");
            system.Bus.TickStorageDma(false); Check.Equal((byte)0x8A, R(system, 0x17), "Target requests the command after selection.");
            Count(system, 6); W(system, 0x18, 0x20); W(system, 0x19, new byte[] { 0x12, 0, 0, 0, 36, 0 });
            Check.Equal((byte)0x19, R(system, 0x17), "Command phase transitions to data in.");
            Count(system, 36); W(system, 0x18, 0x20); W(system, 0x19);
            for (int i = 0; i < 36; i++) system.Bus.ReadByte(0xDD0043);
            Check.Equal((byte)0x1B, R(system, 0x17), "Status phase requested.");
            W(system, 0x18, 0xA0); Check.Equal((byte)0, R(system, 0x19), "GOOD status.");
            Check.Equal((byte)0x1F, R(system, 0x17), "Message in requested.");
            W(system, 0x18, 0xA0); Check.Equal((byte)0, R(system, 0x19), "COMMAND COMPLETE message.");
            Check.Equal((byte)0x20, R(system, 0x17), "Message in pauses with ACK asserted for host inspection.");
            W(system, 0x18, 3);
            Check.Equal((byte)0x85, R(system, 0x17), "Target disconnected.");
            Command(system, BlockCommand(false, 0, 1), 128); W(system, 0x19);
            for (int i = 0; i < 128; i++) system.Bus.ReadByte(0xDD0043);
            Check.Equal((byte)0x19, R(system, 0x17), "Count exhaustion pauses data in.");
            Count(system, 384); W(system, 0x18, 9); W(system, 0x19);
            for (int i = 0; i < 384; i++) system.Bus.ReadByte(0xDD0043);
            Check.Equal((byte)0x16, R(system, 0x17), "Continuation resumes remaining bytes.");
        }
    }
}
