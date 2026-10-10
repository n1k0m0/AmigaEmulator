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
using System;
using System.Collections.Generic;
using AmigaEmulator.Core.Cpu;

namespace AmigaEmulator.Tests
{
    internal static class M68kOpcodeMatrixTests
    {
        /// <summary>
        /// Constant for Reset Stack.
        /// </summary>
        private const uint ResetStack = 0x00000F00;
        /// <summary>
        /// Constant for Program Start.
        /// </summary>
        private const uint ProgramStart = 0x00000100;

        public static IEnumerable<ChipTestCase> Build()
        {
            yield return new ChipTestCase(
                "CPU/OpcodeMatrix",
                "all 65536 opcode words execute one isolated step without host exceptions",
                "Motorola M68000 PRM opcode map; exhaustive decoder smoke matrix",
                TestAllOpcodeWordsStepSafely);
            yield return new ChipTestCase(
                "CPU/OpcodeMatrix",
                "implemented opcode families are not accidentally decoded as illegal",
                "Motorola M68000 PRM opcode map; emulator supported-family decode guard",
                TestImplementedFamiliesDecode);
            yield return new ChipTestCase(
                "CPU/OpcodeMatrix",
                "representative arithmetic, logic, branch, shift, and exception vectors match 68000 results",
                "Motorola M68000 PRM condition codes and effective-address semantics",
                TestRepresentativeSemantics);
            yield return new ChipTestCase("CPU/BCD", "packed decimal arithmetic covers every valid byte pair and sticky zero",
                "Motorola M68000 PRM ABCD/SBCD/NBCD; independent decimal arithmetic oracle", TestPackedDecimalArithmetic);
            yield return new ChipTestCase("CPU/BCD", "decimal memory preserves predecrement aliases and A7 byte alignment",
                "Solid Gold score formatting plus M68000 byte predecrement and deferred execution", TestPackedDecimalMemory);
            yield return new ChipTestCase("CPU/Diagnostics", "opcode descriptions preserve trace and privilege changes without per-instruction allocations",
                "M68000 PRM trace/privilege semantics; allocation budget after decoder warmup", TestDescriptions);
        }

        private static void TestDescriptions()
        {
            var bus = CreatePreparedBus(new ushort[] { 0x2001 }); // MOVE.L D1,D0
            var cpu = CreatePreparedCpu(bus);
            for (int i = 0; i < 100; i++) { cpu.SetProgramCounter(ProgramStart); cpu.Step(); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) { cpu.SetProgramCounter(ProgramStart); cpu.Step(); }
            Check.True(GC.GetAllocatedBytesForCurrentThread() - before < 2048, "Warm decoder must not allocate a description per instruction.");
            Check.Equal(cpu.GetD(1), cpu.GetD(0), "Cached descriptions do not skip execution.");
            Check.Equal("MOVE.L D1,D0", cpu.LastInstruction, "Description retains operands.");

            bus = CreatePreparedBus(new ushort[] { 0x7007, 0x46FC, 0xA700, 0x7007 });
            bus.WriteLong(9 * 4, 0x400);
            cpu = CreatePreparedCpu(bus); cpu.TraceEnabled = true;
            Check.Equal("MOVEQ #7,D0", cpu.Step().Instruction, "Normal description.");
            cpu.Step();
            Check.Equal("MOVEQ #7,D0 + TRACE", cpu.Step().Instruction, "Trace suffix reflects the current SR.");
            Check.Equal(0x400u, cpu.PC, "Trace vector must still execute.");

            bus = CreatePreparedBus(new ushort[] { 0x4E60, 0x46FC, 0x0000, 0x4E60 });
            bus.WriteLong(8 * 4, 0x420);
            cpu = CreatePreparedCpu(bus);
            cpu.Step(); Check.Equal("MOVE A0,USP", cpu.LastInstruction, "Supervisor path.");
            cpu.Step(); cpu.Step();
            Check.Equal("PRIVILEGE", cpu.LastInstruction, "A warm opcode must not hide a privilege fault.");
            Check.Equal(0x420u, cpu.PC, "Privilege vector must still execute.");
        }

        private static uint DecimalByte(int value) => (uint)((value / 10 << 4) | value % 10);

        private static void FinishDecimalInstruction(M68kCpu cpu, bool tick)
        {
            if (!tick) { cpu.Step(); cpu.Step(); return; }
            for (int i = 0; i < 80; i++)
            {
                var trace = cpu.Tick(i % 3 != 0);
                if (trace != null && trace.AccessType == M68kTraceAccessType.OpcodeFetch && trace.PcBefore == ProgramStart + 6) return;
            }
            throw new InvalidOperationException("Decimal instruction did not retire: " + cpu.GetDebugInfo());
        }

        private static void TestPackedDecimalArithmetic()
        {
            foreach (bool tick in new[] { false, true })
            foreach (bool add in new[] { false, true })
            {
                ushort opcode = add ? (ushort)0xC300 : (ushort)0x8300; // D0,D1
                var bus = CreatePreparedBus(new ushort[] { 0x44FC, 0, opcode, 0x4E71 });
                var cpu = CreatePreparedCpu(bus); cpu.TraceEnabled = true;
                for (int source = 0; source < 100; source++)
                for (int destination = 0; destination < 100; destination++)
                for (int extend = 0; extend < 2; extend++)
                for (int zero = 0; zero < 2; zero++)
                {
                    bus.WriteWord(ProgramStart + 2, (ushort)(extend * 16 + zero * 4));
                    cpu.Reset(); cpu.SetD(0, 0x12345600 | DecimalByte(source)); cpu.SetD(1, 0xABCDEF00 | DecimalByte(destination));
                    FinishDecimalInstruction(cpu, tick);
                    int decimalResult = add ? destination + source + extend : destination - source - extend;
                    bool carry = decimalResult < 0 || decimalResult >= 100;
                    int wrapped = (decimalResult + 100) % 100;
                    Check.Equal(0xABCDEF00 | DecimalByte(wrapped), cpu.GetD(1), "Decimal result/upper register bytes.");
                    Check.Equal(0x12345600 | DecimalByte(source), cpu.GetD(0), "Decimal source must be preserved.");
                    Check.Equal((ushort)((carry ? 0x11 : 0) | (wrapped == 0 && zero != 0 ? 4 : 0)), (ushort)(cpu.SR & 0x15), "Decimal carry, extend and sticky zero.");
                }
            }
            foreach (bool tick in new[] { false, true })
            for (int source = 0; source < 100; source++)
            for (int extend = 0; extend < 2; extend++)
            {
                var bus = CreatePreparedBus(new ushort[] { 0x44FC, (ushort)(4 + extend * 16), 0x4801, 0x4E71 });
                var cpu = CreatePreparedCpu(bus); cpu.TraceEnabled = true; cpu.SetD(1, 0x12345600 | DecimalByte(source));
                FinishDecimalInstruction(cpu, tick);
                int result = (100 - source - extend) % 100;
                Check.Equal(0x12345600 | DecimalByte(result), cpu.GetD(1), "NBCD decimal negation.");
                Check.Equal((ushort)((source + extend > 0 ? 0x11 : 0) | (result == 0 ? 4 : 0)), (ushort)(cpu.SR & 0x15), "NBCD flags.");
            }
        }

        private static void TestPackedDecimalMemory()
        {
            foreach (bool tick in new[] { false, true })
            foreach (bool add in new[] { false, true })
            for (int source = 0; source < 8; source++)
            for (int destination = 0; destination < 8; destination++)
            {
                ushort opcode = (ushort)((add ? 0xC108 : 0x8108) | destination << 9 | source);
                var bus = CreatePreparedBus(new ushort[] { 0x44FC, 4, opcode, 0x4E71 });
                var cpu = CreatePreparedCpu(bus); cpu.TraceEnabled = true;
                uint srcStep = source == 7 ? 2u : 1u, dstStep = destination == 7 ? 2u : 1u;
                uint srcAddress = 0x800 - srcStep;
                uint dstAddress = source == destination ? srcAddress - dstStep : 0x900 - dstStep;
                cpu.SetA(source, 0x800); if (source != destination) cpu.SetA(destination, 0x900);
                bus.WriteByte(srcAddress, 0x01); bus.WriteByte(dstAddress, 0x99);
                bus.WriteByte(dstAddress - 1, 0xA5);
                FinishDecimalInstruction(cpu, tick);
                Check.Equal((byte)(add ? 0x00 : 0x98), bus.ReadByte(dstAddress), "Decimal memory result.");
                Check.Equal((byte)0x01, bus.ReadByte(srcAddress), "Source byte preserved.");
                Check.Equal((byte)0xA5, bus.ReadByte(dstAddress - 1), "Only destination byte written.");
                Check.Equal(dstAddress, cpu.GetA(destination), "Destination predecrement including A7/aliasing.");
                if (source != destination) Check.Equal(srcAddress, cpu.GetA(source), "Source predecrement.");
            }
            var chainBus = CreatePreparedBus(new ushort[] { 0x44FC, 4, 0xC109, 0xC109, 0xC109, 0xC109 });
            var chainCpu = CreatePreparedCpu(chainBus);
            chainCpu.SetA(0, 0x804); chainCpu.SetA(1, 0x904);
            chainBus.WriteLong(0x800, 0x00009999); chainBus.WriteLong(0x900, 1);
            for (int i = 0; i < 5; i++) chainCpu.Step();
            Check.Equal(0x00010000u, chainBus.ReadLong(0x800), "Four-byte decimal carry chain used by Solid Gold.");
            Check.Equal((ushort)0, (ushort)(chainCpu.SR & 0x15), "Nonzero decimal chain must clear accumulated Z.");
        }

        /// <summary>
        /// Runs Test All Opcode Words Step Safely.
        /// </summary>
        private static void TestAllOpcodeWordsStepSafely()
        {
            int failures = 0;
            string firstFailure = string.Empty;
            for (int value = 0; value <= 0xFFFF; value++)
            {
                ushort opcode = (ushort)value;
                MatrixBus bus = CreatePreparedBus(opcode);
                M68kCpu cpu = CreatePreparedCpu(bus);
                try
                {
                    cpu.Step();
                }
                catch (Exception ex)
                {
                    failures++;
                    if (firstFailure.Length == 0)
                    {
                        firstFailure = "$" + opcode.ToString("X4") + " threw " + ex.GetType().Name + ": " + ex.Message;
                    }
                }
            }

            Check.Equal(0, failures, firstFailure);
        }

        /// <summary>
        /// Runs Test Implemented Families Decode.
        /// </summary>
        private static void TestImplementedFamiliesDecode()
        {
            int failures = 0;
            string firstFailure = string.Empty;
            for (int value = 0; value <= 0xFFFF; value++)
            {
                ushort opcode = (ushort)value;
                if (!IsImplementedFamilyOpcode(opcode))
                {
                    continue;
                }

                MatrixBus bus = CreatePreparedBus(opcode);
                M68kCpu cpu = CreatePreparedCpu(bus);
                cpu.Step();
                string instruction = cpu.LastInstruction ?? string.Empty;
                if (instruction.StartsWith("ILLEGAL", StringComparison.Ordinal) ||
                    instruction.StartsWith("ADDRESS ERROR", StringComparison.Ordinal))
                {
                    failures++;
                    if (firstFailure.Length == 0)
                    {
                        firstFailure = "$" + opcode.ToString("X4") + " decoded as " + instruction;
                    }
                }
            }

            Check.Equal(0, failures, firstFailure);
        }

        /// <summary>
        /// Runs Test Representative Semantics.
        /// </summary>
        private static void TestRepresentativeSemantics()
        {
            RunCase("MOVEQ sign extends and sets N", new ushort[] { 0x70FF }, delegate(M68kCpu cpu, MatrixBus bus)
            {
                Check.Equal(0xFFFFFFFFu, cpu.GetD(0), cpu.GetDebugInfo());
                Check.Equal(0x2708, cpu.SR & 0xFFFF, "MOVEQ negative flags");
            });

            RunCase("ADDI.B sets carry, overflow, negative, extend", new ushort[] { 0x0600, 0x0001 }, delegate(M68kCpu cpu, MatrixBus bus)
            {
                cpu.SetD(0, 0x0000007F);
            }, delegate(M68kCpu cpu, MatrixBus bus)
            {
                Check.Equal(0x80u, cpu.GetD(0) & 0xFF, cpu.GetDebugInfo());
                Check.Equal(0x270A, cpu.SR & 0xFFFF, "ADDI.B flags");
            });

            RunCase("SUBI.W sets zero when result is zero", new ushort[] { 0x0441, 0x0005 }, delegate(M68kCpu cpu, MatrixBus bus)
            {
                cpu.SetD(1, 0x00000005);
            }, delegate(M68kCpu cpu, MatrixBus bus)
            {
                Check.Equal(0u, cpu.GetD(1) & 0xFFFF, cpu.GetDebugInfo());
                Check.True((cpu.SR & 0x0004) != 0, "SUBI.W should set Z. " + cpu.GetDebugInfo());
            });

            RunCase("CMPI.L preserves destination and sets N", new ushort[] { 0x0C82, 0x0000, 0x0010 }, delegate(M68kCpu cpu, MatrixBus bus)
            {
                cpu.SetD(2, 0x00000005);
            }, delegate(M68kCpu cpu, MatrixBus bus)
            {
                Check.Equal(0x00000005u, cpu.GetD(2), cpu.GetDebugInfo());
                Check.True((cpu.SR & 0x0008) != 0, "CMPI.L should set N. " + cpu.GetDebugInfo());
            });

            RunCase("BSET immediate on data register sets bit and old-zero flag", new ushort[] { 0x08C0, 0x0003 }, delegate(M68kCpu cpu, MatrixBus bus)
            {
                cpu.SetD(0, 0);
            }, delegate(M68kCpu cpu, MatrixBus bus)
            {
                Check.Equal(0x00000008u, cpu.GetD(0), cpu.GetDebugInfo());
                Check.True((cpu.SR & 0x0004) != 0, "BSET should set Z when old bit was clear. " + cpu.GetDebugInfo());
            });

            RunCase("ASL.W by one updates result and flags", new ushort[] { 0xE341 }, delegate(M68kCpu cpu, MatrixBus bus)
            {
                cpu.SetD(1, 0x00004000);
            }, delegate(M68kCpu cpu, MatrixBus bus)
            {
                Check.Equal(0x8000u, cpu.GetD(1) & 0xFFFF, cpu.GetDebugInfo());
                Check.True((cpu.SR & 0x000A) == 0x000A, "ASL.W should set N and V. " + cpu.GetDebugInfo());
            });

            RunCase("BRA.S branches relative to extension base", new ushort[] { 0x6004, 0x7001, 0x7002 }, delegate(M68kCpu cpu, MatrixBus bus)
            {
                Check.Equal(0x00000106u, cpu.PC, cpu.GetDebugInfo());
            });

            RunCase("DBEQ loops when condition false and counter not exhausted", new ushort[] { 0x57C8, 0xFFFC }, delegate(M68kCpu cpu, MatrixBus bus)
            {
                cpu.SetD(0, 2);
            }, delegate(M68kCpu cpu, MatrixBus bus)
            {
                Check.Equal(1u, cpu.GetD(0) & 0xFFFF, cpu.GetDebugInfo());
                Check.Equal(0x000000FEu, cpu.PC, cpu.GetDebugInfo());
            });

            RunCase("LINE-A opcode enters vector 10", new ushort[] { 0xA123 }, delegate(M68kCpu cpu, MatrixBus bus)
            {
                Check.True(cpu.LastInstruction.StartsWith("LINEA", StringComparison.Ordinal), cpu.GetDebugInfo());
                Check.Equal(0x00000300u, cpu.PC, cpu.GetDebugInfo());
            });

            RunCase("LINE-F opcode enters vector 11", new ushort[] { 0xF123 }, delegate(M68kCpu cpu, MatrixBus bus)
            {
                Check.True(cpu.LastInstruction.StartsWith("LINEF", StringComparison.Ordinal), cpu.GetDebugInfo());
                Check.Equal(0x00000320u, cpu.PC, cpu.GetDebugInfo());
            });

            RunCase("DIVS overflow leaves Dn unchanged and sets V", new ushort[] { 0x81FC, 0xFFFF }, delegate(M68kCpu cpu, MatrixBus bus)
            {
                cpu.SetD(0, 0x80000000);
            }, delegate(M68kCpu cpu, MatrixBus bus)
            {
                Check.Equal(0x80000000u, cpu.GetD(0), cpu.GetDebugInfo());
                Check.True((cpu.SR & 0x0002) != 0, "DIVS overflow should set V. " + cpu.GetDebugInfo());
            });

            RunCase("NEGX.B with X set detects signed overflow", new ushort[] { 0x003C, 0x0010, 0x4000 }, delegate(M68kCpu cpu, MatrixBus bus)
            {
                cpu.SetD(0, 0x0000007F);
                cpu.Step();
            }, delegate(M68kCpu cpu, MatrixBus bus)
            {
                Check.Equal(0x80u, cpu.GetD(0) & 0xFF, cpu.GetDebugInfo());
                Check.True((cpu.SR & 0x001B) == 0x001B, "NEGX.B should set X,N,V,C. " + cpu.GetDebugInfo());
            });
        }

        /// <summary>
        /// Runs the Case routine.
        /// </summary>
        private static void RunCase(string name, ushort[] words, Action<M68kCpu, MatrixBus> assert)
        {
            RunCase(name, words, null, assert);
        }

        /// <summary>
        /// Runs the Case routine.
        /// </summary>
        private static void RunCase(string name, ushort[] words, Action<M68kCpu, MatrixBus> arrange, Action<M68kCpu, MatrixBus> assert)
        {
            MatrixBus bus = CreatePreparedBus(words);
            M68kCpu cpu = CreatePreparedCpu(bus);
            if (arrange != null)
            {
                arrange(cpu, bus);
            }

            cpu.Step();
            try
            {
                assert(cpu, bus);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(name + ": " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Creates the Prepared Bus.
        /// </summary>
        private static MatrixBus CreatePreparedBus(ushort opcode)
        {
            return CreatePreparedBus(new ushort[]
            {
                opcode,
                0x0000,
                0x0200,
                0x0000,
                0x0400,
                0x4E71,
                0x4E71
            });
        }

        /// <summary>
        /// Creates the Prepared Bus.
        /// </summary>
        private static MatrixBus CreatePreparedBus(ushort[] programWords)
        {
            var bus = new MatrixBus(64 * 1024);
            bus.WriteLong(0, ResetStack);
            bus.WriteLong(4, ProgramStart);
            bus.WriteLong(4u * 4u, 0x00000340u);
            bus.WriteLong(10u * 4u, 0x00000300u);
            bus.WriteLong(11u * 4u, 0x00000320u);
            for (int vector = 24; vector < 48; vector++)
            {
                bus.WriteLong((uint)(vector * 4), 0x00000380u);
            }

            for (int index = 0; index < programWords.Length; index++)
            {
                bus.WriteWord(ProgramStart + (uint)(index * 2), programWords[index]);
            }

            for (uint address = 0x0200; address < 0x0600; address += 2)
            {
                bus.WriteWord(address, 0x4E71);
            }

            return bus;
        }

        /// <summary>
        /// Creates the Prepared CPU.
        /// </summary>
        private static M68kCpu CreatePreparedCpu(MatrixBus bus)
        {
            var cpu = new M68kCpu(bus);
            cpu.Reset();
            for (int index = 0; index < 8; index++)
            {
                cpu.SetD(index, 0x02020202u * (uint)(index + 1));
                cpu.SetA(index, 0x00000200u + (uint)(index * 0x20));
            }

            cpu.SetA(7, ResetStack);
            return cpu;
        }

        /// <summary>
        /// Returns whether Implemented Family Opcode is true.
        /// </summary>
        private static bool IsImplementedFamilyOpcode(ushort opcode)
        {
            if ((opcode & 0xF1F0) == 0xC100 || (opcode & 0xF1F0) == 0x8100)
                return true;
            if ((opcode & 0xFFC0) == 0x4800)
                return IsAlterableDataEa((opcode >> 3) & 7, opcode & 7);
            if ((opcode & 0xF000) == 0xA000 || (opcode & 0xF000) == 0xF000)
            {
                return true;
            }

            if ((opcode & 0xF100) == 0x7000)
            {
                return true;
            }

            if ((opcode & 0xF000) == 0x1000 || (opcode & 0xF000) == 0x2000 || (opcode & 0xF000) == 0x3000)
            {
                return IsImplementedMove(opcode);
            }

            if ((opcode & 0xF000) == 0x6000 ||
                (opcode & 0xF0F8) == 0x50C8)
            {
                return true;
            }

            if ((opcode & 0xF000) == 0xE000)
            {
                return IsValidShiftRotate(opcode);
            }

            if ((opcode & 0xF000) == 0x8000)
            {
                return IsValidOrAnd(opcode);
            }

            if ((opcode & 0xF000) == 0x9000 || (opcode & 0xF000) == 0xD000)
            {
                return IsValidAddSub(opcode);
            }

            if ((opcode & 0xF000) == 0xB000)
            {
                return IsValidCmpEor(opcode);
            }

            if ((opcode & 0xF000) == 0xC000)
            {
                return IsValidAndFamily(opcode);
            }

            if ((opcode & 0xF0C0) == 0x50C0)
            {
                return IsAlterableDataEa((opcode >> 3) & 7, opcode & 7);
            }

            if ((opcode & 0xF000) == 0x5000 && ((opcode >> 6) & 3) != 3)
            {
                return IsValidAddqSubq(opcode);
            }

            if ((opcode & 0xF1F8) == 0xC140 || (opcode & 0xF1F8) == 0xC148 || (opcode & 0xF1F8) == 0xC188 ||
                (opcode & 0xF138) == 0x0108)
            {
                return true;
            }

            if ((opcode & 0xF1C0) == 0xC0C0 || (opcode & 0xF1C0) == 0xC1C0 ||
                (opcode & 0xF1C0) == 0x80C0 || (opcode & 0xF1C0) == 0x81C0)
            {
                return IsEaValid((opcode >> 3) & 7, opcode & 7);
            }

            if (IsImmediateOpcode(opcode) || IsBitOpcode(opcode))
            {
                return true;
            }

            if (((opcode & 0xFF00) == 0x4000 && (opcode & 0xFFC0) != 0x40C0) ||
                (opcode & 0xFF00) == 0x4200 ||
                ((opcode & 0xFF00) == 0x4400 && (opcode & 0xFFC0) != 0x44C0) ||
                (opcode & 0xFF00) == 0x4600 ||
                (opcode & 0xFF00) == 0x4A00)
            {
                return IsSizedDataAlterableOpcode(opcode);
            }

            if ((opcode & 0xFFC0) == 0x40C0)
            {
                return IsAlterableDataEa((opcode >> 3) & 7, opcode & 7);
            }

            if ((opcode & 0xFFC0) == 0x44C0)
            {
                int mode = (opcode >> 3) & 7;
                int register = opcode & 7;
                return mode != 1 && IsEaValid(mode, register);
            }

            if ((opcode & 0xFFC0) == 0x46C0)
            {
                return IsAlterableDataEa((opcode >> 3) & 7, opcode & 7);
            }

            if ((opcode & 0xFFF8) == 0x4E60 ||
                (opcode & 0xFFF8) == 0x4E68 ||
                (opcode & 0xFFF8) == 0x4E50 ||
                (opcode & 0xFFF8) == 0x4E58 ||
                (opcode & 0xFFF8) == 0x4840 ||
                (opcode & 0xFFF8) == 0x4880 ||
                (opcode & 0xFFF8) == 0x48C0 ||
                (opcode & 0xFFF0) == 0x4E40)
            {
                return true;
            }

            if ((opcode & 0xF1C0) == 0x41C0 ||
                (opcode & 0xFFC0) == 0x4840 ||
                (opcode & 0xFFC0) == 0x4EC0 ||
                (opcode & 0xFFC0) == 0x4E80)
            {
                return IsControlEa((opcode >> 3) & 7, opcode & 7);
            }

            if ((opcode & 0xFB80) == 0x4880 || (opcode & 0xFB80) == 0x4C80)
            {
                bool memoryToRegisters = (opcode & 0x0400) != 0;
                return IsValidMovemEa((opcode >> 3) & 7, opcode & 7, memoryToRegisters);
            }

            switch (opcode)
            {
                case 0x4E70:
                case 0x4E71:
                case 0x4E72:
                case 0x4E73:
                case 0x4E75:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Returns whether Implemented Move is true.
        /// </summary>
        private static bool IsImplementedMove(ushort opcode)
        {
            int opSize = (opcode >> 12) & 3;
            int size = opSize == 1 ? 1 : (opSize == 2 ? 4 : 2);
            int destinationRegister = (opcode >> 9) & 7;
            int destinationMode = (opcode >> 6) & 7;
            int sourceMode = (opcode >> 3) & 7;
            int sourceRegister = opcode & 7;
            return !(sourceMode == 1 && size == 1) &&
                !(destinationMode == 1 && size == 1) &&
                !(destinationMode == 7 && destinationRegister >= 2) &&
                IsEaValid(sourceMode, sourceRegister) &&
                IsEaValid(destinationMode, destinationRegister);
        }

        /// <summary>
        /// Returns whether Immediate Opcode is true.
        /// </summary>
        private static bool IsImmediateOpcode(ushort opcode)
        {
            if (opcode == 0x003C || opcode == 0x023C || opcode == 0x0A3C ||
                opcode == 0x007C || opcode == 0x027C || opcode == 0x0A7C)
            {
                return true;
            }

            int operation = opcode & 0xFF00;
            if (operation != 0x0000 && operation != 0x0200 && operation != 0x0400 &&
                operation != 0x0600 && operation != 0x0A00 && operation != 0x0C00)
            {
                return false;
            }

            int sizeCode = (opcode >> 6) & 3;
            int mode = (opcode >> 3) & 7;
            int register = opcode & 7;
            return sizeCode != 3 && IsAlterableDataEa(mode, register);
        }

        /// <summary>
        /// Returns whether Valid Addq Subq is true.
        /// </summary>
        private static bool IsValidAddqSubq(ushort opcode)
        {
            int sizeCode = (opcode >> 6) & 3;
            int size = sizeCode == 0 ? 1 : (sizeCode == 1 ? 2 : 4);
            int mode = (opcode >> 3) & 7;
            int register = opcode & 7;
            if (mode == 1)
            {
                return size != 1;
            }

            return IsAlterableDataEa(mode, register);
        }

        /// <summary>
        /// Returns whether Valid Or And is true.
        /// </summary>
        private static bool IsValidOrAnd(ushort opcode)
        {
            int operationMode = (opcode >> 6) & 7;
            int mode = (opcode >> 3) & 7;
            int register = opcode & 7;
            if (operationMode <= 2)
            {
                return IsEaValid(mode, register);
            }

            if (operationMode >= 4 && operationMode <= 6)
            {
                return IsAlterableMemoryEa(mode, register);
            }

            return false;
        }

        /// <summary>
        /// Returns whether Valid And Family is true.
        /// </summary>
        private static bool IsValidAndFamily(ushort opcode)
        {
            if ((opcode & 0xF1F8) == 0xC140 || (opcode & 0xF1F8) == 0xC148 || (opcode & 0xF1F8) == 0xC188)
            {
                return true;
            }

            return IsValidOrAnd(opcode);
        }

        /// <summary>
        /// Returns whether Valid Add Sub is true.
        /// </summary>
        private static bool IsValidAddSub(ushort opcode)
        {
            int operationMode = (opcode >> 6) & 7;
            int mode = (opcode >> 3) & 7;
            int register = opcode & 7;
            if (operationMode <= 3 || operationMode == 7)
            {
                return IsEaValid(mode, register);
            }

            if (operationMode >= 4 && operationMode <= 6)
            {
                return mode == 0 || mode == 1 || IsAlterableMemoryEa(mode, register);
            }

            return false;
        }

        /// <summary>
        /// Returns whether Valid Cmp Eor is true.
        /// </summary>
        private static bool IsValidCmpEor(ushort opcode)
        {
            int operationMode = (opcode >> 6) & 7;
            int mode = (opcode >> 3) & 7;
            int register = opcode & 7;
            if (operationMode <= 3 || operationMode == 7)
            {
                return IsEaValid(mode, register);
            }

            if (operationMode >= 4 && operationMode <= 6)
            {
                return mode == 1 || IsAlterableDataEa(mode, register);
            }

            return false;
        }

        /// <summary>
        /// Returns whether Valid Shift Rotate is true.
        /// </summary>
        private static bool IsValidShiftRotate(ushort opcode)
        {
            if ((opcode & 0x00C0) == 0x00C0)
            {
                return IsAlterableMemoryEa((opcode >> 3) & 7, opcode & 7);
            }

            return ((opcode >> 6) & 3) != 3;
        }

        /// <summary>
        /// Returns whether Sized Data Alterable Opcode is true.
        /// </summary>
        private static bool IsSizedDataAlterableOpcode(ushort opcode)
        {
            int sizeCode = (opcode >> 6) & 3;
            int mode = (opcode >> 3) & 7;
            int register = opcode & 7;
            return sizeCode != 3 && IsAlterableDataEa(mode, register);
        }

        /// <summary>
        /// Returns whether Bit Opcode is true.
        /// </summary>
        private static bool IsBitOpcode(ushort opcode)
        {
            int immediateOperation = opcode & 0xFFC0;
            if (immediateOperation == 0x0800 || immediateOperation == 0x0840 ||
                immediateOperation == 0x0880 || immediateOperation == 0x08C0)
            {
                return IsValidBitOperationEa((opcode >> 3) & 7, opcode & 7, immediateOperation != 0x0800);
            }

            int dynamicOperation = opcode & 0xF1C0;
            return (dynamicOperation == 0x0100 || dynamicOperation == 0x0140 ||
                dynamicOperation == 0x0180 || dynamicOperation == 0x01C0) &&
                IsValidBitOperationEa((opcode >> 3) & 7, opcode & 7, dynamicOperation != 0x0100);
        }

        /// <summary>
        /// Returns whether Ea Valid is true.
        /// </summary>
        private static bool IsEaValid(int mode, int register)
        {
            return mode >= 0 && mode <= 6 || (mode == 7 && register <= 4);
        }

        /// <summary>
        /// Returns whether Alterable Data Ea is true.
        /// </summary>
        private static bool IsAlterableDataEa(int mode, int register)
        {
            if (mode == 0 || mode == 2 || mode == 3 || mode == 4 || mode == 5 || mode == 6)
            {
                return true;
            }

            return mode == 7 && register <= 1;
        }

        /// <summary>
        /// Returns whether Control Ea is true.
        /// </summary>
        private static bool IsControlEa(int mode, int register)
        {
            return mode == 2 || mode == 5 || mode == 6 || (mode == 7 && register <= 3);
        }

        /// <summary>
        /// Returns whether Alterable Memory Ea is true.
        /// </summary>
        private static bool IsAlterableMemoryEa(int mode, int register)
        {
            return mode == 2 || mode == 3 || mode == 4 || mode == 5 || mode == 6 || (mode == 7 && register <= 1);
        }

        /// <summary>
        /// Returns whether Memory Ea is true.
        /// </summary>
        private static bool IsMemoryEa(int mode, int register)
        {
            return IsControlEa(mode, register) || mode == 3 || mode == 4;
        }

        /// <summary>
        /// Returns whether Valid Bit Operation Ea is true.
        /// </summary>
        private static bool IsValidBitOperationEa(int mode, int register, bool forWrite)
        {
            if (mode == 0)
            {
                return true;
            }

            return forWrite ? IsAlterableMemoryEa(mode, register) : IsMemoryEa(mode, register);
        }

        /// <summary>
        /// Returns whether Valid Movem Ea is true.
        /// </summary>
        private static bool IsValidMovemEa(int mode, int register, bool memoryToRegisters)
        {
            if (memoryToRegisters)
            {
                return mode == 3 || IsControlEa(mode, register);
            }

            return mode == 4 || (mode != 3 && IsAlterableMemoryEa(mode, register));
        }

        private sealed class MatrixBus : IM68kBus
        {
            /// <summary>
            /// Stores memory.
            /// </summary>
            private readonly byte[] _memory;

            /// <summary>
            /// Initializes a new instance of the MatrixBus class.
            /// </summary>
            public MatrixBus(int bytes)
            {
                _memory = new byte[bytes];
            }

            /// <summary>
            /// Reads an 8-bit value from the opcode-matrix test bus.
            /// </summary>
            public byte ReadByte(uint address)
            {
                return _memory[(int)(address % (uint)_memory.Length)];
            }

            /// <summary>
            /// Reads a big-endian 16-bit value from the opcode-matrix test bus.
            /// </summary>
            public ushort ReadWord(uint address)
            {
                return (ushort)((ReadByte(address) << 8) | ReadByte(address + 1));
            }

            /// <summary>
            /// Reads a big-endian 32-bit value from the opcode-matrix test bus.
            /// </summary>
            public uint ReadLong(uint address)
            {
                return ((uint)ReadWord(address) << 16) | ReadWord(address + 2);
            }

            /// <summary>
            /// Writes an 8-bit value to the opcode-matrix test bus.
            /// </summary>
            public void WriteByte(uint address, byte value)
            {
                _memory[(int)(address % (uint)_memory.Length)] = value;
            }

            /// <summary>
            /// Writes a big-endian 16-bit value to the opcode-matrix test bus.
            /// </summary>
            public void WriteWord(uint address, ushort value)
            {
                WriteByte(address, (byte)(value >> 8));
                WriteByte(address + 1, (byte)value);
            }

            /// <summary>
            /// Writes a big-endian 32-bit value to the opcode-matrix test bus.
            /// </summary>
            public void WriteLong(uint address, uint value)
            {
                WriteWord(address, (ushort)(value >> 16));
                WriteWord(address + 2, (ushort)value);
            }
        }
    }
}
