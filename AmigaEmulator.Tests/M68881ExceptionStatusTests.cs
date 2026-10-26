// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using AmigaEmulator.Core.Cpu;

namespace AmigaEmulator.Tests
{
    internal static class M68881ExceptionStatusTests
    {
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return Test("documented EXC to AEXC table and exact underflow distinction", TestTable);
            yield return Test("all 65536 current and accrued byte combinations against set oracle", TestAccruedOracle);
            yield return Test("all 65536 enable and raised combinations select documented priority", TestPriorityOracle);
            yield return Test("instruction status replaces old EXC while accrued and upper bytes survive", TestSequence);
            yield return Test("existing flags and newly enabled traps do not replay old exceptions", TestNoRetrospectiveSelection);
        }
        private static ChipTestCase Test(string name, Action action) => new ChipTestCase(
            "CPU/FPU isolated exception status", name,
            "Motorola MC68881/MC68882 User's Manual 2.2.1/2.3.3/2.3.4; independent set/table oracle", action);

        private static readonly M68881ExceptionStatus[] Priority =
        {
            M68881ExceptionStatus.BranchOrSetOnUnordered, M68881ExceptionStatus.SignalingNaN,
            M68881ExceptionStatus.OperandError, M68881ExceptionStatus.Overflow,
            M68881ExceptionStatus.Underflow, M68881ExceptionStatus.DivideByZero,
            M68881ExceptionStatus.InexactOperation, M68881ExceptionStatus.InexactDecimalInput
        };
        private static HashSet<M68881ExceptionStatus> Decode(int value)
        {
            var set = new HashSet<M68881ExceptionStatus>();
            foreach (var flag in Priority) if ((value & (byte)flag) != 0) set.Add(flag);
            return set;
        }
        private static int AccruedOracle(int value)
        {
            var set = Decode(value);
            int output = 0;
            if (set.Contains(M68881ExceptionStatus.BranchOrSetOnUnordered) ||
                set.Contains(M68881ExceptionStatus.SignalingNaN) || set.Contains(M68881ExceptionStatus.OperandError)) output += 128;
            if (set.Contains(M68881ExceptionStatus.Overflow)) output += 64;
            if (set.Contains(M68881ExceptionStatus.Underflow) && set.Contains(M68881ExceptionStatus.InexactOperation)) output += 32;
            if (set.Contains(M68881ExceptionStatus.DivideByZero)) output += 16;
            if (set.Contains(M68881ExceptionStatus.InexactDecimalInput) || set.Contains(M68881ExceptionStatus.InexactOperation) ||
                set.Contains(M68881ExceptionStatus.Overflow)) output += 8;
            return output;
        }
        private static void TestTable()
        {
            var cases = new (byte Current, byte Accrued)[]
            {
                (0x00, 0x00), (0x80, 0x80), (0x40, 0x80), (0x20, 0x80),
                (0x10, 0x48), (0x08, 0x00), (0x04, 0x10), (0x02, 0x08), (0x01, 0x08),
                (0x09, 0x08), (0x0A, 0x28), (0x0B, 0x28), (0x12, 0x48),
                (0x41, 0x88), (0x22, 0x88), (0xFF, 0xF8)
            };
            foreach (var pair in cases)
            {
                var r = M68881ExceptionStatusPolicy.CompleteInstruction(0xA5C35A00, (M68881ExceptionStatus)pair.Current, 0);
                Check.Equal(0xA5C30000U | ((uint)pair.Current << 8) | pair.Accrued, r.StatusRegister, "Documented transition literal.");
                Check.Equal(M68881ExceptionStatus.None, r.HighestEnabledStatus, "All exception enables off.");
            }
        }
        private static void TestAccruedOracle()
        {
            for (int current = 0; current < 256; current++)
                for (int oldAccrued = 0; oldAccrued < 256; oldAccrued++)
                {
                    uint old = 0xD3A75A00U | (uint)oldAccrued;
                    int expectedLow = oldAccrued | AccruedOracle(current);
                    var r = M68881ExceptionStatusPolicy.CompleteInstruction(old, (M68881ExceptionStatus)current, 0);
                    Check.Equal(0xD3A70000U | ((uint)current << 8) | (uint)expectedLow, r.StatusRegister, "Independent set oracle over all current/accrued pairs.");
                    Check.Equal((uint)oldAccrued & 7, r.StatusRegister & 7, "Raw low reserved bits are not cleared by accrual.");
                }
        }
        private static void TestPriorityOracle()
        {
            for (int current = 0; current < 256; current++)
                for (int enabled = 0; enabled < 256; enabled++)
                {
                    var present = Decode(current);var permitted = Decode(enabled);var expected = M68881ExceptionStatus.None;
                    foreach (var flag in Priority) if (present.Contains(flag) && permitted.Contains(flag)) { expected = flag; break; }
                    var r = M68881ExceptionStatusPolicy.CompleteInstruction(0xBFFFFF, (M68881ExceptionStatus)current, (byte)enabled);
                    Check.Equal(expected, r.HighestEnabledStatus, "Independent ordered-set priority selection.");
                    Check.Equal((uint)current, (r.StatusRegister >> 8) & 255, "Enabling changes selection, not reported current status.");
                    var disabled = M68881ExceptionStatusPolicy.CompleteInstruction(0xBFFFFF, (M68881ExceptionStatus)current, 0);
                    Check.Equal(disabled.StatusRegister, r.StatusRegister, "Accrual is independent of enable byte.");
                }
        }
        private static void TestSequence()
        {
            uint state = 0xABCD0007;
            foreach (var entry in new[] { M68881ExceptionStatus.SignalingNaN, M68881ExceptionStatus.Overflow,
                M68881ExceptionStatus.Underflow, M68881ExceptionStatus.DivideByZero,
                M68881ExceptionStatus.Underflow | M68881ExceptionStatus.InexactOperation, M68881ExceptionStatus.None })
            {
                uint previous = state;
                state = M68881ExceptionStatusPolicy.CompleteInstruction(state, entry, 0).StatusRegister;
                Check.Equal(0xABCD0000U, state & 0xFFFF0000, "Condition and quotient bytes survive status processing.");
                Check.Equal((uint)(byte)entry, (state >> 8) & 255, "Old current status replaced, including explicit clear.");
                Check.Equal(previous & 255, state & previous & 255, "Accrued bits never disappear.");
            }
            Check.Equal(0xABCD00FFU, state, "Sticky union plus preserved user bits after exact instruction.");
            var random = new Random(0x688812);
            for (int i = 0; i < 4096; i++)
            {
                uint old = ((uint)random.Next() << 1) | (uint)random.Next(2);byte current = (byte)random.Next(256);
                uint expected = (old / 65536) * 65536 + ((uint)current * 256) + (uint)((old % 256) | (uint)AccruedOracle(current));
                Check.Equal(expected, M68881ExceptionStatusPolicy.CompleteInstruction(old, (M68881ExceptionStatus)current, 0).StatusRegister, "Whole-register seeded oracle with independently assembled bytes.");
            }
        }
        private static void TestNoRetrospectiveSelection()
        {
            for (int previous = 0; previous < 256; previous++)
                for (int enabled = 0; enabled < 256; enabled++)
                {
                    uint old = 0xFF170000 | ((uint)previous << 8) | (uint)(255 - previous);
                    var r = M68881ExceptionStatusPolicy.CompleteInstruction(old, M68881ExceptionStatus.None, (byte)enabled);
                    Check.Equal(M68881ExceptionStatus.None, r.HighestEnabledStatus, "Only newly reported status can be selected; no old EXC/AEXC replay.");
                    Check.Equal(old & 0xFFFF00FF, r.StatusRegister, "No new exceptions clears EXC and leaves independent bytes untouched.");
                }
        }
    }
}
