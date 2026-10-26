// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.IO;
using AmigaEmulator.Core;
using AmigaEmulator.Core.Cpu;
using AmigaEmulator.Core.Memory;

namespace AmigaEmulator.Tests
{
    internal static class M68k020Tests
    {
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return Test("brief/full addressing includes scaling, suppression and indirect ordering", TestAddressing);
            yield return Test("odd data transfers work while odd instruction fetch creates an address-error frame", TestAlignment);
            yield return Test("EC020 wraps at 24 bits and 68020 preserves the full physical address", TestAddressWidth);
            yield return Test("tagged block-loader repair preserves relative/absolute descriptors and reaches RAM on every CPU", TestTaggedBlockLoader);
            yield return Test("score-writer repair encodes every requested track independently of scratch D2", TestScoreWriterHeader);
            yield return Test("MOVEC relocates vectors and format zero restores through RTE", TestControlAndFrames);
            yield return Test("user-mode MOVEC probes distinguish a 68000 illegal opcode from 020/030 privilege faults", TestUserMovecProbe);
            yield return Test("user status saves preserve flags through CCR on 020/030 without weakening SR privilege", TestUserStatusSave);
            yield return Test("synchronous exceptions retain IPL and masked interrupts cannot preempt their handlers", TestSynchronousMasks);
            yield return Test("master interrupts restore both stacks through the throwaway frame", TestMasterInterrupt);
            yield return Test("long branches and LINK/RTD preserve complete displacements and return addresses", TestControlFlow);
            yield return Test("bitfields cover every register offset/width and signed memory offsets", TestBitFields);
            yield return Test("long multiply/divide handles 64-bit products, remainders, zero and overflow", TestMultiplyDivide);
            yield return Test("CAS changes memory on equality and updates only the compare operand on failure", TestCas);
            yield return Test("control state and suspended odd transfers survive CPU save/restore", TestState);
            yield return Test("CAS2, bounds, MOVES and decimal conversion preserve operand and flag semantics", TestAdditionalInstructions);
            yield return Test("flow trace uses format two and MOVEM saves the 020 predecrement base", TestTraceAndMovem);
            yield return Test("faster processors keep video timing and retire at most one chip transfer per slot", TestScheduler);
            yield return Test("processor mismatch is rejected before RAM mutation and clock phase restores", TestSystemState);
            yield return Test("all 65536 instruction words enter guest exceptions without host failures", TestDecoderMatrix);
            yield return Test("EC030 adds data cache control readback while absent MMU/FPU opcodes trap", TestEc030);
            yield return Test("68030 translates instruction/data pages, MOVES function codes and page-boundary longs", Test030Translation);
            yield return Test("68030 PMOVE, PTEST, PLOAD and PFLUSH preserve guest MMU state", Test030Instructions);
            yield return Test("68030 guest PLOAD/PTEST/PMOVE distinguish a cached fault from a repaired table", Test030CachedFaultInstructions);
            yield return Test("68030 protection faults create a format A frame and double faults halt", Test030Fault);
            yield return new ChipTestCase("CPU/030MMU", "MOVE postincrements survive a protected write and RTE retry",
                "Motorola MC68030UM 8.2.3, 8.4", Test030MovePostincrementRetry);
            yield return new ChipTestCase("CPU/030MMU", "MOVE device-source data waits for a writable simple destination",
                "Motorola MC68030UM 8.2.1-8.2.3; bounded translation preflight, not full bus-cycle continuation", Test030MoveDeviceSourceRetry);
            yield return new ChipTestCase("CPU/030MMU", "MOVE aliased simple EAs retain ordering and A7 byte stride",
                "Motorola M68000 PRM MOVE; MC68030UM 8.2.1-8.2.3", Test030MoveAliasedEaRetry);
            yield return new ChipTestCase("CPU/030MMU", "split long source read retries after a second-page fault",
                "Motorola MC68030UM 8.2.1-8.2.3", Test030SplitLongSourceRetry);
            yield return new ChipTestCase("CPU/030MMU", "split long register write never repeats its first physical word",
                "Motorola MC68030UM 8.2.1-8.2.3", Test030SplitLongRegisterWriteRetry);
            yield return new ChipTestCase("CPU/030MMU", "MOVEM postincrement does not retire registers before a later page fault",
                "Motorola MC68030UM 8.2.1-8.2.3", Test030MovemPostincrementRetry);
            yield return new ChipTestCase("CPU/030MMU", "MOVEM indirect and extended reads preserve registers on a later page fault",
                "Motorola MC68030UM 8.2.1-8.2.3", Test030MovemExtendedEaReadRetry);
            yield return new ChipTestCase("CPU/030MMU", "MOVEM register stores do not repeat an earlier physical write",
                "Motorola MC68030UM 8.2.1-8.2.3", Test030MovemRegisterWriteRetry);
            yield return new ChipTestCase("CPU/030MMU", "MOVEM predecrement faults before an earlier physical write",
                "Motorola MC68030UM 8.2.1-8.2.3", Test030MovemPredecrementWriteRetry);
            yield return new ChipTestCase("CPU/030MMU", "MOVEM displacement and absolute stores precheck later pages",
                "Motorola MC68030UM 8.2.1-8.2.3", Test030MovemExtendedEaWriteRetry);
            yield return new ChipTestCase("CPU/030MMU", "odd MOVEM transfers precheck every byte across an MMU page",
                "Motorola MC68030UM 8.2.1-8.2.3", Test030OddMovemPageRetry);
            yield return new ChipTestCase("CPU/030MMU", "odd single MOVE transfers precheck their final MMU page",
                "Motorola MC68030UM 8.2.1-8.2.3", Test030OddSingleMovePageRetry);
            yield return new ChipTestCase("CPU/030MMU", "cached split MOVE prechecks both pages before a physical fill",
                "Motorola MC68030UM 6.1.2, 8.2.1-8.2.3", Test030CachedSplitMovePageRetry);
            yield return new ChipTestCase("CPU/030MMU", "odd predecrement MOVEM prechecks its final byte without changing An",
                "Motorola MC68030UM 8.2.1-8.2.3", Test030OddMovemPredecrementPageRetry);
            yield return new ChipTestCase("CPU/030MMU", "MOVEP byte transfers do not repeat a physical access after a later page fault",
                "Motorola MC68030UM 8.2.1-8.2.3", Test030MovepPageRetry);
            yield return Test("68030 locked reads check write protection before TAS/CAS change registers", Test030LockedProtection);
        }
        private static ChipTestCase Test(string name, Action run) => new("CPU/020", name, "Motorola M68000 PRM and MC68020UM; independently encoded integer fixtures", run);

        private static (M68kCpu cpu, TestBus bus) Prepare(params ushort[] code) => Prepare(AmigaCpuModel.M68EC020, code);
        private static (M68kCpu cpu, TestBus bus) Prepare(AmigaCpuModel model, params ushort[] code)
        {
            var bus = new TestBus(); bus.WriteLong(0, 0x8000); bus.WriteLong(4, 0x1000);
            for (int i = 0; i < 256; i++) bus.WriteLong((uint)i * 4, 0x2000);
            bus.WriteLong(0, 0x8000); bus.WriteLong(4, 0x1000);
            for (int i = 0; i < code.Length; i++) bus.WriteWord(0x1000u + (uint)i * 2, code[i]);
            bus.WriteWord(0x2000, 0x4E73);
            var cpu = new M68kCpu(bus, model); cpu.Reset(); return (cpu, bus);
        }

        private static void TestUserMovecProbe()
        {
            foreach (var model in new[] { AmigaCpuModel.M68000, AmigaCpuModel.M68EC020,
                AmigaCpuModel.M68020, AmigaCpuModel.M68EC030, AmigaCpuModel.M68030 })
            {
                var (cpu, bus) = Prepare(model, 0x46FC, 0x0000, 0x4E7A, 0x0002);
                bus.WriteLong(4 * 4, 0x2100); bus.WriteLong(8 * 4, 0x2200);
                cpu.SetD(0, 0xCAFEBABE);
                cpu.Step(); cpu.Step();
                bool originalCpu = model == AmigaCpuModel.M68000;
                Check.Equal(originalCpu ? 0x2100u : 0x2200u, cpu.PC,
                    "An unavailable 68000 opcode and a privileged control read use distinct vectors.");
                Check.Equal(0xCAFEBABEu, cpu.GetD(0), "A rejected control read must not change its destination.");
                Check.Equal(originalCpu ? 0x7FFAu : 0x7FF8u, cpu.A7, "Use the processor's real exception frame.");
                Check.Equal((ushort)0, bus.ReadWord(cpu.A7), "The saved status retains user mode.");
                Check.Equal(0x1004u, bus.ReadLong(cpu.A7 + 2), "The frame identifies the faulting MOVEC instruction.");
                if (!originalCpu) Check.Equal((ushort)32, bus.ReadWord(cpu.A7 + 6), "Format zero records vector eight.");
            }
        }

        private static void TestSynchronousMasks()
        {
            // MC68000UM 6.2.5: only reset and interrupt exceptions update IPL.
            // Exercise actual opcodes through both direct and arbitrated execution.
            foreach (var model in new[] { AmigaCpuModel.M68000, AmigaCpuModel.M68EC020,
                AmigaCpuModel.M68020, AmigaCpuModel.M68EC030, AmigaCpuModel.M68030 })
            foreach (int mask in new[] { 0, 1, 2, 3, 4, 5, 6, 7 })
            foreach (bool deferred in new[] { false, true })
            foreach (var (vector, code, user) in new[] {
                (4, new ushort[] { 0x4AFC }, false), (5, new ushort[] { 0x8EFC, 0 }, false),
                (10, new ushort[] { 0xA000 }, false), (11, new ushort[] { 0xF000 }, false),
                (32, new ushort[] { 0x4E40 }, false), (8, new ushort[] { 0x46FC, 0x2700 }, true) })
            {
                ushort originalSr = (ushort)((user ? 0 : 0x2000) | mask << 8);
                var (cpu, bus) = Prepare(model, [0x46FC, originalSr, .. code]);
                bus.WriteLong((uint)vector * 4, 0x2100); bus.WriteWord(0x2100, 0x60FE);
                cpu.Step();
                int observed = 0;
                cpu.ExceptionEntered += (v, _, _, _, _, _) => observed = v;
                if (deferred) for (int tick = 0; tick < 1000 && observed == 0; tick++) cpu.Tick(true);
                else cpu.Step();
                // Exception notifications occur during planning; allow its queued
                // frame writes to retire. The handler's BRA loop changes no state.
                if (deferred) for (int tick = 0; tick < 40; tick++) cpu.Tick(true);
                Check.Equal(vector, observed, "The independently encoded opcode enters its actual vector.");
                Check.Equal(mask, (cpu.SR >> 8) & 7, "Synchronous exceptions preserve the interrupt priority mask.");
                Check.Equal(originalSr, bus.ReadWord(cpu.A7), "The exception frame retains the previous user/supervisor status.");
                Check.True((cpu.SR & 0x2000) != 0, "Every exception enters supervisor mode.");
            }
            foreach (var model in new[] { AmigaCpuModel.M68000, AmigaCpuModel.M68EC020, AmigaCpuModel.M68030 })
            {
                var (cpu, bus) = Prepare(model, 0x4E40, 0x4E71);
                bus.WriteLong(32 * 4, 0x2100); bus.WriteWord(0x2100, 0x4E71); bus.WriteWord(0x2102, 0x4E73);
                cpu.SetInterruptLevel(3); cpu.Step(); cpu.Step();
                Check.Equal(0x2102u, cpu.PC, "A pending level-three IRQ stays masked after a trap at IPL seven.");
                cpu.Step();
                Check.Equal(0x1002u, cpu.PC, "RTE restores the exact pre-trap continuation.");
                Check.Equal((ushort)0x2700, cpu.SR, "RTE preserves IPL seven.");
                // The real interrupt path must still raise IPL when accepted.
                cpu.SetInterruptLevel(0); bus.WriteWord(0x1002, 0x46FC); bus.WriteWord(0x1004, 0x2000);
                cpu.Step(); cpu.SetInterruptLevel(3); cpu.Step();
                Check.Equal((ushort)0x2300, cpu.SR, "Accepted level-three interrupts update IPL.");
                Check.Equal((ushort)0x2000, bus.ReadWord(cpu.A7), "The IRQ frame saves the previous mask.");
            }
        }

        private static void TestUserStatusSave()
        {
            foreach (var model in new[] { AmigaCpuModel.M68000, AmigaCpuModel.M68EC020,
                AmigaCpuModel.M68020, AmigaCpuModel.M68EC030, AmigaCpuModel.M68030 })
            foreach (ushort flags in new ushort[] { 0, 1, 4, 0x10, 0x1F })
            foreach (bool useCcr in new[] { false, true })
            {
                var (cpu, bus) = Prepare(model, 0x46FC, flags,
                    useCcr ? (ushort)0x42F8 : (ushort)0x40F8, 0x5E2C);
                bus.WriteLong(4 * 4, 0x2100); bus.WriteLong(8 * 4, 0x2200);
                bus.WriteWord(0x5E2C, 0xCAFE);
                for (int i = 0; i < 8; i++) { cpu.SetD(i, 0xDEAD0000u + (uint)i); if (i < 7) cpu.SetA(i, 0xBEEF0000u + (uint)i); }
                cpu.Step(); // Enter user mode with the independently chosen CCR.
                uint userStack = cpu.A7;
                cpu.Step();
                bool legal = model == AmigaCpuModel.M68000 ? !useCcr : useCcr;
                Check.Equal(legal ? 0x1008u : useCcr ? 0x2100u : 0x2200u, cpu.PC,
                    "68000 permits user SR reads; 020/030 require CCR and still fault privileged SR reads.");
                Check.Equal(legal ? flags : (ushort)0xCAFE, bus.ReadWord(0x5E2C),
                    "The real CPU instruction saves exactly the user's flags, or leaves the destination untouched on a fault.");
                for (int i = 0; i < 8; i++) {
                    Check.Equal(0xDEAD0000u + (uint)i, cpu.GetD(i), "Status save preserves every data register.");
                    if (i < 7) Check.Equal(0xBEEF0000u + (uint)i, cpu.GetA(i), "Status save preserves address registers.");
                }
                if (legal) {
                    Check.Equal(flags, cpu.SR, "Saving status does not alter flags or supervisor state.");
                    Check.Equal(userStack, cpu.A7, "Saving status preserves the user stack.");
                } else {
                    Check.Equal(flags, bus.ReadWord(cpu.A7), "The fault frame preserves the user status.");
                    Check.Equal(0x1004u, bus.ReadLong(cpu.A7 + 2), "The frame identifies the rejected status read.");
                }
            }
        }

        private static void TestTaggedBlockLoader()
        {
            // The complete reviewed block-setup window, without ROMs or media.
            // The expected destination is computed from the descriptor format;
            // the following real CPU write proves where the reader would transfer.
            byte[] original = Convert.FromHexString("2169001EFFE408A90007001E67082229002AD3A8FFE42369002200164268FFF44EF900002B8A");
            byte[] repaired = Convert.FromHexString("2229001E08A90007001E67080881001FD2A9002A2141FFE42369002200164268FFF44EF82B8A");
            foreach (var model in new[] { AmigaCpuModel.M68000, AmigaCpuModel.M68EC020,
                AmigaCpuModel.M68020, AmigaCpuModel.M68EC030, AmigaCpuModel.M68030 })
            foreach (uint descriptor in new[] { 0x00080000u, 0x80061800u })
            foreach (bool fix in new[] { false, true })
            {
                var (cpu, bus) = Prepare(model, 0x4E71);
                byte[] code = fix ? repaired : original;
                for (int i = 0; i < code.Length; i++) bus.WriteByte(0x2A5Au + (uint)i, code[i]);
                cpu.SetA(0, 0x3000); cpu.SetA(1, 0x4000);
                for (int i = 0; i < 8; i++) cpu.SetD(i, 0x11223300u + (uint)i);
                bus.WriteLong(0x401E, descriptor); bus.WriteLong(0x4022, 0x2200);
                bus.WriteLong(0x402A, 0x1E800); bus.WriteWord(0x2FF4, 5);
                cpu.SetProgramCounter(0x2A5A);
                for (int i = 0; i < 12 && cpu.PC != 0x2B8A; i++) cpu.Step();
                Check.Equal(0x2B8Au, cpu.PC, "Both setups enter the unchanged disk reader.");
                Check.Equal(descriptor & 0x7FFFFFFF, bus.ReadLong(0x401E), "The descriptor's metadata flag is cleared.");
                Check.Equal(0x2200u, bus.ReadLong(0x4016), "Transfer length retains the descriptor value.");
                Check.Equal((ushort)0, bus.ReadWord(0x2FF4), "The block type is reset before the reader.");
                for (int i = 0; i < 8; i++) if (i != 1)
                    Check.Equal(0x11223300u + (uint)i, cpu.GetD(i), "Only the documented scratch register may change.");
                uint expected = (descriptor & 0x80000000) == 0 ? descriptor :
                    (descriptor & 0x7FFFFFFF) + 0x1E800;
                bool tagged = (descriptor & 0x80000000) != 0;
                Check.Equal(!fix && tagged ? expected | 0x80000000 : expected,
                    bus.ReadLong(0x2FE4), "The repair strips metadata before publishing the transfer address.");
                bus.WriteWord(0x2B8A, 0x2668); bus.WriteWord(0x2B8C, 0xFFE4); // MOVEA.L -28(A0),A3
                bus.WriteWord(0x2B8E, 0x16BC); bus.WriteWord(0x2B90, 0x005A); // MOVE.B #$5A,(A3)
                cpu.Step(); cpu.Step();
                bool wide = model is AmigaCpuModel.M68020 or AmigaCpuModel.M68EC030 or AmigaCpuModel.M68030;
                bool escaped = !fix && tagged && wide;
                Check.Equal(escaped ? (byte)0 : (byte)0x5A, bus.ReadByte(expected), "The actual transfer reaches RAM on every repaired CPU.");
                Check.Equal(escaped ? (byte)0x5A : (byte)0, bus.ReadByte(expected | 0x80000000),
                    "The original full-address CPU control reproduces the unmapped tagged write.");
            }
        }

        private static void TestScoreWriterHeader()
        {
            // Execute the small header-assembly window on each real CPU decoder.
            // Disk format expectations come from FF/track/sector/remaining, not
            // from the replacement's MOVEQ/MOVE encoding. No media is required.
            foreach (var model in new[] { AmigaCpuModel.M68000, AmigaCpuModel.M68EC020,
                AmigaCpuModel.M68020, AmigaCpuModel.M68EC030, AmigaCpuModel.M68030 })
            foreach (bool repaired in new[] { false, true })
            {
                ushort[] prefix = repaired ? [0x76FF, 0x1628, 0xFFF9] : [0x363C, 0xFF00, 0x8642];
                var (cpu, bus) = Prepare(model, [.. prefix, 0x4843, 0x1601, 0xE14B, 0x1600]);
                cpu.SetA(0, 0x3000);
                for (uint track = 0; track < 160; track++)
                for (uint sector = 0; sector < 11; sector++)
                foreach (uint scratch in new[] { 0u, track, 0xA5A5009Fu })
                {
                    bus.WriteWord(0x2FF8, (ushort)track);
                    cpu.SetD(0, 11 - sector); cpu.SetD(1, sector);
                    cpu.SetD(2, scratch); cpu.SetD(3, 0x12345678);
                    cpu.SetProgramCounter(0x1000);
                    for (int instruction = 0; instruction < 6; instruction++) cpu.Step();
                    uint expectedTrack = repaired ? track : scratch & 0xFF;
                    Check.Equal(0xFF000000u | expectedTrack << 16 | sector << 8 | (11 - sector),
                        cpu.GetD(3), "The header must identify the physical target, sector and remaining count.");
                    Check.Equal(11 - sector, cpu.GetD(0), "Remaining-sector counter is preserved.");
                    Check.Equal(sector, cpu.GetD(1), "Sector index is preserved.");
                    Check.Equal(scratch, cpu.GetD(2), "The cached/calibration scratch register is not changed.");
                    Check.Equal(0x3000u, cpu.GetA(0), "The IO descriptor remains the caller's pointer.");
                    Check.Equal((ushort)track, bus.ReadWord(0x2FF8), "The requested track is not changed.");
                    Check.Equal(0x100Eu, cpu.PC, "Both instruction windows retain the same size.");
                    if (!repaired && track != 0 && scratch == 0)
                        Check.True(cpu.GetD(3) != (0xFF000000u | track << 16 | sector << 8 | (11 - sector)),
                            "The original write-protection calibration reproduces the wrong-track header.");
                }
            }
        }

        private static void TestAddressing()
        {
            foreach (int scale in new[] { 0, 1, 2, 3 })
            {
                var (cpu, _) = Prepare(0x43F0, (ushort)(0x1800 | scale << 9 | 0xFC));
                cpu.SetA(0, 0x3000); cpu.SetD(1, 5); cpu.Step();
                Check.Equal(0x2FFCu + (uint)(5 << scale), cpu.GetA(1), "Brief signed displacement and scaling.");
            }
            foreach (bool pcRelative in new[] { false, true })
            foreach (int indirect in new[] { 0, 1, 2, 3, 5, 6, 7 })
            foreach (bool suppressBase in new[] { false, true })
            {
                ushort ext = (ushort)(0x1920 | indirect | (suppressBase ? 0x80 : 0)); // D1.L, full, word bd.
                var code = new List<ushort> { pcRelative ? (ushort)0x43FB : (ushort)0x43F0, ext, 0xFFF0 };
                if ((indirect & 3) == 2) code.Add(0xFFF8);
                if ((indirect & 3) == 3) { code.Add(0xFFFF); code.Add(0xFFF8); }
                var (cpu, bus) = Prepare(code.ToArray()); cpu.SetA(0, 0x3000); cpu.SetD(1, 32);
                uint baseAddress = suppressBase ? 0 : pcRelative ? 0x1002u : 0x3000u;
                uint address = unchecked(baseAddress - 16);
                if (indirect != 0) bus.WriteLong((indirect < 4 ? address + 32 : address) & 0xFFFFFF, 0x6000);
                cpu.Step();
                uint expected = indirect == 0 ? address + 32 : indirect < 4 ? 0x6000u : 0x6020u;
                if ((indirect & 3) > 1) expected -= 8;
                Check.Equal(expected, cpu.GetA(1), "Full extension indirect order and PC base.");
            }
            var (suppressed, _) = Prepare(0x43F0, 0x19F0, 0x1234, 0x5678);
            suppressed.SetA(0, 0xFFFF); suppressed.SetD(1, 0xFFFFFFFF); suppressed.Step();
            Check.Equal(0x12345678u, suppressed.GetA(1), "Both suppressed with long displacement.");
            var (legacy, _) = Prepare(AmigaCpuModel.M68000, 0x43F0, 0x1E00);
            legacy.SetA(0, 0x3000); legacy.SetD(1, 5); legacy.Step();
            Check.Equal(0x3005u, legacy.GetA(1), "68000 ignores brief scale bits.");
        }

        private static void TestAlignment()
        {
            foreach (bool deferred in new[] { false, true })
            {
                var (cpu, bus) = Prepare(0x2010, 0x2280, 0x4E72, 0x2700);
                cpu.SetA(0, 0x3001); cpu.SetA(1, 0x4001); bus.WriteLong(0x3001, 0x12345678);
                if (deferred) for (int i = 0; i < 50; i++) cpu.Tick(i % 3 == 0);
                else { cpu.Step(); cpu.Step(); }
                Check.Equal(0x12345678u, cpu.D0, "Odd long load.");
                Check.Equal(0x12345678u, bus.ReadLong(0x4001), "Ordered byte/word/byte store.");
            }
            var (fault, fb) = Prepare(0x4EF9, 0, 0x3001);
            fault.Step(); fault.Step();
            Check.Equal(0x2000u, fault.PC, "Odd fetch vector.");
            Check.Equal(0x7FE0u, fault.A7, "Format A contains sixteen words.");
            Check.Equal((ushort)0xA00C, fb.ReadWord(fault.A7 + 6), "Address error format and vector offset.");
        }

        private static void TestAddressWidth()
        {
            foreach (var model in new[] { AmigaCpuModel.M68EC020, AmigaCpuModel.M68020, AmigaCpuModel.M68EC030, AmigaCpuModel.M68030 })
            {
                var (cpu, bus) = Prepare(model, 0x2010);
                bus.WriteLong(0x01003000, 0xFEDCBA98); bus.WriteLong(0x3000, 0x12345678);
                cpu.SetA(0, 0x01003000); cpu.Step();
                Check.Equal(model != AmigaCpuModel.M68EC020 ? 0xFEDCBA98u : 0x12345678u, cpu.D0, "Physical address width.");
                using var system = new AmigaSystem(AmigaModel.Create(AmigaMachineModel.A600, AmigaRamProfile.Chip2MbFast4Mb)
                    .WithDevelopmentProcessor(model, 14187580), AmigaAccuracyOptions.Compatible);
                system.Bus.RomOverlayAtZero = false;
                system.Bus.WriteLong(0x3000, 0x12345678);
                system.Bus.WriteLong(0x08003000, 0xFEDCBA98);
                Check.Equal(model != AmigaCpuModel.M68EC020 ? 0x12345678u : 0xFEDCBA98u,
                    system.Bus.PeekLong(0x3000), "ROM high-memory probes cannot corrupt low RAM on a 32-bit board.");
                foreach (uint physical in new uint[] { 0x3000, 0x200000, 0xBFE001, 0xDFF180, 0xDA8000, 0xE80000, 0xF80000 })
                foreach (uint upperBits in new uint[] { 0x08000000, 0x80000000 })
                {
                    // Old disk loaders also store flags in bit 31. A 030 must
                    // retain that bit, rather than invent a Chip RAM alias.
                    uint high = physical | upperBits;
                    Check.Equal(model != AmigaCpuModel.M68EC020 ? AmigaAddressRegion.Unmapped : system.Bus.GetRegion(physical),
                        system.Bus.GetRegion(high), "Device decoding follows the physical CPU address lines.");
                    if (model != AmigaCpuModel.M68EC020)
                        Check.True(!system.Bus.RequiresChipBus(high, 4), "Unpopulated high space does not request Chip RAM arbitration.");
                }
            }
        }

        private static void TestEc030()
        {
            var (cpu, bus) = Prepare(AmigaCpuModel.M68EC030, 0x4E7B, 0x0002, 0x4E7A, 0x1002, 0xF010, 0x4000);
            cpu.SetD(0, uint.MaxValue); cpu.Step(); cpu.Step();
            Check.Equal(0x3313u, cpu.GetD(1), "EC030 implemented CACR bits; clear commands self-clear.");
            cpu.Step(); Check.Equal((ushort)44, bus.ReadWord(cpu.A7 + 6), "The EC variant has no built-in PMMU.");
            var (fpu, fb) = Prepare(AmigaCpuModel.M68EC030, 0xF200, 0);
            fpu.Step(); Check.Equal((ushort)44, fb.ReadWord(fpu.A7 + 6), "Optional external FPU is absent.");
            var (acu, ab) = Prepare(AmigaCpuModel.M68EC030, 0xF010, 0x0800, 0xF011, 0x0A00,
                0xF012, 0x8215, 0xF013, 0x6200, 0xF012, 0x8015, 0xF013, 0x6200);
            acu.SetA(0, 0x3000); acu.SetA(1, 0x4000); acu.SetA(2, 0x12001000); acu.SetA(3, 0x5000);
            ab.WriteLong(0x3000, 0x12008250); // Top byte 12, supervisor data FC5, reads only.
            acu.Step(); acu.Step();
            Check.Equal(0x12008250u, ab.ReadLong(0x4000), "EC030 AC0 register readback.");
            using (var state = new MemoryStream())
            {
                acu.SaveState(new BinaryWriter(state));
                var restored = new M68kCpu(ab, AmigaCpuModel.M68EC030);
                state.Position = 0; restored.LoadState(new BinaryReader(state)); acu = restored;
            }
            acu.Step(); acu.Step(); Check.Equal((ushort)0x40, ab.ReadWord(0x5000), "PTESTR uses the restored descriptor.");
            acu.Step(); acu.Step(); Check.Equal((ushort)0, ab.ReadWord(0x5000), "PTESTW rejects a read-only descriptor.");
            // No privileged operation may apply a control-register write in user mode.
            var (user, ub) = Prepare(AmigaCpuModel.M68EC030, 0x46FC, 0, 0x4E7B, 2);
            user.Step(); user.Step(); Check.Equal((ushort)32, ub.ReadWord(user.A7 + 6), "MOVEC is supervisor-only.");
            var (matrix, mb) = Prepare(AmigaCpuModel.M68EC030); matrix.TraceEnabled = false;
            for (int opcode = 0; opcode <= ushort.MaxValue; opcode++)
            {
                matrix.Reset(); for (uint address = 0x1000; address < 0x1010; address += 2) mb.WriteWord(address, 0);
                mb.WriteWord(0x1000, (ushort)opcode); matrix.Step();
            }
        }

        private static void TestControlAndFrames()
        {
            var (cpu, bus) = Prepare(0x4E7B, 0x0801, 0x4E7A, 0x1801, 0x4E40, 0x4E71);
            cpu.SetD(0, 0x4000); bus.WriteLong(0x4080, 0x2000);
            cpu.Step(); cpu.Step(); cpu.Step();
            Check.Equal(0x4000u, cpu.GetD(1), "MOVEC VBR readback.");
            Check.Equal(0x7FF8u, cpu.A7, "Eight-byte normal exception frame.");
            Check.Equal((ushort)0x80, bus.ReadWord(cpu.A7 + 6), "TRAP vector offset.");
            Check.Equal(0x100Au, bus.ReadLong(cpu.A7 + 2), "Return PC after trap.");
            cpu.Step(); Check.Equal(0x100Au, cpu.PC, "RTE relocated handler return.");
            Check.Equal(0x8000u, cpu.A7, "RTE stack restored.");
            var (bad, bb) = Prepare(0x4E73); bb.WriteWord(0x8006, 0x3000);
            bad.Step(); Check.Equal((ushort)56, bb.ReadWord(bad.A7 + 6), "Unsupported frame produces format error.");
        }

        private static void TestMasterInterrupt()
        {
            var (cpu, bus) = Prepare(0x4E7B, 0x0803, 0x46FC, 0x3000, 0x4E71);
            cpu.SetD(0, 0x9000); cpu.Step(); cpu.Step();
            Check.Equal(0x9000u, cpu.A7, "SR M selects MSP.");
            cpu.RequestInterrupt(3); cpu.Step();
            Check.Equal(0x7FF8u, cpu.A7, "Interrupt selects ISP.");
            Check.Equal((ushort)0x106C, bus.ReadWord(0x7FFE), "ISP throwaway frame.");
            Check.Equal((ushort)0x006C, bus.ReadWord(0x8FFE), "MSP normal frame.");
            cpu.RequestInterrupt(0); cpu.Step();
            Check.Equal(0x9000u, cpu.A7, "RTE follows throwaway to MSP.");
            Check.Equal((ushort)0x3000, cpu.SR, "Master state restored.");
            Check.Equal(0x1008u, cpu.PC, "Interrupted PC restored.");
        }

        private static void TestControlFlow()
        {
            var (cpu, bus) = Prepare(0x61FF, 1, 0);
            cpu.Step(); Check.Equal(0x11002u, cpu.PC, "Long BSR base is extension-word PC.");
            Check.Equal(0x1006u, bus.ReadLong(cpu.A7), "BSR pushes after complete displacement.");
            var (link, lb) = Prepare(0x480E, 0xFFFF, 0xFF00);
            link.SetA(6, 0xDEADBEEF); link.Step();
            Check.Equal(0x7FFCu, link.GetA(6), "LINK frame pointer.");
            Check.Equal(0x7EFCu, link.A7, "LINK.L signed displacement.");
            Check.Equal(0xDEADBEEFu, lb.ReadLong(0x7FFC), "LINK saves old frame pointer.");
            var (rtd, rb) = Prepare(0x4E74, 12); rb.WriteLong(0x8000, 0x3000);
            rtd.Step(); Check.Equal(0x3000u, rtd.PC, "RTD pops PC."); Check.Equal(0x8010u, rtd.A7, "RTD adjusts arguments.");
        }

        private static void TestBitFields()
        {
            const uint original = 0x9ABCDE01;
            foreach (int offset in System.Linq.Enumerable.Range(0, 32))
            foreach (int width in System.Linq.Enumerable.Range(1, 32))
            {
                ushort ext = (ushort)(0x1000 | offset << 6 | (width & 31));
                var (cpu, _) = Prepare(0xE9C0, ext, 0xEFC0, ext);
                cpu.SetD(0, original); cpu.Step();
                uint expected = 0;
                for (int i = 0; i < width; i++) expected = (expected << 1) | ((original >> (31 - ((offset + i) & 31))) & 1);
                Check.Equal(expected, cpu.GetD(1), "Wrapped register bitfield extraction.");
                cpu.SetD(1, 0x12345678); cpu.Step(); uint changed = original;
                for (int i = 0; i < width; i++)
                {
                    int bit = 31 - ((offset + i) & 31);
                    changed = (changed & ~(1u << bit)) | (((0x12345678u >> (width - 1 - i)) & 1) << bit);
                }
                Check.Equal(changed, cpu.D0, "Wrapped insertion preserves all other bits.");
            }
            var (memory, mb) = Prepare(0xE9D0, 0x18C0 | 12); // D3 offset, twelve bits, destination D1.
            memory.SetA(0, 0x3001); memory.SetD(3, unchecked((uint)-4)); mb.WriteWord(0x3000, 0xABCD);
            memory.Step(); Check.Equal(0xBCDu, memory.GetD(1), "Negative signed bit offset.");
        }

        private static void TestMultiplyDivide()
        {
            var (mul, _) = Prepare(0x4C01, 0x0402); mul.SetD(0, 0xFFFFFFFF); mul.SetD(1, 2); mul.Step();
            Check.Equal(0xFFFFFFFEu, mul.D0, "64-bit multiply low."); Check.Equal(1u, mul.GetD(2), "64-bit multiply high.");
            var (div, _) = Prepare(0x4C41, 0x0402); div.SetD(0, 5); div.SetD(2, 1); div.SetD(1, 3); div.Step();
            Check.Equal(1431655767u, div.D0, "64-bit quotient."); Check.Equal(0u, div.GetD(2), "Remainder.");
            var (overflow, _) = Prepare(0x4C41, 0x0C02); overflow.SetD(2, 0x80000000); overflow.SetD(0, 0); overflow.SetD(1, 0xFFFFFFFF);
            overflow.Step(); Check.True((overflow.SR & 2) != 0, "Signed minimum / -1 sets overflow without host exception.");
            Check.Equal(0x80000000u, overflow.GetD(2), "Overflow retains dividend.");
            var (zero, zb) = Prepare(0x4C41, 0); zero.Step();
            Check.Equal(0x7FF4u, zero.A7, "Divide by zero format two.");
            Check.Equal((ushort)0x2014, zb.ReadWord(zero.A7 + 6), "Instruction exception frame.");
        }

        private static void TestCas()
        {
            foreach (bool equal in new[] { false, true })
            {
                var (cpu, bus) = Prepare(0x0AD0, 0x0040); // CAS.B D0,D1,(A0)
                cpu.SetA(0, 0x3000); cpu.SetD(0, 0xDEAD0012); cpu.SetD(1, 0x34);
                bus.WriteByte(0x3000, equal ? (byte)0x12 : (byte)0x56); cpu.Step();
                Check.Equal(equal ? (byte)0x34 : (byte)0x56, bus.ReadByte(0x3000), "CAS memory.");
                Check.Equal(equal ? 0xDEAD0012u : 0xDEAD0056u, cpu.D0, "Compare register byte merge.");
                Check.Equal(equal, (cpu.SR & 4) != 0, "CAS equality flag.");
            }
        }

        private static void TestState()
        {
            var (cpu, bus) = Prepare(0x4E7B, 0x0801, 0x2280, 0x4E72, 0x2700);
            cpu.SetD(0, 0x4000); cpu.SetA(1, 0x3001); cpu.Step(); cpu.SetD(0, 0xCAFEBABE); cpu.Tick(false);
            using var stream = new MemoryStream(); cpu.SaveState(new BinaryWriter(stream));
            var restored = new M68kCpu(bus, AmigaCpuModel.M68EC020); stream.Position = 0; restored.LoadState(new BinaryReader(stream));
            Check.Equal(0x4000u, restored.VectorBase, "VBR serialized.");
            for (int i = 0; i < 40; i++) restored.Tick(true);
            Check.Equal(0xCAFEBABEu, bus.ReadLong(0x3001), "Pending misaligned writes retire after restore.");
        }

        private static void TestAdditionalInstructions()
        {
            foreach (bool equal in new[] { false, true })
            {
                var (cas, cb) = Prepare(0x0EFC, 0x8080, 0x90C1);
                cas.SetA(0, 0x3000); cas.SetA(1, 0x4000);
                cas.SetD(0, 0x12); cas.SetD(1, 0x34); cas.SetD(2, 0x56); cas.SetD(3, 0x78);
                cb.WriteLong(0x3000, 0x12); cb.WriteLong(0x4000, equal ? 0x34u : 0x99u); cas.Step();
                Check.Equal(equal ? 0x56u : 0x12u, cb.ReadLong(0x3000), "CAS2 does not partially update.");
                Check.Equal(equal ? 0x78u : 0x99u, cb.ReadLong(0x4000), "CAS2 second destination.");
                Check.Equal(equal ? 0x34u : 0x99u, cas.GetD(1), "CAS2 failed comparisons update both data registers.");
            }
            foreach (int value in new[] { -1, 0, 10, 11 })
            {
                var (bounds, bb) = Prepare(0x02D0, 0); bounds.SetA(0, 0x3000); bounds.SetD(0, unchecked((uint)value));
                bb.WriteWord(0x3000, 0); bb.WriteWord(0x3002, 10); bounds.Step();
                Check.Equal(value < 0 || value > 10, (bounds.SR & 1) != 0, "CMP2 range.");
                Check.Equal(value == 0 || value == 10, (bounds.SR & 4) != 0, "CMP2 exact boundary.");
            }
            var (moves, mb) = Prepare(0x0E50, 0x1000, 0x0E90, 0x9800);
            moves.SetA(0, 0x3001); moves.SetA(1, 0xFEDCBA98); moves.SetD(1, 0xAAAA0000); mb.WriteWord(0x3001, 0x8000);
            moves.Step(); Check.Equal(0xAAAA8000u, moves.GetD(1), "MOVES word merges data register without changing flags.");
            moves.Step(); Check.Equal(0xFEDCBA98u, mb.ReadLong(0x3001), "MOVES address register to odd long memory.");
            for (int value = 0; value < 100; value++)
            {
                var (pack, _) = Prepare(0x8340, 0xCFD0, 0x8581, 0x3030);
                pack.SetD(0, (uint)(((value / 10 + 0x30) << 8) | (value % 10 + 0x30)));
                pack.SetD(1, 0xAAAA0000); pack.SetD(2, 0xBBBB0000); pack.Step();
                Check.Equal(0xAAAA0000u | (uint)(value / 10 << 4 | value % 10), pack.GetD(1), "PACK ASCII adjustment.");
                pack.Step(); Check.Equal(0xBBBB0000u | pack.D0, pack.GetD(2), "UNPK ASCII round trip.");
                Check.Equal((ushort)0x2700, pack.SR, "Decimal conversion leaves CCR intact.");
            }
        }

        private static void TestTraceAndMovem()
        {
            var (cpu, bus) = Prepare(0x46FC, 0x6000, 0x4E71, 0x6002);
            cpu.Step(); cpu.Step(); Check.Equal(0x1006u, cpu.PC, "T0 does not trace ordinary NOP.");
            cpu.Step(); Check.Equal(0x2000u, cpu.PC, "T0 traces a taken branch.");
            Check.Equal((ushort)0x2024, bus.ReadWord(cpu.A7 + 6), "Trace format two and vector.");
            Check.Equal(0x1006u, bus.ReadLong(cpu.A7 + 8), "Trace instruction address.");
            var (movem, mm) = Prepare(0x48E0, 0x0080); movem.SetA(0, 0x3000); movem.Step();
            Check.Equal(0x2FFCu, mm.ReadLong(0x2FFC), "020 MOVEM predecrement stores initial An minus operand size.");
        }

        private static void TestScheduler()
        {
            var baseline = AmigaModel.Create(AmigaMachineModel.A600, AmigaRamProfile.Chip2MbFast4Mb);
            var development = baseline.WithDevelopmentProcessor(AmigaCpuModel.M68EC020, 14187580);
            Check.Equal(baseline.FrameRateHz, development.FrameRateHz, "CPU upgrade leaves PAL frequency intact.");
            uint Run(AmigaModel model, bool fast)
            {
                using var machine = new AmigaSystem(model, AmigaAccuracyOptions.Compatible);
                machine.Bus.RomOverlayAtZero = false; machine.Bus.AutoConfig.MarkLegacyConfigured();
                uint address = fast ? 0x200000u : 0x1000u;
                machine.Bus.WriteWord(address, 0x5280); machine.Bus.WriteWord(address + 2, 0x60FC);
                machine.Cpu.SetProgramCounter(address); machine.Cpu.SetA(7, 0x8000);
                int transfers = 0;
                machine.Bus.ReadObserved += (a, size, value, region) => { if (!fast && size == 2) transfers++; };
                machine.RunChipCycles(10000);
                if (!fast) Check.True(transfers <= 10000, "Faster CPU cannot reuse a Chip RAM slot.");
                Check.Equal(9999L, machine.Agnus.Beam.GlobalChipCycle, "Chip cycles stay independent.");
                return machine.Cpu.D0;
            }
            uint ordinary = Run(baseline, true), upgraded = Run(development, true);
            Check.True(upgraded > ordinary * 1.9 && upgraded < ordinary * 2.1, "Uncontended integer loop follows CPU clock ratio.");
            Run(development, false);
        }

        private static void TestSystemState()
        {
            var baseline = AmigaModel.Create(AmigaMachineModel.A600, AmigaRamProfile.Chip2MbFast4Mb);
            var development = baseline.WithDevelopmentProcessor(AmigaCpuModel.M68EC020, 16000000);
            using var source = new AmigaSystem(development); source.Bus.RomOverlayAtZero = false;
            source.Bus.WriteWord(0x1000, 0x60FE); source.Cpu.SetProgramCounter(0x1000); source.RunChipCycles(3);
            using var stream = new MemoryStream(); source.SaveState(new BinaryWriter(stream));
            using var wrong = new AmigaSystem(baseline); wrong.Bus.WriteByte(0x600, 0xAC); stream.Position = 0;
            bool rejected = false;
            try { wrong.LoadState(new BinaryReader(stream)); } catch (InvalidDataException) { rejected = true; }
            Check.True(rejected, "Same motherboard with different CPU is rejected.");
            Check.Equal((byte)0xAC, wrong.Bus.ChipRam.Read(0x600), "Preflight preserves RAM.");
            using var restored = new AmigaSystem(development); stream.Position = 0; restored.LoadState(new BinaryReader(stream));
            source.RunChipCycles(100); restored.RunChipCycles(100);
            Check.Equal(source.Cpu.PC, restored.Cpu.PC, "CPU phase restore PC.");
            Check.Equal(source.Cpu.Snapshot.Cycles, restored.Cpu.Snapshot.Cycles, "CPU phase restore cycles.");
        }

        private static void TestDecoderMatrix()
        {
            foreach (var model in new[] { AmigaCpuModel.M68EC020, AmigaCpuModel.M68030 })
            {
            var (cpu, bus) = Prepare(model); cpu.TraceEnabled = false;
            for (int opcode = 0; opcode <= ushort.MaxValue; opcode++)
            {
                cpu.Reset();
                for (uint address = 0x1000; address < 0x1010; address += 2) bus.WriteWord(address, 0);
                bus.WriteWord(0x1000, (ushort)opcode); cpu.Step();
            }
            }
        }

        private static void Map030(M68kCpu cpu, TestBus bus)
        {
            // Four-bit initial shift, two eight-bit tables, twelve-bit pages.
            cpu.MemoryManagementUnit.SetRoot(false, 0x7FFF000200004000UL);
            cpu.MemoryManagementUnit.SetTranslationControl(0x80C48800);
            bus.WriteLong(0x4000, 0x5002);
            for (uint page = 0; page < 16; page++) bus.WriteLong(0x5000 + page * 4, page * 0x1000 | 1);
        }
        private static void Test030Translation()
        {
            foreach (bool deferred in new[] { false, true })
            {
                var (cpu, bus) = Prepare(AmigaCpuModel.M68030);
                Map030(cpu, bus);
                bus.WriteLong(0x5004, 0x6001); // Code logical 1000 -> physical 6000.
                bus.WriteWord(0x6000, 0x2010); bus.WriteWord(0x6002, 0x2280);
                bus.WriteWord(0x6004, 0x4E72); bus.WriteWord(0x6006, 0x2700);
                bus.WriteLong(0x500C, 0x9001); bus.WriteLong(0x5010, 0xA001);
                bus.WriteWord(0x9FFE, 0x1234); bus.WriteWord(0xA000, 0x5678);
                cpu.SetA(0, 0x3FFE); cpu.SetA(1, 0xB000);
                if (deferred) for (int i = 0; i < 200; i++) cpu.Tick(i % 3 == 0);
                else { cpu.Step(); cpu.Step(); }
                Check.Equal(0x12345678u, cpu.D0, "Both code words and split long pages translate.");
                Check.Equal(0x12345678u, bus.ReadLong(0xB000), "Deferred writes retire at physical addresses.");
                Check.True((bus.ReadLong(0x502C) & 24) == 24, "Used/modified bits retire with the write.");
            }
            var (moves, mb) = Prepare(AmigaCpuModel.M68030, 0x0E90, 0x0000);
            Map030(moves, mb);
            moves.MemoryManagementUnit.SetRoot(true, 0x7FFF00020000C000UL);
            moves.MemoryManagementUnit.SetTranslationControl(0x82C48800);
            mb.WriteLong(0xC000, 0xD002); mb.WriteLong(0xD004, 0x1001);
            mb.WriteLong(0xD00C, 0xE001); mb.WriteLong(0x500C, 0x9001);
            mb.WriteLong(0x9000, 0x12345678); mb.WriteLong(0xE000, 0xFFFFFFFF);
            moves.SetA(0, 0x3000); moves.Step(); // SFC defaults to zero, hence CRP/user mapping.
            Check.Equal(0x12345678u, moves.D0, "MOVES uses SFC rather than supervisor data FC.");
        }
        private static void Test030Instructions()
        {
            var (cpu, bus) = Prepare(AmigaCpuModel.M68030, 0xF010, 0x4C00, 0xF011, 0x4000,
                0xF012, 0x8F55, 0xF012, 0x2215, 0xF012, 0x8215, 0xF000, 0x2400);
            bus.WriteLong(0x3000, 0x7FFF0002); bus.WriteLong(0x3004, 0x4000);
            bus.WriteLong(0x3100, 0x80C48800);
            bus.WriteLong(0x4000, 0x5002);
            for (uint page = 0; page < 16; page++) bus.WriteLong(0x5000 + page * 4, page * 0x1000 | 1);
            cpu.SetA(0, 0x3000); cpu.SetA(1, 0x3100); cpu.SetA(2, 0x6000);
            cpu.Step(); cpu.Step(); cpu.Step();
            Check.Equal(0x5018u, cpu.GetA(2), "PTEST A2 returns the physical last descriptor.");
            Check.Equal((ushort)2, cpu.MemoryManagementUnit.Status, "PTEST reports table depth.");
            cpu.SetA(2, 0x6000); cpu.Step(); cpu.Step();
            Check.True((cpu.MemoryManagementUnit.Status & M68030Mmu.Invalid) == 0, "PLOAD creates the ATC entry found by level-zero PTEST.");
            using var stream = new MemoryStream(); cpu.SaveState(new BinaryWriter(stream));
            cpu.Step();
            Check.True((cpu.MemoryManagementUnit.Test(0x6000, 5, false, true).Flags & M68030Mmu.Invalid) != 0, "PFLUSHA removes ATC entry.");
            stream.Position = 0; cpu.LoadState(new BinaryReader(stream));
            Check.True(cpu.MemoryManagementUnit.Test(0x6000, 5, false, true).Accessible(false), "Save/restore preserves MMU and ATC.");
            var (invalid, ib) = Prepare(AmigaCpuModel.M68030, 0xF010, 0x4000);
            invalid.SetA(0, 0x3000); ib.WriteLong(0x3000, 0x80C40000); invalid.Step();
            Check.Equal(0x2000u, invalid.PC, "Invalid TC enters MMU configuration exception.");
            Check.Equal((ushort)224, ib.ReadWord(invalid.A7 + 6), "Vector 56 is recorded in the frame.");
            var (functionCode, _) = Prepare(AmigaCpuModel.M68030, 0xF010, 0x8208);
            functionCode.SetA(0, 0x3000); functionCode.SetD(0, 0xFFFFFFFF); functionCode.Step();
            Check.Equal(0x1004u, functionCode.PC, "A data-register FC uses only its three low bits.");
        }
        private static void Test030CachedFaultInstructions()
        {
            // PLOADR #5,(A2); PTESTR #5,(A2),#0; PMOVE MMUSR,(A3);
            // then a level-seven PTEST followed by another cached-only PTEST.
            var (cpu, bus) = Prepare(AmigaCpuModel.M68030, 0xF012, 0x2215,
                0xF012, 0x8215, 0xF013, 0x6200, 0xF012, 0x9E15, 0xF012, 0x8215);
            Map030(cpu, bus); bus.WriteLong(0x5018, 0);
            cpu.SetA(2, 0x6000); cpu.SetA(3, 0x3000);
            cpu.Step(); // A failed PLOAD creates an entry, not a data-access exception.
            Check.Equal(0x1004u, cpu.PC, "PLOAD completes even when the page descriptor is invalid.");
            bus.WriteLong(0x5018, 0x6001);
            cpu.Step(); cpu.Step();
            Check.Equal((ushort)0x8400, bus.ReadWord(0x3000), "The guest reads B/I through PMOVE MMUSR.");
            cpu.Step();
            Check.Equal((ushort)2, cpu.MemoryManagementUnit.Status, "Level-seven PTEST sees the repaired two-level table.");
            cpu.Step();
            Check.Equal((ushort)0x8400, cpu.MemoryManagementUnit.Status, "PTEST did not repair the fault in the ATC.");
            cpu.MemoryManagementUnit.Flush(5, 7, 0x6000);
            Check.Equal((ushort)0x400, cpu.MemoryManagementUnit.Test(0x6000, 5, false, 0).Flags,
                "Explicit PFLUSH changes the cached bus error to an ATC miss.");
        }

        private static void Test030Fault()
        {
            var (cpu, bus) = Prepare(AmigaCpuModel.M68030, 0x2080);
            Map030(cpu, bus); bus.WriteLong(0x500C, 0x9005);
            cpu.SetA(0, 0x3000); cpu.SetD(0, 0xCAFEBABE); cpu.Step();
            Check.Equal(0x2000u, cpu.PC, "Write protection enters bus error vector.");
            Check.Equal(0x7FE0u, cpu.A7, "Format A length.");
            Check.Equal((ushort)0xA008, bus.ReadWord(cpu.A7 + 6), "Bus error format/vector.");
            Check.Equal(0x3000u, bus.ReadLong(cpu.A7 + 16), "Fault address remains logical.");
            Check.Equal((ushort)0x125, bus.ReadWord(cpu.A7 + 10), "Data write, word transfer and supervisor function code.");
            Check.Equal(0u, bus.ReadLong(0x9000), "Protected memory is unchanged.");
            var (doubleFault, db) = Prepare(AmigaCpuModel.M68030, 0x2010);
            Map030(doubleFault, db); db.WriteLong(0x500C, 0); db.WriteLong(0x501C, 0);
            doubleFault.SetA(0, 0x3000); doubleFault.Step();
            Check.Equal(M68kExecutionState.Stopped, doubleFault.Snapshot.ExecutionState, "A fault while stacking halts the guest.");
        }

        private static void Test030MovePostincrementRetry()
        {
            // MOVE.W (A0)+,(A1)+. The source read succeeds, then the destination
            // MMU translation faults. Re-executing after RTE must not skip source
            // data or write to the following destination word.
            foreach (bool deferred in new[] { false, true })
            {
                var (cpu, bus) = Prepare(AmigaCpuModel.M68030, 0x32D8);
                Map030(cpu, bus);
                bus.WriteLong(0x500C, 0x9009);
                bus.WriteLong(0x5010, 0xA00D);
                bus.WriteWord(0x9000, 0xABCD);
                bus.WriteWord(0x9002, 0x1234);
                cpu.SetA(0, 0x3000);
                cpu.SetA(1, 0x4000);
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x2000; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x2000u, cpu.PC, "Protected destination enters bus error handler.");
                Check.Equal(0x3000u, cpu.GetA(0), "Source postincrement is restartable.");
                Check.Equal(0x4000u, cpu.GetA(1), "Destination postincrement is restartable.");
                Check.Equal((ushort)0, bus.ReadWord(0xA000), "Protected write has no effect.");
                bus.WriteLong(0x5010, 0xA009);
                cpu.MemoryManagementUnit.Flush();
                Check.True(cpu.MemoryManagementUnit.Test(0x4000, 5, true, 7).Accessible(true),
                    "Repaired destination translation permits writes.");
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x1000; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x1000u, cpu.PC, "RTE returns to the faulting MOVE.");
                cpu.Step();
                Check.Equal(0x1002u, cpu.PC, "MOVE completes after remapping.");
                Check.Equal(0x3002u, cpu.GetA(0), "Source advances once.");
                Check.Equal(0x4002u, cpu.GetA(1), "Destination advances once.");
                Check.Equal((ushort)0xABCD, bus.ReadWord(0xA000), "Original source reaches the destination.");
                Check.Equal((ushort)0, bus.ReadWord(0xA002), "Retry does not write the next word.");
            }
        }

        private static void Test030MoveDeviceSourceRetry()
        {
            // The retry implementation cannot replay a completed device read.
            // For these extension-free EAs, defer physical data until source
            // and destination translations both succeed. Exercise all sizes,
            // indirect/postincrement/predecrement combinations and both paths.
            foreach (bool deferred in new[] { false, true })
            foreach (int size in new[] { 1, 2, 4 })
            foreach (int sourceMode in new[] { 2, 3, 4 })
            foreach (int destinationMode in new[] { 2, 3, 4 })
            {
                int prefix = size == 1 ? 0x1000 : size == 2 ? 0x3000 : 0x2000;
                ushort opcode = (ushort)(prefix | 0x0200 | destinationMode << 6 | sourceMode << 3);
                var (cpu, bus) = Prepare(AmigaCpuModel.M68030, opcode);
                Map030(cpu, bus);
                bus.WriteLong(0x500C, 0x9009);
                bus.WriteLong(0x5010, 0xA00D);
                bus.WriteLong(0x9000, 0xABCD1234);
                uint sourceBase = sourceMode == 4 ? 0x3000u + (uint)size : 0x3000u;
                uint destinationBase = destinationMode == 4 ? 0x4000u + (uint)size : 0x4000u;
                cpu.SetA(0, sourceBase);
                cpu.SetA(1, destinationBase);
                bus.TrackedWordReadAddress = 0x9000;
                bus.TrackedByteReadAddress = 0x9000;
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x2000; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x2000u, cpu.PC, "Protected destination enters the handler.");
                Check.Equal(sourceBase, cpu.GetA(0), "Fault leaves source An restartable.");
                Check.Equal(destinationBase, cpu.GetA(1), "Fault leaves destination An restartable.");
                Check.Equal(0, size == 1 ? bus.TrackedByteReadCount : bus.TrackedWordReadCount,
                    "No physical device source read precedes the destination translation fault.");
                Check.Equal(0u, bus.ReadLong(0x5010) & 0x10u, "An unretired destination is not modified.");
                bus.WriteLong(0x5010, 0xA009);
                cpu.MemoryManagementUnit.Flush();
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x1000; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x1000u, cpu.PC, "RTE retries the MOVE.");
                cpu.Step();
                Check.Equal(0x1002u, cpu.PC, "MOVE completes after the repair.");
                Check.Equal(1, size == 1 ? bus.TrackedByteReadCount : bus.TrackedWordReadCount,
                    "The physical device source is read only once.");
                uint expectedSource = sourceMode == 2 ? 0x3000u : sourceMode == 3 ? 0x3000u + (uint)size : 0x3000u;
                uint expectedDestination = destinationMode == 2 ? 0x4000u : destinationMode == 3 ? 0x4000u + (uint)size : 0x4000u;
                Check.Equal(expectedSource, cpu.GetA(0), "Source EA retires once.");
                Check.Equal(expectedDestination, cpu.GetA(1), "Destination EA retires once.");
                uint value = size == 1 ? bus.ReadByte(0xA000) : size == 2 ? bus.ReadWord(0xA000) : bus.ReadLong(0xA000);
                Check.Equal(size == 1 ? 0xABu : size == 2 ? 0xABCDu : 0xABCD1234u, value,
                    "Original source value reaches the repaired destination.");
            }
        }

        private static void Test030MoveAliasedEaRetry()
        {
            foreach (bool deferred in new[] { false, true })
            {
                var (cpu, bus) = Prepare(AmigaCpuModel.M68030, 0x32D8);
                Map030(cpu, bus);
                bus.WriteLong(0x500C, 0);
                bus.WriteLong(0x5010, 0);
                cpu.SetA(0, 0x3000);
                cpu.SetA(1, 0x4000);
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x2000; i++) cpu.Tick(true);
                else cpu.Step();
                if (deferred)
                    for (int i = 0; i < 500 && bus.ReadLong(cpu.A7 + 16) != 0x3000; i++) cpu.Tick(true);
                Check.Equal(0x3000u, bus.ReadLong(cpu.A7 + 16), "Source translation fault takes priority over the destination.");
                Check.Equal(0x3000u, cpu.GetA(0), "A source fault preserves its original address.");
                Check.Equal(0x4000u, cpu.GetA(1), "A source fault cannot advance the destination.");
            }
            foreach (bool deferred in new[] { false, true })
            foreach (bool descending in new[] { false, true })
            {
                // MOVE.W (A0)+,(A0)+ or MOVE.W -(A0),-(A0).
                // Destination EA must include the source's stride even though
                // preflight itself must not change the architectural register.
                var (cpu, bus) = Prepare(AmigaCpuModel.M68030, descending ? (ushort)0x3120 : (ushort)0x30D8);
                Map030(cpu, bus);
                bus.WriteLong(0x500C, descending ? 0x900Du : 0x9009u);
                bus.WriteLong(0x5010, 0xA00D);
                uint initial = descending ? 0x3004u : 0x3FFEu;
                uint sourcePhysical = descending ? 0x9002u : 0x9FFEu;
                uint destinationPhysical = descending ? 0x9000u : 0xA000u;
                bus.WriteWord(sourcePhysical, 0x5678);
                bus.TrackedWordReadAddress = sourcePhysical;
                cpu.SetA(0, initial);
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x2000; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x2000u, cpu.PC, "Aliased protected store faults.");
                Check.Equal(initial, cpu.GetA(0), "Both strides are rewound on fault.");
                Check.Equal(0, bus.TrackedWordReadCount, "Aliased preflight does not read the device source.");
                bus.WriteLong(descending ? 0x500Cu : 0x5010u, descending ? 0x9009u : 0xA009u);
                cpu.MemoryManagementUnit.Flush();
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x1000; i++) cpu.Tick(true);
                else cpu.Step();
                cpu.Step();
                Check.Equal(descending ? 0x3000u : 0x4002u, cpu.GetA(0), "Both ordered strides retire once.");
                Check.Equal((ushort)0x5678, bus.ReadWord(destinationPhysical), "Store uses the post-source destination EA.");
                Check.Equal(1, bus.TrackedWordReadCount, "Aliased source is read exactly once.");
            }
            foreach (bool descending in new[] { false, true })
            {
                var (cpu, bus) = Prepare(AmigaCpuModel.M68030, descending ? (ushort)0x1F27 : (ushort)0x1EDF);
                Map030(cpu, bus);
                cpu.SetA(7, descending ? 0x7FFCu : 0x7FF8u);
                bus.WriteByte(descending ? 0x7FFAu : 0x7FF8u, 0xA5);
                cpu.Step();
                Check.Equal(descending ? 0x7FF8u : 0x7FFCu, cpu.GetA(7), "Byte MOVE applies A7's two-byte stride twice.");
                Check.Equal((byte)0xA5, bus.ReadByte(descending ? 0x7FF8u : 0x7FFAu), "A7 alias copies to the ordered byte address.");
            }
        }

        private static void Test030SplitLongSourceRetry()
        {
            // MOVE.L (A0)+,D0 straddles a 4-KiB MMU page boundary. The first
            // word is readable, but the second word faults until the handler
            // maps its page. A retry must reconstruct the original longword
            // and advance the guest source register only once.
            foreach (bool deferred in new[] { false, true })
            {
                var (cpu, bus) = Prepare(AmigaCpuModel.M68030, 0x2018);
                Map030(cpu, bus);
                bus.WriteLong(0x500C, 0x9009);
                bus.WriteLong(0x5010, 0);
                bus.WriteWord(0x9FFE, 0xABCD);
                bus.WriteWord(0xA000, 0x1234);
                cpu.SetA(0, 0x3FFE);
                cpu.SetD(0, 0xDEADBEEF);
                bus.TrackedWordReadAddress = 0x9FFE;
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x2000; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x2000u, cpu.PC, "Second-page read enters bus error handler.");
                if (deferred)
                    for (int i = 0; i < 500 && bus.ReadLong(cpu.A7 + 16) != 0x4000; i++) cpu.Tick(true);
                Check.Equal(0x4000u, bus.ReadLong(cpu.A7 + 16), "Frame reports the second logical page.");
                Check.Equal(0x3FFEu, cpu.GetA(0), "Source address remains restartable.");
                Check.Equal(0xDEADBEEFu, cpu.D0, "Incomplete long read leaves D0 unchanged.");
                Check.Equal(0, bus.TrackedWordReadCount,
                    "A first-page device read cannot retire before the second-page fault.");
                bus.WriteLong(0x5010, 0xA009);
                cpu.MemoryManagementUnit.Flush();
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x1000; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x1000u, cpu.PC, "RTE returns to the split read.");
                cpu.Step();
                Check.Equal(0x1002u, cpu.PC, "Repaired MOVE completes.");
                Check.Equal(0xABCD1234u, cpu.D0, "Retry reads the original two source words.");
                Check.Equal(0x4002u, cpu.GetA(0), "Source increments once after the longword.");
                Check.Equal(1, bus.TrackedWordReadCount,
                    "The first physical source word is read exactly once after repair.");
            }
        }

        private static void Test030SplitLongRegisterWriteRetry()
        {
            // MOVE.L D0,(A0)+ crosses an MMU page. The second page is absent;
            // the first physical word must not be sent to a device twice when
            // the handler maps that page and RTE retries the instruction.
            foreach (bool deferred in new[] { false, true })
            {
                var (cpu, bus) = Prepare(AmigaCpuModel.M68030, 0x20C0);
                Map030(cpu, bus);
                bus.WriteLong(0x500C, 0x9009);
                bus.WriteLong(0x5010, 0);
                cpu.SetA(0, 0x3FFE);
                cpu.SetD(0, 0xCAFEBABE);
                bus.TrackedWordWriteAddress = 0x9FFE;
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x2000; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x2000u, cpu.PC, "Second-page write enters bus error handler.");
                if (deferred)
                    for (int i = 0; i < 500 && bus.ReadLong(cpu.A7 + 16) != 0x4000; i++) cpu.Tick(true);
                Check.Equal(0x4000u, bus.ReadLong(cpu.A7 + 16), "Frame reports second logical page.");
                Check.Equal(0x3FFEu, cpu.GetA(0), "Faulted destination postincrement is restartable.");
                Check.Equal(0, bus.TrackedWordWriteCount, "First physical word is held until both pages are writable.");
                Check.Equal(0u, bus.ReadLong(0x500C) & 0x10u,
                    "Prechecking the first page does not mark an unretired write modified.");
                bus.WriteLong(0x5010, 0xA009);
                cpu.MemoryManagementUnit.Flush();
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x1000; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x1000u, cpu.PC, "RTE returns to the faulting MOVE.");
                cpu.Step();
                Check.Equal(0x1002u, cpu.PC, "Repaired long write completes.");
                Check.Equal(0x4002u, cpu.GetA(0), "Destination advances once.");
                Check.Equal(0xCAFEBABEu, bus.ReadLong(0x9FFE),
                    "Original longword reaches both physical pages.");
                Check.Equal(1, bus.TrackedWordWriteCount, "First physical word is issued exactly once.");
            }
        }

        private static void Test030MovemPostincrementRetry()
        {
            // MOVEM.L (A0)+,D0-D1 reads a mapped long then faults on the
            // next page. A retry after RTE must preserve both original guest
            // registers and the original source address until all translations
            // needed by the instruction can succeed.
            foreach (bool deferred in new[] { false, true })
            {
                var (cpu, bus) = Prepare(AmigaCpuModel.M68030, 0x4CD8, 0x0003);
                Map030(cpu, bus);
                bus.WriteLong(0x500C, 0x9001);
                bus.WriteLong(0x5010, 0);
                bus.WriteLong(0x9FFC, 0x11223344);
                bus.WriteLong(0xA000, 0x55667788);
                cpu.SetA(0, 0x3FFC);
                cpu.SetD(0, 0xA0A0A0A0);
                cpu.SetD(1, 0xB1B1B1B1);
                bus.TrackedWordReadAddress = 0x9FFC;
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x2000; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x2000u, cpu.PC, "Second MOVEM operand enters bus error handler.");
                if (deferred)
                    for (int i = 0; i < 500 && bus.ReadLong(cpu.A7 + 16) != 0x4000; i++) cpu.Tick(true);
                Check.Equal(0x4000u, bus.ReadLong(cpu.A7 + 16), "Frame reports the later logical page.");
                Check.Equal(0x3FFCu, cpu.GetA(0), "MOVEM postincrement has not committed.");
                Check.Equal(0xA0A0A0A0u, cpu.D0, "First D register remains unchanged at the fault.");
                Check.Equal(0xB1B1B1B1u, cpu.GetD(1), "Second D register remains unchanged at the fault.");
                Check.Equal(0, bus.TrackedWordReadCount, "No first-page data read retires before the later translation succeeds.");
                Check.Equal(0u, bus.ReadLong(0x500C) & 8u,
                    "A speculative first-page check does not set its used bit.");
                bus.WriteLong(0x5010, 0xA009);
                cpu.MemoryManagementUnit.Flush();
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x1000; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x1000u, cpu.PC, "RTE retries MOVEM from its original PC.");
                cpu.Step();
                Check.Equal(0x1004u, cpu.PC, "Repaired MOVEM completes.");
                Check.Equal(0x11223344u, cpu.D0, "First register receives the original longword.");
                Check.Equal(0x55667788u, cpu.GetD(1), "Second register receives the later longword.");
                Check.Equal(0x4004u, cpu.GetA(0), "Source advances by both longwords once.");
                Check.Equal(1, bus.TrackedWordReadCount, "First-page data read retires exactly once.");
            }
        }

        private static void Test030MovemExtendedEaReadRetry()
        {
            // Each form reads one mapped longword and then encounters an
            // unmapped page. The first destination register must not change
            // before a whole-instruction retry from the format-B fault frame.
            foreach (ushort[] code in new[] {
                new ushort[] { 0x4CD0, 0x0003 },
                new ushort[] { 0x4CE8, 0x0003, 0x0000 },
                new ushort[] { 0x4CF9, 0x0003, 0x0000, 0x3FFC } })
            foreach (bool deferred in new[] { false, true })
            {
                var (cpu, bus) = Prepare(AmigaCpuModel.M68030, code);
                Map030(cpu, bus);
                bus.WriteLong(0x500C, 0x9001);
                bus.WriteLong(0x5010, 0);
                bus.WriteLong(0x9FFC, 0x11223344);
                bus.WriteLong(0xA000, 0x55667788);
                cpu.SetA(0, 0x3FFC);
                cpu.SetD(0, 0xA0A0A0A0);
                cpu.SetD(1, 0xB1B1B1B1);
                bus.TrackedWordReadAddress = 0x9FFC;
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x2000; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x2000u, cpu.PC, "Later MOVEM source enters bus error handler.");
                Check.Equal(0x3FFCu, cpu.GetA(0), "MOVEM source address has not advanced.");
                Check.Equal(0xA0A0A0A0u, cpu.D0, "Earlier destination register remains unchanged.");
                Check.Equal(0xB1B1B1B1u, cpu.GetD(1), "Later destination register remains unchanged.");
                Check.Equal(0, bus.TrackedWordReadCount, "First mapped data word was not read before the fault.");
                bus.WriteLong(0x5010, 0xA009);
                cpu.MemoryManagementUnit.Flush();
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x1000; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x1000u, cpu.PC, "RTE retries the complete MOVEM.");
                cpu.Step();
                Check.Equal(0x1000u + (uint)(code.Length * 2), cpu.PC, "Repaired MOVEM completes.");
                Check.Equal(0x11223344u, cpu.D0, "First destination receives its source longword.");
                Check.Equal(0x55667788u, cpu.GetD(1), "Second destination receives its source longword.");
                Check.Equal(1, bus.TrackedWordReadCount, "First mapped data word is read once.");
            }
        }

        private static void Test030MovemRegisterWriteRetry()
        {
            // MOVEM.L D0-D1,(A0) writes a mapped long before the second
            // destination page faults. A whole-instruction RTE retry must
            // not issue the first physical word to a device twice.
            foreach (bool deferred in new[] { false, true })
            {
                var (cpu, bus) = Prepare(AmigaCpuModel.M68030, 0x48D0, 0x0003);
                Map030(cpu, bus);
                bus.WriteLong(0x500C, 0x9009);
                bus.WriteLong(0x5010, 0);
                cpu.SetA(0, 0x3FFC);
                cpu.SetD(0, 0x11223344);
                cpu.SetD(1, 0x55667788);
                bus.TrackedWordWriteAddress = 0x9FFC;
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x2000; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x2000u, cpu.PC, "Later MOVEM destination enters bus error handler.");
                if (deferred)
                    for (int i = 0; i < 500 && bus.ReadLong(cpu.A7 + 16) != 0x4000; i++) cpu.Tick(true);
                Check.Equal(0x4000u, bus.ReadLong(cpu.A7 + 16), "Frame reports the later logical page.");
                Check.Equal(0, bus.TrackedWordWriteCount, "No first-page word is written before all translations succeed.");
                Check.Equal(0u, bus.ReadLong(0x9FFC), "First longword has not changed physical memory.");
                Check.Equal(0u, bus.ReadLong(0x500C) & 0x10u,
                    "An unretired first-page write does not mark its descriptor modified.");
                if (!deferred)
                {
                    using var state = new MemoryStream();
                    cpu.SaveState(new BinaryWriter(state));
                    cpu.Reset();
                    state.Position = 0;
                    cpu.LoadState(new BinaryReader(state));
                    Check.Equal(0x2000u, cpu.PC, "A fault frame survives CPU save/restore.");
                    Check.Equal(0, bus.TrackedWordWriteCount, "Restore does not replay a data write.");
                }
                bus.WriteLong(0x5010, 0xA009);
                cpu.MemoryManagementUnit.Flush();
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x1000; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x1000u, cpu.PC, "RTE returns to the original MOVEM.");
                cpu.Step();
                Check.Equal(0x1004u, cpu.PC, "Repaired MOVEM completes.");
                Check.Equal(0x11223344u, bus.ReadLong(0x9FFC), "First longword reaches the first page.");
                Check.Equal(0x55667788u, bus.ReadLong(0xA000), "Second longword reaches the second page.");
                Check.Equal(1, bus.TrackedWordWriteCount, "First physical word is issued only once.");
            }
        }

        private static void Test030MovemPredecrementWriteRetry()
        {
            // MOVEM.L D0-D1,-(A0): D1 goes to mapped $4000, then D0
            // faults on unmapped $3FFC. Retrying must not write D1 twice.
            foreach (bool deferred in new[] { false, true })
            {
                var (cpu, bus) = Prepare(AmigaCpuModel.M68030, 0x48E0, 0xC000);
                Map030(cpu, bus);
                bus.WriteLong(0x500C, 0);
                bus.WriteLong(0x5010, 0xA009);
                cpu.SetA(0, 0x4004);
                cpu.SetD(0, 0x11223344);
                cpu.SetD(1, 0x55667788);
                bus.TrackedWordWriteAddress = 0xA000;
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x2000; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x2000u, cpu.PC, "Later predecrement destination enters bus error handler.");
                Check.Equal(0x4004u, cpu.GetA(0), "Faulted predecrement leaves its base unchanged.");
                Check.Equal(0, bus.TrackedWordWriteCount, "No first-page write precedes a later fault.");
                Check.Equal(0u, bus.ReadLong(0xA000), "First destination remains unmodified.");
                Check.Equal(0u, bus.ReadLong(0x5010) & 0x10u,
                    "Unretired predecrement does not mark the first descriptor modified.");
                if (!deferred)
                {
                    using var state = new MemoryStream();
                    cpu.SaveState(new BinaryWriter(state));
                    cpu.Reset();
                    state.Position = 0;
                    cpu.LoadState(new BinaryReader(state));
                    Check.Equal(0x2000u, cpu.PC, "Predecrement fault frame survives CPU save/restore.");
                }
                bus.WriteLong(0x500C, 0x9009);
                cpu.MemoryManagementUnit.Flush();
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x1000; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x1000u, cpu.PC, "RTE returns to predecrement MOVEM.");
                cpu.Step();
                Check.Equal(0x1004u, cpu.PC, "Repaired predecrement MOVEM completes.");
                Check.Equal(0x3FFCu, cpu.GetA(0), "Predecrement commits its final base.");
                Check.Equal(0x55667788u, bus.ReadLong(0xA000), "D1 reaches the first page.");
                Check.Equal(0x11223344u, bus.ReadLong(0x9FFC), "D0 reaches the second page.");
                Check.Equal(1, bus.TrackedWordWriteCount, "First physical word is issued only once.");
            }
        }

        private static void Test030MovemExtendedEaWriteRetry()
        {
            // The first long is writable at logical $3FFC; the second
            // destination at $4000 faults. Both extension-word EAs must
            // retry without reissuing the first physical device word.
            foreach (bool absolute in new[] { false, true })
            foreach (bool deferred in new[] { false, true })
            {
                ushort[] code = absolute
                    ? new ushort[] { 0x48F9, 0x0003, 0x0000, 0x3FFC }
                    : new ushort[] { 0x48E8, 0x0003, 0x0000 };
                var (cpu, bus) = Prepare(AmigaCpuModel.M68030, code);
                Map030(cpu, bus);
                bus.WriteLong(0x500C, 0x9009);
                bus.WriteLong(0x5010, 0);
                cpu.SetA(0, 0x3FFC);
                cpu.SetD(0, 0x11223344);
                cpu.SetD(1, 0x55667788);
                bus.TrackedWordWriteAddress = 0x9FFC;
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x2000; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x2000u, cpu.PC, "Extended-EA MOVEM enters bus error handler.");
                Check.Equal(0, bus.TrackedWordWriteCount, "First long is not written before later fault.");
                Check.Equal(0u, bus.ReadLong(0x9FFC), "First physical destination is untouched.");
                bus.WriteLong(0x5010, 0xA009);
                cpu.MemoryManagementUnit.Flush();
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x1000; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x1000u, cpu.PC, "RTE retries extended-EA MOVEM.");
                cpu.Step();
                Check.Equal(absolute ? 0x1008u : 0x1006u, cpu.PC, "Repaired MOVEM completes.");
                Check.Equal(0x11223344u, bus.ReadLong(0x9FFC), "First long reaches memory.");
                Check.Equal(0x55667788u, bus.ReadLong(0xA000), "Second long reaches memory.");
                Check.Equal(1, bus.TrackedWordWriteCount, "First physical word is issued once.");
            }
        }

        private static void Test030CachedSplitMovePageRetry()
        {
            foreach (bool write in new[] { false, true })
            foreach (bool deferred in new[] { false, true })
            {
                // MOVEC D7,CACR enables the 030 data cache before the mapped
                // MOVE.L D0,(A0) / MOVE.L (A0),D0 crosses into page $4000.
                var (cpu, bus) = Prepare(AmigaCpuModel.M68030, 0x4E7B, 0x7002,
                    write ? (ushort)0x2080 : (ushort)0x2010);
                cpu.SetD(7, 0x900); cpu.Step();
                Check.Equal(0x1004u, cpu.PC, "CACR setup completes before the faulting MOVE.");
                Map030(cpu, bus);
                bus.WriteLong(0x500C, write ? 0x9009u : 0x9001u);
                bus.WriteLong(0x5010, 0);
                bus.WriteWord(0x9FFE, 0xABCD);
                bus.WriteWord(0xA000, 0x1234);
                cpu.SetA(0, 0x3FFE);
                cpu.SetD(0, write ? 0x11223344u : 0xDEADBEEFu);
                bus.TrackedWordReadAddress = 0x9FFC;
                bus.TrackedWordWriteAddress = 0x9FFE;
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x2000; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x2000u, cpu.PC, "Cached split MOVE faults on its second page.");
                Check.Equal(0x3FFEu, cpu.GetA(0), "Fault leaves the source/destination address unchanged.");
                Check.Equal(write ? 0x11223344u : 0xDEADBEEFu, cpu.D0, "Fault leaves D0 unchanged.");
                Check.Equal(0, bus.TrackedWordReadCount, "No first-page cache fill precedes a later fault.");
                Check.Equal(0, bus.TrackedWordWriteCount, "No first-page write precedes a later fault.");
                bus.WriteLong(0x5010, write ? 0xA009u : 0xA001u);
                cpu.MemoryManagementUnit.Flush();
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x1004; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x1004u, cpu.PC, "RTE retries the cached split MOVE.");
                cpu.Step();
                Check.Equal(0x1006u, cpu.PC, "Repaired cached MOVE completes.");
                if (write)
                {
                    Check.Equal(0x11223344u, bus.ReadLong(0x9FFE), "Original D0 reaches both pages.");
                    Check.Equal(1, bus.TrackedWordWriteCount, "First physical write retires once.");
                }
                else
                {
                    Check.Equal(0xABCD1234u, cpu.D0, "Cached MOVE reconstructs the source longword.");
                    Check.Equal(1, bus.TrackedWordReadCount, "First physical cache fill retires once.");
                }
            }
        }

        private static void Test030OddSingleMovePageRetry()
        {
            foreach (bool longTransfer in new[] { false, true })
            foreach (bool write in new[] { false, true })
            foreach (bool deferred in new[] { false, true })
            {
                ushort opcode = write ? (longTransfer ? (ushort)0x2080 : (ushort)0x3080) :
                    (longTransfer ? (ushort)0x2010 : (ushort)0x3010);
                var (cpu, bus) = Prepare(AmigaCpuModel.M68030, opcode);
                Map030(cpu, bus);
                bus.WriteLong(0x500C, write ? 0x9009u : 0x9001u);
                bus.WriteLong(0x5010, 0);
                uint address = longTransfer ? 0x3FFDu : 0x3FFFu;
                uint firstPhysicalByte = longTransfer ? 0x9FFDu : 0x9FFFu;
                bus.WriteByte(firstPhysicalByte, longTransfer ? (byte)0x11 : (byte)0x12);
                if (longTransfer)
                {
                    bus.WriteByte(0x9FFE, 0x22);
                    bus.WriteByte(0x9FFF, 0x33);
                }
                bus.WriteByte(0xA000, 0x44);
                cpu.SetA(0, address);
                cpu.SetD(0, write ? 0x11223344u : 0xDEADBEEFu);
                bus.TrackedByteReadAddress = firstPhysicalByte;
                bus.TrackedByteWriteAddress = firstPhysicalByte;
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x2000; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x2000u, cpu.PC, "Odd MOVE faults on its final unmapped byte.");
                if (deferred)
                    for (int i = 0; i < 500 && bus.ReadLong(cpu.A7 + 16) != 0x4000; i++) cpu.Tick(true);
                Check.Equal(0x4000u, bus.ReadLong(cpu.A7 + 16), "Frame identifies the last byte's page.");
                Check.Equal(address, cpu.GetA(0), "MOVE leaves its address register unchanged on fault.");
                Check.Equal(write ? 0x11223344u : 0xDEADBEEFu, cpu.D0, "Fault leaves D0 unchanged.");
                Check.Equal(0, bus.TrackedByteReadCount, "No first physical data read precedes a later fault.");
                Check.Equal(0, bus.TrackedByteWriteCount, "No first physical data write precedes a later fault.");
                bus.WriteLong(0x5010, write ? 0xA009u : 0xA001u);
                cpu.MemoryManagementUnit.Flush();
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x1000; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x1000u, cpu.PC, "RTE retries the original MOVE.");
                cpu.Step();
                Check.Equal(0x1002u, cpu.PC, "Repaired MOVE completes.");
                if (write)
                {
                    Check.Equal(longTransfer ? (byte)0x11 : (byte)0x33, bus.PeekByte(firstPhysicalByte),
                        "First source byte reaches the mapped page.");
                    Check.Equal((byte)0x44, bus.PeekByte(0xA000), "Last source byte reaches repaired page.");
                    Check.Equal(1, bus.TrackedByteWriteCount, "First physical write retires exactly once.");
                }
                else
                {
                    Check.Equal(longTransfer ? 0x11223344u : 0xDEAD1244u, cpu.D0,
                        "Odd MOVE reconstructs its operand after retry.");
                    Check.Equal(1, bus.TrackedByteReadCount, "First physical read retires exactly once.");
                }
            }
        }

        private static void Test030OddMovemPageRetry()
        {
            // On a 68030 an odd word is two bytes; an odd long is byte,
            // aligned word, byte. The last byte here is on the next MMU page.
            foreach (bool longTransfer in new[] { false, true })
            foreach (bool write in new[] { false, true })
            foreach (bool deferred in new[] { false, true })
            {
                ushort opcode = write ? (longTransfer ? (ushort)0x48D0 : (ushort)0x4890) :
                    (longTransfer ? (ushort)0x4CD0 : (ushort)0x4C90);
                var (cpu, bus) = Prepare(AmigaCpuModel.M68030, opcode, 0x0001);
                Map030(cpu, bus);
                bus.WriteLong(0x500C, write ? 0x9009u : 0x9001u);
                bus.WriteLong(0x5010, 0);
                uint address = longTransfer ? 0x3FFDu : 0x3FFFu;
                uint firstPhysicalByte = longTransfer ? 0x9FFDu : 0x9FFFu;
                bus.WriteByte(firstPhysicalByte, longTransfer ? (byte)0x11 : (byte)0x12);
                if (longTransfer)
                {
                    bus.WriteByte(0x9FFE, 0x22);
                    bus.WriteByte(0x9FFF, 0x33);
                }
                bus.WriteByte(0xA000, longTransfer ? (byte)0x44 : (byte)0x34);
                cpu.SetA(0, address);
                cpu.SetD(0, 0x11223344);
                bus.TrackedByteReadAddress = firstPhysicalByte;
                bus.TrackedByteWriteAddress = firstPhysicalByte;
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x2000; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x2000u, cpu.PC, "Odd MOVEM enters the bus-error handler at the final byte.");
                if (deferred)
                    for (int i = 0; i < 500 && bus.ReadLong(cpu.A7 + 16) != 0x4000; i++) cpu.Tick(true);
                Check.Equal(0x4000u, bus.ReadLong(cpu.A7 + 16), "Fault frame names the untranslatable last byte.");
                Check.Equal(address, cpu.GetA(0), "Odd MOVEM has not changed its base register.");
                Check.Equal(0, bus.TrackedByteReadCount, "No first-page device read retires before the last-byte fault.");
                Check.Equal(0, bus.TrackedByteWriteCount, "No first-page device write retires before the last-byte fault.");
                Check.Equal(longTransfer ? (byte)0x11 : (byte)0x12, bus.PeekByte(firstPhysicalByte),
                    "The first physical byte remains intact at the fault.");
                bus.WriteLong(0x5010, write ? 0xA009u : 0xA001u);
                cpu.MemoryManagementUnit.Flush();
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x1000; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x1000u, cpu.PC, "RTE retries the odd MOVEM from its opcode.");
                cpu.Step();
                Check.Equal(0x1004u, cpu.PC, "Repaired odd MOVEM completes.");
                if (write)
                {
                    Check.Equal(longTransfer ? (byte)0x11 : (byte)0x33, bus.PeekByte(firstPhysicalByte),
                        "The first source byte reaches the first physical page.");
                    Check.Equal((byte)0x44, bus.PeekByte(0xA000),
                        "The final source byte reaches the repaired second page.");
                    Check.Equal(1, bus.TrackedByteWriteCount, "First physical device write retires once.");
                }
                else
                {
                    Check.Equal(longTransfer ? 0x11223344u : 0x1234u, cpu.D0,
                        "Odd MOVEM assembles the complete source value.");
                    Check.Equal(1, bus.TrackedByteReadCount, "First physical device read retires once.");
                }
            }
        }

        private static void Test030OddMovemPredecrementPageRetry()
        {
            foreach (bool longTransfer in new[] { false, true })
            foreach (bool deferred in new[] { false, true })
            {
                var (cpu, bus) = Prepare(AmigaCpuModel.M68030,
                    longTransfer ? (ushort)0x48E0 : (ushort)0x48A0, 0x8000);
                Map030(cpu, bus);
                bus.WriteLong(0x500C, 0x9009);
                bus.WriteLong(0x5010, 0);
                uint firstPhysicalByte = longTransfer ? 0x9FFDu : 0x9FFFu;
                bus.WriteByte(firstPhysicalByte, 0xAA);
                cpu.SetA(0, 0x4001);
                cpu.SetD(0, 0x11223344);
                bus.TrackedByteWriteAddress = firstPhysicalByte;
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x2000; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x2000u, cpu.PC, "Odd predecrement enters the bus-error handler.");
                Check.Equal(0x4001u, cpu.GetA(0), "Fault leaves the predecrement base unchanged.");
                Check.Equal(0, bus.TrackedByteWriteCount, "No first-page byte is written before the last-byte fault.");
                Check.Equal((byte)0xAA, bus.PeekByte(firstPhysicalByte), "First device byte is intact at the fault.");
                bus.WriteLong(0x5010, 0xA009);
                cpu.MemoryManagementUnit.Flush();
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x1000; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x1000u, cpu.PC, "RTE retries odd predecrement MOVEM.");
                cpu.Step();
                Check.Equal(0x1004u, cpu.PC, "Repaired odd predecrement completes.");
                Check.Equal(longTransfer ? 0x3FFDu : 0x3FFFu, cpu.GetA(0),
                    "Predecrement base commits only after the complete transfer.");
                Check.Equal(longTransfer ? (byte)0x11 : (byte)0x33, bus.PeekByte(firstPhysicalByte),
                    "First source byte reaches the mapped page.");
                Check.Equal((byte)0x44, bus.PeekByte(0xA000), "Last source byte reaches the repaired page.");
                Check.Equal(1, bus.TrackedByteWriteCount, "First physical device byte is written once.");
            }
        }

        private static void Test030MovepPageRetry()
        {
            // MOVEP.L accesses $3FFC,$3FFE,$4000,$4002 rather than a
            // contiguous longword. A later fault must be detected before a
            // side-effecting device sees either first-page byte.
            foreach (bool longTransfer in new[] { false, true })
            foreach (bool write in new[] { false, true })
            foreach (bool deferred in new[] { false, true })
            {
                var (cpu, bus) = Prepare(AmigaCpuModel.M68030,
                    write ? (longTransfer ? (ushort)0x01C8 : (ushort)0x0188) :
                        (longTransfer ? (ushort)0x0148 : (ushort)0x0108), 0x0000);
                Map030(cpu, bus);
                bus.WriteLong(0x500C, 0x9001);
                bus.WriteLong(0x5010, 0);
                bus.WriteByte(0x9FFC, 0x11);
                bus.WriteByte(0x9FFE, 0x22);
                bus.WriteByte(0xA000, 0x33);
                bus.WriteByte(0xA002, 0x44);
                cpu.SetA(0, longTransfer ? 0x3FFCu : 0x3FFEu);
                cpu.SetD(0, 0xAABBCCDD);
                uint firstPhysicalByte = longTransfer ? 0x9FFCu : 0x9FFEu;
                bus.TrackedByteReadAddress = firstPhysicalByte;
                bus.TrackedByteWriteAddress = firstPhysicalByte;
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x2000; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x2000u, cpu.PC, "Third MOVEP byte enters the bus-error handler.");
                Check.Equal(0xAABBCCDDu, cpu.D0, "MOVEP destination is unchanged before retry.");
                Check.Equal(0, bus.TrackedByteReadCount, "First physical device byte is not read before a later fault.");
                Check.Equal(0, bus.TrackedByteWriteCount, "First physical device byte is not written before a later fault.");
                Check.Equal(0u, bus.ReadLong(0x500C) & 24u,
                    "Speculative MOVEP probes do not mark the earlier page used or modified.");
                bus.WriteLong(0x5010, 0xA009);
                cpu.MemoryManagementUnit.Flush();
                if (deferred)
                    for (int i = 0; i < 500 && cpu.PC != 0x1000; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x1000u, cpu.PC, "RTE retries the complete MOVEP instruction.");
                cpu.Step();
                Check.Equal(0x1004u, cpu.PC, "Repaired MOVEP completes.");
                if (write)
                {
                    Check.Equal(longTransfer ? (byte)0xAA : (byte)0xCC,
                        bus.PeekByte(firstPhysicalByte), "First spaced destination byte.");
                    Check.Equal(longTransfer ? (byte)0xBB : (byte)0xDD,
                        bus.PeekByte(longTransfer ? 0x9FFEu : 0xA000u), "Second spaced destination byte.");
                    if (longTransfer)
                    {
                        Check.Equal((byte)0xCC, bus.PeekByte(0xA000), "Third spaced destination byte.");
                        Check.Equal((byte)0xDD, bus.PeekByte(0xA002), "Fourth spaced destination byte.");
                    }
                    Check.Equal(1, bus.TrackedByteWriteCount, "First physical device byte is written once.");
                }
                else
                {
                    Check.Equal(longTransfer ? 0x11223344u : 0xAABB2233u, cpu.D0,
                        "MOVEP assembles the spaced source bytes.");
                    Check.Equal(1, bus.TrackedByteReadCount, "First physical device byte is read once.");
                }
            }
        }

        private static void Test030LockedProtection()
        {
            foreach (bool deferred in new[] { false, true })
            foreach (ushort[] code in new[] { new ushort[] { 0x4AD0 }, new ushort[] { 0x0CD0, 0x0040 },
                new ushort[] { 0x0CFC, 0x8080, 0x90C1 } })
            {
                var (cpu, bus) = Prepare(AmigaCpuModel.M68030, code);
                Map030(cpu, bus);
                bus.WriteLong(0x500C, 0x9005); // Logical 3000 maps to read-only 9000.
                bus.WriteWord(0x9000, 0x1234);
                cpu.SetA(0, 0x3000); cpu.SetA(1, 0x3002);
                cpu.SetD(0, 0xDEADBEEF); cpu.SetD(1, 0xAABBCCDD);
                if (deferred) for (int i = 0; i < 200 &&
                    (bus.ReadLong(0x7FE2) != 0x1000 || bus.ReadWord(0x7FE0) != 0x2700); i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x2000u, cpu.PC, "Write-protected locked read enters vector 2 even when CAS comparison would fail.");
                Check.Equal(0x1000u, bus.ReadLong(cpu.A7 + 2), "Exception points at the uncompleted atomic instruction.");
                Check.Equal((ushort)0x2700, bus.ReadWord(cpu.A7), "Comparison/TAS flags did not change before protection was checked.");
                Check.Equal((ushort)(code[0] == 0x4AD0 ? 0x1D5 : 0x1E5), bus.ReadWord(cpu.A7 + 10), "SSW marks the faulting RMW read, transfer size and supervisor FC.");
                Check.Equal(0xDEADBEEFu, cpu.D0, "A failed protected CAS does not update its compare register.");
                Check.Equal(0xAABBCCDDu, cpu.GetD(1), "Second compare register remains intact.");
                Check.Equal((ushort)0x1234, bus.ReadWord(0x9000), "Protected memory remains intact.");
                // Repair the mapping, then RTE and retry to prove the temporary
                // locked-read flag did not leak into exception stacking or RTE.
                bus.WriteLong(0x500C, 0x9001); cpu.MemoryManagementUnit.Flush();
                if (deferred) for (int i = 0; i < 500 && cpu.PC != 0x1000; i++) cpu.Tick(true);
                else cpu.Step();
                Check.Equal(0x1000u, cpu.PC, "RTE returns to the faulting instruction.");
                cpu.Step();
                Check.Equal(code[0] == 0x4AD0 ? 0x1002u : code[0] == 0x0CD0 ? 0x1004u : 0x1006u,
                    cpu.PC, "Atomic instruction completes after write protection is removed.");
            }
        }

        private sealed class TestBus : IM68kPeekableBus, IM68kBusArbitration
        {
            private readonly Dictionary<uint, byte> _memory = new();
            public uint TrackedWordWriteAddress { get; set; } = uint.MaxValue;
            public int TrackedWordWriteCount { get; private set; }
            public uint TrackedWordReadAddress { get; set; } = uint.MaxValue;
            public int TrackedWordReadCount { get; private set; }
            public uint TrackedByteReadAddress { get; set; } = uint.MaxValue;
            public int TrackedByteReadCount { get; private set; }
            public uint TrackedByteWriteAddress { get; set; } = uint.MaxValue;
            public int TrackedByteWriteCount { get; private set; }
            public byte ReadByte(uint address) { if (address == TrackedByteReadAddress) TrackedByteReadCount++; return PeekByte(address); }
            public ushort ReadWord(uint address) { if (address == TrackedWordReadAddress) TrackedWordReadCount++; return PeekWord(address); }
            public uint ReadLong(uint address) => ((uint)ReadWord(address) << 16) | ReadWord(unchecked(address + 2));
            public void WriteByte(uint address, byte value) { if (address == TrackedByteWriteAddress) TrackedByteWriteCount++; _memory[address] = value; }
            public void WriteWord(uint address, ushort value) { if (address == TrackedWordWriteAddress) TrackedWordWriteCount++; WriteByte(address, (byte)(value >> 8)); WriteByte(unchecked(address + 1), (byte)value); }
            public void WriteLong(uint address, uint value) { WriteWord(address, (ushort)(value >> 16)); WriteWord(unchecked(address + 2), (ushort)value); }
            public byte PeekByte(uint address) => _memory.TryGetValue(address, out byte b) ? b : (byte)0;
            public ushort PeekWord(uint address) => (ushort)((PeekByte(address) << 8) | PeekByte(unchecked(address + 1)));
            public uint PeekLong(uint address) => ((uint)PeekWord(address) << 16) | PeekWord(unchecked(address + 2));
            public bool RequiresChipBus(uint address, int size) => true;
        }
    }
}
