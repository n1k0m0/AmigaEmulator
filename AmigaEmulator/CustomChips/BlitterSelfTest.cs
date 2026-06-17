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
using AmigaEmulator.Core.Memory;

namespace AmigaEmulator.Core.CustomChips
{
    /// <summary>
    /// Provides Blitter Self Test behavior used by the emulator.
    /// </summary>
    public static class BlitterSelfTest
    {
        /// <summary>
        /// Defines the Bltcon0 constant.
        /// </summary>
        private const uint Bltcon0 = 0x040;
        /// <summary>
        /// Defines the Bltcon1 constant.
        /// </summary>
        private const uint Bltcon1 = 0x042;
        /// <summary>
        /// Defines the Bltafwm constant.
        /// </summary>
        private const uint Bltafwm = 0x044;
        /// <summary>
        /// Defines the Bltalwm constant.
        /// </summary>
        private const uint Bltalwm = 0x046;
        /// <summary>
        /// Defines the Bltcpt constant.
        /// </summary>
        private const uint Bltcpt = 0x048;
        /// <summary>
        /// Defines the Bltbpt constant.
        /// </summary>
        private const uint Bltbpt = 0x04C;
        /// <summary>
        /// Defines the Bltapt constant.
        /// </summary>
        private const uint Bltapt = 0x050;
        /// <summary>
        /// Defines the Bltdpt constant.
        /// </summary>
        private const uint Bltdpt = 0x054;
        /// <summary>
        /// Defines the Bltsize constant.
        /// </summary>
        private const uint Bltsize = 0x058;
        /// <summary>
        /// Defines the Bltcmod constant.
        /// </summary>
        private const uint Bltcmod = 0x060;
        /// <summary>
        /// Defines the Bltbmod constant.
        /// </summary>
        private const uint Bltbmod = 0x062;
        /// <summary>
        /// Defines the Bltamod constant.
        /// </summary>
        private const uint Bltamod = 0x064;
        /// <summary>
        /// Defines the Bltdmod constant.
        /// </summary>
        private const uint Bltdmod = 0x066;
        /// <summary>
        /// Defines the Bltcdat constant.
        /// </summary>
        private const uint Bltcdat = 0x070;
        /// <summary>
        /// Defines the Bltbdat constant.
        /// </summary>
        private const uint Bltbdat = 0x072;
        /// <summary>
        /// Defines the Bltadat constant.
        /// </summary>
        private const uint Bltadat = 0x074;

        /// <summary>
        /// Runs the value routine.
        /// </summary>
        public static int Run(StringBuilder log)
        {
            int failures = 0;
            failures += RunCase(log, "disabled source constants feed minterm", TestDisabledSourceConstants);
            failures += RunCase(log, "disabled source mask does not corrupt constant latch", TestDisabledSourceMaskKeepsLatch);
            failures += RunCase(log, "inclusive fill scans right-to-left", TestInclusiveFill);
            failures += RunCase(log, "exclusive fill scans right-to-left", TestExclusiveFill);
            failures += RunCase(log, "descending blit adds signed modulo unchanged", TestDescendingModuloUsesRegisterValue);
            failures += RunCase(log, "descending blit applies edge masks by screen word", TestDescendingMasks);
            failures += RunCase(log, "descending blit shift carries from previous fetch", TestDescendingShiftCarry);
            failures += RunCase(log, "blitter busy is visible immediately after BLTSIZE", TestBusyVisibleImmediatelyAfterStart);
            failures += RunCase(log, "block blit performs A/B/C reads before D write", TestBlockMicroPipelineWritesOnDSlot);
            failures += RunCase(log, "line mode draws advancing mask bits", TestLineModeHorizontal);
            failures += RunCase(log, "line mode writes when D enabled without C", TestLineModeDWithoutC);
            failures += RunCase(log, "line mode does not write when D disabled", TestLineModeCWithoutD);
            failures += RunCase(log, "line mode zero flag follows result when D disabled", TestLineModeZeroFlagWithoutD);
            failures += RunCase(log, "line mode destination follows C pointer", TestLineModeDestinationFollowsC);
            log.AppendLine(failures == 0 ? "Blitter self-test OK" : "Blitter self-test FAILED: " + failures);
            return failures;
        }

        /// <summary>
        /// Runs the Case routine.
        /// </summary>
        private static int RunCase(StringBuilder log, string name, System.Func<bool> test)
        {
            bool ok = false;
            try
            {
                ok = test();
            }
            catch (System.Exception ex)
            {
                log.AppendLine("FAIL " + name + ": " + ex.GetType().Name + " " + ex.Message);
                return 1;
            }

            log.AppendLine((ok ? "OK   " : "FAIL ") + name);
            return ok ? 0 : 1;
        }

        /// <summary>
        /// Executes the Test Disabled Source Constants logic.
        /// </summary>
        private static bool TestDisabledSourceConstants()
        {
            var harness = new Harness();
            harness.WriteWord(0x0200, 0x0000);
            harness.WritePointer(Bltcpt, 0x0200);
            harness.WritePointer(Bltdpt, 0x0300);
            harness.Registers.WriteWord(Bltadat, 0xFFFF);
            harness.Registers.WriteWord(Bltbdat, 0xFFFF);
            harness.Registers.WriteWord(Bltcdat, 0x0000);
            harness.Registers.WriteWord(Bltafwm, 0xFFFF);
            harness.Registers.WriteWord(Bltalwm, 0xFFFF);
            harness.Registers.WriteWord(Bltcon0, 0x03CA);
            harness.Registers.WriteWord(Bltcon1, 0x0000);
            harness.StartAndRun(Size(1, 1));
            return harness.ReadWord(0x0300) == 0xFFFF;
        }

        /// <summary>
        /// Executes the Test Disabled Source Mask Keeps Latch logic.
        /// </summary>
        private static bool TestDisabledSourceMaskKeepsLatch()
        {
            var harness = new Harness();
            harness.WritePointer(Bltdpt, 0x0300);
            harness.Registers.WriteWord(Bltadat, 0xFFFF);
            harness.Registers.WriteWord(Bltafwm, 0xFF00);
            harness.Registers.WriteWord(Bltalwm, 0x00FF);
            harness.Registers.WriteWord(Bltcon0, 0x01F0);
            harness.Registers.WriteWord(Bltcon1, 0x0000);
            harness.StartAndRun(Size(2, 1));
            return harness.ReadWord(0x0300) == 0xFF00 &&
                harness.ReadWord(0x0302) == 0x00FF &&
                harness.Registers.ReadWord(Bltadat) == 0xFFFF;
        }

        /// <summary>
        /// Executes the Test Inclusive Fill logic.
        /// </summary>
        private static bool TestInclusiveFill()
        {
            var harness = new Harness();
            harness.WriteWord(0x0200, 0x2418);
            harness.WritePointer(Bltcpt, 0x0200);
            harness.WritePointer(Bltdpt, 0x0300);
            harness.Registers.WriteWord(Bltadat, 0x0000);
            harness.Registers.WriteWord(Bltbdat, 0x0000);
            harness.Registers.WriteWord(Bltafwm, 0xFFFF);
            harness.Registers.WriteWord(Bltalwm, 0xFFFF);
            harness.Registers.WriteWord(Bltcon0, 0x03CA);
            harness.Registers.WriteWord(Bltcon1, 0x000A);
            harness.StartAndRun(Size(1, 1));
            return harness.ReadWord(0x0300) == 0x3C18;
        }

        /// <summary>
        /// Executes the Test Exclusive Fill logic.
        /// </summary>
        private static bool TestExclusiveFill()
        {
            var harness = new Harness();
            harness.WriteWord(0x0200, 0x2418);
            harness.WritePointer(Bltcpt, 0x0200);
            harness.WritePointer(Bltdpt, 0x0300);
            harness.Registers.WriteWord(Bltadat, 0x0000);
            harness.Registers.WriteWord(Bltbdat, 0x0000);
            harness.Registers.WriteWord(Bltafwm, 0xFFFF);
            harness.Registers.WriteWord(Bltalwm, 0xFFFF);
            harness.Registers.WriteWord(Bltcon0, 0x03CA);
            harness.Registers.WriteWord(Bltcon1, 0x0012);
            harness.StartAndRun(Size(1, 1));
            return harness.ReadWord(0x0300) == 0x1C08;
        }

        /// <summary>
        /// Executes the Test Descending Modulo Uses Register Value logic.
        /// </summary>
        private static bool TestDescendingModuloUsesRegisterValue()
        {
            var harness = new Harness();
            harness.WriteWord(0x0200, 0x1111);
            harness.WriteWord(0x0202, 0x2222);
            harness.WriteWord(0x0210, 0x3333);
            harness.WriteWord(0x0212, 0x4444);
            harness.WritePointer(Bltapt, 0x0212);
            harness.WritePointer(Bltdpt, 0x0312);
            harness.Registers.WriteWord(Bltafwm, 0xFFFF);
            harness.Registers.WriteWord(Bltalwm, 0xFFFF);
            harness.Registers.WriteWord(Bltamod, 0xFFF4);
            harness.Registers.WriteWord(Bltdmod, 0xFFF4);
            harness.Registers.WriteWord(Bltcon0, 0x09F0);
            harness.Registers.WriteWord(Bltcon1, 0x0002);
            harness.StartAndRun(Size(2, 2));
            return harness.ReadWord(0x0300) == 0x1111 &&
                harness.ReadWord(0x0302) == 0x2222 &&
                harness.ReadWord(0x0310) == 0x3333 &&
                harness.ReadWord(0x0312) == 0x4444;
        }

        /// <summary>
        /// Executes the Test Descending Masks logic.
        /// </summary>
        private static bool TestDescendingMasks()
        {
            var harness = new Harness();
            harness.WriteWord(0x0200, 0xFFFF);
            harness.WriteWord(0x0202, 0xFFFF);
            harness.WriteWord(0x0204, 0xFFFF);
            harness.WritePointer(Bltapt, 0x0204);
            harness.WritePointer(Bltdpt, 0x0304);
            harness.Registers.WriteWord(Bltafwm, 0x0FFF);
            harness.Registers.WriteWord(Bltalwm, 0xFFF0);
            harness.Registers.WriteWord(Bltamod, 0x0000);
            harness.Registers.WriteWord(Bltdmod, 0x0000);
            harness.Registers.WriteWord(Bltcon0, 0x09F0);
            harness.Registers.WriteWord(Bltcon1, 0x0002);
            harness.StartAndRun(Size(3, 1));
            return harness.ReadWord(0x0300) == 0x0FFF &&
                harness.ReadWord(0x0302) == 0xFFFF &&
                harness.ReadWord(0x0304) == 0xFFF0;
        }

        /// <summary>
        /// Executes the Test Descending Shift Carry logic.
        /// </summary>
        private static bool TestDescendingShiftCarry()
        {
            var harness = new Harness();
            harness.WriteWord(0x0200, 0x1234);
            harness.WriteWord(0x0202, 0xABCD);
            harness.WriteWord(0x0204, 0x8001);
            harness.WritePointer(Bltapt, 0x0204);
            harness.WritePointer(Bltdpt, 0x0304);
            harness.Registers.WriteWord(Bltafwm, 0xFFFF);
            harness.Registers.WriteWord(Bltalwm, 0xFFFF);
            harness.Registers.WriteWord(Bltamod, 0x0000);
            harness.Registers.WriteWord(Bltdmod, 0x0000);
            harness.Registers.WriteWord(Bltcon0, 0x49F0);
            harness.Registers.WriteWord(Bltcon1, 0x0002);
            harness.StartAndRun(Size(3, 1));
            return harness.ReadWord(0x0304) == 0x0010 &&
                harness.ReadWord(0x0302) == 0xBCD8 &&
                harness.ReadWord(0x0300) == 0x234A;
        }

        /// <summary>
        /// Verifies that DMACONR-style busy status is visible as soon as BLTSIZE starts a blit.
        /// </summary>
        private static bool TestBusyVisibleImmediatelyAfterStart()
        {
            var harness = new Harness();
            harness.WriteWord(0x0200, 0xFFFF);
            harness.WritePointer(Bltcpt, 0x0200);
            harness.WritePointer(Bltdpt, 0x0200);
            harness.Registers.WriteWord(Bltcon0, 0x03CA);
            harness.Registers.WriteWord(Bltcon1, 0x0000);
            harness.Registers.WriteWord(Bltsize, Size(1, 1));
            harness.Blitter.NotifyCustomRegisterWrite(Bltsize, 2);

            return harness.Blitter.Busy && harness.Blitter.DmaPending;
        }

        /// <summary>
        /// Executes the block micro-pipeline phase-order regression test.
        /// </summary>
        private static bool TestBlockMicroPipelineWritesOnDSlot()
        {
            var harness = new Harness();
            harness.WriteWord(0x0200, 0xBEEF);
            harness.WriteWord(0x0220, 0x1234);
            harness.WriteWord(0x0240, 0x5678);
            harness.WriteWord(0x0300, 0x0000);
            harness.WritePointer(Bltapt, 0x0200);
            harness.WritePointer(Bltbpt, 0x0220);
            harness.WritePointer(Bltcpt, 0x0240);
            harness.WritePointer(Bltdpt, 0x0300);
            harness.Registers.WriteWord(Bltafwm, 0xFFFF);
            harness.Registers.WriteWord(Bltalwm, 0xFFFF);
            harness.Registers.WriteWord(Bltcon0, 0x0FF0);
            harness.Registers.WriteWord(Bltcon1, 0x0000);
            harness.Registers.WriteWord(Bltsize, Size(1, 1));
            harness.Blitter.NotifyCustomRegisterWrite(Bltsize, 2);

            harness.Blitter.Tick(true);
            bool unchangedAfterA = harness.ReadWord(0x0300) == 0x0000;
            harness.Blitter.Tick(true);
            bool unchangedAfterB = harness.ReadWord(0x0300) == 0x0000;
            harness.Blitter.Tick(true);
            bool unchangedAfterC = harness.ReadWord(0x0300) == 0x0000;
            harness.Blitter.Tick(true);
            return unchangedAfterA &&
                unchangedAfterB &&
                unchangedAfterC &&
                harness.ReadWord(0x0300) == 0xBEEF &&
                !harness.Blitter.DmaPending;
        }

        /// <summary>
        /// Executes the Test Line Mode Horizontal logic.
        /// </summary>
        private static bool TestLineModeHorizontal()
        {
            var harness = new Harness();
            harness.WriteWord(0x0200, 0x0000);
            harness.WritePointer(Bltapt, 0x8000);
            harness.WritePointer(Bltcpt, 0x0200);
            harness.WritePointer(Bltdpt, 0x0200);
            harness.Registers.WriteWord(Bltadat, 0x8000);
            harness.Registers.WriteWord(Bltbdat, 0xFFFF);
            harness.Registers.WriteWord(Bltafwm, 0xFFFF);
            harness.Registers.WriteWord(Bltalwm, 0xFFFF);
            harness.Registers.WriteWord(Bltamod, 0x0000);
            harness.Registers.WriteWord(Bltbmod, 0x0000);
            harness.Registers.WriteWord(Bltcmod, 0x0028);
            harness.Registers.WriteWord(Bltdmod, 0x0028);
            harness.Registers.WriteWord(Bltcon0, 0x0BEA);
            harness.Registers.WriteWord(Bltcon1, 0x0051);
            harness.StartAndRun(Size(1, 3));
            return harness.ReadWord(0x0200) == 0xE000;
        }

        /// <summary>
        /// Executes the Test Line Mode D Without C logic.
        /// </summary>
        private static bool TestLineModeDWithoutC()
        {
            var harness = new Harness();
            harness.WriteWord(0x0200, 0x0000);
            harness.WritePointer(Bltapt, 0x8000);
            harness.WritePointer(Bltdpt, 0x0200);
            harness.Registers.WriteWord(Bltadat, 0x8000);
            harness.Registers.WriteWord(Bltbdat, 0xFFFF);
            harness.Registers.WriteWord(Bltcdat, 0x0000);
            harness.Registers.WriteWord(Bltafwm, 0xFFFF);
            harness.Registers.WriteWord(Bltalwm, 0xFFFF);
            harness.Registers.WriteWord(Bltamod, 0x0000);
            harness.Registers.WriteWord(Bltbmod, 0x0000);
            harness.Registers.WriteWord(Bltdmod, 0x0028);
            harness.Registers.WriteWord(Bltcon0, 0x09EA);
            harness.Registers.WriteWord(Bltcon1, 0x0051);
            harness.StartAndRun(Size(1, 3));
            return harness.ReadWord(0x0200) == 0x2000;
        }

        /// <summary>
        /// Executes the Test Line Mode C Without D logic.
        /// </summary>
        private static bool TestLineModeCWithoutD()
        {
            var harness = new Harness();
            harness.WriteWord(0x0200, 0x1234);
            harness.WritePointer(Bltapt, 0x8000);
            harness.WritePointer(Bltcpt, 0x0200);
            harness.WritePointer(Bltdpt, 0x0200);
            harness.Registers.WriteWord(Bltadat, 0x8000);
            harness.Registers.WriteWord(Bltbdat, 0xFFFF);
            harness.Registers.WriteWord(Bltafwm, 0xFFFF);
            harness.Registers.WriteWord(Bltalwm, 0xFFFF);
            harness.Registers.WriteWord(Bltamod, 0x0000);
            harness.Registers.WriteWord(Bltbmod, 0x0000);
            harness.Registers.WriteWord(Bltcmod, 0x0028);
            harness.Registers.WriteWord(Bltcon0, 0x0AEA);
            harness.Registers.WriteWord(Bltcon1, 0x0051);
            harness.StartAndRun(Size(1, 3));
            return harness.ReadWord(0x0200) == 0x1234;
        }

        /// <summary>
        /// Executes the Test Line Mode Zero Flag Without D logic.
        /// </summary>
        private static bool TestLineModeZeroFlagWithoutD()
        {
            var harness = new Harness();
            harness.WriteWord(0x0200, 0x0000);
            harness.WritePointer(Bltapt, 0x8000);
            harness.WritePointer(Bltcpt, 0x0200);
            harness.WritePointer(Bltdpt, 0x0200);
            harness.Registers.WriteWord(Bltadat, 0x8000);
            harness.Registers.WriteWord(Bltbdat, 0xFFFF);
            harness.Registers.WriteWord(Bltafwm, 0xFFFF);
            harness.Registers.WriteWord(Bltalwm, 0xFFFF);
            harness.Registers.WriteWord(Bltamod, 0x0000);
            harness.Registers.WriteWord(Bltbmod, 0x0000);
            harness.Registers.WriteWord(Bltcmod, 0x0028);
            harness.Registers.WriteWord(Bltcon0, 0x0AEA);
            harness.Registers.WriteWord(Bltcon1, 0x0051);
            harness.StartAndRun(Size(1, 1));
            return harness.ReadWord(0x0200) == 0x0000 && !harness.Blitter.ZeroFlag;
        }

        /// <summary>
        /// Executes the Test Line Mode Destination Follows C logic.
        /// </summary>
        private static bool TestLineModeDestinationFollowsC()
        {
            var harness = new Harness();
            harness.WritePointer(Bltapt, 0xFFEA);
            harness.WritePointer(Bltcpt, 0x0300);
            harness.WritePointer(Bltdpt, 0x0300);
            harness.Registers.WriteWord(Bltadat, 0x8000);
            harness.Registers.WriteWord(Bltbdat, 0xFFFF);
            harness.Registers.WriteWord(Bltafwm, 0xFFFF);
            harness.Registers.WriteWord(Bltalwm, 0xFFFF);
            harness.Registers.WriteWord(Bltamod, 0xFFD4);
            harness.Registers.WriteWord(Bltbmod, 0x0000);
            harness.Registers.WriteWord(Bltcmod, 0x0028);
            harness.Registers.WriteWord(Bltdmod, 0x0000);
            harness.Registers.WriteWord(Bltcon0, 0x0BEA);
            harness.Registers.WriteWord(Bltcon1, 0x8041);
            harness.StartAndRun(Size(2, 3));
            return harness.ReadWord(0x0300) == 0x8000 &&
                harness.ReadWord(0x0328) == 0x8000 &&
                harness.ReadWord(0x0350) == 0x8000 &&
                harness.ReadWord(0x0302) == 0x0000;
        }

        /// <summary>
        /// Executes the Size logic.
        /// </summary>
        private static ushort Size(int widthWords, int heightLines)
        {
            return (ushort)(((heightLines & 0x03FF) << 6) | (widthWords & 0x003F));
        }

        /// <summary>
        /// Provides Harness behavior used by the emulator.
        /// </summary>
        private sealed class Harness
        {
            /// <summary>
            /// Tracks the memory.
            /// </summary>
            private readonly ushort[] _memory = new ushort[0x80000 / 2];

            /// <summary>
            /// Initializes a new instance of the Harness class.
            /// </summary>
            public Harness()
            {
                Registers = new CustomRegisterMap();
                Blitter = new Blitter(Registers);
                Blitter.AttachChipMemory(ReadWord, WriteWord);
            }

            /// <summary>
            /// Gets or updates the Registers.
            /// </summary>
            public CustomRegisterMap Registers { get; private set; }

            /// <summary>
            /// Gets or updates the Blitter.
            /// </summary>
            public Blitter Blitter { get; private set; }

            /// <summary>
            /// Reads a chip-memory word for the Blitter self-test fixture.
            /// </summary>
            public ushort ReadWord(uint address)
            {
                return _memory[(address & 0x0007FFFEu) >> 1];
            }

            /// <summary>
            /// Writes a chip-memory word for the Blitter self-test fixture.
            /// </summary>
            public void WriteWord(uint address, ushort value)
            {
                _memory[(address & 0x0007FFFEu) >> 1] = value;
            }

            /// <summary>
            /// Stores a Blitter pointer pair in the self-test register map.
            /// </summary>
            public void WritePointer(uint offset, uint value)
            {
                Registers.WriteWord(offset, (ushort)(value >> 16));
                Registers.WriteWord(offset + 2, (ushort)value);
            }

            /// <summary>
            /// Executes the Start And Run logic.
            /// </summary>
            public void StartAndRun(ushort size)
            {
                Registers.WriteWord(Bltsize, size);
                Blitter.NotifyCustomRegisterWrite(Bltsize, 2);
                for (int guard = 0; Blitter.DmaPending && guard < 100000; guard++)
                {
                    Blitter.Tick(true);
                }
            }
        }
    }
}
