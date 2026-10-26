// Copyright 2026 Nils Kopal
// SPDX-License-Identifier: Apache-2.0
using System;
using System.Collections.Generic;
using AmigaEmulator.Core.Cpu;

namespace AmigaEmulator.Tests
{
    internal static class M68881InstructionExceptionDecisionTests
    {
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return Test("literal instruction flag to architectural vector table", TestVectors);
            yield return Test("literal combined cases use priority rather than vector-number order", TestCombined);
            yield return Test("all 65536 new-flag and enable pairs match independent set oracle", TestExhaustive);
            yield return Test("masked overflow and INEX2 enable select inexact without changing EXC", TestOverflowFallback);
            yield return Test("old current and accrued flags never replay through newly enabled classes", TestOldFlags);
            yield return Test("selection has no data, control, status, or address register mutations", TestPureSelection);
        }
        private static ChipTestCase Test(string name, Action action) => new ChipTestCase(
            "CPU/FPU isolated instruction exception decision", name,
            "MC68881/MC68882 User's Manual Table6-1/6.1.9/6.1.10; independent literal/set oracle", action);
        private static M68881InstructionTrapVector Select(byte raised, byte enables) =>
            M68881InstructionExceptionDecision.SelectFromNewInstruction((M68881ExceptionStatus)raised, enables);
        private static void TestVectors()
        {
            var cases = new (byte Flag, int Vector)[]
            { (128,48), (64,54), (32,52), (16,53), (8,51), (4,50), (2,49), (1,49) };
            foreach (var x in cases)
            {
                Check.Equal(x.Vector, (int)Select(x.Flag, x.Flag), "Manual table vector, not bit index.");
                Check.Equal(0, (int)Select(x.Flag, 0), "Disabled flag alone selects nothing.");
            }
            Check.Equal(0, (int)Select(0,255), "No new flags.");
        }
        private static void TestCombined()
        {
            var cases = new (byte Raised, byte Enables, int Vector)[]
            {
                (255,255,48), (127,255,54), (63,255,52), (31,255,53),
                (15,255,51), (7,255,50), (3,255,49), (255,64,54),
                (48,2,49), (48,18,53), (80,2,49), (24,10,51), (20,6,50),
                (16,1,0), (16,2,49), (16,18,53), (8,2,0), (0,255,0)
            };
            foreach (var x in cases) Check.Equal(x.Vector, (int)Select(x.Raised,x.Enables), "Independent combined literal.");
        }
        private static int Oracle(int flags, int enables)
        {
            var reported = new HashSet<int>(); var allowed = new HashSet<int>();
            foreach (int flag in new[] {1,2,4,8,16,32,64,128})
            {
                if ((flags / flag) % 2 == 1) reported.Add(flag);
                if ((enables / flag) % 2 == 1) allowed.Add(flag);
            }
            // Table order explicitly differs from vector number order.
            foreach (var pair in new (int Flag, int Vector)[] {(128,48),(64,54),(32,52),(16,53),(8,51),(4,50)})
                if (reported.Contains(pair.Flag) && allowed.Contains(pair.Flag)) return pair.Vector;
            bool inexactFromRoundingOrOverflow = allowed.Contains(2) && (reported.Contains(2) || reported.Contains(16));
            bool inexactFromDecimal = allowed.Contains(1) && reported.Contains(1);
            return inexactFromRoundingOrOverflow || inexactFromDecimal ? 49 : 0;
        }
        private static void TestExhaustive()
        {
            for (int raised=0;raised<256;raised++)
                for (int enables=0;enables<256;enables++)
                    Check.Equal(Oracle(raised,enables),(int)Select((byte)raised,(byte)enables),"Independent full byte-pair oracle.");
        }
        private static void TestOverflowFallback()
        {
            for (int enable=0;enable<256;enable++)
            {
                int expected=(enable/16)%2==1 ? 53 : (enable/2)%2==1 ? 49 : 0;
                Check.Equal(expected,(int)Select(16,(byte)enable),"Overflow enabled wins, otherwise only INEX2 enables fallback.");
                var raw=M68881ExceptionStatusPolicy.CompleteInstruction(0xA5C30007,M68881ExceptionStatus.Overflow,(byte)enable);
                Check.Equal(0xA5C3104FU,raw.StatusRegister,"EXC remains OVFL only, accrued OVFL/INEX set; low reserved bits retained.");
                Check.Equal((enable&16)!=0 ? M68881ExceptionStatus.Overflow : M68881ExceptionStatus.None,
                    raw.HighestEnabledStatus,"Earlier status policy is raw enabled-EXC intersection, not a complete trap selector.");
            }
        }
        private static void TestOldFlags()
        {
            for (int old=0;old<256;old++)
                for (int enables=0;enables<256;enables++)
                {
                    uint status=0xD3170000U+(uint)old*256+(uint)(255-old);
                    var raw=M68881ExceptionStatusPolicy.CompleteInstruction(status,M68881ExceptionStatus.None,(byte)enables);
                    Check.Equal(0,(int)Select(0,(byte)enables),"Newly enabled old current/accrued flags are not new instruction events.");
                    Check.Equal(status&0xFFFF00FFU,raw.StatusRegister,"Explicit new empty EXC replaces old flags; upper/sticky bytes retained.");
                }
        }
        private static void TestPureSelection()
        {
            var r=M68881DevelopmentRegisters.CreateIsolated();
            var values=new M68881Extended[8];
            for(int i=0;i<8;i++) { values[i]=new M68881Extended((ushort)(0x7FFF+i),0x8000000000000001UL+(ulong)i); r.WriteRawDataRegister(i,values[i]); }
            uint seed=0x688816;
            for(int i=0;i<8192;i++)
            {
                seed=unchecked(seed*1664525U+1013904223U);r.WriteControlRegister(seed);r.WriteStatusRegister(seed^0xDEADBEEF);r.WriteInstructionAddressRegister(~seed);
                Select((byte)seed,(byte)(seed>>8));
                Check.Equal(seed&65535U,r.ControlRegister,"No control mutation.");Check.Equal(seed^0xDEADBEEF,r.StatusRegister,"No current/accrued/condition/quotient mutation.");
                Check.Equal(~seed,r.InstructionAddressRegister,"No instruction capture or trapqueue.");
                for(int j=0;j<8;j++)Check.Equal(values[j],r.ReadRawDataRegister(j),"No data mutation or NaN quieting.");
            }
        }
    }
}
