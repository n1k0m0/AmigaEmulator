/*
   Copyright 2026 Nils Kopal <Nils.Kopal<at>kopaldev.de

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/
using System.Text;
using AmigaEmulator.Core.Cia;
using AmigaEmulator.Core.Memory;

namespace AmigaEmulator.Core.Cpu
{
    /// <summary>
    /// Provides 68000 Self Test functionality.
    /// </summary>
    public static class M68kSelfTest
    {
        /// <summary>
        /// Runs the value routine.
        /// </summary>
        public static int Run(StringBuilder log)
        {
            log.AppendLine("M68K SELF TEST");

            int failures = 0;
            failures += RunMoveImmediateAndBranchTest(log);
            failures += RunAddressingStackAndMoveTest(log);
            failures += RunArithmeticAndMovemTest(log);
            failures += RunAddxSubxTest(log);
            failures += RunGraphicsLibraryAddressingPatternTest(log);
            failures += RunGraphicsLibraryBitAndIndexPatternTest(log);
            failures += RunGraphicsLibraryFrameAndCopyPatternTest(log);
            failures += RunGraphicsLibraryPcIndexedMovePatternTest(log);
            failures += RunGraphicsLibraryDualIndexedWordMovePatternTest(log);
            failures += RunGraphicsLibraryDualIndexedRomTemplatePatternTest(log);
            failures += RunReadModifyWriteExtensionTest(log);
            failures += RunWordDisplacementBaseTest(log);
            failures += RunLogicalMemoryDestinationTest(log);
            failures += RunSetConditionTest(log);
            failures += RunExtendDataRegisterTest(log);
            failures += RunDivideInstructionTest(log);
            failures += RunMoveUspDirectionTest(log);
            failures += RunAddressRegisterFullWidthTest(log);
            failures += RunRegisterShiftZeroCountTest(log);
            failures += RunExecAllocatorArithmeticPatternTest(log);
            failures += RunBootRelevantCpuFamilyTest(log);
            failures += RunMovepTest(log);
            failures += RunStatusRegisterMoveTest(log);
            failures += RunSubroutineAndFrameTest(log);
            failures += RunExceptionEntryTest(log);
            failures += RunAddressErrorExceptionTest(log);
            failures += RunStopInterruptWakeTest(log);
            failures += RunMaskedInterruptLineLatchTest(log);
            failures += RunRteUserModeStackSwitchTest(log);
            failures += RunPrivilegeViolationFromUserModeTest(log);
            failures += RunPrivilegeViolationBeforeOperandFetchTest(log);
            failures += RunIllegalAddressRegisterDestinationTest(log);
            failures += RunIllegalAddqByteAddressRegisterTest(log);
            failures += RunIllegalEffectiveAddressSideEffectTest(log);
            failures += RunLineTrapTest(log);
            failures += RunTrapInstructionTest(log);
            failures += RunTickDeferredWriteSideEffectTest(log);
            failures += RunTickDeferredReadSideEffectTest(log);

            log.AppendLine(failures == 0 ? "OK" : "FAILURES=" + failures);
            return failures;
        }

        /// <summary>
        /// Runs the Move Immediate And Branch Test routine.
        /// </summary>
        private static int RunMoveImmediateAndBranchTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            WriteProgram(
                bus,
                0x0100,
                0x7001,             // MOVEQ #1,D0
                0x7205,             // MOVEQ #5,D1
                0x0080, 0x0000, 0x0002, // ORI.L #2,D0
                0x0241, 0x00FF,    // ANDI.W #$00FF,D1
                0x0A00, 0x0007,    // EORI.B #7,D0
                0x0680, 0x0000, 0x0010, // ADDI.L #$10,D0
                0x0480, 0x0000, 0x0010, // SUBI.L #$10,D0
                0x0C00, 0x0004,    // CMPI.B #4,D0
                0x6602,             // BNE.S skip, should not branch
                0x7409,             // MOVEQ #9,D2
                0x4E71);

            M68kCpu cpu = CreateCpu(bus);
            Step(cpu, 10);

            int failures = 0;
            failures += Expect(log, "MOVEQ/ORI/EORI result", cpu.GetD(0) == 4, cpu.GetDebugInfo());
            failures += Expect(log, "ANDI result", cpu.GetD(1) == 5, cpu.GetDebugInfo());
            failures += Expect(log, "CMPI/BNE flow", cpu.GetD(2) == 9, cpu.GetDebugInfo());
            return failures;
        }

        /// <summary>
        /// Runs the Addressing Stack And Move Test routine.
        /// </summary>
        private static int RunAddressingStackAndMoveTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            WriteProgram(
                bus,
                0x0100,
                0x207C, 0x0000, 0x0200, // MOVEA.L #$200,A0
                0x20BC, 0x1234, 0x5678, // MOVE.L #$12345678,(A0)
                0x2610,                 // MOVE.L (A0),D3
                0x43E8, 0x0004,          // LEA 4(A0),A1
                0x4851,                 // PEA (A1)
                0x4A83,                 // TST.L D3
                0x4283);                // CLR.L D3

            M68kCpu cpu = CreateCpu(bus);
            Step(cpu, 7);

            int failures = 0;
            failures += Expect(log, "MOVE/MOVEA memory roundtrip", cpu.GetD(3) == 0, cpu.GetA(0) == 0x0200 ? "D3=" + cpu.GetD(3).ToString("X8") : cpu.GetDebugInfo());
            failures += Expect(log, "LEA target", cpu.GetA(1) == 0x0204, cpu.GetDebugInfo());
            failures += Expect(log, "PEA stack", cpu.A7 == 0x0FFC && bus.ReadLong(cpu.A7) == 0x0204, "A7=" + cpu.A7.ToString("X8"));
            failures += Expect(log, "CLR flags", (cpu.SR & 0x0004) != 0, cpu.GetDebugInfo());
            return failures;
        }

        /// <summary>
        /// Runs the Arithmetic And Movem Test routine.
        /// </summary>
        private static int RunArithmeticAndMovemTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            WriteProgram(
                bus,
                0x0100,
                0x7003,             // MOVEQ #3,D0
                0x7202,             // MOVEQ #2,D1
                0xD041,             // ADD.W D1,D0
                0x5340,             // SUBQ.W #1,D0
                0xB041,             // CMP.W D1,D0
                0x48E7, 0xC000,    // MOVEM.L D0-D1,-(A7), predecrement mask order
                0x4CDF, 0x0030);   // MOVEM.L (A7)+,D4-D5

            M68kCpu cpu = CreateCpu(bus);
            Step(cpu, 7);

            int failures = 0;
            failures += Expect(log, "ADD/SUB/CMP result", cpu.GetD(0) == 4 && (cpu.SR & 0x0004) == 0, cpu.GetDebugInfo());
            failures += Expect(log, "MOVEM restore D4", cpu.GetD(4) == 4, "D4=" + cpu.GetD(4).ToString("X8"));
            failures += Expect(log, "MOVEM restore D5", cpu.GetD(5) == 2, "D5=" + cpu.GetD(5).ToString("X8"));
            failures += Expect(log, "MOVEM stack balanced", cpu.A7 == 0x1000, "A7=" + cpu.A7.ToString("X8"));
            return failures;
        }

        /// <summary>
        /// Runs the Addx Subx Test routine.
        /// </summary>
        private static int RunAddxSubxTest(StringBuilder log)
        {
            int failures = 0;

            var addRegisterBus = CreateBus();
            WriteResetVectors(addRegisterBus, 0x1000, 0x0100);
            WriteProgram(
                addRegisterBus,
                0x0100,
                0x103C, 0x007F, // MOVE.B #$7F,D0
                0x123C, 0x0000, // MOVE.B #$00,D1
                0x023C, 0x0000, // ANDI #0,CCR
                0x003C, 0x0010, // ORI #$10,CCR
                0xD101);        // ADDX.B D1,D0

            M68kCpu addRegisterCpu = CreateCpu(addRegisterBus);
            Step(addRegisterCpu, 5);
            failures += Expect(log, "ADDX.B register result", (addRegisterCpu.GetD(0) & 0xFF) == 0x80, "D0=" + addRegisterCpu.GetD(0).ToString("X8"));
            failures += Expect(log, "ADDX.B register flags", (addRegisterCpu.SR & 0x001F) == 0x000A, "SR=" + addRegisterCpu.SR.ToString("X4"));

            var subRegisterBus = CreateBus();
            WriteResetVectors(subRegisterBus, 0x1000, 0x0100);
            WriteProgram(
                subRegisterBus,
                0x0100,
                0x103C, 0x0000, // MOVE.B #$00,D0
                0x123C, 0x0000, // MOVE.B #$00,D1
                0x023C, 0x0000, // ANDI #0,CCR
                0x003C, 0x0014, // ORI #$14,CCR
                0x9101);        // SUBX.B D1,D0

            M68kCpu subRegisterCpu = CreateCpu(subRegisterBus);
            Step(subRegisterCpu, 5);
            failures += Expect(log, "SUBX.B register result", (subRegisterCpu.GetD(0) & 0xFF) == 0xFF, "D0=" + subRegisterCpu.GetD(0).ToString("X8"));
            failures += Expect(log, "SUBX.B register flags", (subRegisterCpu.SR & 0x001F) == 0x0019, "SR=" + subRegisterCpu.SR.ToString("X4"));

            var addMemoryBus = CreateBus();
            WriteResetVectors(addMemoryBus, 0x1000, 0x0100);
            addMemoryBus.WriteLong(0x0300, 0xFFFFFFFF);
            addMemoryBus.WriteLong(0x0400, 0x00000000);
            WriteProgram(
                addMemoryBus,
                0x0100,
                0x207C, 0x0000, 0x0304, // MOVEA.L #$304,A0
                0x227C, 0x0000, 0x0404, // MOVEA.L #$404,A1
                0x023C, 0x0000,          // ANDI #0,CCR
                0x003C, 0x0014,          // ORI #$14,CCR
                0xD189);                 // ADDX.L -(A1),-(A0)

            M68kCpu addMemoryCpu = CreateCpu(addMemoryBus);
            Step(addMemoryCpu, 5);
            failures += Expect(log, "ADDX.L memory result", addMemoryBus.ReadLong(0x0300) == 0, "mem=" + addMemoryBus.ReadLong(0x0300).ToString("X8"));
            failures += Expect(log, "ADDX.L memory predecrement", addMemoryCpu.GetA(0) == 0x0300 && addMemoryCpu.GetA(1) == 0x0400, addMemoryCpu.GetDebugInfo());
            failures += Expect(log, "ADDX.L memory flags", (addMemoryCpu.SR & 0x001F) == 0x0015, "SR=" + addMemoryCpu.SR.ToString("X4"));

            return failures;
        }

        /// <summary>
        /// Runs the Subroutine And Frame Test routine.
        /// </summary>
        private static int RunSubroutineAndFrameTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            WriteProgram(
                bus,
                0x0100,
                0x6104,       // BSR.S $106
                0x7007,       // MOVEQ #7,D0, runs after RTS
                0x4E71,       // NOP
                0x4E56, 0xFFFC, // LINK A6,#-4
                0x4E5E,       // UNLK A6
                0x4E75);      // RTS

            M68kCpu cpu = CreateCpu(bus);
            Step(cpu, 5);

            int failures = 0;
            failures += Expect(log, "BSR/RTS return", cpu.PC == 0x0104 && cpu.A7 == 0x1000, cpu.GetDebugInfo());
            cpu.Step();
            failures += Expect(log, "Post-RTS execution", cpu.GetD(0) == 7, cpu.GetDebugInfo());
            return failures;
        }

        /// <summary>
        /// Runs the Graphics Library Addressing Pattern Test routine.
        /// </summary>
        private static int RunGraphicsLibraryAddressingPatternTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            bus.WriteLong(0x018C, 0x00000200);
            bus.WriteLong(0x0204, 0x00000220);
            bus.WriteLong(0x0208, 0x00000240);
            bus.WriteLong(0x0224, 0x00002368);
            bus.WriteLong(0x0244, 0x00002408);

            WriteProgram(
                bus,
                0x0100,
                0x2C7C, 0x0000, 0x0300, // MOVEA.L #$300,A6
                0x2278, 0x018C,          // MOVEA.L ($018C).W,A1
                0x48E7, 0x0030,          // MOVEM.L A2-A3,-(A7)
                0x264E,                  // MOVEA.L A6,A3
                0x2069, 0x0004,          // MOVEA.L 4(A1),A0
                0x2768, 0x0004, 0x0032,  // MOVE.L 4(A0),$32(A3)
                0x2069, 0x0008,          // MOVEA.L 8(A1),A0
                0x2768, 0x0004, 0x0036,  // MOVE.L 4(A0),$36(A3)
                0x4CDF, 0x0C00);         // MOVEM.L (A7)+,A2-A3

            M68kCpu cpu = CreateCpu(bus);
            cpu.SetA(2, 0x00AAAAAA);
            cpu.SetA(3, 0x00555555);
            Step(cpu, 9);

            int failures = 0;
            failures += Expect(log, "graphics MOVEA.W absolute source", cpu.GetA(1) == 0x0200, "A1=" + cpu.GetA(1).ToString("X8"));
            failures += Expect(log, "graphics nested long copy LOF", bus.ReadLong(0x0332) == 0x00002368, "mem=" + bus.ReadLong(0x0332).ToString("X8"));
            failures += Expect(log, "graphics nested long copy SHF", bus.ReadLong(0x0336) == 0x00002408, "mem=" + bus.ReadLong(0x0336).ToString("X8"));
            failures += Expect(log, "graphics MOVEM restore A2", cpu.GetA(2) == 0x00AAAAAA, "A2=" + cpu.GetA(2).ToString("X8"));
            failures += Expect(log, "graphics MOVEM restore A3", cpu.GetA(3) == 0x00555555, "A3=" + cpu.GetA(3).ToString("X8"));
            failures += Expect(log, "graphics MOVEM stack balanced", cpu.A7 == 0x1000, "A7=" + cpu.A7.ToString("X8"));
            return failures;
        }

        /// <summary>
        /// Runs the Graphics Library Bit And Index Pattern Test routine.
        /// </summary>
        private static int RunGraphicsLibraryBitAndIndexPatternTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            bus.WriteByte(0x0204, 0x04);
            bus.WriteByte(0x0205, 0x00);
            WriteProgram(
                bus,
                0x0100,
                0x207C, 0x0000, 0x0200, // MOVEA.L #$200,A0
                0x7406,                 // MOVEQ #6,D2
                0x43F0, 0x2008,          // LEA 8(A0,D2.W),A1
                0x4870, 0x2008,          // PEA 8(A0,D2.W)
                0x0828, 0x0002, 0x0004, // BTST #2,4(A0)
                0x7605,                 // MOVEQ #5,D3
                0x0728, 0x0005);        // BTST D3,5(A0)

            M68kCpu cpu = CreateCpu(bus);
            Step(cpu, 7);

            int failures = 0;
            failures += Expect(log, "graphics indexed LEA", cpu.GetA(1) == 0x020E, "A1=" + cpu.GetA(1).ToString("X8"));
            failures += Expect(log, "graphics indexed PEA", cpu.A7 == 0x0FFC && bus.ReadLong(cpu.A7) == 0x020E, "A7=" + cpu.A7.ToString("X8"));
            failures += Expect(log, "graphics BTST memory clear", (cpu.SR & 0x0004) != 0, cpu.GetDebugInfo());
            return failures;
        }

        /// <summary>
        /// Runs the Graphics Library Frame And Copy Pattern Test routine.
        /// </summary>
        private static int RunGraphicsLibraryFrameAndCopyPatternTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            bus.WriteLong(0x0204, 0x12345678);
            bus.WriteWord(0x0208, 0x9ABC);
            bus.WriteByte(0x039F, 0x04);
            WriteProgram(
                bus,
                0x0100,
                0x247C, 0x0000, 0x4984, // MOVEA.L #$4984,A2
                0x267C, 0x0000, 0x49A6, // MOVEA.L #$49A6,A3
                0x48E7, 0x0030,          // MOVEM.L A2-A3,-(A7)
                0x227C, 0x0000, 0x0200, // MOVEA.L #$200,A1
                0x267C, 0x0000, 0x0300, // MOVEA.L #$300,A3
                0x2769, 0x0004, 0x0032, // MOVE.L 4(A1),$32(A3)
                0x3769, 0x0008, 0x0036, // MOVE.W 8(A1),$36(A3)
                0x082B, 0x0002, 0x009F, // BTST #2,$9F(A3)
                0x4CDF, 0x0C00);         // MOVEM.L (A7)+,A2-A3

            M68kCpu cpu = CreateCpu(bus);
            Step(cpu, 9);

            int failures = 0;
            failures += Expect(log, "graphics MOVEM frame stack A2", bus.ReadLong(0x0FF8) == 0x00004984, "stack=" + bus.ReadLong(0x0FF8).ToString("X8"));
            failures += Expect(log, "graphics MOVEM frame stack A3", bus.ReadLong(0x0FFC) == 0x000049A6, "stack=" + bus.ReadLong(0x0FFC).ToString("X8"));
            failures += Expect(log, "graphics dual-extension long copy", bus.ReadLong(0x0332) == 0x12345678, "mem=" + bus.ReadLong(0x0332).ToString("X8"));
            failures += Expect(log, "graphics dual-extension word copy", bus.ReadWord(0x0336) == 0x9ABC, "mem=" + bus.ReadWord(0x0336).ToString("X4"));
            failures += Expect(log, "graphics BTST d16(A3) set", (cpu.SR & 0x0004) == 0, cpu.GetDebugInfo());
            failures += Expect(log, "graphics MOVEM frame restore A2", cpu.GetA(2) == 0x00004984, "A2=" + cpu.GetA(2).ToString("X8"));
            failures += Expect(log, "graphics MOVEM frame restore A3", cpu.GetA(3) == 0x000049A6, "A3=" + cpu.GetA(3).ToString("X8"));
            return failures;
        }

        /// <summary>
        /// Runs the Graphics Library PC Indexed Move Pattern Test routine.
        /// </summary>
        private static int RunGraphicsLibraryPcIndexedMovePatternTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            bus.WriteByte(0x013A, 0xA5);
            WriteProgram(
                bus,
                0x0100,
                0x13BB, 0x00B8, 0x1028); // MOVE.B (d8,PC,D0.W),(d8,A1,D1.W)

            M68kCpu cpu = CreateCpu(bus);
            cpu.SetD(0, 0x00000080);
            cpu.SetD(1, 0x00000005);
            cpu.SetA(1, 0x00000200);
            cpu.Step();

            int failures = 0;
            failures += Expect(log, "graphics PC-indexed to address-indexed MOVE.B", bus.ReadByte(0x022D) == 0xA5, "mem=" + bus.ReadByte(0x022D).ToString("X2") + " " + cpu.GetDebugInfo());
            failures += Expect(log, "graphics PC-indexed MOVE flags", (cpu.SR & 0x000C) == 0x0008, cpu.GetDebugInfo());
            return failures;
        }

        /// <summary>
        /// Runs the Graphics Library Dual Indexed Word Move Pattern Test routine.
        /// </summary>
        private static int RunGraphicsLibraryDualIndexedWordMovePatternTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            bus.WriteWord(0x0324, 0x49C0);
            WriteProgram(
                bus,
                0x0100,
                0x31B2, 0x181C, 0x0800); // MOVE.W $1C(A2,D1.W),$00(A0,D0.W)

            M68kCpu cpu = CreateCpu(bus);
            cpu.SetA(0, 0x00000400);
            cpu.SetA(2, 0x00000300);
            cpu.SetD(0, 0x00000008);
            cpu.SetD(1, 0x00000008);
            cpu.Step();

            int failures = 0;
            failures += Expect(log, "graphics dual-indexed MOVE.W destination", bus.ReadWord(0x0408) == 0x49C0, "mem=" + bus.ReadWord(0x0408).ToString("X4") + " " + cpu.GetDebugInfo());
            failures += Expect(log, "graphics dual-indexed MOVE.W flags", (cpu.SR & 0x000C) == 0x0000, cpu.GetDebugInfo());
            failures += Expect(log, "graphics dual-indexed MOVE.W PC", cpu.PC == 0x0106, cpu.GetDebugInfo());
            return failures;
        }

        /// <summary>
        /// Runs the Graphics Library Dual Indexed ROM Template Pattern Test routine.
        /// </summary>
        private static int RunGraphicsLibraryDualIndexedRomTemplatePatternTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            bus.WriteWord(0x0322, 0xAAAA);
            bus.WriteWord(0x0324, 0xBBBB);

            WriteProgram(
                bus,
                0x0100,
                0x35B1, 0x0800, 0x181C); // MOVE.W 0(A1,D0.L),$1C(A2,D1.W)

            M68kCpu cpu = CreateCpu(bus);
            cpu.SetA(1, 0x00000322);
            cpu.SetA(2, 0x00005A68);
            cpu.SetD(0, 0x00000002);
            cpu.SetD(1, 0x00000008);
            cpu.Step();

            int failures = 0;
            failures += Expect(log, "graphics ROM-template dual-indexed MOVE.W destination", bus.ReadWord(0x5A8C) == 0xBBBB, "mem=" + bus.ReadWord(0x5A8C).ToString("X4") + " " + cpu.GetDebugInfo());
            failures += Expect(log, "graphics ROM-template dual-indexed MOVE.W PC", cpu.PC == 0x0106, cpu.GetDebugInfo());
            return failures;
        }

        /// <summary>
        /// Runs the Read Modify Write Extension Test routine.
        /// </summary>
        private static int RunReadModifyWriteExtensionTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            bus.WriteByte(0x0204, 0x01);
            WriteProgram(
                bus,
                0x0100,
                0x207C, 0x0000, 0x0200, // MOVEA.L #$200,A0
                0x0028, 0x0080, 0x0004, // ORI.B #$80,4(A0)
                0x4E71);

            M68kCpu cpu = CreateCpu(bus);
            Step(cpu, 2);

            int failures = 0;
            failures += Expect(log, "RMW extension PC", cpu.PC == 0x010C, cpu.GetDebugInfo());
            failures += Expect(log, "RMW extension write", bus.ReadByte(0x0204) == 0x81, "mem=" + bus.ReadByte(0x0204).ToString("X2"));
            return failures;
        }

        /// <summary>
        /// Runs the Word Displacement Base Test routine.
        /// </summary>
        private static int RunWordDisplacementBaseTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            WriteProgram(
                bus,
                0x0100,
                0x7201,       // MOVEQ #1,D1
                0x5240,       // ADDQ.W #1,D0, should run twice through DBF
                0x51C9, 0xFFFC, // DBF D1,$0102
                0x6000, 0x0004, // BRA.W $010E
                0xFFFF,       // skipped if word displacement uses the extension word base
                0x7407);      // MOVEQ #7,D2

            M68kCpu cpu = CreateCpu(bus);
            Step(cpu, 6);

            int failures = 0;
            failures += Expect(log, "DBcc word displacement base", (cpu.GetD(0) & 0xFFFF) == 2, cpu.GetDebugInfo());
            failures += Expect(log, "BRA.W word displacement base", cpu.PC == 0x010E, cpu.GetDebugInfo());
            cpu.Step();
            failures += Expect(log, "BRA.W landed on target", cpu.GetD(2) == 7, cpu.GetDebugInfo());
            return failures;
        }

        /// <summary>
        /// Runs the Logical Memory Destination Test routine.
        /// </summary>
        private static int RunLogicalMemoryDestinationTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            bus.WriteWord(0x0328, 0x1200);
            bus.WriteLong(0x041A, 0x00FF00FF);
            WriteProgram(
                bus,
                0x0100,
                0x303C, 0x00F0, // MOVE.W #$00F0,D0
                0x2C7C, 0x0000, 0x0200, // MOVEA.L #$200,A6
                0x816E, 0x0128, // OR.W D0,$0128(A6)
                0x227C, 0x0000, 0x0400, // MOVEA.L #$400,A1
                0x7205, // MOVEQ #5,D1
                0xC389, // EXG D1,A1
                0x2A01, // MOVE.L D1,D5
                0x740F, // MOVEQ #$0F,D2
                0xC442, // AND.W D2,D2
                0x7603, // MOVEQ #3,D3
                0x07C0, // BSET D3,D0
                0x227C, 0x0000, 0x0400, // MOVEA.L #$400,A1
                0x223C, 0x000F, 0x000F, // MOVE.L #$000F000F,D1
                0xB3A9, 0x001A); // EOR.L D1,$001A(A1)

            M68kCpu cpu = CreateCpu(bus);
            Step(cpu, 14);

            int failures = 0;
            failures += Expect(log, "OR.W Dn,<ea>", bus.ReadWord(0x0328) == 0x12F0, "mem=" + bus.ReadWord(0x0328).ToString("X4"));
            failures += Expect(log, "EXG Dn,An", cpu.GetD(5) == 0x0400, cpu.GetDebugInfo());
            failures += Expect(log, "AND.W <ea>,Dn", (cpu.GetD(2) & 0xFFFF) == 0x000F, cpu.GetDebugInfo());
            failures += Expect(log, "BSET Dn,Dn", (cpu.GetD(0) & 0x0008) != 0, cpu.GetDebugInfo());
            failures += Expect(log, "EOR.L Dn,<ea>", bus.ReadLong(0x041A) == 0x00F000F0, "mem=" + bus.ReadLong(0x041A).ToString("X8"));
            return failures;
        }

        /// <summary>
        /// Runs the Set Condition Test routine.
        /// </summary>
        private static int RunSetConditionTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            WriteProgram(
                bus,
                0x0100,
                0x7001, // MOVEQ #1,D0, clears Z
                0x56C1, // SNE D1
                0x4A80, // TST.L D0, keeps Z clear
                0x57C2); // SEQ D2

            M68kCpu cpu = CreateCpu(bus);
            Step(cpu, 4);

            int failures = 0;
            failures += Expect(log, "SNE true", (cpu.GetD(1) & 0xFF) == 0xFF, cpu.GetDebugInfo());
            failures += Expect(log, "SEQ false", (cpu.GetD(2) & 0xFF) == 0x00, cpu.GetDebugInfo());
            return failures;
        }

        /// <summary>
        /// Runs the Extend Data Register Test routine.
        /// </summary>
        private static int RunExtendDataRegisterTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            WriteProgram(
                bus,
                0x0100,
                0x7200,         // MOVEQ #0,D1
                0x123C, 0x0080, // MOVE.B #$80,D1
                0x4881,         // EXT.W D1
                0x48C1,         // EXT.L D1
                0x7400,         // MOVEQ #0,D2
                0x343C, 0x7FFF, // MOVE.W #$7FFF,D2
                0x48C2);        // EXT.L D2

            M68kCpu cpu = CreateCpu(bus);
            Step(cpu, 7);

            int failures = 0;
            failures += Expect(log, "EXT.L sign extends negative", cpu.GetD(1) == 0xFFFFFF80, "D1=" + cpu.GetD(1).ToString("X8"));
            failures += Expect(log, "EXT.L keeps positive", cpu.GetD(2) == 0x00007FFF, "D2=" + cpu.GetD(2).ToString("X8"));
            return failures;
        }

        /// <summary>
        /// Runs the Divide Instruction Test routine.
        /// </summary>
        private static int RunDivideInstructionTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            WriteProgram(
                bus,
                0x0100,
                0x720A,         // MOVEQ #10,D1
                0x7403,         // MOVEQ #3,D2
                0x82C2,         // DIVU.W D2,D1
                0x263C, 0xFFFF, 0xFFF6, // MOVE.L #-10,D3
                0x7803,         // MOVEQ #3,D4
                0x87C4);        // DIVS.W D4,D3

            M68kCpu cpu = CreateCpu(bus);
            Step(cpu, 6);

            int failures = 0;
            failures += Expect(log, "DIVU.W quotient/remainder", cpu.GetD(1) == 0x00010003, "D1=" + cpu.GetD(1).ToString("X8"));
            failures += Expect(log, "DIVS.W quotient/remainder", cpu.GetD(3) == 0xFFFF_FFFD, "D3=" + cpu.GetD(3).ToString("X8"));
            return failures;
        }

        /// <summary>
        /// Runs the Move Usp Direction Test routine.
        /// </summary>
        private static int RunMoveUspDirectionTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            WriteProgram(
                bus,
                0x0100,
                0x207C, 0x0000, 0x2340, // MOVEA.L #$2340,A0
                0x4E60,                 // MOVE A0,USP
                0x2248,                 // MOVEA.L A0,A1
                0x91C8,                 // SUBA.L A0,A0
                0x4E68);                // MOVE USP,A0

            M68kCpu cpu = CreateCpu(bus);
            Step(cpu, 5);

            int failures = 0;
            failures += Expect(log, "MOVE An,USP writes USP", cpu.USP == 0x2340, "USP=" + cpu.USP.ToString("X8"));
            failures += Expect(log, "MOVE USP,An restores address register", cpu.GetA(0) == 0x2340, "A0=" + cpu.GetA(0).ToString("X8"));
            failures += Expect(log, "MOVE USP sequence keeps unrelated A register", cpu.GetA(1) == 0x2340, "A1=" + cpu.GetA(1).ToString("X8"));
            return failures;
        }

        /// <summary>
        /// Runs the Address Register Full Width Test routine.
        /// </summary>
        private static int RunAddressRegisterFullWidthTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            bus.WriteLong(0x000204, 0xAABBCCDD);
            WriteProgram(
                bus,
                0x0100,
                0x207C, 0x1200, 0x0200, // MOVEA.L #$12000200,A0
                0xD1FC, 0x0100, 0x0004, // ADDA.L #$01000004,A0
                0x2248,                 // MOVEA.L A0,A1
                0x4E60,                 // MOVE A0,USP
                0x2010);                // MOVE.L (A0),D0, bus address masks to $000204

            M68kCpu cpu = CreateCpu(bus);
            Step(cpu, 5);

            int failures = 0;
            failures += Expect(log, "A-register full width after ADDA", cpu.GetA(0) == 0x13000204, "A0=" + cpu.GetA(0).ToString("X8"));
            failures += Expect(log, "A-register full width MOVEA", cpu.GetA(1) == 0x13000204, "A1=" + cpu.GetA(1).ToString("X8"));
            failures += Expect(log, "USP full width", cpu.USP == 0x13000204, "USP=" + cpu.USP.ToString("X8"));
            failures += Expect(log, "A-register access still uses 24-bit bus", cpu.GetD(0) == 0xAABBCCDD, "D0=" + cpu.GetD(0).ToString("X8"));
            return failures;
        }

        /// <summary>
        /// Runs the Register Shift Zero Count Test routine.
        /// </summary>
        private static int RunRegisterShiftZeroCountTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            WriteProgram(
                bus,
                0x0100,
                0x7000,       // MOVEQ #0,D0
                0x123C, 0x01, // MOVE.B #1,D1
                0x023C, 0x0000, // ANDI #0,CCR
                0x003C, 0x001B, // ORI #$1B,CCR
                0xE029);      // LSR.B D0,D1, zero register count leaves value and CCR unchanged

            M68kCpu cpu = CreateCpu(bus);
            Step(cpu, 5);

            int failures = 0;
            failures += Expect(log, "register shift zero count value", (cpu.GetD(1) & 0xFF) == 0x01, "D1=" + cpu.GetD(1).ToString("X8"));
            failures += Expect(log, "register shift zero count preserves CCR", (cpu.SR & 0x001F) == 0x001B, "SR=" + cpu.SR.ToString("X4"));
            return failures;
        }

        /// <summary>
        /// Runs the Exec Allocator Arithmetic Pattern Test routine.
        /// </summary>
        private static int RunExecAllocatorArithmeticPatternTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            WriteProgram(
                bus,
                0x0100,
                0x227C, 0x0000, 0x1235, // MOVEA.L #$1235,A1
                0x223C, 0x0000, 0x0081, // MOVE.L #$81,D1
                0x7618,                 // MOVEQ #$18,D3
                0x4243,                 // CLR.W D3
                0x76F8,                 // MOVEQ #-8,D3
                0xC283,                 // AND.L D3,D1
                0xC389,                 // EXG D1,A1
                0x9289,                 // SUB.L A1,D1
                0xD280,                 // ADD.L D0,D1
                0xD2BC, 0x0000, 0x0007, // ADD.L #7,D1
                0xC283,                 // AND.L D3,D1
                0xB3C1,                 // CMPA.L D1,A1
                0x6402,                 // BCC.S skip
                0x7001,                 // MOVEQ #1,D0, must run because A1 < D1
                0x7207);                // MOVEQ #7,D1 low byte marker

            M68kCpu cpu = CreateCpu(bus);
            cpu.SetD(0, 0x00000010);
            Step(cpu, 15);

            int failures = 0;
            failures += Expect(log, "exec allocator EXG Dn,An", cpu.GetA(1) == 0x00000080 && cpu.GetD(1) == 7, cpu.GetDebugInfo());
            failures += Expect(log, "exec allocator unsigned branch condition", cpu.GetD(0) == 1, cpu.GetDebugInfo());
            return failures;
        }

        /// <summary>
        /// Runs the Boot Relevant CPU Family Test routine.
        /// </summary>
        private static int RunBootRelevantCpuFamilyTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            bus.WriteWord(0x1004, 0x8001);
            bus.WriteWord(0x0200, 0x8001);
            WriteProgram(
                bus,
                0x0100,
                0x303C, 0xFFFE, // MOVE.W #$FFFE,D0
                0xC0FC, 0x0003, // MULU.W #3,D0
                0x323C, 0xFFFE, // MOVE.W #$FFFE,D1
                0xC3FC, 0x0003, // MULS.W #3,D1
                0x243C, 0x0000, 0x0005, // MOVE.L #5,D2
                0x4482, // NEG.L D2
                0x363C, 0x0000, // MOVE.W #0,D3
                0x003C, 0x0010, // ORI #$10,CCR
                0x4043, // NEGX.W D3
                0x023C, 0x0000, // ANDI #0,CCR
                0x383C, 0x0000, // MOVE.W #0,D4
                0x4044, // NEGX.W D4, keeps Z set when result remains zero
                0xE2EF, 0x0004, // LSR.W 4(A7)
                0xE7F8, 0x0200); // ROL.W ($0200).W

            M68kCpu cpu = CreateCpu(bus);
            Step(cpu, 12);

            int failures = 0;
            failures += Expect(log, "MULU.W immediate result", cpu.GetD(0) == 0x0002FFFA, "D0=" + cpu.GetD(0).ToString("X8"));
            failures += Expect(log, "MULS.W immediate result", cpu.GetD(1) == 0xFFFFFFFA, "D1=" + cpu.GetD(1).ToString("X8"));
            failures += Expect(log, "NEG.L Dn result", cpu.GetD(2) == 0xFFFFFFFB, "D2=" + cpu.GetD(2).ToString("X8"));
            failures += Expect(log, "NEGX.W with extend result", (cpu.GetD(3) & 0xFFFF) == 0xFFFF, "D3=" + cpu.GetD(3).ToString("X8"));
            failures += Expect(log, "NEGX.W zero keeps Z", (cpu.GetD(4) & 0xFFFF) == 0 && (cpu.SR & 0x0015) == 0x0004, cpu.GetDebugInfo());

            cpu.Step();
            failures += Expect(log, "Memory LSR.W d16(A7)", bus.ReadWord(0x1004) == 0x4000 && (cpu.SR & 0x0011) == 0x0011, "mem=" + bus.ReadWord(0x1004).ToString("X4") + " " + cpu.GetDebugInfo());

            cpu.Step();
            failures += Expect(log, "Memory ROL.W absolute", bus.ReadWord(0x0200) == 0x0003 && (cpu.SR & 0x0011) == 0x0011, "mem=" + bus.ReadWord(0x0200).ToString("X4") + " " + cpu.GetDebugInfo());
            return failures;
        }

        /// <summary>
        /// Runs the Status Register Move Test routine.
        /// </summary>
        private static int RunStatusRegisterMoveTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            bus.WriteWord(0x0200, 0x001B);
            WriteProgram(
                bus,
                0x0100,
                0x40E7, // MOVE SR,-(A7)
                0x207C, 0x0000, 0x0200, // MOVEA.L #$200,A0
                0x44D8); // MOVE (A0)+,CCR

            M68kCpu cpu = CreateCpu(bus);
            cpu.Step();

            int failures = 0;
            failures += Expect(log, "MOVE SR,-(A7) stack", cpu.A7 == 0x0FFE && bus.ReadWord(cpu.A7) == 0x2700, "A7=" + cpu.A7.ToString("X8") + " stack=" + bus.ReadWord(cpu.A7).ToString("X4"));
            cpu.Step();
            cpu.Step();
            failures += Expect(log, "MOVE (A0)+,CCR reads word", cpu.GetA(0) == 0x0202 && (cpu.SR & 0x001F) == 0x001B, "A0=" + cpu.GetA(0).ToString("X8") + " SR=" + cpu.SR.ToString("X4"));
            return failures;
        }

        /// <summary>
        /// Runs the Exception Entry Test routine.
        /// </summary>
        private static int RunExceptionEntryTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            bus.WriteLong(32 * 4, 0x00000300);
            WriteProgram(bus, 0x0100, 0x4E71);

            M68kCpu cpu = CreateCpu(bus);
            cpu.EnterException(32);

            int failures = 0;
            failures += Expect(log, "Exception vector PC", cpu.PC == 0x0300, cpu.GetDebugInfo());
            failures += Expect(log, "Exception stack SR", bus.ReadWord(cpu.A7) == 0x2700, "stackSR=" + bus.ReadWord(cpu.A7).ToString("X4"));
            failures += Expect(log, "Exception stack PC", bus.ReadLong(cpu.A7 + 2) == 0x0100, "stackPC=" + bus.ReadLong(cpu.A7 + 2).ToString("X8"));
            return failures;
        }

        /// <summary>
        /// Runs the Address Error Exception Test routine.
        /// </summary>
        private static int RunAddressErrorExceptionTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            bus.WriteLong(3 * 4, 0x00000300);
            WriteProgram(
                bus,
                0x0100,
                0x3039, 0x0000, 0x0201); // MOVE.W $00000201,D0

            M68kCpu cpu = CreateCpu(bus);
            cpu.Step();

            int failures = 0;
            failures += Expect(log, "Odd word access vectors address error", cpu.PC == 0x0300, cpu.GetDebugInfo());
            failures += Expect(log, "Address error frame size", cpu.A7 == 0x0FF2, "A7=" + cpu.A7.ToString("X8"));
            failures += Expect(log, "Address error stack SR", bus.ReadWord(cpu.A7) == 0x2700, "stackSR=" + bus.ReadWord(cpu.A7).ToString("X4"));
            failures += Expect(log, "Address error stack PC", bus.ReadLong(cpu.A7 + 2) == 0x0100, "stackPC=" + bus.ReadLong(cpu.A7 + 2).ToString("X8"));
            failures += Expect(log, "Address error stack opcode", bus.ReadWord(cpu.A7 + 6) == 0x3039, "opcode=" + bus.ReadWord(cpu.A7 + 6).ToString("X4"));
            failures += Expect(log, "Address error stack address", bus.ReadLong(cpu.A7 + 8) == 0x00000201, "addr=" + bus.ReadLong(cpu.A7 + 8).ToString("X8"));
            return failures;
        }

        /// <summary>
        /// Runs the Stop Interrupt Wake Test routine.
        /// </summary>
        private static int RunStopInterruptWakeTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            bus.WriteLong(27 * 4, 0x00000300);
            WriteProgram(
                bus,
                0x0100,
                0x4E72, 0x2000, // STOP #$2000
                0x7001);        // MOVEQ #1,D0, skipped by interrupt vector
            WriteProgram(bus, 0x0300, 0x7007); // MOVEQ #7,D0

            M68kCpu cpu = CreateCpu(bus);
            cpu.Step();
            cpu.RequestInterrupt(3);
            cpu.Step();
            bool vectored = cpu.PC == 0x0300;
            cpu.Step();

            int failures = 0;
            failures += Expect(log, "STOP interrupt vector", vectored, cpu.GetDebugInfo());
            failures += Expect(log, "STOP interrupt handler executes", cpu.PC == 0x0302 && cpu.GetD(0) == 7, cpu.GetDebugInfo());
            failures += Expect(log, "STOP interrupt stacked PC", bus.ReadLong(cpu.A7 + 2) == 0x0104, "stackPC=" + bus.ReadLong(cpu.A7 + 2).ToString("X8"));
            return failures;
        }

        /// <summary>
        /// Runs the Masked Interrupt Line Latch Test routine.
        /// </summary>
        private static int RunMaskedInterruptLineLatchTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            bus.WriteLong(27 * 4, 0x00000300);
            WriteProgram(bus, 0x0100, 0x4E72, 0x2000); // STOP #$2000 lowers interrupt mask to zero
            WriteProgram(bus, 0x0300, 0x7003); // MOVEQ #3,D0

            M68kCpu cpu = CreateCpu(bus);
            cpu.SetInterruptLevel(3);
            bool masked = cpu.PC == 0x0100 && (cpu.SR & 0x0700) == 0x0700;
            cpu.Step();
            bool stoppedBeforeAccept = cpu.PC == 0x0104 && cpu.IsStopped;
            cpu.Step();
            bool vectored = cpu.PC == 0x0300;
            cpu.Step();

            int failures = 0;
            failures += Expect(log, "masked interrupt held while SR mask blocks it", masked, cpu.GetDebugInfo());
            failures += Expect(log, "STOP lowers mask before latched interrupt", stoppedBeforeAccept, cpu.GetDebugInfo());
            failures += Expect(log, "latched interrupt vectors after mask lowers", vectored, cpu.GetDebugInfo());
            failures += Expect(log, "latched interrupt handler executes", cpu.PC == 0x0302 && cpu.GetD(0) == 3, cpu.GetDebugInfo());
            failures += Expect(log, "latched interrupt stacked STOP PC", bus.ReadLong(cpu.A7 + 2) == 0x0104, "stackPC=" + bus.ReadLong(cpu.A7 + 2).ToString("X8"));
            return failures;
        }

        /// <summary>
        /// Runs the Rte User Mode Stack Switch Test routine.
        /// </summary>
        private static int RunRteUserModeStackSwitchTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            bus.WriteWord(0x1000, 0x0010);
            bus.WriteLong(0x1002, 0x00000300);
            WriteProgram(
                bus,
                0x0100,
                0x207C, 0x0000, 0x2000, // MOVEA.L #$2000,A0
                0x4E60,                 // MOVE A0,USP
                0x4E73);                // RTE to a user-mode frame on SSP

            M68kCpu cpu = CreateCpu(bus);
            Step(cpu, 3);

            int failures = 0;
            failures += Expect(log, "RTE user PC", cpu.PC == 0x0300, cpu.GetDebugInfo());
            failures += Expect(log, "RTE user SR", (cpu.SR & 0x2000) == 0, cpu.GetDebugInfo());
            failures += Expect(log, "RTE user A7", cpu.A7 == 0x2000, "A7=" + cpu.A7.ToString("X8"));
            failures += Expect(log, "RTE supervisor stack consumed", cpu.SSP == 0x1006, "SSP=" + cpu.SSP.ToString("X8"));
            return failures;
        }

        /// <summary>
        /// Runs the Privilege Violation From User Mode Test routine.
        /// </summary>
        private static int RunPrivilegeViolationFromUserModeTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            bus.WriteLong(8 * 4, 0x00000400);
            bus.WriteWord(0x1000, 0x0010);
            bus.WriteLong(0x1002, 0x00000300);
            WriteProgram(
                bus,
                0x0100,
                0x207C, 0x0000, 0x2000, // MOVEA.L #$2000,A0
                0x4E60,                 // MOVE A0,USP
                0x4E73);                // RTE to user mode
            WriteProgram(
                bus,
                0x0300,
                0x007C, 0x2000);        // ORI #$2000,SR, privileged in user mode

            M68kCpu cpu = CreateCpu(bus);
            Step(cpu, 4);

            int failures = 0;
            failures += Expect(log, "Privilege vector PC", cpu.PC == 0x0400, cpu.GetDebugInfo());
            failures += Expect(log, "Privilege vector supervisor", (cpu.SR & 0x2000) != 0, cpu.GetDebugInfo());
            failures += Expect(log, "Privilege stack SR", bus.ReadWord(cpu.A7) == 0x0010, "stackSR=" + bus.ReadWord(cpu.A7).ToString("X4"));
            failures += Expect(log, "Privilege stack PC", bus.ReadLong(cpu.A7 + 2) == 0x0300, "stackPC=" + bus.ReadLong(cpu.A7 + 2).ToString("X8"));
            failures += Expect(log, "Privilege user stack saved", cpu.USP == 0x2000, "USP=" + cpu.USP.ToString("X8"));
            return failures;
        }

        /// <summary>
        /// Runs the Privilege Violation Before Operand Fetch Test routine.
        /// </summary>
        private static int RunPrivilegeViolationBeforeOperandFetchTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            bus.WriteLong(8 * 4, 0x00000400);
            bus.WriteWord(0x1000, 0x0010);
            bus.WriteLong(0x1002, 0x00000300);
            bus.WriteWord(0x0500, 0x2700);
            WriteProgram(
                bus,
                0x0100,
                0x207C, 0x0000, 0x2000, // MOVEA.L #$2000,A0
                0x4E60,                 // MOVE A0,USP
                0x4E73);                // RTE to user mode
            WriteProgram(
                bus,
                0x0300,
                0x207C, 0x0000, 0x0500, // MOVEA.L #$500,A0
                0x46D8);                // MOVE (A0)+,SR, privileged before operand fetch

            M68kCpu cpu = CreateCpu(bus);
            Step(cpu, 5);

            int failures = 0;
            failures += Expect(log, "Privilege before operand fetch vector", cpu.PC == 0x0400, cpu.GetDebugInfo());
            failures += Expect(log, "Privilege before operand fetch A0", cpu.GetA(0) == 0x0500, "A0=" + cpu.GetA(0).ToString("X8"));
            failures += Expect(log, "Privilege before operand fetch stacked PC", bus.ReadLong(cpu.A7 + 2) == 0x0306, "stackPC=" + bus.ReadLong(cpu.A7 + 2).ToString("X8"));
            return failures;
        }

        /// <summary>
        /// Runs the Illegal Address Register Destination Test routine.
        /// </summary>
        private static int RunIllegalAddressRegisterDestinationTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            bus.WriteLong(4 * 4, 0x00000300);
            WriteProgram(
                bus,
                0x0100,
                0x207C, 0x0012, 0x3456, // MOVEA.L #$123456,A0
                0x4288);                // CLR.L A0 is illegal on 68000

            M68kCpu cpu = CreateCpu(bus);
            Step(cpu, 2);

            int failures = 0;
            failures += Expect(log, "Illegal An destination vector", cpu.PC == 0x0300, cpu.GetDebugInfo());
            failures += Expect(log, "Illegal An destination preserves register", cpu.GetA(0) == 0x00123456, "A0=" + cpu.GetA(0).ToString("X8"));
            return failures;
        }

        /// <summary>
        /// Runs the Illegal Addq Byte Address Register Test routine.
        /// </summary>
        private static int RunIllegalAddqByteAddressRegisterTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            bus.WriteLong(4 * 4, 0x00000300);
            WriteProgram(
                bus,
                0x0100,
                0x207C, 0x0000, 0x0500, // MOVEA.L #$500,A0
                0x5208);                // ADDQ.B #1,A0 is illegal on 68000

            M68kCpu cpu = CreateCpu(bus);
            Step(cpu, 2);

            int failures = 0;
            failures += Expect(log, "Illegal ADDQ.B An vectors", cpu.PC == 0x0300, cpu.GetDebugInfo());
            failures += Expect(log, "Illegal ADDQ.B An preserves A0", cpu.GetA(0) == 0x0500, "A0=" + cpu.GetA(0).ToString("X8"));
            return failures;
        }

        /// <summary>
        /// Runs the Illegal Effective Address Side Effect Test routine.
        /// </summary>
        private static int RunIllegalEffectiveAddressSideEffectTest(StringBuilder log)
        {
            int failures = 0;

            var moveBus = CreateBus();
            WriteResetVectors(moveBus, 0x1000, 0x0100);
            moveBus.WriteLong(4 * 4, 0x00000300);
            moveBus.WriteByte(0x0500, 0x7A);
            WriteProgram(
                moveBus,
                0x0100,
                0x207C, 0x0000, 0x0500, // MOVEA.L #$500,A0
                0x1240);                // MOVE.B D0,A1 is illegal before source side effects

            M68kCpu moveCpu = CreateCpu(moveBus);
            Step(moveCpu, 2);
            failures += Expect(log, "Illegal MOVE.B to An vectors", moveCpu.PC == 0x0300, moveCpu.GetDebugInfo());
            failures += Expect(log, "Illegal MOVE.B to An preserves A0", moveCpu.GetA(0) == 0x0500, "A0=" + moveCpu.GetA(0).ToString("X8"));

            var leaBus = CreateBus();
            WriteResetVectors(leaBus, 0x1000, 0x0100);
            leaBus.WriteLong(4 * 4, 0x00000300);
            WriteProgram(
                leaBus,
                0x0100,
                0x207C, 0x0000, 0x0500, // MOVEA.L #$500,A0
                0x41D8);                // LEA (A0)+,A0 is illegal

            M68kCpu leaCpu = CreateCpu(leaBus);
            Step(leaCpu, 2);
            failures += Expect(log, "Illegal LEA vectors", leaCpu.PC == 0x0300, leaCpu.GetDebugInfo());
            failures += Expect(log, "Illegal LEA preserves A0", leaCpu.GetA(0) == 0x0500, "A0=" + leaCpu.GetA(0).ToString("X8"));

            var bitBus = CreateBus();
            WriteResetVectors(bitBus, 0x1000, 0x0100);
            bitBus.WriteLong(4 * 4, 0x00000300);
            WriteProgram(
                bitBus,
                0x0100,
                0x207C, 0x0000, 0x1234, // MOVEA.L #$1234,A0
                0x01FA, 0x0000);        // BSET D0,d16(PC) is illegal

            M68kCpu bitCpu = CreateCpu(bitBus);
            Step(bitCpu, 2);
            failures += Expect(log, "Illegal bit op vectors", bitCpu.PC == 0x0300, bitCpu.GetDebugInfo());
            failures += Expect(log, "Illegal bit op preserves A0", bitCpu.GetA(0) == 0x1234, "A0=" + bitCpu.GetA(0).ToString("X8"));

            return failures;
        }

        /// <summary>
        /// Runs the Line Trap Test routine.
        /// </summary>
        private static int RunLineTrapTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            bus.WriteLong(11 * 4, 0x00000340);
            WriteProgram(bus, 0x0100, 0xFFFF);

            M68kCpu cpu = CreateCpu(bus);
            cpu.Step();

            int failures = 0;
            failures += Expect(log, "Line-F vector PC", cpu.PC == 0x0340, cpu.GetDebugInfo());
            failures += Expect(log, "Line-F stacked PC", bus.ReadLong(cpu.A7 + 2) == 0x0102, "stackPC=" + bus.ReadLong(cpu.A7 + 2).ToString("X8"));
            return failures;
        }

        /// <summary>
        /// Runs the Movep Test routine.
        /// </summary>
        private static int RunMovepTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            WriteProgram(
                bus,
                0x0100,
                0x207C, 0x0000, 0x0500, // MOVEA.L #$500,A0
                0x203C, 0x1234, 0x5678, // MOVE.L #$12345678,D0
                0x01C8, 0x0010,         // MOVEP.L D0,$10(A0)
                0x7400,                 // MOVEQ #0,D2
                0x0548, 0x0010,         // MOVEP.L $10(A0),D2
                0x303C, 0x89AB,         // MOVE.W #$89AB,D0
                0x0188, 0x0020,         // MOVEP.W D0,$20(A0)
                0x323C, 0xFFFF,         // MOVE.W #$FFFF,D1
                0x0108, 0x0020);        // MOVEP.W $20(A0),D0

            M68kCpu cpu = CreateCpu(bus);
            Step(cpu, 9);

            int failures = 0;
            failures += Expect(log, "MOVEP.L sparse write byte 0", bus.ReadByte(0x0510) == 0x12, "b0=" + bus.ReadByte(0x0510).ToString("X2"));
            failures += Expect(log, "MOVEP.L sparse write byte 1", bus.ReadByte(0x0512) == 0x34, "b1=" + bus.ReadByte(0x0512).ToString("X2"));
            failures += Expect(log, "MOVEP.L sparse write byte 2", bus.ReadByte(0x0514) == 0x56, "b2=" + bus.ReadByte(0x0514).ToString("X2"));
            failures += Expect(log, "MOVEP.L sparse write byte 3", bus.ReadByte(0x0516) == 0x78, "b3=" + bus.ReadByte(0x0516).ToString("X2"));
            failures += Expect(log, "MOVEP.L read", cpu.GetD(2) == 0x12345678, "D2=" + cpu.GetD(2).ToString("X8"));
            failures += Expect(log, "MOVEP.W sparse write high", bus.ReadByte(0x0520) == 0x89, "b0=" + bus.ReadByte(0x0520).ToString("X2"));
            failures += Expect(log, "MOVEP.W sparse write low", bus.ReadByte(0x0522) == 0xAB, "b1=" + bus.ReadByte(0x0522).ToString("X2"));
            failures += Expect(log, "MOVEP.W read preserves upper D0", cpu.GetD(0) == 0x123489AB, "D0=" + cpu.GetD(0).ToString("X8"));
            return failures;
        }

        /// <summary>
        /// Runs the Trap Instruction Test routine.
        /// </summary>
        private static int RunTrapInstructionTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            bus.WriteLong((32 + 15) * 4, 0x00000380);
            WriteProgram(bus, 0x0100, 0x4E4F);

            M68kCpu cpu = CreateCpu(bus);
            cpu.Step();

            int failures = 0;
            failures += Expect(log, "TRAP #15 vector PC", cpu.PC == 0x0380, cpu.GetDebugInfo());
            failures += Expect(log, "TRAP #15 stacked PC", bus.ReadLong(cpu.A7 + 2) == 0x0102, "stackPC=" + bus.ReadLong(cpu.A7 + 2).ToString("X8"));
            return failures;
        }

        /// <summary>
        /// Runs a Tick-based regression test for deferred CPU write side effects.
        /// </summary>
        private static int RunTickDeferredWriteSideEffectTest(StringBuilder log)
        {
            var bus = CreateBus();
            WriteResetVectors(bus, 0x1000, 0x0100);
            WriteProgram(
                bus,
                0x0100,
                0x33FC, 0x1234, 0x0000, 0x0200, // MOVE.W #$1234,$00000200
                0x4E71);

            M68kCpu cpu = CreateCpu(bus);
            cpu.Tick(true);
            int failures = 0;
            failures += Expect(log, "deferred write not visible in opcode slot", bus.ReadWord(0x0200) == 0, "mem=" + bus.ReadWord(0x0200).ToString("X4"));
            cpu.Tick(false);
            failures += Expect(log, "deferred write waits while bus is blocked", bus.ReadWord(0x0200) == 0, "mem=" + bus.ReadWord(0x0200).ToString("X4"));
            cpu.Tick(true);
            cpu.Tick(true);
            cpu.Tick(true);
            failures += Expect(log, "deferred write not visible before write slot", bus.ReadWord(0x0200) == 0, "mem=" + bus.ReadWord(0x0200).ToString("X4"));
            cpu.Tick(true);
            failures += Expect(log, "deferred write retires on write slot", bus.ReadWord(0x0200) == 0x1234, "mem=" + bus.ReadWord(0x0200).ToString("X4"));
            return failures;
        }

        /// <summary>
        /// Runs a Tick-based regression test for deferred CPU read side effects.
        /// </summary>
        private static int RunTickDeferredReadSideEffectTest(StringBuilder log)
        {
            var ciaA = new Cia8520("CIAA");
            var bus = new AmigaBus(AmigaModel.A500Pal, new CustomRegisterMap(), null, ciaA, null);
            bus.RomOverlayAtZero = false;
            WriteResetVectors(bus, 0x1000, 0x0100);
            WriteProgram(
                bus,
                0x0100,
                0x1039, 0x00BF, 0xED01, // MOVE.B $00BFED01,D0 ; CIAA ICR
                0x4E71);

            ciaA.RequestInterrupt(0x01);
            M68kCpu cpu = CreateCpu(bus);
            cpu.Tick(true);
            int failures = 0;
            failures += Expect(log, "deferred read does not clear ICR in opcode slot", ciaA.InterruptRequest != 0, "ICR=" + ciaA.InterruptRequest.ToString("X2"));
            cpu.Tick(true);
            cpu.Tick(true);
            failures += Expect(log, "deferred read waits until operand slot", ciaA.InterruptRequest != 0, "ICR=" + ciaA.InterruptRequest.ToString("X2"));
            cpu.Tick(true);
            failures += Expect(log, "deferred read clears ICR on operand slot", ciaA.InterruptRequest == 0, "ICR=" + ciaA.InterruptRequest.ToString("X2"));
            return failures;
        }

        /// <summary>
        /// Creates the Bus.
        /// </summary>
        private static AmigaBus CreateBus()
        {
            var registers = new CustomRegisterMap();
            var bus = new AmigaBus(AmigaModel.A500Pal, registers, null, null, null);
            bus.RomOverlayAtZero = false;
            return bus;
        }

        /// <summary>
        /// Creates the CPU.
        /// </summary>
        private static M68kCpu CreateCpu(AmigaBus bus)
        {
            var cpu = new M68kCpu(bus);
            cpu.Reset();
            return cpu;
        }

        /// <summary>
        /// Runs Step.
        /// </summary>
        private static void Step(M68kCpu cpu, int instructions)
        {
            for (int index = 0; index < instructions; index++)
            {
                cpu.Step();
            }
        }

        /// <summary>
        /// Writes the 68000 reset stack pointer and entry point into the self-test bus.
        /// </summary>
        private static void WriteResetVectors(AmigaBus bus, uint stackPointer, uint programCounter)
        {
            bus.WriteLong(0, stackPointer);
            bus.WriteLong(4, programCounter);
        }

        /// <summary>
        /// Writes a big-endian instruction sequence into the self-test bus.
        /// </summary>
        private static void WriteProgram(AmigaBus bus, uint address, params ushort[] words)
        {
            for (int index = 0; index < words.Length; index++)
            {
                bus.WriteWord(address + (uint)(index * 2), words[index]);
            }
        }

        /// <summary>
        /// Runs Expect.
        /// </summary>
        private static int Expect(StringBuilder log, string name, bool condition, string detail)
        {
            if (condition)
            {
                return 0;
            }

            log.AppendLine("FAIL: " + name + " (" + detail + ")");
            return 1;
        }
    }
}
