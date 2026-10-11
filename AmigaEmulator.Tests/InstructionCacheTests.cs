// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.IO;
using AmigaEmulator.Core;
using AmigaEmulator.Core.Cpu;

namespace AmigaEmulator.Tests
{
    internal static class InstructionCacheTests
    {
        private static readonly AmigaCpuModel[] Models = { AmigaCpuModel.M68EC020, AmigaCpuModel.M68020, AmigaCpuModel.M68EC030, AmigaCpuModel.M68030 };
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return Test("instruction cache retains code until CACR clear and performs aligned fills", TestCodeAndClear);
            yield return Test("freeze and disable preserve resident instructions without allocating misses", TestFreezeAndDisable);
            yield return Test("030 line replacement invalidates siblings while 020 tags are independent", TestGeometry);
            yield return Test("CAAR clears one longword and preserves adjacent instructions", TestEntryClear);
            yield return Test("instruction tags distinguish supervisor and user code", TestFunctionCode);
            yield return Test("instruction cache survives state restore, reset and legacy extension loading", TestRestore);
            yield return Test("030 MMU cache inhibit, logical remapping and resident ATC faults remain distinct", TestMmu);
            yield return Test("board cache inhibit prevents instruction fills in device apertures", TestBoardInhibit);
            yield return Test("EC030 AC0 and AC1 cache inhibition uses address, function code and read masks", TestAccessControl);
            yield return Test("suspended cache fills retire on chip slots and restore without duplicate fetches", TestDeferred);
        }
        private static ChipTestCase Test(string name, Action action) => new("CPU/InstructionCache", name,
            "Motorola MC68020UM section 4; MC68030UM sections 6 and 9; independent guest opcodes and bus observations", action);
        private static (M68kCpu cpu, Bus bus) Prepare(AmigaCpuModel model)
        {
            var bus = new Bus(); bus.WriteLong(0, 0x8000); bus.WriteLong(4, 0x1000); bus.WriteLong(8, 0x2000);
            var cpu = new M68kCpu(bus, model); cpu.Reset();
            Control(cpu, bus, 2, 9); return (cpu, bus);
        }
        private static void Control(M68kCpu cpu, Bus bus, int register, uint value)
        {
            // Fresh helper addresses use a cache index disjoint from tested
            // code. No hidden flush or CAAR write can alter the tested entries.
            uint address = bus.NextControlAddress; bus.NextControlAddress += 0x100;
            bus.WriteWord(address, 0x4E7B); bus.WriteWord(address + 2, (ushort)(0x7000 | register));
            cpu.SetD(7, value); cpu.SetProgramCounter(address); cpu.Step();
        }
        private static uint Execute(M68kCpu cpu, Bus bus, uint address, ushort opcode)
        {
            bus.WriteWord(address, opcode); bus.WriteWord(address + 2, 0x4E71);
            cpu.SetProgramCounter(address); cpu.Step(); return cpu.D0;
        }
        private static void TestCodeAndClear()
        {
            foreach (var model in Models)
            {
                var (cpu, bus) = Prepare(model);
                Check.Equal(1u, Execute(cpu, bus, 0x1000, 0x7001), "Initial MOVEQ fills code.");
                int reads = bus.CodeReads;
                Check.Equal(1u, Execute(cpu, bus, 0x1000, 0x7002), "Host/DMA writes do not snoop the instruction cache.");
                Check.Equal(reads, bus.CodeReads, "A hit performs no external instruction read.");
                Control(cpu, bus, 2, 9);
                Check.Equal(2u, Execute(cpu, bus, 0x1000, 0x7002), "CACR clear exposes the modified code.");
                bus.WriteWord(0x1002, 0x7003); cpu.SetProgramCounter(0x1002); cpu.Step();
                Check.Equal(2u, cpu.D0, "The cached low word was NOP: aligned fill stored both words.");
            }
        }
        private static void TestFreezeAndDisable()
        {
            foreach (var model in Models)
            {
                var (cpu, bus) = Prepare(model); Execute(cpu, bus, 0x1000, 0x7001);
                Control(cpu, bus, 2, 3);
                Check.Equal(1u, Execute(cpu, bus, 0x1000, 0x7002), "Frozen hits retain original code.");
                Check.Equal(3u, Execute(cpu, bus, 0x1100, 0x7003), "Frozen conflicting miss runs from memory.");
                Check.Equal(1u, Execute(cpu, bus, 0x1000, 0x7002), "Frozen miss does not evict the resident tag.");
                Control(cpu, bus, 2, 0);
                Check.Equal(2u, Execute(cpu, bus, 0x1000, 0x7002), "Disabled cache runs current memory.");
                Control(cpu, bus, 2, 1);
                Check.Equal(1u, Execute(cpu, bus, 0x1000, 0x7002), "Reenable retains entries; it is not clear-all.");
            }
        }
        private static void TestGeometry()
        {
            foreach (var model in Models)
            {
                var (cpu, bus) = Prepare(model); Execute(cpu, bus, 0x1000, 0x7001); Execute(cpu, bus, 0x1004, 0x7002);
                Execute(cpu, bus, 0x1100, 0x7003);
                bool newer = model == AmigaCpuModel.M68EC030 || model == AmigaCpuModel.M68030;
                Check.Equal(newer ? 4u : 2u, Execute(cpu, bus, 0x1004, 0x7004), "030 shared tag replacement clears all four valid bits; 020 only replaces one entry.");
            }
        }
        private static void TestEntryClear()
        {
            foreach (var model in Models)
            {
                var (cpu, bus) = Prepare(model); Execute(cpu, bus, 0x1000, 0x7001); Execute(cpu, bus, 0x1004, 0x7002);
                Control(cpu, bus, 0x802, 0xCAFE1000); Control(cpu, bus, 2, 5);
                Check.Equal(3u, Execute(cpu, bus, 0x1000, 0x7003), "CAAR selects entry using A7-A2.");
                Check.Equal(2u, Execute(cpu, bus, 0x1004, 0x7004), "CE preserves the adjacent entry.");
            }
        }
        private static void TestFunctionCode()
        {
            foreach (var model in Models)
            {
                var (cpu, bus) = Prepare(model); Execute(cpu, bus, 0x1000, 0x7001);
                bus.WriteWord(0x3020, 0x46FC); bus.WriteWord(0x3022, 0); cpu.SetProgramCounter(0x3020); cpu.Step();
                Check.Equal(2u, Execute(cpu, bus, 0x1000, 0x7002), "User fetch cannot hit supervisor-tagged instructions.");
            }
        }
        private static void TestRestore()
        {
            foreach (var model in Models)
            {
                var (cpu, bus) = Prepare(model); Execute(cpu, bus, 0x1000, 0x7001);
                using var save = new MemoryStream(); cpu.SaveState(new BinaryWriter(save));
                Control(cpu, bus, 2, 9); Execute(cpu, bus, 0x1000, 0x7002);
                save.Position = 0; cpu.LoadState(new BinaryReader(save));
                Check.Equal(1u, Execute(cpu, bus, 0x1000, 0x7002), "Restore preserves stale cached code independently from current RAM.");
                cpu.Reset(); Control(cpu, bus, 2, 1);
                Check.Equal(2u, Execute(cpu, bus, 0x1000, 0x7002), "Hardware reset invalidates all instruction entries.");
                // Independently assemble the old extension by retaining the
                // unchanged field prefix and removing the new cache payload.
                byte[] current = save.ToArray(); int extensionLength = 8 + 6 * 4;
                if (model == AmigaCpuModel.M68EC030) extensionLength += 10;
                if (model == AmigaCpuModel.M68030) extensionLength += 38; // Empty ATC.
                int legacyLength = current.Length - 64 * 9 - (model == AmigaCpuModel.M68EC030 || model == AmigaCpuModel.M68030 ? 336 : 0);
                byte[] legacy = current.AsSpan(0, legacyLength).ToArray();
                int extensionStart = legacyLength - extensionLength;
                BitConverter.GetBytes(model == AmigaCpuModel.M68030 ? 3 : model == AmigaCpuModel.M68EC030 ? 2 : 1).CopyTo(legacy, extensionStart);
                using var oldSave = new MemoryStream(legacy); cpu.LoadState(new BinaryReader(oldSave));
                Check.Equal(2u, Execute(cpu, bus, 0x1000, 0x7002), "Legacy save starts with an empty cache, never a previous machine's entries.");
            }
        }
        private static void Map(M68kCpu cpu, Bus bus, bool inhibit = false)
        {
            var mmu = cpu.MemoryManagementUnit; mmu.SetRoot(false, 0x7FFF000200004000UL);
            mmu.SetTranslationControl(0x80C48800); bus.WriteLong(0x4000, 0x5002);
            for (uint page = 0; page < 16; page++) bus.WriteLong(0x5000 + page * 4, page * 0x1000 | 1);
            bus.WriteLong(0x5004, 0x6001u | (inhibit ? 0x40u : 0));
        }
        private static void TestMmu()
        {
            var (cpu, bus) = Prepare(AmigaCpuModel.M68030); Map(cpu, bus, true);
            bus.WriteWord(0x6000, 0x7001); bus.WriteWord(0x6002, 0x4E71); cpu.SetProgramCounter(0x1000); cpu.Step();
            bus.WriteWord(0x6000, 0x7002); cpu.SetProgramCounter(0x1000); cpu.Step();
            Check.Equal(2u, cpu.D0, "Page CI inhibits fill.");
            Check.Equal((ushort)2, cpu.MemoryManagementUnit.Test(0x1000, 6, false, 7).Flags, "MMUSR does not expose internal CI storage.");
            Map(cpu, bus); cpu.SetProgramCounter(0x1000); cpu.Step();
            bus.WriteWord(0x7000, 0x7003); bus.WriteWord(0x7002, 0x4E71); bus.WriteLong(0x5004, 0x7001);
            cpu.MemoryManagementUnit.Flush(); cpu.SetProgramCounter(0x1000); cpu.Step();
            Check.Equal(2u, cpu.D0, "Logical cache survives table changes and ATC-only flush.");
            bus.WriteLong(0x5004, 0); cpu.MemoryManagementUnit.Load(0x1000, 6, false);
            cpu.MemoryManagementUnit.SetTransparent(0, 0x00008107, false);
            cpu.SetProgramCounter(0x1000); cpu.Step();
            Check.Equal(2u, cpu.D0, "A transparent window bypasses an old resident ATC fault.");
            cpu.MemoryManagementUnit.SetTransparent(0, 0, false);
            cpu.SetProgramCounter(0x1000); cpu.Step();
            Check.Equal(0x2000u, cpu.PC, "Resident failing ATC aborts even a valid instruction-cache hit.");
            var (transparent, tb) = Prepare(AmigaCpuModel.M68030);
            transparent.MemoryManagementUnit.SetTransparent(0, 0x00008707);
            Check.Equal(1u, Execute(transparent, tb, 0x1000, 0x7001), "TT instruction access works.");
            Check.Equal(2u, Execute(transparent, tb, 0x1000, 0x7002), "Transparent CI inhibits fill with translation disabled too.");
        }
        private static void TestAccessControl()
        {
            void AccessControl(M68kCpu cpu, Bus bus, int index, uint value)
            {
                uint helper = bus.NextControlAddress; bus.NextControlAddress += 0x100;
                bus.WriteLong(0x9000, value); cpu.SetA(0, 0x9000);
                bus.WriteWord(helper, 0xF010); bus.WriteWord(helper + 2, index == 0 ? (ushort)0x0800 : (ushort)0x0C00);
                cpu.SetProgramCounter(helper); cpu.Step();
            }
            foreach (int index in new[] { 0, 1 })
            {
                var (cpu, bus) = Prepare(AmigaCpuModel.M68EC030);
                AccessControl(cpu, bus, index, 0x00008607); // Block 00, any FC, reads, enabled/CI.
                Execute(cpu, bus, 0x1000, 0x7001);
                Check.Equal(2u, Execute(cpu, bus, 0x1000, 0x7002), "An inhibited block cannot retain newly fetched instructions.");
                AccessControl(cpu, bus, index, 0x01008607); // Different upper address byte.
                Execute(cpu, bus, 0x1000, 0x7003);
                Check.Equal(3u, Execute(cpu, bus, 0x1000, 0x7004), "A nonmatching address does not inhibit the cache.");
                Control(cpu, bus, 2, 9);
                AccessControl(cpu, bus, index, 0x00008620); // User program FC2 only.
                Execute(cpu, bus, 0x1000, 0x7005);
                Check.Equal(5u, Execute(cpu, bus, 0x1000, 0x7006), "An FC mismatch keeps supervisor instructions cacheable.");
                Control(cpu, bus, 2, 9);
                AccessControl(cpu, bus, index, 0x00008407); // Writes only.
                Execute(cpu, bus, 0x1000, 0x7007);
                Check.Equal(7u, Execute(cpu, bus, 0x1000, 0x7008), "A write-only block cannot inhibit instruction reads.");
            }
        }

        private static void TestBoardInhibit()
        {
            foreach (var model in Models)
            {
                var (cpu, bus) = Prepare(model); bus.Inhibit = true;
                Execute(cpu, bus, 0x1000, 0x7001);
                Check.Equal(2u, Execute(cpu, bus, 0x1000, 0x7002), "Device bus values are never retained as instructions.");
            }
        }
        private static void TestDeferred()
        {
            foreach (var model in Models)
            {
                var (cpu, bus) = Prepare(model); bus.WriteWord(0x1000, 0x7001); bus.WriteWord(0x1002, 0x60FC);
                cpu.SetProgramCounter(0x1000); cpu.TraceEnabled = false;
                for (int i = 0; i < 2; i++) cpu.Tick(false);
                using var state = new MemoryStream(); cpu.SaveState(new BinaryWriter(state));
                var restored = new M68kCpu(bus, model); state.Position = 0; restored.LoadState(new BinaryReader(state));
                for (int i = 0; i < 80; i++) { cpu.Tick(i % 2 == 0); restored.Tick(i % 2 == 0); }
                Check.Equal(cpu.PC, restored.PC, "Pending fill and cache-hit flow retain PC.");
                Check.Equal(cpu.Snapshot.Cycles, restored.Snapshot.Cycles, "Pending fill and cache-hit flow retain cycles.");
                Check.Equal(cpu.D0, restored.D0, "The loop still executes guest code.");
            }
        }
        private sealed class Bus : IM68kPeekableBus, IM68kBusArbitration, IM68kCacheBus
        {
            private readonly Dictionary<uint, byte> _memory = new();
            public bool Inhibit; public int CodeReads; public uint NextControlAddress = 0x30E0;
            public byte ReadByte(uint a) => _memory.GetValueOrDefault(a);
            public ushort ReadWord(uint a) { if (a >= 0x1000 && a < 0x1010) CodeReads++; return PeekWord(a); }
            public uint ReadLong(uint a) => ((uint)ReadWord(a) << 16) | ReadWord(a + 2);
            public void WriteByte(uint a, byte v) => _memory[a] = v;
            public void WriteWord(uint a, ushort v) { WriteByte(a, (byte)(v >> 8)); WriteByte(a + 1, (byte)v); }
            public void WriteLong(uint a, uint v) { WriteWord(a, (ushort)(v >> 16)); WriteWord(a + 2, (ushort)v); }
            public byte PeekByte(uint a) => ReadByte(a);
            public ushort PeekWord(uint a) => (ushort)((PeekByte(a) << 8) | PeekByte(a + 1));
            public uint PeekLong(uint a) => ((uint)PeekWord(a) << 16) | PeekWord(a + 2);
            public bool RequiresChipBus(uint a, int size) => true;
            public bool IsInstructionCacheable(uint a) => !Inhibit;
        }
    }
}
