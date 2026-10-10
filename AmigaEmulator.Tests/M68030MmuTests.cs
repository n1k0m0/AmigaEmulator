// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using System.IO;
using AmigaEmulator.Core.Cpu;

namespace AmigaEmulator.Tests
{
    internal static class M68030MmuTests
    {
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return new ChipTestCase("CPU/030MMU", "two-level translation updates U/M, protects pages and retains/flushes ATC entries", "Motorola MC68030UM 9.5/9.7; independent tables", TestShortTables);
            yield return new ChipTestCase("CPU/030MMU", "long descriptors, root limits, supervisor tables and indirect pages use physical addresses", "Motorola MC68030UM descriptor formats", TestDescriptors);
            yield return new ChipTestCase("CPU/030MMU", "configuration errors, transparent FC/RW windows and cache restore remain distinct", "Motorola MC68030UM register programming", TestControl);
            yield return new ChipTestCase("CPU/030MMU", "cached invalid, limit and supervisor faults report ATC B/I rather than table-search status", "Motorola MC68030UM 9.4, 9.7.4 table 9-3", TestCachedFaultStatus);
            yield return new ChipTestCase("CPU/030MMU", "cached write protection is readable and PTEST table walks leave cached faults intact", "Motorola MC68030UM 9.4, 9.7.6", TestCachedProtectionAndRepair);
        }
        private static (M68030Mmu mmu, Dictionary<uint, uint> memory) Setup()
        {
            var memory = new Dictionary<uint, uint>();
            var mmu = new M68030Mmu(a => memory.TryGetValue(a, out uint value) ? value : 0, (a, v) => memory[a] = v);
            Check.True(mmu.SetTranslationControl(0x80C48800), "4K pages, four initial bits, two eight-bit indices.");
            Check.True(mmu.SetRoot(false, 0x7FFF000200001000), "Unlimited short root table.");
            memory[0x1048] = 0x2002; memory[0x20D0] = 0x500001;
            return (mmu, memory);
        }

        private static void TestCachedFaultStatus()
        {
            foreach (string fault in new[] { "invalid", "limit", "supervisor", "physical-read", "physical-update" })
            {
                var (mmu, mem) = Setup();
                if (fault == "invalid") mem[0x20D0] = 0;
                if (fault == "limit") mmu.SetRoot(false, 0x0011000200001000);
                if (fault == "supervisor")
                {
                    mmu.SetRoot(false, 0x7FFF000300001000);
                    mem[0x1090] = 0x7FFF0103; mem[0x1094] = 0x2000;
                }
                if (fault.StartsWith("physical-"))
                {
                    bool read = fault == "physical-read";
                    mmu = new M68030Mmu(a => read ? throw new IOException("table bus error") : mem.GetValueOrDefault(a),
                        (a, value) => throw new IOException("descriptor update bus error"));
                    mmu.SetTranslationControl(0x80C48800); mmu.SetRoot(false, 0x7FFF000200001000);
                }
                var walk = mmu.Translate(0x01234567, 1, false);
                Check.True(!walk.Accessible(false), $"{fault} blocks the initial access.");
                int relevant = M68030Mmu.BusError | M68030Mmu.Invalid | M68030Mmu.Limit | M68030Mmu.SupervisorOnly | 7;
                Check.Equal(M68030Mmu.BusError | M68030Mmu.Invalid,
                    mmu.Test(0x01234567, 1, false, 0).Flags & relevant,
                    $"{fault}: a resident failing ATC entry reports B/I and clears L/S/N.");
                using var state = new MemoryStream(); mmu.SaveState(new BinaryWriter(state));
                mmu.Flush(); state.Position = 0; mmu.LoadState(new BinaryReader(state));
                Check.Equal(M68030Mmu.BusError | M68030Mmu.Invalid,
                    mmu.Test(0x01234567, 1, true, 0).Flags & relevant,
                    $"{fault}: restored ATC retains its fault, independent of PTESTR/PTESTW.");
                Check.Equal(M68030Mmu.Invalid, mmu.Test(0x01234567, 2, false, 0).Flags,
                    "Another function code is an ATC miss, with I but without B.");
            }
        }

        private static void TestCachedProtectionAndRepair()
        {
            var (mmu, mem) = Setup(); mem[0x20D0] = 0x500005;
            Check.True(mmu.Translate(0x01234567, 1, false).Accessible(false), "WP permits ordinary reads.");
            Check.Equal(M68030Mmu.WriteProtected, mmu.Test(0x01234567, 1, true, 0).Flags,
                "PTESTW reports WP without inventing a cached bus error.");
            Check.True(!mmu.Translate(0x01234567, 1, true).Accessible(true), "WP still denies writes.");
            mmu.Flush(); mem[0x20D0] = 0; mmu.Load(0x01234567, 1, false);
            mem[0x20D0] = 0x600001;
            Check.Equal((ushort)2, mmu.Test(0x01234567, 1, false, 7).Flags,
                "PTEST table search sees the repaired mapping without altering U/M.");
            Check.Equal(0x600001u, mem[0x20D0], "PTEST does not update the repaired page descriptor.");
            Check.Equal((ushort)(M68030Mmu.BusError | M68030Mmu.Invalid), mmu.Test(0x01234567, 1, false, 0).Flags,
                "A table-search PTEST must not replace the resident fault.");
            Check.True(!mmu.Translate(0x01234567, 1, false).Accessible(false), "Access retains the fault until explicit invalidation.");
            Check.Equal(0x600567u, mmu.Load(0x01234567, 1, false).Physical, "PLOAD repairs the selected ATC entry.");
            Check.Equal((ushort)0, mmu.Test(0x01234567, 1, false, 0).Flags, "Repaired ATC reports no fault/depth.");
        }
        private static void TestShortTables()
        {
            var (mmu, mem) = Setup();
            Check.Equal(M68030Mmu.Invalid, mmu.Test(0x01234567, 1, false, true).Flags, "Empty ATC.");
            var result = mmu.Translate(0x01234567, 1, false);
            Check.Equal(0x500567u, result.Physical, "Logical address selects both tables and keeps page offset.");
            Check.True(result.Accessible(false), "Readable page.");
            Check.Equal(0x200Au, mem[0x1048], "Pointer U bit updated."); Check.Equal(0x500009u, mem[0x20D0], "Page U bit updated.");
            mem[0x20D0] = 0x600001;
            Check.Equal(0x500568u, mmu.Translate(0x01234568, 1, false).Physical, "ATC entry survives table changes.");
            mmu.Flush(2, 7, 0x01234567); Check.Equal(0x500567u, mmu.Translate(0x01234567, 1, false).Physical, "Different FC flush leaves entry.");
            mmu.Flush(1, 7, 0x01234567); Check.Equal(0x600567u, mmu.Translate(0x01234567, 1, true).Physical, "Matching FC/address flush exposes new page.");
            Check.Equal(0x600019u, mem[0x20D0], "First write sets M and U.");
            Check.True((mmu.Test(0x01234567, 1, false, true).Flags & M68030Mmu.Modified) != 0, "PTEST zero reports ATC modified flag.");
            mmu.Flush(); mem[0x1048] = 0x2006; mem[0x20D0] = 0x500001;
            result = mmu.Translate(0x01234567, 1, true);
            Check.True(result.Accessible(false) && !result.Accessible(true), "Write protection accrues from parent table.");
            Check.Equal(0x500009u, mem[0x20D0], "Denied write never sets M.");
        }
        private static void TestDescriptors()
        {
            var (mmu, mem) = Setup();
            mmu.SetRoot(false, 0x7FFF000300001000);
            mem[0x1090] = 0x7FFF0003; mem[0x1094] = 0x2000;
            mem[0x21A0] = 1; mem[0x21A4] = 0x700000;
            Check.Equal(0x700567u, mmu.Translate(0x01234567, 5, false).Physical, "Eight-byte descriptors.");
            mmu.Flush(); mem[0x1090] |= 0x100;
            var denied = mmu.Translate(0x01234567, 1, false);
            Check.True((denied.Flags & M68030Mmu.SupervisorOnly) != 0 && !denied.Accessible(false), "Long supervisor-only parent.");
            Check.Equal(0x700567u, mmu.Translate(0x01234567, 5, false).Physical, "Supervisor can still access.");
            mmu.SetRoot(false, 0x0011000200001000); // Limit below first index 0x12.
            Check.True((mmu.Translate(0x01234567, 1, false).Flags & M68030Mmu.Limit) != 0, "Upper root limit.");
            mmu.SetRoot(false, 0x8013000200001000);
            Check.True((mmu.Translate(0x01234567, 1, false).Flags & M68030Mmu.Limit) != 0, "Lower root limit.");
            mmu.SetRoot(false, 0x7FFF000200001000); mem[0x1048] = 0x2002;
            mem[0x20D0] = 0x3006; mem[0x3004] = 0x800001;
            Check.Equal(0x800567u, mmu.Translate(0x01234567, 1, false).Physical, "Indirect descriptor retains address bit two.");
            Check.Equal(0x3006u, mem[0x20D0], "Indirect pointer has no used/write-protect fields.");
            mmu.SetRoot(true, 0x7FFF000100900000); mmu.SetTranslationControl(0x82C48800);
            Check.Equal(0x01B34567u, mmu.Translate(0x01234567, 5, false).Physical, "Supervisor root page applies a full-address offset.");
            mmu.Flush(); mem[0x1048] = 0xA00001;
            Check.Equal(0xA34567u, mmu.Translate(0x01234567, 1, false).Physical, "Early page termination adds remaining logical bits.");
            var faulting = new M68030Mmu(_ => throw new IOException("physical bus error"), (_, _) => { });
            faulting.SetRoot(false, 0x7FFF000200001000); faulting.SetTranslationControl(0x80C48800);
            Check.True((faulting.Translate(0x01234567, 1, false).Flags & M68030Mmu.BusError) != 0, "Table read error becomes MMU status.");
        }
        private static void TestControl()
        {
            var (mmu, mem) = Setup();
            Check.True(!mmu.SetTranslationControl(0x80748800), "Reserved page sizes raise configuration error.");
            Check.True((mmu.TranslationControl & 0x80000000) == 0, "Configuration error disables translation.");
            Check.Equal(0x12345678u, mmu.Translate(0x12345678, 1, false).Physical, "Disabled translation is identity.");
            Check.True(!mmu.SetRoot(false, 0), "Invalid root requests configuration exception.");
            mmu.SetRoot(false, 0x7FFF000200001000); mmu.SetTranslationControl(0x80C48800);
            mmu.SetTransparent(0, 0x12008210); // 0x12xxxxxx, data FC1, read-only window.
            Check.Equal(M68030Mmu.Transparent, mmu.Translate(0x12345678, 1, false).Flags, "FC/read window bypasses page tables.");
            Check.True((mmu.Translate(0x12345678, 2, false).Flags & M68030Mmu.Transparent) == 0, "Program FC not matched.");
            Check.True((mmu.Translate(0x12345678, 1, true).Flags & M68030Mmu.Transparent) == 0, "Write not matched.");
            Check.Equal(0xFE123456u, mmu.Translate(0xFE123456, 7, true).Physical, "CPU-space bypass.");
            mmu.Translate(0x01234567, 1, false);
            using var state = new MemoryStream(); mmu.SaveState(new BinaryWriter(state));
            mem[0x20D0] = 0xB00001; mmu.Flush(); state.Position = 0; mmu.LoadState(new BinaryReader(state));
            Check.Equal(0x500567u, mmu.Translate(0x01234567, 1, false).Physical, "Save/restore retains cached old translation.");
            mmu.Reset(); Check.Equal(0x12345678u, mmu.Translate(0x12345678, 1, false).Physical, "External reset disables windows and translation.");
            mmu.SetTranslationControl(0x80C48800, false);
            Check.Equal(0x500567u, mmu.Translate(0x01234567, 1, false).Physical, "Reset retains ATC.");
        }
    }
}
