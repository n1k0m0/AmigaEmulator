// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using AmigaEmulator.Core.Cpu;

namespace AmigaEmulator.Tests
{
    internal static class M68881DevelopmentRegistersTests
    {
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return Test("eight positive quiet NaNs and zero control registers on isolated reset", TestReset);
            yield return Test("raw 80-bit transport preserves exceptional encodings and independent slots", TestRawTransport);
            yield return Test("all 65536 FPCR low words with ignored high word and documented mode table", TestControl);
            yield return Test("raw FPSR and FPIAR software writes preserve every bit and other registers", TestControlIsolation);
            yield return Test("invalid data-register indices reject before any storage mutation", TestBounds);
            yield return Test("control enables never modify old status or instruction address", TestNoRetrospectiveMutation);
        }
        private static ChipTestCase Test(string name, Action action) => new ChipTestCase(
            "CPU/FPU isolated development registers", name,
            "MC68881/MC68882 User's Manual 2.1..2.4; literal field table and raw-bit storage oracle", action);
        private static M68881Extended[] Snapshot(M68881DevelopmentRegisters r)
        {
            var copy = new M68881Extended[8];
            for (int i = 0; i < 8; i++) copy[i] = r.ReadRawDataRegister(i);
            return copy;
        }
        private static void SameData(M68881Extended[] expected, M68881DevelopmentRegisters r)
        {
            for (int i = 0; i < 8; i++) Check.Equal(expected[i], r.ReadRawDataRegister(i), "Independent raw register retained.");
        }
        private static void TestReset()
        {
            var r = M68881DevelopmentRegisters.CreateIsolated();
            var other = M68881DevelopmentRegisters.CreateIsolated();
            for (int pass = 0; pass < 3; pass++)
            {
                Check.Equal(8, M68881DevelopmentRegisters.DataRegisterCount, "Eight data registers.");
                Check.Equal(0U, r.ControlRegister, "Reset FPCR.");
                Check.Equal(0U, r.StatusRegister, "Reset FPSR.");
                Check.Equal(0U, r.InstructionAddressRegister, "Reset FPIAR.");
                Check.Equal(M68881RoundingMode.NearestEven, r.RoundingMode, "Reset rounding mode.");
                Check.Equal(M68881PrecisionControl.Extended, r.PrecisionControl, "Reset precision.");
                for (int i = 0; i < 8; i++)
                {
                    var v = r.ReadRawDataRegister(i);
                    Check.Equal(M68881ExtendedClass.QuietNaN, v.Classification, "Reset is quiet NaN, not zero or infinity.");
                    Check.True(!v.IsNegative, "Reset NaN is positive; no hardware payload assertion.");
                    r.WriteRawDataRegister(i, new M68881Extended((ushort)(0x8000 + i), (ulong)i));
                }
                r.WriteControlRegister(uint.MaxValue); r.WriteStatusRegister(uint.MaxValue);
                r.WriteInstructionAddressRegister(uint.MaxValue); r.Reset();
            }
            r.WriteRawDataRegister(0, new M68881Extended(0, 0));
            Check.Equal(M68881ExtendedClass.QuietNaN, other.ReadRawDataRegister(0).Classification, "Instances do not share backing data.");
            other.WriteStatusRegister(0x12345678);
            Check.Equal(0U, r.StatusRegister, "Control storage belongs to its instance.");
        }
        private static void TestRawTransport()
        {
            var r = M68881DevelopmentRegisters.CreateIsolated();
            r.WriteControlRegister(0xF37D); r.WriteStatusRegister(0xDEADBEEF); r.WriteInstructionAddressRegister(0x13579BDF);
            var cases = new List<M68881Extended>
            {
                new M68881Extended(0,0), new M68881Extended(0x8000,0),
                new M68881Extended(0x7FFF,0x8000000000000000), new M68881Extended(0xFFFF,0),
                new M68881Extended(0x7FFF,1), new M68881Extended(0xFFFF,0x8000000000000001),
                new M68881Extended(0x7FFF,0x4000000000000001), new M68881Extended(0xFFFF,ulong.MaxValue),
                new M68881Extended(0,1), new M68881Extended(0,0x8000000000000000),
                new M68881Extended(1,1), new M68881Extended(0x4000,0x7FFFFFFFFFFFFFFF)
            };
            ulong seed = 0x6888123456789ABC;
            for (int i = 0; i < 8192; i++)
            {
                seed ^= seed << 13; seed ^= seed >> 7; seed ^= seed << 17;
                cases.Add(new M68881Extended((ushort)(seed >> 48), seed));
            }
            foreach (var value in cases)
                for (int slot = 0; slot < 8; slot++)
                {
                    var before = Snapshot(r); r.WriteRawDataRegister(slot, value);
                    for (int i = 0; i < 8; i++)
                    {
                        var actual = r.ReadRawDataRegister(i); var expected = i == slot ? value : before[i];
                        Check.Equal(expected.SignExponent, actual.SignExponent, "Exact sign/exponent bits, including noncanonical forms.");
                        Check.Equal(expected.Significand, actual.Significand, "Exact payload/J/significand bits.");
                    }
                    Check.Equal(0xF37DU, r.ControlRegister, "Raw data write leaves FPCR.");
                    Check.Equal(0xDEADBEEFU, r.StatusRegister, "No implicit SNAN processing or EXC update.");
                    Check.Equal(0x13579BDFU, r.InstructionAddressRegister, "Raw transport does not capture an instruction address.");
                }
        }
        private static void TestControl()
        {
            var r = M68881DevelopmentRegisters.CreateIsolated();
            r.WriteStatusRegister(0xFEDCBA98); r.WriteInstructionAddressRegister(0x76543210);
            var before = Snapshot(r);
            var rounding = new[] { M68881RoundingMode.NearestEven, M68881RoundingMode.TowardZero,
                M68881RoundingMode.TowardNegative, M68881RoundingMode.TowardPositive };
            var precision = new[] { M68881PrecisionControl.Extended, M68881PrecisionControl.Single,
                M68881PrecisionControl.Double, M68881PrecisionControl.Reserved };
            for (uint low = 0; low <= ushort.MaxValue; low++)
            {
                uint high = (low * 40503U + 173U) % 65536;
                r.WriteControlRegister(high * 65536 + low);
                Check.Equal(low, r.ControlRegister, "FPCR high half reads zero, low half unchanged.");
                Check.Equal((byte)(low / 256), r.ExceptionEnables, "Literal enable byte location.");
                Check.Equal(rounding[(low / 16) % 4], r.RoundingMode, "Documented RND encoding table.");
                Check.Equal(precision[(low / 64) % 4], r.PrecisionControl, "Reserved precision remains explicitly reserved.");
                Check.Equal(0xFEDCBA98U, r.StatusRegister, "Control writes preserve all old status bits.");
                Check.Equal(0x76543210U, r.InstructionAddressRegister, "Control writes do not change FPIAR.");
            }
            SameData(before, r);
        }
        private static void TestControlIsolation()
        {
            var r = M68881DevelopmentRegisters.CreateIsolated();
            for (int i = 0; i < 8; i++) r.WriteRawDataRegister(i, new M68881Extended((ushort)(0x3FF0+i), 0xFEDC000000000000UL+(ulong)i));
            var before = Snapshot(r); r.WriteControlRegister(0xABCD);
            uint seed = 0x68881;
            for (int i = 0; i < 4096; i++)
            {
                seed = unchecked(seed * 1664525U + 1013904223U);
                uint address = seed ^ 0xA5C33C5A; uint oldAddress = r.InstructionAddressRegister;
                r.WriteStatusRegister(seed);
                Check.Equal(seed, r.StatusRegister, "All 32 user-written FPSR bits retained, including reserved positions.");
                Check.Equal(oldAddress, r.InstructionAddressRegister, "FPSR write leaves previous logical address.");
                r.WriteInstructionAddressRegister(address);
                Check.Equal(address, r.InstructionAddressRegister, "All 32 raw FPIAR bits retained.");
                Check.Equal(seed, r.StatusRegister, "FPIAR write leaves EXC/AEXC/quotient/condition bits untouched.");
                Check.Equal(0xABCDU, r.ControlRegister, "Independent FPCR.");
            }
            r.WriteStatusRegister(uint.MaxValue); r.WriteStatusRegister(0);
            Check.Equal(0U, r.StatusRegister, "Explicit user write can clear accrued flags without a policy transition.");
            SameData(before, r);
        }
        private static void Reject(Action action)
        {
            bool rejected = false;
            try { action(); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check.True(rejected, "Reject out-of-range register index.");
        }
        private static void TestBounds()
        {
            var r = M68881DevelopmentRegisters.CreateIsolated();
            r.WriteControlRegister(0x1234); r.WriteStatusRegister(0x98765432); r.WriteInstructionAddressRegister(0xABCDEF01);
            var before = Snapshot(r);
            foreach (int bad in new[] { int.MinValue, -1, 8, 9, int.MaxValue })
            {
                Reject(() => r.ReadRawDataRegister(bad));
                Reject(() => r.WriteRawDataRegister(bad, new M68881Extended(0,0)));
                SameData(before, r);
                Check.Equal(0x1234U, r.ControlRegister, "Invalid write has no control side effects.");
                Check.Equal(0x98765432U, r.StatusRegister, "Invalid write has no status side effects.");
                Check.Equal(0xABCDEF01U, r.InstructionAddressRegister, "Invalid write has no address side effects.");
            }
        }
        private static void TestNoRetrospectiveMutation()
        {
            var r = M68881DevelopmentRegisters.CreateIsolated();
            r.WriteInstructionAddressRegister(0xFEDCBA98);
            for (uint old = 0; old < 256; old++)
                for (uint enables = 0; enables < 256; enables++)
                {
                    uint status = 0xD3170000 + old * 256 + (255 - old);
                    r.WriteStatusRegister(status); r.WriteControlRegister(enables * 256 + 0x70);
                    Check.Equal(status, r.StatusRegister, "Enabling a class leaves old EXC and sticky AEXC untouched.");
                    Check.Equal(0xFEDCBA98U, r.InstructionAddressRegister, "No retroactive address capture or trap dispatch.");
                }
        }
    }
}
