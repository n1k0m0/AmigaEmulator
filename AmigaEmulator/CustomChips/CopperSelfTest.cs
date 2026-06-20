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
using System.Text;
using AmigaEmulator.Core.Memory;

namespace AmigaEmulator.Core.CustomChips
{
    /// <summary>
    /// Provides Copper Self Test functionality.
    /// </summary>
    public static class CopperSelfTest
    {
        /// <summary>
        /// Defines the DMACON custom register offset or bit mask.
        /// </summary>
        private const uint Dmacon = 0x096;
        /// <summary>
        /// Defines the COPCON custom register offset or bit mask.
        /// </summary>
        private const uint Copcon = 0x02E;
        /// <summary>
        /// Defines the COP2LCH custom register offset or bit mask.
        /// </summary>
        private const uint Cop2lch = 0x084;
        /// <summary>
        /// Defines the COP2LCL custom register offset or bit mask.
        /// </summary>
        private const uint Cop2lcl = 0x086;
        /// <summary>
        /// Defines the COPJMP1 custom register offset or bit mask.
        /// </summary>
        private const uint Copjmp1 = 0x088;
        /// <summary>
        /// Constant for Color 00.
        /// </summary>
        private const uint Color00 = 0x180;
        /// <summary>
        /// Constant for Bltcon 0.
        /// </summary>
        private const uint Bltcon0 = 0x040;

        /// <summary>
        /// Runs the value routine.
        /// </summary>
        public static int Run(StringBuilder log)
        {
            int failures = 0;
            failures += RunCase(log, "OCS protected register ranges", TestProtectedRegisterRanges);
            failures += RunCase(log, "BFD waits for blitter when clear", TestBfdWaitsForBlitterWhenClear);
            failures += RunCase(log, "COPJMP2 rejects system-area targets", TestCopjmp2RejectsSystemAreaTarget);
            failures += RunCase(log, "COPJMP1 read strobes copper list 1", TestCopjmp1ReadStrobesCopperList1);
            log.AppendLine(failures == 0 ? "Copper self-test OK" : "Copper self-test FAILED: " + failures);
            return failures;
        }

        /// <summary>
        /// Runs the Case routine.
        /// </summary>
        private static int RunCase(StringBuilder log, string name, Func<bool> test)
        {
            bool ok;
            try
            {
                ok = test();
            }
            catch (Exception ex)
            {
                log.AppendLine("FAIL " + name + ": " + ex.GetType().Name + " " + ex.Message);
                return 1;
            }

            log.AppendLine((ok ? "OK   " : "FAIL ") + name);
            return ok ? 0 : 1;
        }

        /// <summary>
        /// Runs Test Protected Register Ranges.
        /// </summary>
        private static bool TestProtectedRegisterRanges()
        {
            var harness = new Harness();

            harness.RunMove(0x020, 0x1111);
            if (harness.Registers.ReadWord(0x020) != 0 || harness.LastKind != CopperInstructionKind.BlockedMove)
            {
                return false;
            }

            harness.RunMove(Bltcon0, 0x2222);
            if (harness.Registers.ReadWord(Bltcon0) != 0 || harness.LastKind != CopperInstructionKind.BlockedMove)
            {
                return false;
            }

            harness.Registers.WriteWord(Copcon, 0x0002);
            harness.RunMove(Bltcon0, 0x3333);
            if (harness.Registers.ReadWord(Bltcon0) != 0x3333 || harness.LastKind != CopperInstructionKind.Move)
            {
                return false;
            }

            harness.Registers.WriteWord(Copcon, 0x0000);
            harness.RunMove(Color00, 0x0444);
            return harness.Registers.ReadWord(Color00) == 0x0444 && harness.LastKind == CopperInstructionKind.Move;
        }

        /// <summary>
        /// Runs Test Bfd Waits For Blitter When Clear.
        /// </summary>
        private static bool TestBfdWaitsForBlitterWhenClear()
        {
            var harness = new Harness();
            bool blitterBusy = true;
            harness.Copper.AttachBlitterStatus(() => blitterBusy);
            harness.WriteCopperInstruction(0x0100, 0x0001, 0x7FFE);
            harness.Copper.LoadList(0x0100);

            harness.Copper.Tick(true, 0, 0);
            harness.Copper.Tick(true, 0, 0);
            if (harness.Copper.ProgramCounter != 0x0100)
            {
                return false;
            }

            blitterBusy = false;
            harness.Copper.Tick(true, 0, 0);
            if (harness.Copper.ProgramCounter != 0x0104)
            {
                return false;
            }

            harness.WriteCopperInstruction(0x0200, 0x0001, 0xFFFE);
            harness.Copper.LoadList(0x0200);
            blitterBusy = true;
            harness.Copper.Tick(true, 0, 0);
            harness.Copper.Tick(true, 0, 0);
            return harness.Copper.ProgramCounter == 0x0204;
        }

        /// <summary>
        /// Runs Test Copjmp 2 Rejects System Area Target.
        /// </summary>
        private static bool TestCopjmp2RejectsSystemAreaTarget()
        {
            var harness = new Harness();
            harness.Registers.WriteWord(Cop2lch, 0x0000);
            harness.Registers.WriteWord(Cop2lcl, 0x0676);
            harness.WriteCopperInstruction(0x0100, 0x008A, 0x0000);
            harness.Copper.LoadList(0x0100);

            harness.Copper.Tick(true, 0, 0);
            harness.Copper.Tick(true, 0, 0);
            if (harness.Copper.ProgramCounter != 0x0100 || harness.LastKind != CopperInstructionKind.BlockedMove)
            {
                return false;
            }

            harness.Registers.WriteWord(Cop2lch, 0x0000);
            harness.Registers.WriteWord(Cop2lcl, 0x1000);
            harness.WriteCopperInstruction(0x0100, 0x008A, 0x0000);
            harness.WriteCopperInstruction(0x1000, 0x0180, 0x0123);
            harness.Copper.LoadList(0x0100);

            harness.Copper.Tick(true, 0, 0);
            harness.Copper.Tick(true, 0, 0);
            return harness.Copper.ProgramCounter == 0x1000 && harness.LastKind == CopperInstructionKind.Jump2;
        }

        /// <summary>
        /// Runs Test Copjmp 1 Read Strobes Copper List 1.
        /// </summary>
        private static bool TestCopjmp1ReadStrobesCopperList1()
        {
            var harness = new Harness();
            harness.Registers.WriteWord(0x080, 0x0000);
            harness.Registers.WriteWord(0x082, 0x0100);
            harness.Registers.ReadWord(Copjmp1);
            return harness.Copper.ProgramCounter == 0x0100;
        }

        /// <summary>
        /// Provides Harness functionality.
        /// </summary>
        private sealed class Harness
        {
            /// <summary>
            /// Stores memory.
            /// </summary>
            private readonly ushort[] _memory = new ushort[0x80000 / 2];

            /// <summary>
            /// Initializes a new instance of the Harness class.
            /// </summary>
            public Harness()
            {
                Registers = new CustomRegisterMap();
                Copper = new Copper(Registers, ReadWord);
                Copper.InstructionExecuted += entry => LastKind = entry.Kind;
                Registers.CopperJumpRequested += Copper.StrobeJump;
                Registers.WriteWord(Dmacon, 0x8280);
            }

            /// <summary>
            /// Gets or sets Registers.
            /// </summary>
            public CustomRegisterMap Registers { get; private set; }

            /// <summary>
            /// Gets or sets Copper.
            /// </summary>
            public Copper Copper { get; private set; }

            /// <summary>
            /// Gets or sets Last Kind.
            /// </summary>
            public CopperInstructionKind LastKind { get; private set; }

            /// <summary>
            /// Runs the Move routine.
            /// </summary>
            public void RunMove(uint registerOffset, ushort value)
            {
                WriteCopperInstruction(0x0100, (ushort)(registerOffset & 0x01FE), value);
                Copper.LoadList(0x0100);
                LastKind = CopperInstructionKind.Wait;
                Copper.Tick(true, 0, 0);
                Copper.Tick(true, 0, 0);
            }

            /// <summary>
            /// Writes a two-word Copper instruction into the self-test memory image.
            /// </summary>
            public void WriteCopperInstruction(uint address, ushort first, ushort second)
            {
                WriteWord(address, first);
                WriteWord(address + 2, second);
            }

            /// <summary>
            /// Reads a word from the Copper self-test memory image.
            /// </summary>
            private ushort ReadWord(uint address)
            {
                return _memory[(address & 0x0007FFFEu) >> 1];
            }

            /// <summary>
            /// Writes a word to the Copper self-test memory image.
            /// </summary>
            private void WriteWord(uint address, ushort value)
            {
                _memory[(address & 0x0007FFFEu) >> 1] = value;
            }
        }
    }
}
