// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using AmigaEmulator.Core.CustomChips;
using AmigaEmulator.Core.Memory;

namespace AmigaEmulator.Tests
{
    internal static class BlitterDiagnosticsTests
    {
        public static IEnumerable<ChipTestCase> Build()
        {
            foreach (bool line in new[] { false, true })
            {
                bool lineMode = line;
                string mode = line ? "line" : "block";
                yield return new ChipTestCase("Blitter/Diagnostics", mode + " DMA without tracing avoids per-word allocations",
                    "Allocation regression; independent A-to-D copy / horizontal line fixtures", () => TestAllocation(lineMode));
                yield return new ChipTestCase("Blitter/Diagnostics", mode + " trace entries retain independent results and can unsubscribe",
                    "Commodore HRM Blitter chapter; diagnostic event contract", () => TestTrace(lineMode));
            }
        }

        private static void TestAllocation(bool line)
        {
            var fixture = new Fixture();
            for (int i = 0; i < 20; i++) fixture.Run(line);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++) fixture.Run(line);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Check.True(bytes <= 1024, "Unobserved DMA must not allocate per word/pixel: " + bytes + " bytes.");
            fixture.Verify(line);
        }

        private static void TestTrace(bool line)
        {
            var fixture = new Fixture();
            var entries = new List<BlitterWordTraceEntry>();
            Action<BlitterWordTraceEntry> handler = entries.Add;
            fixture.Blitter.WordExecuted += handler;
            fixture.Run(line);
            int count = line ? 3 : 1024;
            Check.Equal(count, entries.Count, "One snapshot per completed pixel/word.");
            for (int i = 0; i < count; i++)
            {
                var entry = entries[i];
                Check.Equal(line, entry.LineMode, "Trace mode.");
                Check.Equal(line ? i : i / 32, entry.Line, "Trace row/pixel.");
                Check.Equal(line ? 0 : i % 32, entry.Word, "Trace word.");
                Check.Equal(line ? (ushort)(0x8000 >> i) : Fixture.Source(i), entry.ShiftedA, "Source snapshot.");
                Check.Equal(line ? (ushort)(0xFFFF << (15 - i)) : Fixture.Source(i), entry.Result, "Minterm result.");
                if (i > 0) Check.True(!ReferenceEquals(entries[i - 1], entry), "Retained snapshots must be independent.");
            }
            fixture.Verify(line);
            fixture.Blitter.WordExecuted -= handler;
            fixture.Run(line);
            Check.Equal(count, entries.Count, "Unsubscribed handler receives no further entries.");
            fixture.Verify(line);
        }

        private sealed class Fixture
        {
            private readonly CustomRegisterMap _registers = new();
            private readonly ushort[] _memory = new ushort[0x80000 / 2];
            public readonly Blitter Blitter;

            public Fixture()
            {
                Blitter = new Blitter(_registers);
                Blitter.AttachChipMemory(address => _memory[(address & 0x7FFFE) >> 1],
                    (address, value) => _memory[(address & 0x7FFFE) >> 1] = value);
                for (int i = 0; i < 1024; i++) _memory[0x10000 / 2 + i] = Source(i);
            }

            public static ushort Source(int i) => (ushort)(0xA55A ^ (i * 37));

            private void Pointer(uint offset, uint value)
            {
                _registers.WriteWord(offset, (ushort)(value >> 16));
                _registers.WriteWord(offset + 2, (ushort)value);
            }

            public void Run(bool line)
            {
                Array.Clear(_memory, 0x20000 / 2, 1024);
                Pointer(0x050, line ? 0x8000u : 0x10000u);
                Pointer(0x048, 0x20000);
                Pointer(0x054, 0x20000);
                _registers.WriteWord(0x074, 0x8000);
                _registers.WriteWord(0x072, 0xFFFF);
                _registers.WriteWord(0x044, 0xFFFF);
                _registers.WriteWord(0x046, 0xFFFF);
                _registers.WriteWord(0x064, 0);
                _registers.WriteWord(0x062, 0);
                _registers.WriteWord(0x060, line ? (ushort)0x28 : (ushort)0);
                _registers.WriteWord(0x066, line ? (ushort)0x28 : (ushort)0);
                _registers.WriteWord(0x040, line ? (ushort)0x0BEA : (ushort)0x09F0);
                _registers.WriteWord(0x042, line ? (ushort)0x0051 : (ushort)0);
                _registers.WriteWord(0x058, line ? (ushort)((3 << 6) | 1) : (ushort)((32 << 6) | 32));
                Blitter.NotifyCustomRegisterWrite(0x058, 2);
                int ticks = 0;
                while (Blitter.DmaPending && ticks++ < 10000) Blitter.Tick(true);
                if (Blitter.DmaPending) throw new InvalidOperationException("DMA did not finish.");
            }

            public void Verify(bool line)
            {
                Check.True(!Blitter.ZeroFlag, "Nonzero output clears the zero flag.");
                if (line) Check.Equal((ushort)0xE000, _memory[0x20000 / 2], "Three horizontal pixels.");
                else for (int i = 0; i < 1024; i++)
                    Check.Equal(Source(i), _memory[0x20000 / 2 + i], "A-to-D copy word " + i);
            }
        }
    }
}
