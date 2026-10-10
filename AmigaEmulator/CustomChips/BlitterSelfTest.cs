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
    public static class BlitterSelfTest
    {
        /// <summary>
        /// Constant for Bltcon 0.
        /// </summary>
        private const uint Bltcon0 = 0x040;
        /// <summary>
        /// Constant for Bltcon 1.
        /// </summary>
        private const uint Bltcon1 = 0x042;
        /// <summary>
        /// Constant for Bltafwm.
        /// </summary>
        private const uint Bltafwm = 0x044;
        /// <summary>
        /// Constant for Bltalwm.
        /// </summary>
        private const uint Bltalwm = 0x046;
        /// <summary>
        /// Constant for Bltcpt.
        /// </summary>
        private const uint Bltcpt = 0x048;
        /// <summary>
        /// Constant for Bltbpt.
        /// </summary>
        private const uint Bltbpt = 0x04C;
        /// <summary>
        /// Constant for Bltapt.
        /// </summary>
        private const uint Bltapt = 0x050;
        /// <summary>
        /// Constant for Bltdpt.
        /// </summary>
        private const uint Bltdpt = 0x054;
        /// <summary>
        /// Constant for Bltsize.
        /// </summary>
        private const uint Bltsize = 0x058;
        /// <summary>
        /// Constant for Bltcmod.
        /// </summary>
        private const uint Bltcmod = 0x060;
        /// <summary>
        /// Constant for Bltbmod.
        /// </summary>
        private const uint Bltbmod = 0x062;
        /// <summary>
        /// Constant for Bltamod.
        /// </summary>
        private const uint Bltamod = 0x064;
        /// <summary>
        /// Constant for Bltdmod.
        /// </summary>
        private const uint Bltdmod = 0x066;
        /// <summary>
        /// Constant for Bltcdat.
        /// </summary>
        private const uint Bltcdat = 0x070;
        /// <summary>
        /// Constant for Bltbdat.
        /// </summary>
        private const uint Bltbdat = 0x072;
        /// <summary>
        /// Constant for Bltadat.
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
            failures += RunCase(log, "disabled B source uses stable hold latch", TestDisabledBUsesHoldLatch);
            failures += RunCase(log, "inclusive fill scans right-to-left", TestInclusiveFill);
            failures += RunCase(log, "exclusive fill scans right-to-left", TestExclusiveFill);
            failures += RunCase(log, "descending blit subtracts line modulo", TestDescendingModuloSubtractsLineStride);
            failures += RunCase(log, "descending blit applies edge masks by screen word", TestDescendingMasks);
            failures += RunCase(log, "descending blit shift carries from previous fetch", TestDescendingShiftCarry);
            failures += RunCase(log, "blitter busy is visible immediately after BLTSIZE", TestBusyVisibleImmediatelyAfterStart);
            failures += RunCase(log, "block blit performs A/B/C reads before D write", TestBlockMicroPipelineWritesOnDSlot);
            failures += RunCase(log, "block blit delays D writes until after following source reads", TestPendingDWriteDelaysOverlappingCRead);
            failures += RunCase(log, "block blit skips inactive B/C phases in A-to-D copies", TestAdCopyUsesOnlyActiveChannels);
            failures += RunCase(log, "block blit skips disabled channel DMA cycles", TestDisabledChannelsCompleteWithoutDma);
            failures += RunCase(log, "line mode draws advancing mask bits", TestLineModeHorizontal);
            failures += RunCase(log, "line mode pixel occupies four DMA phases", TestLineModePixelUsesFourDmaPhases);
            failures += RunCase(log, "line mode does not draw without C channel", TestLineModeWithoutCDoesNotDraw);
            failures += RunCase(log, "line mode draws through C channel even when D is disabled", TestLineModeCWithoutD);
            failures += RunCase(log, "line mode zero flag follows C-channel result when D disabled", TestLineModeZeroFlagWithoutD);
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
        /// Runs Test Disabled Source Constants.
        /// </summary>
        private static bool TestDisabledSourceConstants()
        {
            var harness = new Harness();
            harness.WriteWord(0x0200, 0x0000);
            harness.WritePointer(Bltcpt, 0x0200);
            harness.WritePointer(Bltdpt, 0x0300);
            harness.Registers.WriteWord(Bltadat, 0xFFFF);
            harness.Registers.WriteWord(Bltbdat, 0xFFFF);
            harness.Blitter.NotifyCustomRegisterWrite(Bltbdat, 2);
            harness.Registers.WriteWord(Bltcdat, 0x0000);
            harness.Registers.WriteWord(Bltafwm, 0xFFFF);
            harness.Registers.WriteWord(Bltalwm, 0xFFFF);
            harness.Registers.WriteWord(Bltcon0, 0x03CA);
            harness.Registers.WriteWord(Bltcon1, 0x0000);
            harness.StartAndRun(Size(1, 1));
            return harness.ReadWord(0x0300) == 0xFFFF;
        }

        /// <summary>
        /// Runs Test Disabled Source Mask Keeps Latch.
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
        /// Runs Test Disabled B Uses Hold Latch.
        /// </summary>
        private static bool TestDisabledBUsesHoldLatch()
        {
            var harness = new Harness();
            harness.WritePointer(Bltdpt, 0x0300);
            harness.Registers.WriteWord(Bltafwm, 0xFFFF);
            harness.Registers.WriteWord(Bltalwm, 0xFFFF);
            harness.Registers.WriteWord(Bltcon0, 0x01CC);
            harness.Registers.WriteWord(Bltcon1, 0x4000);
            harness.Registers.WriteWord(Bltbdat, 0x000F);
            harness.Blitter.NotifyCustomRegisterWrite(Bltbdat, 2);
            harness.StartAndRun(Size(2, 1));
            return harness.ReadWord(0x0300) == 0x0000 &&
                harness.ReadWord(0x0302) == 0x0000;
        }

        /// <summary>
        /// Runs Test Inclusive Fill.
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
        /// Runs Test Exclusive Fill.
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
        /// Runs Test Descending Modulo Subtracts Line Stride.
        /// </summary>
        private static bool TestDescendingModuloSubtractsLineStride()
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
            harness.Registers.WriteWord(Bltamod, 0x000C);
            harness.Registers.WriteWord(Bltdmod, 0x000C);
            harness.Registers.WriteWord(Bltcon0, 0x09F0);
            harness.Registers.WriteWord(Bltcon1, 0x0002);
            harness.StartAndRun(Size(2, 2));
            return harness.ReadWord(0x0300) == 0x1111 &&
                harness.ReadWord(0x0302) == 0x2222 &&
                harness.ReadWord(0x0310) == 0x3333 &&
                harness.ReadWord(0x0312) == 0x4444;
        }

        /// <summary>
        /// Runs Test Descending Masks.
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
            return harness.ReadWord(0x0300) == 0xFFF0 &&
                harness.ReadWord(0x0302) == 0xFFFF &&
                harness.ReadWord(0x0304) == 0x0FFF;
        }

        /// <summary>
        /// Runs Test Descending Shift Carry.
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
        /// Verifies the blitter block micro-pipeline phase order.
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
        /// Verifies that a pending D write is delayed until after the next word has read overlapping sources.
        /// </summary>
        private static bool TestPendingDWriteDelaysOverlappingCRead()
        {
            var harness = new Harness();
            harness.WriteWord(0x0200, 0x0001);
            harness.WriteWord(0x0202, 0x0002);
            harness.WriteWord(0x0300, 0x0000);
            harness.WritePointer(Bltapt, 0x0200);
            harness.WritePointer(Bltcpt, 0x0300);
            harness.WritePointer(Bltdpt, 0x0300);
            harness.Registers.WriteWord(Bltafwm, 0xFFFF);
            harness.Registers.WriteWord(Bltalwm, 0xFFFF);
            harness.Registers.WriteWord(Bltamod, 0x0000);
            harness.Registers.WriteWord(Bltcmod, 0xFFFE);
            harness.Registers.WriteWord(Bltdmod, 0xFFFE);
            harness.Registers.WriteWord(Bltcon0, 0x0BFA);
            harness.Registers.WriteWord(Bltcon1, 0x0000);
            harness.StartAndRun(Size(1, 2));

            return harness.ReadWord(0x0300) == 0x0002;
        }

        /// <summary>
        /// Verifies that an A-to-D block copy runs only the enabled A and D memory cycles.
        /// </summary>
        private static bool TestAdCopyUsesOnlyActiveChannels()
        {
            var harness = new Harness();
            harness.WriteWord(0x0200, 0xCAFE);
            harness.WriteWord(0x0300, 0x0000);
            harness.WritePointer(Bltapt, 0x0200);
            harness.WritePointer(Bltdpt, 0x0300);
            harness.Registers.WriteWord(Bltafwm, 0xFFFF);
            harness.Registers.WriteWord(Bltalwm, 0xFFFF);
            harness.Registers.WriteWord(Bltcon0, 0x09F0);
            harness.Registers.WriteWord(Bltcon1, 0x0000);
            harness.Registers.WriteWord(Bltsize, Size(1, 1));
            harness.Blitter.NotifyCustomRegisterWrite(Bltsize, 2);

            harness.Blitter.Tick(true);
            bool unchangedAfterA = harness.ReadWord(0x0300) == 0x0000 && harness.Blitter.DmaPending;
            harness.Blitter.Tick(true);
            bool writtenAfterD = harness.ReadWord(0x0300) == 0xCAFE && !harness.Blitter.DmaPending;

            return unchangedAfterA && writtenAfterD;
        }

        /// <summary>
        /// Verifies that disabled block-blitter channels do not consume memory cycles.
        /// </summary>
        private static bool TestDisabledChannelsCompleteWithoutDma()
        {
            var harness = new Harness();
            harness.Registers.WriteWord(Bltcon0, 0x0000);
            harness.Registers.WriteWord(Bltcon1, 0x0000);
            harness.Registers.WriteWord(Bltsize, Size(1, 1));
            harness.Blitter.NotifyCustomRegisterWrite(Bltsize, 2);

            return !harness.Blitter.DmaPending && !harness.Blitter.Busy;
        }

        /// <summary>
        /// Runs Test Line Mode Horizontal.
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
        /// Verifies that line mode consumes the fixed four-phase blitter pipeline per pixel.
        /// </summary>
        private static bool TestLineModePixelUsesFourDmaPhases()
        {
            var harness = new Harness();
            harness.WriteWord(0x0200, 0x0000);
            harness.WritePointer(Bltapt, 0x8000);
            harness.WritePointer(Bltcpt, 0x0200);
            harness.WritePointer(Bltdpt, 0x0200);
            harness.Registers.WriteWord(Bltadat, 0x8000);
            harness.Registers.WriteWord(Bltbdat, 0xFFFF);
            harness.Registers.WriteWord(Bltcdat, 0x0000);
            harness.Registers.WriteWord(Bltafwm, 0xFFFF);
            harness.Registers.WriteWord(Bltalwm, 0xFFFF);
            harness.Registers.WriteWord(Bltcon0, 0x0BEA);
            harness.Registers.WriteWord(Bltcon1, 0x0051);
            harness.Registers.WriteWord(Bltsize, Size(2, 1));
            harness.Blitter.NotifyCustomRegisterWrite(Bltsize, 2);

            harness.Blitter.Tick(true);
            bool pendingAfterFirst = harness.Blitter.DmaPending && harness.ReadWord(0x0200) == 0x0000;
            harness.Blitter.Tick(true);
            bool pendingAfterSecond = harness.Blitter.DmaPending && harness.ReadWord(0x0200) == 0x0000;
            harness.Blitter.Tick(true);
            bool pendingAfterThird = harness.Blitter.DmaPending && harness.ReadWord(0x0200) == 0x0000;
            harness.Blitter.Tick(true);

            return pendingAfterFirst &&
                pendingAfterSecond &&
                pendingAfterThird &&
                !harness.Blitter.DmaPending &&
                harness.ReadWord(0x0200) == 0x8000;
        }

        /// <summary>
        /// Verifies that line mode needs the C channel to reach chip memory.
        /// </summary>
        private static bool TestLineModeWithoutCDoesNotDraw()
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
            return harness.ReadWord(0x0200) == 0x0000;
        }

        /// <summary>
        /// Verifies that line mode writes through the C-channel address even when D is disabled.
        /// </summary>
        private static bool TestLineModeCWithoutD()
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
            harness.StartAndRun(Size(1, 3));
            return harness.ReadWord(0x0200) == 0xE000;
        }

        /// <summary>
        /// Runs Test Line Mode Zero Flag Without D.
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
            return harness.ReadWord(0x0200) == 0x8000 && !harness.Blitter.ZeroFlag;
        }

        /// <summary>
        /// Runs Test Line Mode Destination Follows C.
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

        private static ushort Size(int widthWords, int heightLines)
        {
            return (ushort)(((heightLines & 0x03FF) << 6) | (widthWords & 0x003F));
        }

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
                Blitter = new Blitter(Registers);
                Blitter.AttachChipMemory(ReadWord, WriteWord);
            }

            public CustomRegisterMap Registers { get; private set; }

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
            /// Runs Start And Run.
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
