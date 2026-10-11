// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.IO;
using AmigaEmulator.Core;
using AmigaEmulator.Core.Cpu;

namespace AmigaEmulator.Tests
{
    internal static class DataCacheTests
    {
        private static readonly AmigaCpuModel[] Models = { AmigaCpuModel.M68EC030, AmigaCpuModel.M68030 };
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return Test("aligned fills serve bytes/words without DMA snooping or instruction/data aliasing", TestReads);
            yield return Test("writethrough hits update partial data even frozen, while disabled entries are retained", TestWrites);
            yield return Test("write allocation replaces aligned longwords and invalidates only partial misses", TestAllocation);
            yield return Test("misaligned operands span independent cache longwords and preserve adjacent bytes", TestMisaligned);
            yield return Test("MOVES uses all FC tag bits and never caches CPU space", TestFunctionCodes);
            yield return Test("TAS and CAS read external memory despite a resident cache hit", TestLocked);
            yield return Test("MMU CI, logical remaps, write faults and resident failing translations remain distinct", TestMmu);
            yield return Test("device inhibition and EC030 ACU prevent extra reads and cache allocation", TestInhibit);
            yield return Test("data cache survives restore/reset and old CPU extensions start empty", TestRestore);
            yield return Test("cached writes retain their pending bus transfer across a suspended restore", TestDeferred);
            yield return Test("Amiga Chip RAM remains DMA coherent while motherboard Fast RAM retains cached data", TestMotherboard);
        }
        private static ChipTestCase Test(string name, Action action) => new("CPU/DataCache", name,
            "Motorola MC68030UM 6.1.2/6.3 and EC030 ACU; independent guest opcodes and bus observations", action);
        private static (M68kCpu cpu, Bus bus) Prepare(AmigaCpuModel model)
        {
            var bus = new Bus(); bus.WriteLong(0, 0x8000); bus.WriteLong(4, 0x4000); bus.WriteLong(8, 0x2000);
            var cpu = new M68kCpu(bus, model); cpu.Reset(); Control(cpu, bus, 2, 0x900); return (cpu, bus);
        }
        private static void Run(M68kCpu cpu, Bus bus, ushort opcode, ushort? extension = null)
        {
            bus.WriteWord(0x4000, opcode); bus.WriteWord(0x4002, extension ?? 0x4E71);
            cpu.SetProgramCounter(0x4000); cpu.Step();
        }
        private static void Control(M68kCpu cpu, Bus bus, int register, uint value)
        {
            cpu.SetD(7, value); Run(cpu, bus, 0x4E7B, (ushort)(0x7000 | register));
        }
        private static uint Read(M68kCpu cpu, Bus bus, uint address, int size = 4)
        {
            cpu.SetA(0, address); cpu.SetD(0, 0);
            Run(cpu, bus, size == 4 ? (ushort)0x2010 : size == 2 ? (ushort)0x3010 : (ushort)0x1010); return cpu.D0;
        }
        private static void Write(M68kCpu cpu, Bus bus, uint address, uint value, int size = 4)
        {
            cpu.SetA(0, address); cpu.SetD(1, value);
            Run(cpu, bus, size == 4 ? (ushort)0x2081 : size == 2 ? (ushort)0x3081 : (ushort)0x1081);
        }
        private static void TestReads()
        {
            foreach (var model in Models)
            {
                var (cpu, bus) = Prepare(model); bus.WriteLong(0x1000, 0x11223344);
                Check.Equal(0x22u, Read(cpu, bus, 0x1001, 1), "Byte miss fills an aligned longword.");
                Check.Equal(2, bus.DataReads, "16-bit bus performs two word reads for the single fill.");
                bus.WriteLong(0x1000, 0x55667788); int reads = bus.DataReads;
                Check.Equal(0x3344u, Read(cpu, bus, 0x1002, 2), "DMA changes do not invalidate cached words.");
                Check.Equal(0x11223344u, Read(cpu, bus, 0x1000), "Instruction fetches sharing the cache index do not alter data.");
                Check.Equal(reads, bus.DataReads, "Hits perform no external reads.");
                Control(cpu, bus, 0x802, 0x1000); Control(cpu, bus, 2, 0x500);
                Check.Equal(0x55667788u, Read(cpu, bus, 0x1000), "CED invalidates the selected data longword.");
            }
        }
        private static void TestWrites()
        {
            foreach (var model in Models)
            {
                var (cpu, bus) = Prepare(model); bus.WriteLong(0x1000, 0x11223344); Read(cpu, bus, 0x1000);
                Control(cpu, bus, 2, 0x2300); Write(cpu, bus, 0x1001, 0xAA, 1);
                Check.Equal(0x11AA3344u, bus.PeekLong(0x1000), "Write reaches external memory even frozen.");
                bus.WriteLong(0x1000, 0); Check.Equal(0x11AA3344u, Read(cpu, bus, 0x1000), "Frozen hits update cached bytes.");
                Write(cpu, bus, 0x1100, 0x55667788); Check.Equal(0x11AA3344u, Read(cpu, bus, 0x1000), "Frozen misses cannot evict or allocate.");
                Control(cpu, bus, 2, 0); Write(cpu, bus, 0x1000, 0x99AABBCC);
                Control(cpu, bus, 2, 0x100); Check.Equal(0x11AA3344u, Read(cpu, bus, 0x1000), "Disable preserves entries and bypasses updates.");
                Control(cpu, bus, 2, 0x900); Check.Equal(0x99AABBCCu, Read(cpu, bus, 0x1000), "CD exposes the changed memory.");
            }
        }
        private static void TestAllocation()
        {
            foreach (var model in Models)
            {
                var (cpu, bus) = Prepare(model); bus.WriteLong(0x1000, 1); bus.WriteLong(0x1004, 2);
                Read(cpu, bus, 0x1000); Read(cpu, bus, 0x1004); bus.WriteLong(0x1000, 3); bus.WriteLong(0x1004, 4);
                Write(cpu, bus, 0x1100, 5); Check.Equal(1u, Read(cpu, bus, 0x1000), "WA=0 misses do not replace a tag.");
                Control(cpu, bus, 2, 0x2100); Write(cpu, bus, 0x1101, 6, 1);
                Check.Equal(2u, Read(cpu, bus, 0x1004), "Partial tag miss preserves unselected sibling and old tag.");
                Check.Equal(3u, Read(cpu, bus, 0x1000), "Partial tag miss invalidates the selected longword.");
                Write(cpu, bus, 0x1100, 7); bus.WriteLong(0x1100, 8);
                Check.Equal(7u, Read(cpu, bus, 0x1100), "Aligned WA longword becomes valid without a read allocation.");
                Check.Equal(4u, Read(cpu, bus, 0x1004), "Aligned tag replacement invalidates the old siblings.");
            }
        }
        private static void TestMisaligned()
        {
            foreach (var model in Models)
            {
                var (cpu, bus) = Prepare(model); bus.WriteLong(0x1000, 0x11223344); bus.WriteLong(0x1004, 0x55667788);
                Check.Equal(0x44556677u, Read(cpu, bus, 0x1003), "Misaligned read assembles both entries in big-endian order.");
                Write(cpu, bus, 0x1003, 0xAABBCCDD);
                Check.Equal(0x112233AAu, Read(cpu, bus, 0x1000), "First partial hit updates only its last byte.");
                Check.Equal(0xBBCCDD88u, Read(cpu, bus, 0x1004), "Second partial hit updates only its first three bytes.");
                Check.Equal(0xAABBCCDDu, bus.PeekLong(0x1003), "Misaligned write is also visible on the bus.");
            }
        }
        private static void TestFunctionCodes()
        {
            foreach (var model in Models)
            {
                var (cpu, bus) = Prepare(model);
                foreach (uint fc in new uint[] { 1, 2, 5, 6, 7 })
                {
                    Control(cpu, bus, 0, fc); cpu.SetA(0, 0x1000); bus.WriteLong(0x1000, fc);
                    Run(cpu, bus, 0x0E90, 0); Check.Equal(fc, cpu.D0, "MOVES selects a distinct FC tag.");
                    bus.WriteLong(0x1000, 0xAA); Run(cpu, bus, 0x0E90, 0);
                    Check.Equal(fc == 7 ? 0xAAu : fc, cpu.D0, "CPU space never hits; all other FCs cache independently.");
                }
            }
        }
        private static void TestLocked()
        {
            foreach (var model in Models)
            {
                var (cpu, bus) = Prepare(model); bus.WriteLong(0x1000, 0x11223344); Read(cpu, bus, 0x1000);
                bus.WriteLong(0x1000, 0x04223344); cpu.SetA(0, 0x1000); Run(cpu, bus, 0x4AD0);
                Check.Equal(0x84u, (uint)bus.PeekByte(0x1000), "TAS observes the external semaphore value.");
                Check.Equal(0x84223344u, Read(cpu, bus, 0x1000), "RMW read and write update the cached entry.");
                bus.WriteLong(0x1000, 9); cpu.SetD(0, 9); cpu.SetD(1, 10); Run(cpu, bus, 0x0ED0, 0x40);
                Check.Equal(10u, bus.PeekLong(0x1000), "CAS compares fresh bus data, not its old cached value.");
            }
        }
        private static void Map(M68kCpu cpu, Bus bus)
        {
            cpu.MemoryManagementUnit.SetRoot(false, 0x7FFF000200009000UL);
            cpu.MemoryManagementUnit.SetTranslationControl(0x80C48800); bus.WriteLong(0x9000, 0xA002);
            for (uint page = 0; page < 16; page++) bus.WriteLong(0xA000 + page * 4, page * 0x1000 | 1);
            bus.WriteLong(0xA004, 0x6001);
        }
        private static void TestMmu()
        {
            var (cpu, bus) = Prepare(AmigaCpuModel.M68030); Map(cpu, bus);
            bus.WriteLong(0x6000, 1); Check.Equal(1u, Read(cpu, bus, 0x1000), "Read uses the logical mapping.");
            bus.WriteLong(0xA004, 0x7001); bus.WriteLong(0x7000, 2); cpu.MemoryManagementUnit.Flush();
            Check.Equal(1u, Read(cpu, bus, 0x1000), "ATC-only flush does not clear a logical cache hit.");
            bus.WriteLong(0xA004, 0x7005); cpu.MemoryManagementUnit.Flush(); Write(cpu, bus, 0x1000, 3);
            Check.Equal(0x2000u, cpu.PC, "Write protection enters the bus error handler.");
            Check.Equal(2u, Read(cpu, bus, 0x1000), "Denied write invalidates its matching cache entry.");
            Control(cpu, bus, 2, 0x900); bus.WriteLong(0xA004, 0x7041); cpu.MemoryManagementUnit.Flush();
            Check.Equal(2u, Read(cpu, bus, 0x1000), "CI permits an external read.");
            bus.WriteLong(0x7000, 4); Check.Equal(4u, Read(cpu, bus, 0x1000), "CI read was not allocated.");
            bus.WriteLong(0xA004, 0x7001); cpu.MemoryManagementUnit.Flush(); Read(cpu, bus, 0x1000);
            bus.WriteLong(0xA004, 0); cpu.MemoryManagementUnit.Load(0x1000, 5, false); Read(cpu, bus, 0x1000);
            Check.Equal(0x2000u, cpu.PC, "A resident failing ATC aborts even an existing data hit.");
            foreach (int inhibitedIndex in new[] { 0, 1 })
            {
                var (transparent, tb) = Prepare(AmigaCpuModel.M68030);
                transparent.MemoryManagementUnit.SetTransparent(0, inhibitedIndex == 0 ? 0x00008707u : 0x00008307u);
                transparent.MemoryManagementUnit.SetTransparent(1, inhibitedIndex == 1 ? 0x00008707u : 0x00008307u);
                tb.WriteLong(0x1000, 1); Check.Equal(1u, Read(transparent, tb, 0x1000), "Overlapping TT data access works.");
                tb.WriteLong(0x1000, 2); Check.Equal(2u, Read(transparent, tb, 0x1000), "Either overlapping CI window prevents data allocation.");
            }
        }
        private static void TestInhibit()
        {
            foreach (var model in Models)
            {
                var (cpu, bus) = Prepare(model); bus.Inhibit = true; bus.WriteLong(0x1000, 0x11223344);
                Check.Equal(0x22u, Read(cpu, bus, 0x1001, 1), "Inhibited byte reads retain their exact transfer size.");
                Check.Equal(1, bus.DataReads, "I/O bypass does not speculatively read adjacent data.");
                bus.WriteByte(0x1001, 0xAA); Check.Equal(0xAAu, Read(cpu, bus, 0x1001, 1), "I/O is not cached.");
                Control(cpu, bus, 2, 0x2100); Write(cpu, bus, 0x1000, 9); bus.WriteLong(0x1000, 10);
                Check.Equal(10u, Read(cpu, bus, 0x1000), "Device write allocation is inhibited.");
            }
            var (ec, b) = Prepare(AmigaCpuModel.M68EC030); b.WriteLong(0x8000, 0x00008607); ec.SetA(0, 0x8000);
            Run(ec, b, 0xF010, 0x0800); b.WriteLong(0x1000, 1); Read(ec, b, 0x1000); b.WriteLong(0x1000, 2);
            Check.Equal(2u, Read(ec, b, 0x1000), "Enabled AC0 CI prevents data fills.");
        }
        private static void TestRestore()
        {
            foreach (var model in Models)
            {
                var (cpu, bus) = Prepare(model); bus.WriteLong(0x1000, 1); Read(cpu, bus, 0x1000);
                using var save = new MemoryStream(); cpu.SaveState(new BinaryWriter(save)); bus.WriteLong(0x1000, 2);
                Control(cpu, bus, 2, 0x900); Read(cpu, bus, 0x1000); save.Position = 0; cpu.LoadState(new BinaryReader(save));
                Check.Equal(1u, Read(cpu, bus, 0x1000), "Restore retains the cached value independently of current RAM.");
                byte[] legacy = save.ToArray().AsSpan(0, (int)save.Length - 336).ToArray();
                int prefix = 8 + 24 + (model == AmigaCpuModel.M68EC030 ? 10 : 38);
                BitConverter.GetBytes(4).CopyTo(legacy, legacy.Length - 576 - prefix);
                using var old = new MemoryStream(legacy); cpu.LoadState(new BinaryReader(old));
                Check.Equal(2u, Read(cpu, bus, 0x1000), "Version-4 CPU states load an empty data cache.");
                cpu.Reset(); Control(cpu, bus, 2, 0x100); bus.WriteLong(0x1000, 3);
                Check.Equal(3u, Read(cpu, bus, 0x1000), "Reset invalidates data entries.");
            }
        }
        private static void TestDeferred()
        {
            foreach (var model in Models)
            {
                var (cpu, bus) = Prepare(model); Control(cpu, bus, 2, 0x2100);
                bus.WriteWord(0x4000, 0x2081); cpu.SetA(0, 0x1000); cpu.SetD(1, 0x11223344); cpu.SetProgramCounter(0x4000);
                cpu.Tick(false); Check.Equal(0u, bus.PeekLong(0x1000), "Planned write waits for a chip slot.");
                using var save = new MemoryStream(); cpu.SaveState(new BinaryWriter(save));
                var resumed = new M68kCpu(bus, model); save.Position = 0; resumed.LoadState(new BinaryReader(save));
                for (int i = 0; i < 20; i++) resumed.Tick(true);
                Check.Equal(0x11223344u, bus.PeekLong(0x1000), "Restore retires the pending external write.");
                bus.WriteLong(0x1000, 5); Check.Equal(0x11223344u, Read(resumed, bus, 0x1000), "Restored write allocation retains its data.");
            }
        }
        private static void TestMotherboard()
        {
            foreach (var machine in new[] { AmigaMachineModel.A3000, AmigaMachineModel.A4000Ec030 })
            {
                var model = AmigaModel.Create(machine, AmigaRamProfile.Chip2MbFast4Mb);
                using var system = new AmigaSystem(model);
                var cpu = system.Cpu; var bus = system.Bus; bus.RomOverlayAtZero = false;
                bus.WriteLong(0, 0x8000); bus.WriteLong(4, 0x4000); cpu.Reset();
                bus.WriteWord(0x4000, 0x4E7B); bus.WriteWord(0x4002, 0x7002); cpu.SetD(7, 0x2900); cpu.Step();
                uint ReadAt(uint address)
                {
                    bus.WriteWord(0x4000, 0x2010); cpu.SetA(0, address); cpu.SetProgramCounter(0x4000); cpu.Step(); return cpu.D0;
                }
                bus.WriteLong(0x1000, 1); Check.Equal(1u, ReadAt(0x1000), "Initial Chip RAM read.");
                bus.WriteLong(0x1000, 2); Check.Equal(2u, ReadAt(0x1000), "DMA-visible Chip RAM is never retained in the data cache.");
                uint fast = model.MotherboardFastRamStart + 0x1000;
                bus.WriteLong(fast, 3); Check.Equal(3u, ReadAt(fast), "Initial motherboard Fast RAM read.");
                bus.WriteLong(fast, 4); Check.Equal(3u, ReadAt(fast), "Fast RAM does not acquire automatic DMA/host write snooping.");
                Check.True(bus.IsInstructionCacheable(0x1000), "Chip instructions can still be cached.");
                Check.True(!bus.IsDataCacheable(0xDFF000), "Volatile custom registers remain inhibited.");
            }
        }
        private sealed class Bus : IM68kPeekableBus, IM68kCacheBus, IM68kBusArbitration
        {
            private readonly Dictionary<uint, byte> _memory = new();
            public bool Inhibit; public int DataReads;
            public byte ReadByte(uint a) { if (a >= 0x1000 && a < 0x1400) DataReads++; return PeekByte(a); }
            public ushort ReadWord(uint a) { if (a >= 0x1000 && a < 0x1400) DataReads++; return PeekWord(a); }
            public uint ReadLong(uint a) => ((uint)ReadWord(a) << 16) | ReadWord(a + 2);
            public byte PeekByte(uint a) => _memory.GetValueOrDefault(a);
            public ushort PeekWord(uint a) => (ushort)((PeekByte(a) << 8) | PeekByte(a + 1));
            public uint PeekLong(uint a) => ((uint)PeekWord(a) << 16) | PeekWord(a + 2);
            public void WriteByte(uint a, byte v) => _memory[a] = v;
            public void WriteWord(uint a, ushort v) { WriteByte(a, (byte)(v >> 8)); WriteByte(a + 1, (byte)v); }
            public void WriteLong(uint a, uint v) { WriteWord(a, (ushort)(v >> 16)); WriteWord(a + 2, (ushort)v); }
            public bool IsInstructionCacheable(uint a) => !Inhibit;
            public bool RequiresChipBus(uint a, int size) => true;
        }
    }
}
