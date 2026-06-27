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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using AmigaEmulator.Core;
using AmigaEmulator.Core.Cia;
using AmigaEmulator.Core.Cpu;
using AmigaEmulator.Core.CustomChips;
using AmigaEmulator.Core.DevTools;
using AmigaEmulator.Core.Input;
using AmigaEmulator.Core.Media;
using AmigaEmulator.Core.Memory;
using AmigaEmulator.Core.SaveStates;
using AmigaEmulator.Core.Video;

namespace AmigaEmulator.Tests
{
    /// <summary>
    /// Provides the command-line diagnostics, self-tests, and WinForms application entry point.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// Runs Main.
        /// </summary>
        private static int Main(string[] args)
        {
            TestOptions options = TestOptions.Parse(args);
            List<ChipTestCase> tests = BuildTests(options);

            if (!string.IsNullOrWhiteSpace(options.Filter))
            {
                tests = tests
                    .Where(test => test.Name.IndexOf(options.Filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        test.Category.IndexOf(options.Filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            if (options.ListOnly)
            {
                foreach (ChipTestCase test in tests)
                {
                    Console.WriteLine(test.Category + " | " + test.Name + " | " + test.Source);
                }

                return 0;
            }

            int failures = 0;
            int skipped = 0;
            foreach (ChipTestCase test in tests)
            {
                try
                {
                    test.Run();
                    Console.WriteLine("OK   " + test.Category + " | " + test.Name);
                }
                catch (SkippedTestException ex)
                {
                    skipped++;
                    Console.WriteLine("SKIP " + test.Category + " | " + test.Name);
                    Console.WriteLine("     " + ex.Message);
                    Console.WriteLine("     Source: " + test.Source);
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.WriteLine("FAIL " + test.Category + " | " + test.Name);
                    Console.WriteLine("     " + ex.GetType().Name + ": " + ex.Message);
                    Console.WriteLine("     Source: " + test.Source);
                }
            }

            Console.WriteLine("Tests: " + tests.Count + "  Failed: " + failures + "  Skipped: " + skipped);
            return failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// Builds the Tests.
        /// </summary>
        private static List<ChipTestCase> BuildTests(TestOptions options)
        {
            var tests = new List<ChipTestCase>();
            tests.AddRange(RegressionSelfTests.Build());
            tests.AddRange(CpuTimingTests.Build());
            tests.AddRange(M68kOpcodeMatrixTests.Build());
            tests.AddRange(AgnusTests.Build());
            tests.AddRange(DeniseBitplaneTests.Build());
            tests.AddRange(SpriteTests.Build());
            tests.AddRange(PaulaTests.Build());
            tests.AddRange(IntegrationTests.Build());
            tests.AddRange(SaveStateTests.Build());
            if (options.IncludeAdf)
            {
                tests.AddRange(GoldenAdfTests.Build(options));
            }
            if (options.IncludeVAmigaTs)
            {
                tests.AddRange(VAmigaTsTests.Build(options));
            }
            if (options.IncludeAmigaDevtest)
            {
                tests.AddRange(AmigaDevtestTests.Build(options));
            }

            return tests;
        }
    }

    /// <summary>
    /// Provides Test Options functionality.
    /// </summary>
    internal sealed class TestOptions
    {
        /// <summary>
        /// Gets or sets Include ADF.
        /// </summary>
        public bool IncludeAdf { get; private set; }
        /// <summary>
        /// Gets or sets Include V Amiga Ts.
        /// </summary>
        public bool IncludeVAmigaTs { get; private set; }
        /// <summary>
        /// Gets or sets Include Amiga Devtest.
        /// </summary>
        public bool IncludeAmigaDevtest { get; private set; }
        /// <summary>
        /// Gets or sets Amiga Devtest ADF path.
        /// </summary>
        public string AmigaDevtestAdfPath { get; private set; }
        /// <summary>
        /// Gets or sets Amiga Devtest Cycles.
        /// </summary>
        public int AmigaDevtestCycles { get; private set; }
        /// <summary>
        /// Gets or sets V Amiga Ts All.
        /// </summary>
        public bool VAmigaTsAll { get; private set; }
        /// <summary>
        /// Gets or sets List Only.
        /// </summary>
        public bool ListOnly { get; private set; }
        /// <summary>
        /// Gets or sets Filter.
        /// </summary>
        public string Filter { get; private set; }
        /// <summary>
        /// Gets or sets V Amiga Ts Limit.
        /// </summary>
        public int VAmigaTsLimit { get; private set; }
        /// <summary>
        /// Gets or sets V Amiga Ts Cycles.
        /// </summary>
        public int VAmigaTsCycles { get; private set; }

        /// <summary>
        /// Runs Parse.
        /// </summary>
        public static TestOptions Parse(string[] args)
        {
            var options = new TestOptions
            {
                VAmigaTsCycles = 40_000_000,
                AmigaDevtestCycles = 180_000_000
            };
            for (int index = 0; args != null && index < args.Length; index++)
            {
                string arg = args[index];
                if (string.Equals(arg, "--include-adf", StringComparison.OrdinalIgnoreCase))
                {
                    options.IncludeAdf = true;
                }
                else if (string.Equals(arg, "--include-vamigats", StringComparison.OrdinalIgnoreCase))
                {
                    options.IncludeVAmigaTs = true;
                }
                else if (string.Equals(arg, "--include-amiga-devtest", StringComparison.OrdinalIgnoreCase))
                {
                    options.IncludeAmigaDevtest = true;
                }
                else if (string.Equals(arg, "--vamigats-all", StringComparison.OrdinalIgnoreCase))
                {
                    options.IncludeVAmigaTs = true;
                    options.VAmigaTsAll = true;
                }
                else if (string.Equals(arg, "--list", StringComparison.OrdinalIgnoreCase))
                {
                    options.ListOnly = true;
                }
                else if (string.Equals(arg, "--filter", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
                {
                    options.Filter = args[++index];
                }
                else if (string.Equals(arg, "--vamigats-limit", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
                {
                    int.TryParse(args[++index], out int limit);
                    options.VAmigaTsLimit = Math.Max(0, limit);
                }
                else if (string.Equals(arg, "--vamigats-cycles", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
                {
                    int.TryParse(args[++index], out int cycles);
                    options.VAmigaTsCycles = Math.Max(40_000_000, cycles);
                }
                else if (string.Equals(arg, "--amiga-devtest-adf", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
                {
                    options.AmigaDevtestAdfPath = args[++index];
                    options.IncludeAmigaDevtest = true;
                }
                else if (string.Equals(arg, "--amiga-devtest-cycles", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
                {
                    int.TryParse(args[++index], out int cycles);
                    options.AmigaDevtestCycles = Math.Max(40_000_000, cycles);
                }
            }

            return options;
        }
    }

    /// <summary>
    /// Provides Chip Test Case functionality.
    /// </summary>
    internal sealed class ChipTestCase
    {
        /// <summary>
        /// Stores run.
        /// </summary>
        private readonly Action _run;

        /// <summary>
        /// Initializes a new instance of the ChipTestCase class.
        /// </summary>
        public ChipTestCase(string category, string name, string source, Action run)
        {
            Category = category;
            Name = name;
            Source = source;
            _run = run;
        }

        /// <summary>
        /// Gets or sets Category.
        /// </summary>
        public string Category { get; private set; }
        /// <summary>
        /// Gets or sets Name.
        /// </summary>
        public string Name { get; private set; }
        /// <summary>
        /// Gets or sets Source.
        /// </summary>
        public string Source { get; private set; }

        /// <summary>
        /// Runs the value routine.
        /// </summary>
        public void Run()
        {
            _run();
        }
    }

    /// <summary>
    /// Provides Check functionality.
    /// </summary>
    internal static class Check
    {
        /// <summary>
        /// Runs True.
        /// </summary>
        public static void True(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        /// <summary>
        /// Gets message.
        /// </summary>
        public static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(message + " Expected=" + expected + " Actual=" + actual);
            }
        }

        /// <summary>
        /// Skips an optional test when a required external artifact is unavailable.
        /// </summary>
        public static void Skip(string message)
        {
            throw new SkippedTestException(message);
        }
    }

    /// <summary>
    /// Signals that an optional external test was intentionally not run.
    /// </summary>
    internal sealed class SkippedTestException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the SkippedTestException class.
        /// </summary>
        public SkippedTestException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Provides Regression Self Tests functionality.
    /// </summary>
    internal static class RegressionSelfTests
    {
        /// <summary>
        /// Builds the Build result.
        /// </summary>
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return SelfTest("CPU", "existing M68K regression self-test", "M68000 PRM; existing emulator regression suite", M68kSelfTest.Run);
            yield return SelfTest("Blitter", "existing blitter regression self-test", "Amiga HRM Blitter chapter; existing emulator regression suite", BlitterSelfTest.Run);
            yield return SelfTest("Copper", "existing copper regression self-test", "Amiga HRM Copper chapter; existing emulator regression suite", CopperSelfTest.Run);
            yield return SelfTest("CIA", "existing 8520 CIA regression self-test", "Amiga HRM Appendix F; existing emulator regression suite", CiaSelfTest.Run);
            yield return SelfTest("Input", "existing keyboard/joystick/mouse regression self-test", "Amiga HRM input register descriptions; existing emulator regression suite", AmigaInputSelfTest.Run);
        }

        /// <summary>
        /// Runs Self Test.
        /// </summary>
        private static ChipTestCase SelfTest(string category, string name, string source, Func<StringBuilder, int> run)
        {
            return new ChipTestCase(category, name, source, delegate
            {
                var log = new StringBuilder();
                int failures = run(log);
                Check.Equal(0, failures, log.ToString());
            });
        }
    }

    /// <summary>
    /// Provides focused CPU timing regression tests.
    /// </summary>
    internal static class CpuTimingTests
    {
        /// <summary>
        /// Builds the Build result.
        /// </summary>
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return new ChipTestCase(
                "CPU",
                "bit manipulation opcodes are not estimated as branch instructions",
                "M68000 PRM bit manipulation and branch timing",
                TestBitManipulationTimingEstimate);
        }

        /// <summary>
        /// Verifies that BTST/BCHG/BCLR/BSET do not fall through the generic Bcc timing path.
        /// </summary>
        private static void TestBitManipulationTimingEstimate()
        {
            var method = typeof(M68kCpu).GetMethod(
                "EstimateInstructionCycles",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Check.True(method != null, "CPU timing estimator should exist.");

            int btst = (int)method.Invoke(null, new object[] { (ushort)0x0800, "BTST #0,D0", 0x1000u, 0x1004u });
            int bne = (int)method.Invoke(null, new object[] { (ushort)0x6600, "BNE.S $1004", 0x1000u, 0x1004u });

            Check.Equal(6, btst, "BTST #imm,Dn should use immediate bit-op timing.");
            Check.Equal(10, bne, "Bcc should still use branch timing.");
            Check.True(btst != bne, "Bit operations must not be classified as branch mnemonics.");
        }
    }

    /// <summary>
    /// Provides Agnus Tests functionality.
    /// </summary>
    internal static class AgnusTests
    {
        /// <summary>
        /// Defines the DMACON custom register offset or bit mask.
        /// </summary>
        private const uint Dmacon = 0x096;
        /// <summary>
        /// Constant for Diwstrt.
        /// </summary>
        private const uint Diwstrt = 0x08E;
        /// <summary>
        /// Constant for Diwstop.
        /// </summary>
        private const uint Diwstop = 0x090;
        /// <summary>
        /// Constant for Ddfstrt.
        /// </summary>
        private const uint Ddfstrt = 0x092;
        /// <summary>
        /// Constant for Ddfstop.
        /// </summary>
        private const uint Ddfstop = 0x094;
        /// <summary>
        /// Defines the BPLCON0 custom register offset or bit mask.
        /// </summary>
        private const uint Bplcon0 = 0x100;
        /// <summary>
        /// Defines the BPL1PTH custom register offset or bit mask.
        /// </summary>
        private const uint Bpl1pth = 0x0E0;
        /// <summary>
        /// Defines the BPL1PTL custom register offset or bit mask.
        /// </summary>
        private const uint Bpl1ptl = 0x0E2;
        /// <summary>
        /// Defines the BPL2PTH custom register offset or bit mask.
        /// </summary>
        private const uint Bpl2pth = 0x0E4;
        /// <summary>
        /// Defines the BPL2PTL custom register offset or bit mask.
        /// </summary>
        private const uint Bpl2ptl = 0x0E6;

        /// <summary>
        /// Builds the Build result.
        /// </summary>
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return new ChipTestCase(
                "Agnus",
                "PAL beam advances from line 0 to line 1 after one 227-cycle scanline",
                "Amiga HRM video timing; A500 PAL model constants",
                TestBeamLineAdvance);
            yield return new ChipTestCase(
                "Agnus",
                "production bitplane DMA fetches one plane word per chip cycle slot",
                "Amiga HRM DMA slot diagrams and Denise bitplane prefetch grouping",
                TestProductionBitplaneDmaSplitFetch);
            yield return new ChipTestCase(
                "Agnus",
                "sprite DMA slots outrank copper and blitter slots",
                "Amiga HRM OCS DMA slot priority around sprite fetch slots",
                TestSpriteDmaPriority);
            yield return new ChipTestCase(
                "Agnus",
                "OCS fixed DMA table assigns refresh, audio, disk, and sprite slots",
                "Amiga HRM OCS DMA slot allocation",
                TestFixedOcsSlotTable);
            yield return new ChipTestCase(
                "Agnus",
                "bitplane DMA cannot steal fixed audio, disk, or sprite slots",
                "Amiga HRM OCS fixed-slot priority before display DMA",
                TestBitplanesDoNotStealFixedSlots);
            yield return new ChipTestCase(
                "Agnus",
                "BLTPRI lets the Blitter take otherwise CPU-available slots",
                "Amiga HRM DMACON BLTPRI bus priority behavior",
                TestBlitterPrioritySlot);
            yield return new ChipTestCase(
                "Agnus",
                "waiting Copper releases fetch slots to the Blitter",
                "Amiga HRM Copper WAIT bus behavior and Agnus DMA arbitration",
                TestWaitingCopperReleasesBlitterSlots);
        }

        /// <summary>
        /// Runs Test Beam Line Advance.
        /// </summary>
        private static void TestBeamLineAdvance()
        {
            var agnus = new Agnus(AmigaModel.A500Pal);
            for (int index = 0; index < AmigaModel.A500Pal.ChipCyclesPerLine + 1; index++)
            {
                agnus.Tick();
            }

            Check.Equal(0, agnus.Beam.BeamX, "Beam X should wrap at the PAL line length.");
            Check.Equal(1, agnus.Beam.BeamY, "Beam Y should advance after one PAL line.");
        }

        /// <summary>
        /// Runs Test Production Bitplane Dma Split Fetch.
        /// </summary>
        private static void TestProductionBitplaneDmaSplitFetch()
        {
            var agnus = new Agnus(AmigaModel.A500Pal);
            ushort[] memory = new ushort[4];
            memory[0] = 0x8000;
            memory[1] = 0x8000;
            agnus.AttachChipWordReader(address => memory[(address >> 1) & 3]);
            agnus.CustomRegisters.WriteWord(Bplcon0, 0x2000);
            agnus.CustomRegisters.WriteWord(Dmacon, 0x8300);
            agnus.CustomRegisters.WriteWord(Diwstrt, 0x2C81);
            agnus.CustomRegisters.WriteWord(Diwstop, 0x2D81);
            agnus.CustomRegisters.WriteWord(Ddfstrt, 0x0038);
            agnus.CustomRegisters.WriteWord(Ddfstop, 0x0038);
            agnus.CustomRegisters.WriteWord(Bpl1pth, 0x0000);
            agnus.CustomRegisters.WriteWord(Bpl1ptl, 0x0000);
            agnus.CustomRegisters.WriteWord(Bpl2pth, 0x0000);
            agnus.CustomRegisters.WriteWord(Bpl2ptl, 0x0002);

            int cyclesToFirstFetch = (BitplaneUnit.PalVisibleTop * AmigaModel.A500Pal.ChipCyclesPerLine) + 0x38;
            for (int index = 0; index < cyclesToFirstFetch; index++)
            {
                agnus.Tick();
            }

            agnus.Tick();
            Check.Equal(DmaOwner.Bitplane, agnus.LastDmaSlot.Owner, "OCS lowres slot $38 should fetch BPL2 in a two-plane display.");
            Check.Equal(0, agnus.Bitplanes.RenderColorIndex(0, BitplaneUnit.PalVisibleTop), "First split slot must not expose an incomplete two-plane fetch group.");

            agnus.Tick();

            Check.Equal(DmaOwner.Bitplane, agnus.LastDmaSlot.Owner, "OCS lowres slot $39 should fetch BPL1 and complete the group.");
            Check.Equal(3, agnus.Bitplanes.RenderColorIndex(0, BitplaneUnit.PalVisibleTop), "After both split slots commit, the first fetched pixel should remain visible at the left edge.");
        }

        /// <summary>
        /// Runs Test Sprite Dma Priority.
        /// </summary>
        private static void TestSpriteDmaPriority()
        {
            var registers = new CustomRegisterMap();
            registers.WriteWord(Dmacon, 0x82E0);
            var scheduler = new DmaScheduler(registers);
            scheduler.AttachBlitterStatus(() => true);
            Check.Equal(DmaOwner.Sprite, scheduler.GetSlot(0, 24, BitplaneUnit.PalVisibleTop).Owner, "Sprite slot should not be stolen by Copper or Blitter.");
        }

        /// <summary>
        /// Runs Test Fixed Ocs Slot Table.
        /// </summary>
        private static void TestFixedOcsSlotTable()
        {
            var registers = new CustomRegisterMap();
            registers.WriteWord(Dmacon, 0x823F);
            var scheduler = new DmaScheduler(registers);
            scheduler.AttachDiskDmaStatus(() => true);

            Check.Equal(DmaOwner.Refresh, scheduler.GetSlot(0, 0, 0).Owner, "Slot 0 should be refresh.");
            Check.Equal(DmaOwner.Audio, scheduler.GetSlot(0, 8, 0).Owner, "Slot 8 should be audio when audio DMA is enabled.");
            Check.Equal(0, scheduler.GetSlot(0, 8, 0).AudioChannelIndex, "Slot 8 should be assigned to audio channel 0.");
            Check.Equal(3, scheduler.GetSlot(0, 14, 0).AudioChannelIndex, "Slot 14 should be assigned to audio channel 3.");
            Check.Equal(DmaOwner.Disk, scheduler.GetSlot(0, 16, 0).Owner, "Slot 16 should be disk when disk DMA is active.");
            DmaSlotTraceEntry spriteSlot = scheduler.GetSlot(0, 20, 0);
            Check.Equal(DmaOwner.Sprite, spriteSlot.Owner, "Slot 20 should be sprite DMA when sprite DMA is enabled.");
            Check.Equal(0, spriteSlot.SpriteIndex, "Slot 20 should address sprite 0.");
            Check.Equal(0, spriteSlot.SpriteSlotPhase, "Slot 20 should be the first word of sprite 0.");
        }

        /// <summary>
        /// Runs Test Bitplanes Do Not Steal Fixed Slots.
        /// </summary>
        private static void TestBitplanesDoNotStealFixedSlots()
        {
            var registers = new CustomRegisterMap();
            var bitplanes = new BitplaneUnit(registers);
            var scheduler = new DmaScheduler(registers, bitplanes);
            registers.WriteWord(Dmacon, 0x833F);
            registers.WriteWord(Bplcon0, 0x1000);
            registers.WriteWord(Diwstrt, 0x2C81);
            registers.WriteWord(Diwstop, 0x2D81);
            registers.WriteWord(Ddfstrt, 0x0010);
            registers.WriteWord(Ddfstop, 0x0020);
            bitplanes.NotifyCustomRegisterWordWrite(Dmacon, 0x833F);
            bitplanes.NotifyCustomRegisterWordWrite(Bplcon0, 0x1000);
            bitplanes.NotifyCustomRegisterWordWrite(Diwstrt, 0x2C81);
            bitplanes.NotifyCustomRegisterWordWrite(Diwstop, 0x2D81);
            bitplanes.NotifyCustomRegisterWordWrite(Ddfstrt, 0x0010);
            bitplanes.NotifyCustomRegisterWordWrite(Ddfstop, 0x0020);
            scheduler.AttachDiskDmaStatus(() => true);

            Check.Equal(DmaOwner.Disk, scheduler.GetSlot(0, 16, BitplaneUnit.PalVisibleTop).Owner, "Bitplane fetch timing must not override the fixed disk DMA slot.");
            Check.Equal(DmaOwner.Sprite, scheduler.GetSlot(0, 20, BitplaneUnit.PalVisibleTop).Owner, "Bitplane fetch timing must not override the fixed sprite DMA slot.");
        }

        /// <summary>
        /// Runs Test Blitter Priority Slot.
        /// </summary>
        private static void TestBlitterPrioritySlot()
        {
            var registers = new CustomRegisterMap();
            registers.WriteWord(Dmacon, 0x8640);
            var scheduler = new DmaScheduler(registers);
            scheduler.AttachBlitterStatus(() => true);

            Check.Equal(DmaOwner.Blitter, scheduler.GetSlot(0, 37, 0).Owner, "BLTPRI should let the Blitter take a CPU-available slot.");
        }

        /// <summary>
        /// Runs Test Waiting Copper Releases Blitter Slots.
        /// </summary>
        private static void TestWaitingCopperReleasesBlitterSlots()
        {
            var registers = new CustomRegisterMap();
            registers.WriteWord(Dmacon, 0x82C0);
            var scheduler = new DmaScheduler(registers);
            scheduler.AttachBlitterStatus(() => true);

            scheduler.AttachCopperBusDemand(() => true);
            Check.Equal(DmaOwner.Copper, scheduler.GetSlot(0, 40, 0).Owner, "A Copper that needs an instruction word should own a Copper fetch slot.");

            scheduler.AttachCopperBusDemand(() => false);
            Check.Equal(DmaOwner.Blitter, scheduler.GetSlot(0, 40, 0).Owner, "A waiting Copper should not reserve its fetch slot while the Blitter is pending.");
        }
    }

    /// <summary>
    /// Provides Denise Bitplane Tests functionality.
    /// </summary>
    internal static class DeniseBitplaneTests
    {
        /// <summary>
        /// Defines the DMACON custom register offset or bit mask.
        /// </summary>
        private const uint Dmacon = 0x096;
        /// <summary>
        /// Constant for Diwstrt.
        /// </summary>
        private const uint Diwstrt = 0x08E;
        /// <summary>
        /// Constant for Diwstop.
        /// </summary>
        private const uint Diwstop = 0x090;
        /// <summary>
        /// Constant for Ddfstrt.
        /// </summary>
        private const uint Ddfstrt = 0x092;
        /// <summary>
        /// Constant for Ddfstop.
        /// </summary>
        private const uint Ddfstop = 0x094;
        /// <summary>
        /// Defines the BPLCON0 custom register offset or bit mask.
        /// </summary>
        private const uint Bplcon0 = 0x100;
        /// <summary>
        /// Defines the BPLCON1 custom register offset or bit mask.
        /// </summary>
        private const uint Bplcon1 = 0x102;
        /// <summary>
        /// Defines the BPLCON2 custom register offset or bit mask.
        /// </summary>
        private const uint Bplcon2 = 0x104;
        /// <summary>
        /// Defines the BPL1PTH custom register offset or bit mask.
        /// </summary>
        private const uint Bpl1pth = 0x0E0;
        /// <summary>
        /// Defines the BPL1PTL custom register offset or bit mask.
        /// </summary>
        private const uint Bpl1ptl = 0x0E2;
        /// <summary>
        /// Defines the BPL2PTH custom register offset or bit mask.
        /// </summary>
        private const uint Bpl2pth = 0x0E4;
        /// <summary>
        /// Defines the BPL2PTL custom register offset or bit mask.
        /// </summary>
        private const uint Bpl2ptl = 0x0E6;
        /// <summary>
        /// Defines the BPL1DAT custom register offset or bit mask.
        /// </summary>
        private const uint Bpl1dat = 0x110;
        /// <summary>
        /// Defines the BPL2DAT custom register offset or bit mask.
        /// </summary>
        private const uint Bpl2dat = 0x112;
        /// <summary>
        /// Defines the BPL6DAT custom register offset or bit mask.
        /// </summary>
        private const uint Bpl6dat = 0x11A;
        /// <summary>
        /// Defines the COLOR00 custom register offset.
        /// </summary>
        private const uint Color00 = 0x180;
        /// <summary>
        /// Defines the COLOR01 custom register offset.
        /// </summary>
        private const uint Color01 = 0x182;

        /// <summary>
        /// Builds the Build result.
        /// </summary>
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "BPLxDAT write feeds bitplane latch when bitplane DMA is off",
                "Amiga HRM playfield hardware: BPLxDAT latches and Denise shift registers",
                TestBplDatLatchWithoutDma);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "bitplane DMA fetch loads the visible bitplane word",
                "Amiga HRM bitplane DMA, DDFSTRT/DDFSTOP, BPLxPT",
                TestBitplaneDmaFetch);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "split bitplane DMA plane fetch commits only after all active planes arrive",
                "Amiga HRM bitplane DMA slot order and Denise BPLxDAT prefetch-to-shifter pipeline",
                TestSplitBitplanePrefetchGroup);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "OCS lowres six-plane DMA follows hardware fetch slot order",
                "WinUAE/HRM OCS lowres bitplane DMA sequence 8,4,6,2,7,3,5,1",
                TestOcsLowresSixPlaneFetchSequence);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "BPLCON1 applies odd and even scroll nibbles to the matching planes",
                "Amiga HRM BPLCON1 lower nibble scrolls even planes and upper nibble scrolls odd planes",
                TestBplcon1OddEvenScrollNibbles);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "dual-playfield nonzero odd plane maps to playfield 2 color bank",
                "Amiga HRM dual playfield mode and BPLCON2 priority/color bank rules",
                TestDualPlayfieldColorBank);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "border rows above a lowered DIW repaint with COLOR00",
                "Amiga HRM display window timing and Denise border color output",
                TestBorderRowsAboveLoweredDiw);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "standard DIW does not clip the right half of the fetched playfield",
                "Amiga HRM DIWSTRT/DIWSTOP horizontal display window with standard DDF width",
                TestStandardDiwDoesNotClipRightHalf);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "standard DDF renders the final fetched lowres word at the right edge",
                "Amiga HRM DDFSTRT/DDFSTOP inclusive fetch range and bitplane shifter output width",
                TestStandardDdfRendersFinalWordAtRightEdge);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "delayed bitplane DMA starts at BPLxPT instead of skipped DIW rows",
                "Amiga HRM bitplane DMA pointer advancement only happens on fetched display lines",
                TestDelayedBitplaneDmaStartsAtPointerRegister);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "Copper DIW changes do not rewind bitplane DMA row tracking",
                "Amiga OCS bitplane DMA pointers advance by fetched beam line, independent of mid-frame DIWSTRT changes",
                TestDiwTopChangeKeepsBitplaneDmaLineProgress);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "DDF fetches before the visible edge are skipped at screen X zero",
                "Amiga HRM DDFSTRT can start bitplane DMA before the left display edge",
                TestDdfStartBeforeVisibleEdgeSkipsPrefetchedPixels);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "five-plane lowres DDF lead-in waits for the extended fetch group",
                "Amiga OCS five-plane lowres DMA needs the longer fetch group before the visible data phase",
                TestFivePlaneDdfLeadInUsesCompleteFetchGroup);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "five-plane lowres wide DDF renders continuous words without column holes",
                "Amiga OCS five-plane lowres DMA must keep adjacent fetched words contiguous on screen",
                TestFivePlaneWideDdfRendersContinuousWords);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "five-plane standard DDF waits for the complete fetch group",
                "Amiga OCS five-plane lowres DMA still needs the full fetch-group delay at the normal DDF start",
                TestFivePlaneStandardDdfWaitsForCompleteFetchGroup);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "partial mid-line BPL pointer rewrites wait for a complete plane set",
                "OCS bitplane DMA must not render a scanline from mixed old and new BPLxPT bases",
                TestPartialMidLineBitplanePointerRewriteWaitsForCompleteSet);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "wide DDF overscan reports the full presentation source width",
                "Amiga HRM DDFSTRT/DDFSTOP can fetch wider-than-standard PAL overscan lines",
                TestWideDdfOverscanReportsPresentationSourceWidth);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "vertical DIW overscan reports the full presentation source height",
                "Amiga HRM DIWSTRT/DIWSTOP can open the display window above the standard PAL view",
                TestVerticalDiwOverscanReportsPresentationSourceHeight);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "bitplane frame start invalidates previous-frame fetched rows",
                "Amiga HRM bitplane DMA reloads display data every frame",
                TestFrameStartInvalidatesFetchedRows);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "sparse top overscan artifacts are cleared from the presented frame",
                "PAL monitor presentation should suppress isolated top-border transition noise",
                TestSparseTopOverscanArtifactsAreCleared);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "wide sparse top overscan artifacts are cleared from the presented frame",
                "PAL monitor presentation should suppress wider first-line transition noise without cropping artwork",
                TestWideSparseTopOverscanArtifactsAreCleared);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "dense top-edge artwork is preserved in the presented frame",
                "PAL monitor presentation cleanup must not crop real title or HUD artwork",
                TestDenseTopArtworkIsPreserved);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "wide horizontal overscan scales into the presented frame",
                "PAL monitor presentation should preserve right-edge overscan content",
                TestWideOverscanFrameScalesIntoPresentation);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "hires pixels are not duplicated like lowres pixels",
                "Amiga HRM BPLCON0 HIRES mode outputs one bitplane bit per hires pixel",
                TestHiresBitplanePixelsAreSingleWidth);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "misaligned hires DDF fetches the complete OCS block",
                "A500 OCS DDFSTRT/DDFSTOP use fetch blocks, so $3C..$D0 still fetches forty 2-plane hires words",
                TestMisalignedHiresDdfFetchesCompleteBlock);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "HAM control codes modify the previous RGB component",
                "Amiga HRM Hold-And-Modify color resolution for HAM6 screens",
                TestHamControlCodesModifyPreviousColor);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "six-plane EHB halves color bank 0 through plane 6",
                "Amiga HRM Extra Half-Brite output mode",
                TestExtraHalfBriteColor);
            yield return new ChipTestCase(
                "Denise/Collision",
                "CLXDAT latches playfield collision and clears on read",
                "Amiga HRM CLXCON/CLXDAT read-clear collision registers",
                TestClxdatReadClear);
        }

        /// <summary>
        /// Runs Test Bpl Dat Latch Without Dma.
        /// </summary>
        private static void TestBplDatLatchWithoutDma()
        {
            var registers = new CustomRegisterMap();
            var bitplanes = new BitplaneUnit(registers, null);
            registers.WriteWord(Bplcon0, 0x1000);
            bitplanes.NotifyCustomRegisterWordWrite(Bpl1dat, 0x8000);
            bitplanes.RefreshFromRegisters();
            Check.Equal(1, bitplanes.RenderColorIndex(0, BitplaneUnit.PalVisibleTop), "BPL1DAT bit 15 should render color index 1 at the first pixel.");
        }

        /// <summary>
        /// Runs Test Bitplane Dma Fetch.
        /// </summary>
        private static void TestBitplaneDmaFetch()
        {
            var registers = new CustomRegisterMap();
            ushort[] memory = new ushort[4];
            memory[0] = 0x8000;
            var bitplanes = new BitplaneUnit(registers, address => memory[(address >> 1) & 3]);
            registers.WriteWord(Bplcon0, 0x1000);
            registers.WriteWord(Dmacon, 0x8300);
            registers.WriteWord(Diwstrt, 0x2C81);
            registers.WriteWord(Diwstop, 0x2D81);
            registers.WriteWord(Ddfstrt, 0x0038);
            registers.WriteWord(Ddfstop, 0x0038);
            registers.WriteWord(Bpl1pth, 0x0000);
            registers.WriteWord(Bpl1ptl, 0x0000);
            bitplanes.FetchDmaWords(0x38, BitplaneUnit.PalVisibleTop);
            Check.Equal(1, bitplanes.RenderColorIndex(0, BitplaneUnit.PalVisibleTop), "Fetched DMA word bit 15 should render color index 1.");
        }

        /// <summary>
        /// Runs Test Split Bitplane Prefetch Group.
        /// </summary>
        private static void TestSplitBitplanePrefetchGroup()
        {
            var registers = new CustomRegisterMap();
            ushort[] memory = new ushort[4];
            memory[0] = 0x8000;
            memory[1] = 0x8000;
            var bitplanes = new BitplaneUnit(registers, address => memory[(address >> 1) & 3]);
            registers.WriteWord(Bplcon0, 0x2000);
            registers.WriteWord(Dmacon, 0x8300);
            registers.WriteWord(Diwstrt, 0x2C81);
            registers.WriteWord(Diwstop, 0x2D81);
            registers.WriteWord(Ddfstrt, 0x0038);
            registers.WriteWord(Ddfstop, 0x0038);
            registers.WriteWord(Bpl1pth, 0x0000);
            registers.WriteWord(Bpl1ptl, 0x0000);
            registers.WriteWord(Bpl2pth, 0x0000);
            registers.WriteWord(Bpl2ptl, 0x0002);

            bitplanes.FetchDmaPlaneWord(0x38, BitplaneUnit.PalVisibleTop);
            Check.Equal(0, bitplanes.RenderColorIndex(0, BitplaneUnit.PalVisibleTop), "A half-fetched split group must not become visible.");

            bitplanes.FetchDmaPlaneWord(0x39, BitplaneUnit.PalVisibleTop);
            Check.Equal(3, bitplanes.RenderColorIndex(0, BitplaneUnit.PalVisibleTop), "After both plane words commit, the group should be visible at the left edge without shrinking the right edge.");
        }

        /// <summary>
        /// Verifies the OCS low-resolution bitplane DMA slot sequence used by six-plane/EHB screens.
        /// </summary>
        private static void TestOcsLowresSixPlaneFetchSequence()
        {
            var registers = new CustomRegisterMap();
            ushort[] memory = new ushort[8];
            memory[3] = 0x8000;
            var bitplanes = new BitplaneUnit(registers, address => memory[(address >> 1) & 7]);
            registers.WriteWord(Bplcon0, 0x6000);
            registers.WriteWord(Dmacon, 0x8300);
            registers.WriteWord(Diwstrt, 0x2C81);
            registers.WriteWord(Diwstop, 0x2D81);
            registers.WriteWord(Ddfstrt, 0x0038);
            registers.WriteWord(Ddfstop, 0x0038);
            for (int plane = 0; plane < 6; plane++)
            {
                registers.WriteWord((uint)(Bpl1pth + (plane * 4)), 0x0000);
                registers.WriteWord((uint)(Bpl1ptl + (plane * 4)), (ushort)(plane * 2));
            }

            int[] fetchSlots = { 0x39, 0x3A, 0x3B, 0x3D, 0x3E, 0x3F };
            foreach (int slot in fetchSlots)
            {
                bitplanes.FetchDmaPlaneWord(slot, BitplaneUnit.PalVisibleTop);
            }

            Check.Equal(0, bitplanes.RenderColorIndex(0, BitplaneUnit.PalVisibleTop), "Six-plane lowres DMA should not display before the extended fetch group has completed.");
            Check.Equal(8, bitplanes.RenderColorIndex(4, BitplaneUnit.PalVisibleTop), "Slot $39 must feed BPL4, selecting color bit 3 after the six-plane OCS lead-in.");
        }

        /// <summary>
        /// Verifies BPLCON1 fine-scroll nibble assignment for odd and even planes.
        /// </summary>
        private static void TestBplcon1OddEvenScrollNibbles()
        {
            var registers = new CustomRegisterMap();
            var bitplanes = new BitplaneUnit(registers, null);
            registers.WriteWord(Bplcon0, 0x2000);
            registers.WriteWord(Bplcon1, 0x0010);
            bitplanes.NotifyCustomRegisterWordWrite(Bpl1dat, 0x8000);
            bitplanes.NotifyCustomRegisterWordWrite(Bpl2dat, 0x8000);
            bitplanes.RefreshFromRegisters();

            Check.Equal(2, bitplanes.RenderColorIndex(0, BitplaneUnit.PalVisibleTop), "Upper BPLCON1 nibble should delay odd BPL1 while even BPL2 remains at the left edge.");
        }

        /// <summary>
        /// Runs Test Dual Playfield Color Bank.
        /// </summary>
        private static void TestDualPlayfieldColorBank()
        {
            var registers = new CustomRegisterMap();
            var bitplanes = new BitplaneUnit(registers, null);
            registers.WriteWord(Bplcon0, 0x2400);
            registers.WriteWord(Bplcon2, 0x0040);
            bitplanes.NotifyCustomRegisterWordWrite(Bpl1dat, 0x0000);
            bitplanes.NotifyCustomRegisterWordWrite(Bpl2dat, 0x8000);
            bitplanes.RefreshFromRegisters();
            Check.Equal(9, bitplanes.RenderColorIndex(0, BitplaneUnit.PalVisibleTop), "Odd plane bit should select playfield 2 color bank 8+1.");
        }

        /// <summary>
        /// Verifies that Denise repaints visible border lines even when the playfield DIW starts lower.
        /// </summary>
        private static void TestBorderRowsAboveLoweredDiw()
        {
            var registers = new CustomRegisterMap();
            var frameBuffer = new AmigaFrameBuffer(640, 256);
            var bitplanes = new BitplaneUnit(registers, null);
            var sprites = new SpriteUnit(registers, null);
            var denise = new Denise(frameBuffer, bitplanes, sprites);
            frameBuffer.Clear(0xFFFF00FF);
            registers.WriteWord(Color00, 0x000F);
            registers.WriteWord(Diwstrt, 0x5081);
            registers.WriteWord(Diwstop, 0x5181);
            registers.WriteWord(Bplcon0, 0x0000);

            denise.Tick(new BeamTraceEntry { BeamX = 0x3E, BeamY = BitplaneUnit.PalVisibleTop, Frame = 0 });

            Check.Equal(0xFF0000FFu, frameBuffer.Pixels[0], "Visible border pixels above DIW must be repainted from COLOR00 instead of preserving stale pixels.");
        }

        /// <summary>
        /// Verifies that the common DIW/DDF setup remains visible across the fetched line width.
        /// </summary>
        private static void TestStandardDiwDoesNotClipRightHalf()
        {
            var registers = new CustomRegisterMap();
            var bitplanes = new BitplaneUnit(registers, null);
            registers.WriteWord(Bplcon0, 0x1000);
            registers.WriteWord(Diwstrt, 0x2C81);
            registers.WriteWord(Diwstop, 0x2CC1);
            registers.WriteWord(Ddfstrt, 0x0038);
            registers.WriteWord(Ddfstop, 0x00D0);
            bitplanes.NotifyCustomRegisterWordWrite(Bpl1dat, 0xFFFF);
            bitplanes.RefreshFromRegisters();

            Check.Equal(1, bitplanes.RenderColorIndex(500, BitplaneUnit.PalVisibleTop), "Standard DIW should not clip the right half of a 640-pixel fetched playfield.");
        }

        /// <summary>
        /// Verifies that the last DDF word reaches the final visible lowres pixel pair.
        /// </summary>
        private static void TestStandardDdfRendersFinalWordAtRightEdge()
        {
            var registers = new CustomRegisterMap();
            ushort[] memory = new ushort[32];
            memory[19] = 0x0001;
            var bitplanes = new BitplaneUnit(registers, address => memory[(address >> 1) & 31]);
            registers.WriteWord(Bplcon0, 0x1000);
            registers.WriteWord(Dmacon, 0x8300);
            registers.WriteWord(Diwstrt, 0x2C81);
            registers.WriteWord(Diwstop, 0x2DCA);
            registers.WriteWord(Ddfstrt, 0x0038);
            registers.WriteWord(Ddfstop, 0x00D0);
            registers.WriteWord(Bpl1pth, 0x0000);
            registers.WriteWord(Bpl1ptl, 0x0000);

            for (int beamX = 0x38; beamX < 0xD8; beamX++)
            {
                if (bitplanes.IsPlaneFetchSlot(beamX, BitplaneUnit.PalVisibleTop))
                {
                    bitplanes.FetchDmaPlaneWord(beamX, BitplaneUnit.PalVisibleTop);
                }
            }

            Check.Equal(1, bitplanes.RenderColorIndex(638, BitplaneUnit.PalVisibleTop), "The final fetched DDF word must reach the rightmost lowres pixel pair.");
            Check.Equal(1, bitplanes.RenderColorIndex(639, BitplaneUnit.PalVisibleTop), "The duplicated lowres pixel must also reach the final hires pixel.");
        }

        /// <summary>
        /// Verifies that a playfield enabled below the visible top does not pre-skip unfetched rows.
        /// </summary>
        private static void TestDelayedBitplaneDmaStartsAtPointerRegister()
        {
            var registers = new CustomRegisterMap();
            ushort[] memory = new ushort[256];
            memory[0] = 0x8000;
            memory[145] = 0x0000;
            var bitplanes = new BitplaneUnit(registers, address => memory[(address >> 1) & 0xFF]);
            registers.WriteWord(Bplcon0, 0x1000);
            registers.WriteWord(Dmacon, 0x8300);
            registers.WriteWord(Diwstrt, 0x1921);
            registers.WriteWord(Diwstop, 0x2FF1);
            registers.WriteWord(Ddfstrt, 0x0038);
            registers.WriteWord(Ddfstop, 0x0038);
            registers.WriteWord(Bpl1pth, 0x0000);
            registers.WriteWord(Bpl1ptl, 0x0000);

            bitplanes.FetchDmaWords(0x38, 0xBD);

            Check.Equal(1, bitplanes.RenderColorIndex(0, 0xBD), "The first active DMA line must fetch from BPL1PT, not from an offset for skipped DIW rows.");
        }

        /// <summary>
        /// Verifies that Copper DIWSTRT changes between display bands do not reset the bitplane DMA line stream.
        /// </summary>
        private static void TestDiwTopChangeKeepsBitplaneDmaLineProgress()
        {
            var registers = new CustomRegisterMap();
            ushort[] memory = new ushort[4];
            memory[0] = 0x8000;
            memory[1] = 0x4000;
            var bitplanes = new BitplaneUnit(registers, address => memory[(address >> 1) & 3]);
            registers.WriteWord(Bplcon0, 0x1000);
            registers.WriteWord(Dmacon, 0x8300);
            registers.WriteWord(Diwstrt, 0x1881);
            registers.WriteWord(Diwstop, 0x5081);
            registers.WriteWord(Ddfstrt, 0x0038);
            registers.WriteWord(Ddfstop, 0x0038);
            registers.WriteWord(Bpl1pth, 0x0000);
            registers.WriteWord(Bpl1ptl, 0x0000);

            bitplanes.FetchDmaPlaneWord(0x39, 0x30);
            Check.Equal(1, bitplanes.RenderColorIndex(0, 0x30), "The first fetched line should render the first memory word.");

            registers.WriteWord(Diwstrt, 0x2081);
            registers.WriteWord(Diwstop, 0x5081);
            bitplanes.FetchDmaPlaneWord(0x39, 0x31);

            Check.Equal(0, bitplanes.RenderColorIndex(0, 0x31), "The second physical beam line must not refetch the first word after DIWSTRT changes.");
            Check.Equal(1, bitplanes.RenderColorIndex(2, 0x31), "The second physical beam line should continue to the next DMA word.");
        }

        /// <summary>
        /// Verifies that DDF fetches before the PAL viewport are consumed before visible pixels begin.
        /// </summary>
        private static void TestDdfStartBeforeVisibleEdgeSkipsPrefetchedPixels()
        {
            var registers = new CustomRegisterMap();
            ushort[] memory = new ushort[4];
            memory[0] = 0x0000;
            memory[1] = 0x8000;
            var bitplanes = new BitplaneUnit(registers, address => memory[(address >> 1) & 3]);
            registers.WriteWord(Bplcon0, 0x1000);
            registers.WriteWord(Dmacon, 0x8300);
            registers.WriteWord(Diwstrt, 0x2C81);
            registers.WriteWord(Diwstop, 0x2D81);
            registers.WriteWord(Ddfstrt, 0x0030);
            registers.WriteWord(Ddfstop, 0x0038);
            registers.WriteWord(Bpl1pth, 0x0000);
            registers.WriteWord(Bpl1ptl, 0x0000);

            bitplanes.FetchDmaWords(0x30, BitplaneUnit.PalVisibleTop);
            bitplanes.FetchDmaWords(0x38, BitplaneUnit.PalVisibleTop);

            Check.Equal(1, bitplanes.RenderColorIndex(0, BitplaneUnit.PalVisibleTop), "Visible X zero should show the word fetched after the offscreen DDF lead-in.");
        }

        /// <summary>
        /// Verifies that five-plane lowres fetches do not display a word before all OCS DMA slots have completed.
        /// </summary>
        private static void TestFivePlaneDdfLeadInUsesCompleteFetchGroup()
        {
            var registers = new CustomRegisterMap();
            ushort[] memory = new ushort[8];
            memory[5] = 0x8000;
            var bitplanes = new BitplaneUnit(registers, address => memory[(address >> 1) & 7]);
            registers.WriteWord(Bplcon0, 0x5000);
            registers.WriteWord(Dmacon, 0x8300);
            registers.WriteWord(Diwstrt, 0x2C81);
            registers.WriteWord(Diwstop, 0x2D81);
            registers.WriteWord(Ddfstrt, 0x0030);
            registers.WriteWord(Ddfstop, 0x0038);
            for (int plane = 0; plane < 5; plane++)
            {
                registers.WriteWord((uint)(Bpl1pth + (plane * 4)), 0x0000);
                registers.WriteWord((uint)(Bpl1ptl + (plane * 4)), (ushort)(plane * 2));
            }

            int[] fetchSlots = { 0x39, 0x3B, 0x3D, 0x3E, 0x3F };
            foreach (int slot in fetchSlots)
            {
                bitplanes.FetchDmaPlaneWord(slot, BitplaneUnit.PalVisibleTop);
            }

            Check.Equal(-32, bitplanes.PresentationSourceLeft, "Presentation should still preserve the left overscan source while the five-plane data phase is delayed.");
            Check.Equal(16, bitplanes.RenderColorIndex(4, BitplaneUnit.PalVisibleTop), "Five-plane DDF lead-in should wait one chip cycle before presenting the completed fetch group.");
        }

        /// <summary>
        /// Verifies that five-plane lowres DMA can fill adjacent display words without vertical gaps.
        /// </summary>
        private static void TestFivePlaneWideDdfRendersContinuousWords()
        {
            var registers = new CustomRegisterMap();
            ushort[] memory = new ushort[5 * 4];
            for (int index = 0; index < memory.Length; index++)
            {
                memory[index] = 0xFFFF;
            }

            var bitplanes = new BitplaneUnit(registers, address => memory[(address >> 1) % memory.Length]);
            registers.WriteWord(Bplcon0, 0x5000);
            registers.WriteWord(Dmacon, 0x8300);
            registers.WriteWord(Diwstrt, 0x5821);
            registers.WriteWord(Diwstop, 0x3FF1);
            registers.WriteWord(Ddfstrt, 0x0030);
            registers.WriteWord(Ddfstop, 0x0048);
            for (int plane = 0; plane < 5; plane++)
            {
                registers.WriteWord((uint)(Bpl1pth + (plane * 4)), 0x0000);
                registers.WriteWord((uint)(Bpl1ptl + (plane * 4)), (ushort)(plane * 2));
            }

            const int displayLine = 0x58;
            for (int beamX = 0x30; beamX < 0x30 + (4 * 8); beamX++)
            {
                bitplanes.FetchDmaPlaneWord(beamX, displayLine);
            }

            for (int x = 8; x < 96; x++)
            {
                Check.Equal(31, bitplanes.RenderColorIndex(x, displayLine), "Five-plane wide fetch should not produce a black vertical column at visible X " + x + ".");
            }
        }

        /// <summary>
        /// Verifies that normal $38 lowres five-plane DDF does not show each word before plane 1 arrives.
        /// </summary>
        private static void TestFivePlaneStandardDdfWaitsForCompleteFetchGroup()
        {
            var registers = new CustomRegisterMap();
            ushort[] memory = new ushort[5 * 4];
            for (int index = 0; index < memory.Length; index++)
            {
                memory[index] = 0xFFFF;
            }

            var bitplanes = new BitplaneUnit(registers, address => memory[(address >> 1) % memory.Length]);
            registers.WriteWord(Bplcon0, 0x5000);
            registers.WriteWord(Dmacon, 0x8300);
            registers.WriteWord(Diwstrt, 0x2C81);
            registers.WriteWord(Diwstop, 0x2CC1);
            registers.WriteWord(Ddfstrt, 0x0038);
            registers.WriteWord(Ddfstop, 0x0050);
            for (int plane = 0; plane < 5; plane++)
            {
                registers.WriteWord((uint)(Bpl1pth + (plane * 4)), 0x0000);
                registers.WriteWord((uint)(Bpl1ptl + (plane * 4)), (ushort)(plane * 2));
            }

            for (int beamX = 0x38; beamX < 0x38 + (4 * 8); beamX++)
            {
                bitplanes.FetchDmaPlaneWord(beamX, BitplaneUnit.PalVisibleTop);
            }

            Check.Equal(0, bitplanes.RenderColorIndex(0, BitplaneUnit.PalVisibleTop), "The first four hires pixels belong to the fetch-group lead-in.");
            for (int x = 4; x < 96; x++)
            {
                Check.Equal(31, bitplanes.RenderColorIndex(x, BitplaneUnit.PalVisibleTop), "Five-plane standard DDF should not produce a black vertical column at visible X " + x + ".");
            }
        }

        /// <summary>
        /// Verifies that a scanline cannot be built from a partially rewritten BPL pointer set.
        /// </summary>
        private static void TestPartialMidLineBitplanePointerRewriteWaitsForCompleteSet()
        {
            var registers = new CustomRegisterMap();
            ushort[] memory = new ushort[0x100];
            const int newBaseWord = 0x40;
            for (int plane = 0; plane < 5; plane++)
            {
                memory[newBaseWord + plane] = 0xFFFF;
            }

            var bitplanes = new BitplaneUnit(registers, address => memory[(address >> 1) % memory.Length]);
            registers.WriteWord(Bplcon0, 0x5000);
            registers.WriteWord(Dmacon, 0x8300);
            registers.WriteWord(Diwstrt, 0x2C81);
            registers.WriteWord(Diwstop, 0x2FC1);
            registers.WriteWord(Ddfstrt, 0x0038);
            registers.WriteWord(Ddfstop, 0x0038);
            for (int plane = 0; plane < 5; plane++)
            {
                registers.WriteWord((uint)(Bpl1pth + (plane * 4)), 0x0000);
                registers.WriteWord((uint)(Bpl1ptl + (plane * 4)), (ushort)(plane * 2));
            }

            for (int beamX = 0x38; beamX < 0x40; beamX++)
            {
                bitplanes.FetchDmaPlaneWord(beamX, BitplaneUnit.PalVisibleTop);
            }

            registers.WriteWord(Bpl1ptl, (ushort)(newBaseWord * 2));
            registers.WriteWord(Bpl2ptl, (ushort)((newBaseWord + 1) * 2));
            for (int beamX = 0x38; beamX < 0x40; beamX++)
            {
                bitplanes.FetchDmaPlaneWord(beamX, BitplaneUnit.PalVisibleTop + 1);
            }

            Check.Equal(0, bitplanes.RenderColorIndex(4, BitplaneUnit.PalVisibleTop + 1), "A partial BPL1/BPL2 rewrite must not leak a mixed pointer row into the display.");

            for (int plane = 2; plane < 5; plane++)
            {
                registers.WriteWord((uint)(Bpl1ptl + (plane * 4)), (ushort)((newBaseWord + plane) * 2));
            }

            for (int beamX = 0x38; beamX < 0x40; beamX++)
            {
                bitplanes.FetchDmaPlaneWord(beamX, BitplaneUnit.PalVisibleTop + 2);
            }

            Check.Equal(31, bitplanes.RenderColorIndex(4, BitplaneUnit.PalVisibleTop + 2), "A complete BPL pointer rewrite should become visible on the following fetched row.");
        }

        /// <summary>
        /// Verifies that wide DDF fetches ask presentation to include the right overscan content.
        /// </summary>
        private static void TestWideDdfOverscanReportsPresentationSourceWidth()
        {
            var registers = new CustomRegisterMap();
            var bitplanes = new BitplaneUnit(registers, null);
            registers.WriteWord(Bplcon0, 0x4000);
            registers.WriteWord(Dmacon, 0x8300);
            registers.WriteWord(Diwstrt, 0x5821);
            registers.WriteWord(Diwstop, 0x3FF1);
            registers.WriteWord(Ddfstrt, 0x0030);
            registers.WriteWord(Ddfstop, 0x00D8);

            Check.Equal(-32, bitplanes.PresentationSourceLeft, "A $30 DDF start begins 32 hires pixels left of the standard PAL presentation edge.");
            Check.Equal(704, bitplanes.PresentationSourceWidth, "A $30..$D8 lowres fetch should preserve the complete overscan source instead of subtracting the left lead-in.");

            registers.WriteWord(Ddfstop, 0x00C8);
            Check.Equal(672, bitplanes.PresentationSourceWidth, "A $30..$C8 fetch must still include the standard right PAL edge when left overscan is visible.");
        }

        /// <summary>
        /// Verifies that DIW top overscan is reported to the presenter instead of being host-cropped away.
        /// </summary>
        private static void TestVerticalDiwOverscanReportsPresentationSourceHeight()
        {
            var registers = new CustomRegisterMap();
            var bitplanes = new BitplaneUnit(registers, null);
            registers.WriteWord(Bplcon0, 0x4000);
            registers.WriteWord(Dmacon, 0x8300);
            registers.WriteWord(Diwstrt, 0x2081);
            registers.WriteWord(Diwstop, 0xFFC1);
            registers.WriteWord(Ddfstrt, 0x0038);
            registers.WriteWord(Ddfstop, 0x00D0);

            Check.Equal(32, bitplanes.PresentationSourceTop, "A DIW start above the standard PAL top must remain part of the presentation source.");
            Check.Equal(268, bitplanes.PresentationSourceHeight, "The presentation source must include top overscan while still preserving the standard bottom edge.");
        }

        /// <summary>
        /// Verifies that fetched bitplane words from a previous frame cannot leak into the next frame.
        /// </summary>
        private static void TestFrameStartInvalidatesFetchedRows()
        {
            var registers = new CustomRegisterMap();
            ushort[] memory = new ushort[4];
            memory[0] = 0x8000;
            var bitplanes = new BitplaneUnit(registers, address => memory[(address >> 1) & 3]);
            registers.WriteWord(Bplcon0, 0x1000);
            registers.WriteWord(Dmacon, 0x8300);
            registers.WriteWord(Diwstrt, 0x2C81);
            registers.WriteWord(Diwstop, 0x2D81);
            registers.WriteWord(Ddfstrt, 0x0038);
            registers.WriteWord(Ddfstop, 0x0038);
            registers.WriteWord(Bpl1pth, 0x0000);
            registers.WriteWord(Bpl1ptl, 0x0000);

            bitplanes.FetchDmaWords(0x38, BitplaneUnit.PalVisibleTop);
            Check.Equal(1, bitplanes.RenderColorIndex(0, BitplaneUnit.PalVisibleTop), "The first frame should render the fetched bit.");

            memory[0] = 0x0000;
            bitplanes.BeginFrame();
            Check.Equal(0, bitplanes.RenderColorIndex(0, BitplaneUnit.PalVisibleTop), "Before the next frame fetches, previous-frame row data must be invalid.");

            bitplanes.FetchDmaWords(0x38, BitplaneUnit.PalVisibleTop);
            Check.Equal(0, bitplanes.RenderColorIndex(0, BitplaneUnit.PalVisibleTop), "After the next fetch, the new frame should render the updated word.");
        }

        /// <summary>
        /// Verifies that isolated top-border transition pixels are hidden before presentation.
        /// </summary>
        private static void TestSparseTopOverscanArtifactsAreCleared()
        {
            var frame = new AmigaFrameBuffer(640, 256);
            frame.Clear(0xFF002288);
            for (int y = 0; y < 10; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    frame.SetPixel(x, y, 0xFF000000);
                }

                for (int x = 184; x < 192; x++)
                {
                    frame.SetPixel(x, y, 0xFF00CCEE);
                }
            }

            frame.ClearSparseTopBorderArtifacts(10, 512);

            for (int y = 0; y < 10; y++)
            {
                Check.Equal(0xFF002288u, frame.Pixels[y * frame.Width], "Sparse top-border cleanup should use the dominant border color, not the left gutter pixel.");
                for (int x = 184; x < 192; x++)
                {
                    Check.Equal(0xFF002288u, frame.Pixels[(y * frame.Width) + x], "Sparse top-border artifact pixel should be restored to COLOR00.");
                }
            }
        }

        /// <summary>
        /// Verifies that wider first-line transition noise is still treated as border garbage.
        /// </summary>
        private static void TestWideSparseTopOverscanArtifactsAreCleared()
        {
            var frame = new AmigaFrameBuffer(640, 256);
            frame.Clear(0xFF0060AA);
            for (int y = 0; y < 10; y++)
            {
                for (int x = 0; x < 360; x++)
                {
                    frame.SetPixel(x, y, (x & 1) == 0 ? 0xFFFFFFFFu : 0xFF000000u);
                }
            }

            frame.ClearSparseTopBorderArtifacts(24, 6000);

            for (int y = 0; y < 10; y++)
            {
                for (int x = 0; x < 360; x++)
                {
                    Check.Equal(0xFF0060AAu, frame.Pixels[(y * frame.Width) + x], "Wide sparse top-border transition pixels should be restored to the dominant border color.");
                }
            }
        }

        /// <summary>
        /// Verifies that real top-edge artwork is not mistaken for sparse overscan noise.
        /// </summary>
        private static void TestDenseTopArtworkIsPreserved()
        {
            var frame = new AmigaFrameBuffer(640, 256);
            frame.Clear(0xFFEE8800);
            for (int y = 0; y < 10; y++)
            {
                for (int x = 16; x < 640; x++)
                {
                    frame.SetPixel(x, y, 0xFF4422EE);
                }
            }

            frame.ClearSparseTopBorderArtifacts(24, 6000);

            Check.Equal(0xFF4422EEu, frame.Pixels[(5 * frame.Width) + 320], "Dense top-edge artwork should survive sparse overscan cleanup.");
        }

        /// <summary>
        /// Verifies that wide internal overscan can be mapped into the stable presentation frame.
        /// </summary>
        private static void TestWideOverscanFrameScalesIntoPresentation()
        {
            var source = new AmigaFrameBuffer(672, 1);
            var target = new AmigaFrameBuffer(640, 1);
            source.Clear(0xFF000000);
            source.SetPixel(671, 0, 0xFFFFFFFF);

            target.CopyScaledWindowFrom(source, 0, 0, 672, 1);

            Check.Equal(0xFFFFFFFFu, target.Pixels[639], "Scaled presentation must keep the rightmost overscan source pixel visible.");
        }

        /// <summary>
        /// Verifies that HIRES mode consumes one bitplane bit per hires pixel.
        /// </summary>
        private static void TestHiresBitplanePixelsAreSingleWidth()
        {
            var registers = new CustomRegisterMap();
            ushort[] memory = new ushort[4];
            memory[0] = 0x8000;
            var bitplanes = new BitplaneUnit(registers, address => memory[(address >> 1) & 3]);
            registers.WriteWord(Bplcon0, 0x9000);
            registers.WriteWord(Dmacon, 0x8300);
            registers.WriteWord(Diwstrt, 0x2C81);
            registers.WriteWord(Diwstop, 0x2D81);
            registers.WriteWord(Ddfstrt, 0x0038);
            registers.WriteWord(Ddfstop, 0x0038);
            registers.WriteWord(Bpl1pth, 0x0000);
            registers.WriteWord(Bpl1ptl, 0x0000);

            bitplanes.FetchDmaWords(0x38, BitplaneUnit.PalVisibleTop);

            Check.Equal(1, bitplanes.RenderColorIndex(0, BitplaneUnit.PalVisibleTop), "HIRES bit 15 should affect only the first hires pixel.");
            Check.Equal(0, bitplanes.RenderColorIndex(1, BitplaneUnit.PalVisibleTop), "HIRES must not duplicate the first bit into the second pixel.");
        }

        /// <summary>
        /// Verifies that an unaligned OCS hires DDF range still consumes the complete fetch block.
        /// </summary>
        private static void TestMisalignedHiresDdfFetchesCompleteBlock()
        {
            var registers = new CustomRegisterMap();
            ushort[] memory = new ushort[160];
            memory[39] = 0x0001;
            var bitplanes = new BitplaneUnit(registers, address => memory[(address >> 1) % memory.Length]);
            registers.WriteWord(Bplcon0, 0xA000);
            registers.WriteWord(Dmacon, 0x8300);
            registers.WriteWord(Diwstrt, 0x2C00);
            registers.WriteWord(Diwstop, 0x2DFF);
            registers.WriteWord(Ddfstrt, 0x003C);
            registers.WriteWord(Ddfstop, 0x00D0);
            registers.WriteWord(Bpl1pth, 0x0000);
            registers.WriteWord(Bpl1ptl, 0x0000);
            registers.WriteWord(Bpl2pth, 0x0000);
            registers.WriteWord(Bpl2ptl, 0x0100);

            for (int beamX = 0x3C; beamX < 0xDC; beamX++)
            {
                if (bitplanes.IsPlaneFetchSlot(beamX, BitplaneUnit.PalVisibleTop))
                {
                    bitplanes.FetchDmaPlaneWord(beamX, BitplaneUnit.PalVisibleTop);
                }
            }

            Check.Equal(1, bitplanes.RenderColorIndex(655, BitplaneUnit.PalVisibleTop), "The final word of a $3C..$D0 two-plane hires fetch must not be dropped.");
        }

        /// <summary>
        /// Verifies HAM6 base colors and component modify operations.
        /// </summary>
        private static void TestHamControlCodesModifyPreviousColor()
        {
            var registers = new CustomRegisterMap();
            var bitplanes = new BitplaneUnit(registers, null);
            registers.WriteWord(Bplcon0, 0x6800);
            registers.WriteWord(Color01, 0x0F00);
            bitplanes.RefreshFromRegisters();

            Check.Equal(0xFFFF0000u, bitplanes.ResolveColorArgb(0x01, 0xFF112233u), "HAM control 0 should select a base palette color.");
            Check.Equal(0xFF1122AAu, bitplanes.ResolveColorArgb(0x1A, 0xFF112233u), "HAM control 1 should replace blue with the expanded data nibble.");
            Check.Equal(0xFFAA2233u, bitplanes.ResolveColorArgb(0x2A, 0xFF112233u), "HAM control 2 should replace red with the expanded data nibble.");
            Check.Equal(0xFF11AA33u, bitplanes.ResolveColorArgb(0x3A, 0xFF112233u), "HAM control 3 should replace green with the expanded data nibble.");
        }

        /// <summary>
        /// Verifies Extra Half-Brite color bank behavior.
        /// </summary>
        private static void TestExtraHalfBriteColor()
        {
            var registers = new CustomRegisterMap();
            var bitplanes = new BitplaneUnit(registers, null);
            registers.WriteWord(Bplcon0, 0x6000);
            registers.WriteWord(Color01, 0x0FFF);
            bitplanes.NotifyCustomRegisterWordWrite(Bpl1dat, 0x8000);
            bitplanes.NotifyCustomRegisterWordWrite(Bpl6dat, 0x8000);
            bitplanes.RefreshFromRegisters();
            Check.Equal(33, bitplanes.RenderColorIndex(0, BitplaneUnit.PalVisibleTop), "BPL1 plus BPL6 should select EHB color index 33.");
            Check.Equal(0xFF7F7F7Fu, bitplanes.ReadColorArgb(33), "EHB color 33 should be half-bright COLOR01.");
        }

        /// <summary>
        /// Verifies CLXDAT playfield collision latch and clear-on-read semantics.
        /// </summary>
        private static void TestClxdatReadClear()
        {
            var registers = new CustomRegisterMap();
            var collisions = new CollisionUnit();
            registers.AttachCollisionUnit(collisions);
            var frameBuffer = new AmigaFrameBuffer(640, 256);
            var bitplanes = new BitplaneUnit(registers, null);
            var sprites = new SpriteUnit(registers, null);
            var denise = new Denise(frameBuffer, bitplanes, sprites, collisions);

            registers.WriteWord(Bplcon0, 0x2000);
            bitplanes.NotifyCustomRegisterWordWrite(Bpl1dat, 0x8000);
            bitplanes.NotifyCustomRegisterWordWrite(Bpl2dat, 0x8000);
            denise.Tick(new BeamTraceEntry { BeamX = 0x3E, BeamY = BitplaneUnit.PalVisibleTop, Frame = 0 });

            Check.Equal(0x0001, registers.ReadWord(CustomRegisterMap.Clxdat), "PF1/PF2 collision should set CLXDAT bit 0.");
            Check.Equal(0x0000, registers.ReadWord(CustomRegisterMap.Clxdat), "Reading CLXDAT should clear collision bits.");
        }

    }

    /// <summary>
    /// Provides Sprite Tests functionality.
    /// </summary>
    internal static class SpriteTests
    {
        /// <summary>
        /// Defines the DMACON custom register offset or bit mask.
        /// </summary>
        private const uint Dmacon = 0x096;
        /// <summary>
        /// Defines the SPR0POS custom register offset or bit mask.
        /// </summary>
        private const uint Spr0pos = 0x140;
        /// <summary>
        /// Defines the SPR0CTL custom register offset or bit mask.
        /// </summary>
        private const uint Spr0ctl = 0x142;
        /// <summary>
        /// Defines the SPR0DATA custom register offset or bit mask.
        /// </summary>
        private const uint Spr0data = 0x144;
        /// <summary>
        /// Defines the SPR0DATB custom register offset or bit mask.
        /// </summary>
        private const uint Spr0datb = 0x146;

        /// <summary>
        /// Builds the Build result.
        /// </summary>
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return new ChipTestCase(
                "Denise/Sprite",
                "direct SPRxPOS/CTL/DATA register sprite renders without a sprite pointer list",
                "Amiga HRM sprite registers and direct sprite data latches",
                TestDirectSpriteRegisterRendering);
            yield return new ChipTestCase(
                "Denise/Sprite",
                "sprite horizontal position is relative to the emulator framebuffer start",
                "Amiga HRM SPRxPOS horizontal start and standard PAL fetch start",
                TestSpriteHorizontalPositionRelativeToFramebuffer);
            yield return new ChipTestCase(
                "Denise/Sprite",
                "direct sprite registers render without sprite DMA or chip-memory reader",
                "Amiga HRM manual sprite data registers",
                TestDirectSpriteWithoutDmaOrReader);
            yield return new ChipTestCase(
                "Denise/Sprite",
                "single playfield sprite priority uses BPLCON2 PF2P bits",
                "Amiga HRM BPLCON2 sprite/playfield priority rules",
                TestSinglePlayfieldSpritePriority);
            yield return new ChipTestCase(
                "Denise/Sprite",
                "sprite DMA state machine fetches control and data through Agnus slots",
                "Amiga HRM sprite DMA pointer, control, and data fetch sequence",
                TestSpriteDmaStateMachine);
            yield return new ChipTestCase(
                "Denise/Sprite",
                "sprite DMA renders the VSTART line",
                "Amiga HRM SPRxPOS vertical start semantics",
                TestSpriteDmaRendersVStartLine);
            yield return new ChipTestCase(
                "Denise/Sprite",
                "sprite pointer write reloads control before bitmap data",
                "Amiga HRM sprite pointer registers and DMA control-word fetch sequence",
                TestSpritePointerWriteReloadsControlWords);
            yield return new ChipTestCase(
                "Denise/Sprite",
                "sprite DMA stops at a zero control-word terminator",
                "Amiga HRM sprite DMA list termination",
                TestSpriteDmaTerminatorStopsFetches);
            yield return new ChipTestCase(
                "Denise/Sprite",
                "sprite DMA stops rendering on the VSTOP line",
                "Amiga HRM SPRxCTL vertical stop semantics",
                TestSpriteDmaStopsOnVStopLine);
            yield return new ChipTestCase(
                "Denise/Sprite",
                "sprite POS write does not move stale line data",
                "Amiga HRM sprite data latch and position register timing",
                TestSpritePositionWriteDoesNotMoveStaleLineData);
        }

        /// <summary>
        /// Runs Test Direct Sprite Register Rendering.
        /// </summary>
        private static void TestDirectSpriteRegisterRendering()
        {
            var registers = new CustomRegisterMap();
            var sprites = new SpriteUnit(registers, address => 0);
            registers.WriteWord(Dmacon, 0x8220);
            registers.WriteWord(Spr0pos, 0x2C40);
            registers.WriteWord(Spr0ctl, 0x2D00);
            registers.WriteWord(Spr0data, 0x8000);
            registers.WriteWord(Spr0datb, 0x0000);
            sprites.RefreshFromRegisters();

            Check.True(sprites.TryRenderPixel(0, BitplaneUnit.PalVisibleTop, out uint _, out int group), "Sprite pixel should render from direct SPR0DATA.");
            Check.Equal(0, group, "SPR0 should be in priority group 0.");
        }

        /// <summary>
        /// Runs Test Sprite Horizontal Position Relative To Framebuffer.
        /// </summary>
        private static void TestSpriteHorizontalPositionRelativeToFramebuffer()
        {
            var registers = new CustomRegisterMap();
            registers.WriteWord(Spr0pos, 0x2C46);
            registers.WriteWord(Spr0ctl, 0x2D00);
            registers.WriteWord(Spr0data, 0x8000);
            registers.WriteWord(Spr0datb, 0x0000);

            var sprites = new SpriteUnit(registers, null);
            sprites.RefreshFromRegisters();

            Check.True(!sprites.TryRenderPixel(0, BitplaneUnit.PalVisibleTop, out uint _, out int _), "SPRxPOS HPOS=$46 should not be rendered at framebuffer X zero.");
            Check.True(sprites.TryRenderPixel(0x18, BitplaneUnit.PalVisibleTop, out uint _, out int _), "SPRxPOS HPOS=$46 should render 24 hires pixels after the framebuffer's viewport edge.");
        }

        /// <summary>
        /// Runs Test Direct Sprite Without Dma Or Reader.
        /// </summary>
        private static void TestDirectSpriteWithoutDmaOrReader()
        {
            var registers = new CustomRegisterMap();
            var sprites = new SpriteUnit(registers, null);
            registers.WriteWord(Spr0pos, 0x2C40);
            registers.WriteWord(Spr0ctl, 0x2D00);
            registers.WriteWord(Spr0data, 0x8000);
            registers.WriteWord(Spr0datb, 0x0000);
            sprites.RefreshFromRegisters();

            Check.True(sprites.TryRenderPixel(0, BitplaneUnit.PalVisibleTop, out uint _, out int group), "Manual sprite should render without Sprite DMA or a Chip RAM reader.");
            Check.Equal(0, group, "SPR0 should be in priority group 0.");
        }

        /// <summary>
        /// Runs Test Single Playfield Sprite Priority.
        /// </summary>
        private static void TestSinglePlayfieldSpritePriority()
        {
            /// <summary>
            /// Defines the BPLCON0 custom register offset or bit mask.
            /// </summary>
            const uint bplcon0 = 0x100;
            /// <summary>
            /// Defines the BPLCON2 custom register offset or bit mask.
            /// </summary>
            const uint bplcon2 = 0x104;
            /// <summary>
            /// Defines the BPL1DAT custom register offset or bit mask.
            /// </summary>
            const uint bpl1dat = 0x110;
            /// <summary>
            /// Constant for color 01.
            /// </summary>
            const uint color01 = 0x182;
            /// <summary>
            /// Constant for color 17.
            /// </summary>
            const uint color17 = 0x1A2;

            var registers = new CustomRegisterMap();
            var frame = new AmigaFrameBuffer(640, 256);
            var bitplanes = new BitplaneUnit(registers, null);
            var denise = new Denise(frame, bitplanes, registers, null);
            registers.WriteWord(bplcon0, 0x1000);
            registers.WriteWord(bplcon2, 0x0020);
            registers.WriteWord(color01, 0x0F0);
            registers.WriteWord(color17, 0xF00);
            bitplanes.NotifyCustomRegisterWordWrite(bpl1dat, 0x8000);
            registers.WriteWord(Spr0pos, 0x2C40);
            registers.WriteWord(Spr0ctl, 0x2D00);
            registers.WriteWord(Spr0data, 0x8000);
            registers.WriteWord(Spr0datb, 0x0000);

            denise.Tick(new BeamTraceEntry { BeamX = 0x3E, BeamY = BitplaneUnit.PalVisibleTop, Frame = 0 });
            Check.Equal(0xFFFF0000u, frame.Pixels[0], "PF2P priority bits should allow SPR0 to cover a nonzero single-playfield pixel.");
        }

        /// <summary>
        /// Runs Test Sprite Dma State Machine.
        /// </summary>
        private static void TestSpriteDmaStateMachine()
        {
            var registers = new CustomRegisterMap();
            var sprites = new SpriteUnit(registers, address =>
            {
                switch (address)
                {
                    case 0x100: return 0x2C40;
                    case 0x102: return 0x2E00;
                    case 0x104: return 0x8000;
                    case 0x106: return 0x0000;
                    case 0x108: return 0x8000;
                    case 0x10A: return 0x0000;
                    default: return 0;
                }
            });
            registers.WriteWord(Dmacon, 0x8220);
            registers.WriteWord(0x120, 0x0000);
            registers.WriteWord(0x122, 0x0100);
            sprites.RefreshFromRegisters();

            sprites.TickDmaSlot(0, 0, 0);
            sprites.TickDmaSlot(0, 1, 0);
            Check.True(!sprites.TryRenderPixel(0, BitplaneUnit.PalVisibleTop, out uint _, out int _), "Control fetch alone should not render data.");

            sprites.TickDmaSlot(0, 0, BitplaneUnit.PalVisibleTop);
            sprites.TickDmaSlot(0, 1, BitplaneUnit.PalVisibleTop);
            Check.True(sprites.TryRenderPixel(0, BitplaneUnit.PalVisibleTop, out uint _, out int groupAtStart), "Sprite DMA should render the VSTART scanline after the first data fetch.");
            Check.Equal(0, groupAtStart, "Fetched sprite data should belong to sprite group 0.");

            sprites.TickDmaSlot(0, 0, BitplaneUnit.PalVisibleTop + 1);
            sprites.TickDmaSlot(0, 1, BitplaneUnit.PalVisibleTop + 1);
            Check.True(sprites.TryRenderPixel(0, BitplaneUnit.PalVisibleTop + 1, out uint _, out int group), "Next scanline sprite slot pair should fetch visible data.");
            Check.Equal(0, group, "Fetched sprite data should belong to sprite group 0.");
        }

        /// <summary>
        /// Runs Test Sprite Dma Renders V Start Line.
        /// </summary>
        private static void TestSpriteDmaRendersVStartLine()
        {
            var registers = new CustomRegisterMap();
            var words = new Dictionary<uint, ushort>();
            words[0x100] = 0x2C40;
            words[0x102] = 0x2D00;
            words[0x104] = 0x8000;
            words[0x106] = 0x0000;
            words[0x108] = 0x0000;
            words[0x10A] = 0x0000;

            var sprites = new SpriteUnit(registers, address => words.TryGetValue(address, out ushort value) ? value : (ushort)0);
            registers.WriteWord(Dmacon, 0x8220);
            registers.WriteWord(0x120, 0x0000);
            registers.WriteWord(0x122, 0x0100);
            sprites.RefreshFromRegisters();

            sprites.TickDmaSlot(0, 0, 0);
            sprites.TickDmaSlot(0, 1, 0);
            sprites.TickDmaSlot(0, 0, BitplaneUnit.PalVisibleTop);
            sprites.TickDmaSlot(0, 1, BitplaneUnit.PalVisibleTop);
            Check.True(sprites.TryRenderPixel(0, BitplaneUnit.PalVisibleTop, out uint _, out int _), "The first sprite bitmap word should be visible on VSTART, not one line later.");
        }

        /// <summary>
        /// Runs Test Sprite Pointer Write Reloads Control Words.
        /// </summary>
        private static void TestSpritePointerWriteReloadsControlWords()
        {
            var registers = new CustomRegisterMap();
            var words = new Dictionary<uint, ushort>();
            words[0x080] = 0x1040;
            words[0x082] = 0x1200;
            words[0x100] = 0x2C40;
            words[0x102] = 0x2E00;
            words[0x104] = 0x8000;
            words[0x106] = 0x0000;

            var sprites = new SpriteUnit(registers, address => words.TryGetValue(address, out ushort value) ? value : (ushort)0);
            registers.WriteWord(Dmacon, 0x8220);
            registers.WriteWord(0x120, 0x0000);
            registers.WriteWord(0x122, 0x0080);
            sprites.RefreshFromRegisters();

            sprites.TickDmaSlot(0, 0, 0);
            sprites.TickDmaSlot(0, 1, 0);

            registers.WriteWord(0x120, 0x0000);
            sprites.NotifyCustomRegisterWordWrite(0x120, 0x0000, 1);
            registers.WriteWord(0x122, 0x0100);
            sprites.NotifyCustomRegisterWordWrite(0x122, 0x0100, 1);

            sprites.TickDmaSlot(0, 0, 1);
            sprites.TickDmaSlot(0, 1, 1);
            sprites.TickDmaSlot(0, 0, BitplaneUnit.PalVisibleTop);
            sprites.TickDmaSlot(0, 1, BitplaneUnit.PalVisibleTop);

            Check.True(sprites.TryRenderPixel(0, BitplaneUnit.PalVisibleTop, out uint _, out int _), "SPRxPT writes must force a fresh control fetch before the first bitmap data fetch.");
        }

        /// <summary>
        /// Runs Test Sprite Dma Terminator Stops Fetches.
        /// </summary>
        private static void TestSpriteDmaTerminatorStopsFetches()
        {
            var registers = new CustomRegisterMap();
            var words = new Dictionary<uint, ushort>();
            words[0x100] = 0x7060;
            words[0x102] = 0x8000;
            for (uint line = 0; line < 16; line++)
            {
                words[0x104 + (line * 4u)] = 0x8000;
                words[0x106 + (line * 4u)] = 0x0000;
            }

            uint terminator = 0x104 + (16u * 4u);
            words[terminator] = 0x0000;
            words[terminator + 2u] = 0x0000;
            words[terminator + 4u] = 0xFFFF;
            words[terminator + 6u] = 0xFFFF;

            var sprites = new SpriteUnit(registers, address => words.TryGetValue(address, out ushort value) ? value : (ushort)0);
            registers.WriteWord(Dmacon, 0x8220);
            registers.WriteWord(0x120, 0x0000);
            registers.WriteWord(0x122, 0x0100);
            sprites.RefreshFromRegisters();

            for (int beamY = 0; beamY <= 141; beamY++)
            {
                sprites.TickDmaSlot(0, 0, beamY);
                sprites.TickDmaSlot(0, 1, beamY);
            }

            Check.True(!sprites.TryRenderPixel(0xC0, 141, out uint _, out int _), "Sprite DMA must not render words after a zero control-word terminator.");
        }

        /// <summary>
        /// Runs Test Sprite Dma Stops On V Stop Line.
        /// </summary>
        private static void TestSpriteDmaStopsOnVStopLine()
        {
            var registers = new CustomRegisterMap();
            var words = new Dictionary<uint, ushort>();
            words[0x100] = 0x2C40;
            words[0x102] = 0x2E00;
            words[0x104] = 0x8000;
            words[0x106] = 0x0000;
            words[0x108] = 0x8000;
            words[0x10A] = 0x0000;
            words[0x10C] = 0x0000;
            words[0x10E] = 0x0000;

            var sprites = new SpriteUnit(registers, address => words.TryGetValue(address, out ushort value) ? value : (ushort)0);
            registers.WriteWord(Dmacon, 0x8220);
            registers.WriteWord(0x120, 0x0000);
            registers.WriteWord(0x122, 0x0100);
            sprites.RefreshFromRegisters();

            sprites.TickDmaSlot(0, 0, BitplaneUnit.PalVisibleTop);
            sprites.TickDmaSlot(0, 1, BitplaneUnit.PalVisibleTop);
            sprites.TickDmaSlot(0, 0, BitplaneUnit.PalVisibleTop + 1);
            sprites.TickDmaSlot(0, 1, BitplaneUnit.PalVisibleTop + 1);
            Check.True(sprites.TryRenderPixel(0, BitplaneUnit.PalVisibleTop + 1, out uint _, out int _), "Sprite should render before VSTOP.");

            sprites.TickDmaSlot(0, 0, BitplaneUnit.PalVisibleTop + 2);
            sprites.TickDmaSlot(0, 1, BitplaneUnit.PalVisibleTop + 2);
            Check.True(!sprites.TryRenderPixel(0, BitplaneUnit.PalVisibleTop + 2, out uint _, out int _), "Sprite must not render stale data on the VSTOP line.");
        }

        /// <summary>
        /// Runs Test Sprite Position Write Does Not Move Stale Line Data.
        /// </summary>
        private static void TestSpritePositionWriteDoesNotMoveStaleLineData()
        {
            var registers = new CustomRegisterMap();
            registers.WriteWord(Spr0pos, 0x2C40);
            registers.WriteWord(Spr0ctl, 0x2D00);
            registers.WriteWord(Spr0data, 0x8000);
            registers.WriteWord(Spr0datb, 0x0000);

            var sprites = new SpriteUnit(registers, null);
            sprites.RefreshFromRegisters();
            Check.True(sprites.TryRenderPixel(0, BitplaneUnit.PalVisibleTop, out uint _, out int _), "Initial direct sprite data should render at the original X position.");

            registers.WriteWord(Spr0pos, 0x2C46);
            sprites.NotifyCustomRegisterWordWrite(Spr0pos, 0x2C46, BitplaneUnit.PalVisibleTop);
            Check.True(!sprites.TryRenderPixel(0x18, BitplaneUnit.PalVisibleTop, out uint _, out int _), "Moving SPRxPOS must not drag the previous line's data to the new X position before SPRxDATA is refreshed.");
        }
    }

    /// <summary>
    /// Provides Paula Tests functionality.
    /// </summary>
    internal static class PaulaTests
    {
        /// <summary>
        /// Builds the Build result.
        /// </summary>
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return new ChipTestCase(
                "Paula/Disk",
                "standard ADF MFM track starts with gap and contains paired sync words",
                "Amiga HRM disk controller; AmigaDOS MFM sector layout",
                TestGeneratedMfmTrackSync);
            yield return new ChipTestCase(
                "Paula/Disk",
                "DSKLEN 0x1900 track read can capture every AmigaDOS sector",
                "Custom trackloaders depend on a full ADF MFM revolution fitting in a 0x1900-word read",
                TestFullTrackReadCapturesAllSectors);
            yield return new ChipTestCase(
                "Paula/Disk",
                "disk write DMA transfers one chip-memory word per disk DMA slot",
                "Amiga HRM DSKLEN write mode and Paula disk DMA timing",
                TestDiskWriteDmaTiming);
            yield return new ChipTestCase(
                "Paula/Disk",
                "inactive DSKLEN stop clears disk write status",
                "Paula DSKLEN stop semantics and DSKBYTR status polling",
                TestDiskStopClearsWriteStatus);
            yield return new ChipTestCase(
                "Paula/Disk",
                "WORDSYNC read DMA starts after the matching sync word",
                "Paula disk WORDSYNC DMA start behavior used by custom trackloaders",
                TestWordSyncReadDmaStartsAfterMatchingSyncWord);
            yield return new ChipTestCase(
                "Paula/Disk",
                "WORDSYNC live stream match survives a full DMA queue",
                "Paula DSKSYNC compares the live disk stream, not only queued DMA words",
                TestWordSyncLiveMatchSurvivesFullQueue);
            yield return new ChipTestCase(
                "Paula/Disk",
                "WORDSYNC waits for enabled disk DMA slots",
                "Paula disk WORDSYNC must not arm read DMA while DMACON.DMAEN or DSKEN is disabled",
                TestWordSyncWaitsForEnabledDiskDmaSlots);
            yield return new ChipTestCase(
                "Paula/Disk",
                "passive disk sync words do not raise DSKSYN interrupts",
                "Paula exposes sync matches for polling, but the level-5 DSKSYN IRQ belongs to active WORDSYNC DMA",
                TestPassiveDiskSyncDoesNotRaiseDiskSyncInterrupt);
            yield return new ChipTestCase(
                "Paula/Disk",
                "captured MFM write sectors decode back into ADF sectors",
                "AmigaDOS MFM sector layout; Paula disk write path",
                TestCapturedMfmWriteSectorDecodesToAdf);
            yield return new ChipTestCase(
                "Paula/Disk",
                "writable ADF sector changes persist only after an explicit flush",
                "ADF host-file persistence for floppy writeback",
                TestAdfSectorWritesFlushToHostFile);
            yield return new ChipTestCase(
                "Paula/Disk",
                "selected DF0 follows CIA-B motor line changes without requiring reselection",
                "Amiga CIA-B floppy select and motor control lines",
                TestSelectedDriveFollowsMotorLine);
            yield return new ChipTestCase(
                "Paula/Disk",
                "disk flux is available before CIA ready asserts",
                "Paula DSKBYTR/DSKLEN see the selected drive flux stream independently of the CIA-A RDY status bit",
                TestDiskFluxBeforeReadySignal);
            yield return new ChipTestCase(
                "Paula/Disk",
                "motor-off grace keeps the floppy stream rotating",
                "Real drives keep spinning briefly after MOTOR is released; trackloaders can still see flux and index timing",
                TestMotorOffGraceKeepsFluxAndIndexRunning);
            yield return new ChipTestCase(
                "Paula/Disk",
                "deselected running drive keeps its MFM phase rotating",
                "Floppy rotation is physical drive state, not a side effect of Paula sampling a selected drive",
                TestDeselectedRunningDriveKeepsMfmPhaseRotating);
            yield return new ChipTestCase(
                "Paula/Disk",
                "DF0 steps only while selected and follows CIA-B direction polarity",
                "Amiga CIA-B floppy STEP and DIR control lines",
                TestSelectedDriveStepDirection);
            yield return new ChipTestCase(
                "Paula/Disk",
                "absent external floppy drives leave CIA-A disk status lines inactive",
                "A500 CIA-A floppy status lines with no DF1-DF3 hardware attached",
                TestAbsentExternalDriveStatusLines);
            yield return new ChipTestCase(
                "Paula/Disk",
                "external floppy media can be mounted and selected through CIA-B",
                "DF1-DF3 use the same shared Amiga floppy bus signals as DF0",
                TestExternalDriveMediaSelection);
            yield return new ChipTestCase(
                "Paula/Disk",
                "external selected drives raise the shared CIAB index pulse",
                "The Amiga floppy INDEX line is selected-drive bus state, not a DF0-only signal",
                TestExternalDriveIndexPulseReachesController);
            yield return new ChipTestCase(
                "Paula/Disk",
                "DF0 media swaps preserve the physical head and CIA control-line state",
                "Amiga floppy media change mechanics for multi-disk trackloaders",
                TestMediaSwapPreservesDriveMechanics);
            yield return new ChipTestCase(
                "Paula/Serial",
                "SERDATR idle read does not expose stale custom-register shadow data",
                "Amiga Paula SERDATR transmit-ready status during Kickstart reset",
                TestSerdatrIdleReadIsNotStaleShadow);
        }

        /// <summary>
        /// Runs Test Generated Mfm Track Sync.
        /// </summary>
        private static void TestGeneratedMfmTrackSync()
        {
            string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "amiga_tests_mfm_" + Guid.NewGuid().ToString("N") + ".adf");
            byte[] bytes = new byte[AdfImage.StandardSize];
            System.IO.File.WriteAllBytes(tempPath, bytes);
            try
            {
                AdfImage image = AdfImage.Load(tempPath);
                AdfTrackStream track = image.BuildTrackStream(0, 0);
                Check.True(track.WordCount > AdfMfmEncoder.SectorWords, "Generated track should contain a full MFM stream.");
                Check.Equal(AdfMfmEncoder.GapWord, track.ReadWord(0), "Track should begin with gap words.");

                bool foundPairedSync = false;
                int firstPairedSync = -1;
                int pairedSyncs = 0;
                int previousPairedSync = -1;
                for (int index = 1; index < track.WordCount; index++)
                {
                    if (track.ReadWord(index - 1) == AdfMfmEncoder.SyncWord &&
                        track.ReadWord(index) == AdfMfmEncoder.SyncWord)
                    {
                        foundPairedSync = true;
                        pairedSyncs++;
                        if (firstPairedSync < 0)
                        {
                            firstPairedSync = index - 1;
                        }

                        if (previousPairedSync >= 0)
                        {
                            int spacing = (index - 1) - previousPairedSync;
                            Check.Equal(AdfMfmEncoder.SectorWords, spacing, "Generated MFM sectors should keep the standard AmigaDOS sector spacing.");
                        }

                        previousPairedSync = index - 1;
                        index++;
                    }
                }

                Check.True(foundPairedSync, "Generated MFM track should contain paired Amiga sync words.");
                Check.Equal(AdfMfmEncoder.TrackGapWords + 2, firstPairedSync, "Generated MFM track should leave a stable lead-in gap before the first sector sync.");
                Check.Equal(AdfImage.SectorsPerTrack, pairedSyncs, "Generated MFM track should contain one paired sync marker per sector.");
            }
            finally
            {
                try
                {
                    System.IO.File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// Verifies that Paula owns SERDATR reads so reset code cannot see stale shadow values.
        /// </summary>
        private static void TestSerdatrIdleReadIsNotStaleShadow()
        {
            var paula = new Paula();
            ushort value;
            Check.True(paula.TryReadCustomRegisterWord(0x018, out value), "Paula should handle SERDATR reads.");
            Check.Equal(0x30FF, value, "Idle SERDATR should report transmitter-ready status with the receive line high.");

            Check.True(paula.TryPeekCustomRegisterWord(0x018, out value), "Paula should handle SERDATR peeks.");
            Check.Equal(0x30FF, value, "Idle SERDATR peek should match the side-effecting read.");
            Check.Equal(0x007F, value & 0x007F, "Kickstart reset waits for the idle receive line through SERDATR low bits.");
        }

        /// <summary>
        /// Verifies that the selected drive tracks live motor-line changes.
        /// </summary>
        private static void TestSelectedDriveFollowsMotorLine()
        {
            var controller = new FloppyController();

            controller.ApplyCiaBPort(0x77, 0xFF);
            Check.True(controller.Drive0.Selected, "DF0 should be selected when CIA-B SEL0 is low.");
            Check.True(controller.Drive0.MotorOn, "DF0 motor should turn on when selected and MOTOR is low.");

            controller.ApplyCiaBPort(0xF7, 0xFF);
            Check.True(controller.Drive0.Selected, "DF0 should remain selected while only MOTOR changes.");
            Check.True(!controller.Drive0.MotorOn, "DF0 motor should follow MOTOR high while selected.");

            controller.ApplyCiaBPort(0x77, 0xFF);
            Check.True(controller.Drive0.MotorOn, "DF0 motor should relatch on while the drive remains selected.");

            controller.ApplyCiaBPort(0xFF, 0xFF);
            Check.True(!controller.Drive0.Selected, "DF0 should deselect when CIA-B SEL0 is high.");
            Check.True(controller.Drive0.MotorOn, "A deselected drive should keep its latched motor state.");

            controller.ApplyCiaBPort(0xF7, 0xFF);
            Check.True(controller.Drive0.Selected, "DF0 should select again when CIA-B SEL0 returns low.");
            Check.True(!controller.Drive0.MotorOn, "DF0 motor should turn off when SEL0 is reasserted with MOTOR high.");

            controller.ApplyCiaBPort(0xFF, 0xFF);
            controller.ApplyCiaBPort(0x7F, 0xFF);
            Check.True(!controller.Drive0.Selected, "DF0 should stay deselected while only MOTOR changes.");
            Check.True(!controller.Drive0.MotorOn, "A deselected drive should not latch MOTOR changes.");

            controller.ApplyCiaBPort(0x77, 0xFF);
            Check.True(controller.Drive0.MotorOn, "DF0 motor should turn on when SEL0 is reasserted after MOTOR went low.");
        }

        /// <summary>
        /// Verifies that the raw MFM stream is not gated by the separate CIA-A ready signal.
        /// </summary>
        private static void TestDiskFluxBeforeReadySignal()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "amiga_tests_flux_before_ready_" + Guid.NewGuid().ToString("N") + ".adf");
            System.IO.File.WriteAllBytes(path, new byte[AdfImage.StandardSize]);
            try
            {
                var controller = new FloppyController();
                controller.InsertDrive0(AdfImage.Load(path), true);
                controller.ApplyCiaBPort(0x73, 0xFF);

                Check.True(controller.Drive0.Selected, "DF0 should be selected.");
                Check.True(controller.Drive0.MotorOn, "DF0 motor should be running.");
                Check.True(!controller.Drive0.ReadySignal, "Freshly inserted DF0 should not assert CIA-A RDY until spin-up completes.");
                bool foundSync = false;
                for (int cycle = 0; cycle < 800000; cycle++)
                {
                    controller.Tick();
                    if (controller.Drive0.ReadNextMfmWord() == 0x4489)
                    {
                        foundSync = true;
                        break;
                    }
                }

                Check.True(foundSync, "Paula should still see the rotating MFM stream before CIA-A RDY asserts.");
            }
            finally
            {
                TryDeleteFile(path);
            }
        }

        /// <summary>
        /// Verifies that a deselected motor-off grace period still advances the physical disk stream.
        /// </summary>
        private static void TestMotorOffGraceKeepsFluxAndIndexRunning()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "amiga_tests_motor_grace_" + Guid.NewGuid().ToString("N") + ".adf");
            System.IO.File.WriteAllBytes(path, new byte[AdfImage.StandardSize]);
            try
            {
                var controller = new FloppyController();
                controller.InsertDrive0(AdfImage.Load(path), true);
                controller.ApplyCiaBPort(0x73, 0xFF);
                WaitForDriveReady(controller.Drive0);

                for (int cycle = 0; cycle < 512; cycle++)
                {
                    controller.Tick();
                }

                int streamBeforeStop = controller.Drive0.StreamWordPosition;
                int indexBeforeStop = controller.Drive0.IndexPulseCountdown;
                Check.True(streamBeforeStop > 0, "The selected running drive should advance physical MFM phase before MOTOR is released.");

                controller.ApplyCiaBPort(0xFF, 0xFF);
                controller.ApplyCiaBPort(0xF7, 0xFF);
                Check.True(!controller.Drive0.MotorOn, "DF0 motor should turn off when selected with CIA-B MOTOR high.");
                Check.True(controller.Drive0.CanReadFlux, "DF0 should continue to expose flux during motor-off grace.");
                Check.True(controller.Drive0.ReadySignal, "DF0 RDY should remain asserted during motor-off grace.");

                for (int cycle = 0; cycle < 512; cycle++)
                {
                    controller.Tick();
                }

                controller.Drive0.ReadNextMfmWord();
                Check.True(controller.Drive0.StreamWordPosition != streamBeforeStop, "The stream position should advance during motor-off grace.");
                Check.True(controller.Drive0.IndexPulseCountdown < indexBeforeStop, "The index countdown should keep running while the disk is coasting.");
            }
            finally
            {
                TryDeleteFile(path);
            }
        }

        /// <summary>
        /// Verifies that deselecting a still-running drive does not freeze its disk angular phase.
        /// </summary>
        private static void TestDeselectedRunningDriveKeepsMfmPhaseRotating()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "amiga_tests_deselect_rotation_" + Guid.NewGuid().ToString("N") + ".adf");
            System.IO.File.WriteAllBytes(path, new byte[AdfImage.StandardSize]);
            try
            {
                var controller = new FloppyController();
                controller.InsertDrive0(AdfImage.Load(path), true);
                controller.ApplyCiaBPort(0x73, 0xFF);
                WaitForDriveReady(controller.Drive0);

                for (int cycle = 0; cycle < 1024; cycle++)
                {
                    controller.Tick();
                }

                int selectedPosition = controller.Drive0.StreamWordPosition;
                controller.ApplyCiaBPort(0xFF, 0xFF);
                Check.True(!controller.Drive0.Selected, "DF0 should be deselected.");
                Check.True(controller.Drive0.MotorOn, "DF0 should keep its latched motor state while deselected.");

                for (int cycle = 0; cycle < 4096; cycle++)
                {
                    controller.Tick();
                }

                int deselectedPosition = controller.Drive0.StreamWordPosition;
                Check.True(deselectedPosition != selectedPosition, "A deselected but running DF0 must keep advancing the physical MFM phase.");

                controller.ApplyCiaBPort(0x77, 0xFF);
                Check.True(controller.Drive0.Selected, "DF0 should be reselected before Paula samples flux.");
                ushort sampledBefore = controller.Drive0.ReadNextMfmWord();
                int positionBeforeSample = controller.Drive0.StreamWordPosition;
                ushort sampledAgain = controller.Drive0.ReadNextMfmWord();
                Check.Equal(sampledBefore, sampledAgain, "Sampling the same physical phase twice should return the same word.");
                Check.Equal(positionBeforeSample, controller.Drive0.StreamWordPosition, "ReadNextMfmWord should sample the current phase without advancing rotation.");
            }
            finally
            {
                TryDeleteFile(path);
            }
        }

        /// <summary>
        /// Verifies that one 0x1900-word custom-loader read covers every sector after WORDSYNC.
        /// </summary>
        private static void TestFullTrackReadCapturesAllSectors()
        {
            string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "amiga_tests_mfm_fulltrack_" + Guid.NewGuid().ToString("N") + ".adf");
            byte[] bytes = new byte[AdfImage.StandardSize];
            for (int index = 0; index < bytes.Length; index++)
            {
                bytes[index] = (byte)((index * 37) + (index >> 8));
            }

            System.IO.File.WriteAllBytes(tempPath, bytes);
            try
            {
                AdfImage image = AdfImage.Load(tempPath);
                AdfTrackStream track = image.BuildTrackStream(0, 0);
                Check.Equal(0x1900, track.WordCount, "Generated ADF MFM tracks must fit a standard 0x1900-word trackloader read.");

                var pairedSyncStarts = new List<int>();
                for (int index = 1; index < track.WordCount; index++)
                {
                    if (track.ReadWord(index - 1) == AdfMfmEncoder.SyncWord &&
                        track.ReadWord(index) == AdfMfmEncoder.SyncWord)
                    {
                        pairedSyncStarts.Add(index - 1);
                        index++;
                    }
                }

                Check.Equal(AdfImage.SectorsPerTrack, pairedSyncStarts.Count, "Generated track should expose all sector sync pairs.");
                int startWord = pairedSyncStarts[4] + 1;
                ushort[] captured = new ushort[0x1900];
                for (int index = 0; index < captured.Length; index++)
                {
                    captured[index] = track.ReadWord((startWord + index) % track.WordCount);
                }

                var sectors = new HashSet<int>();
                for (int index = 0; index + 540 < captured.Length; index++)
                {
                    if (captured[index] != AdfMfmEncoder.SyncWord ||
                        captured[index + 1] == AdfMfmEncoder.SyncWord)
                    {
                        continue;
                    }

                    uint header = AdfMfmEncoder.DecodeOddEvenLong(
                        ReadMfmLong(captured, index + 1),
                        ReadMfmLong(captured, index + 3));
                    if ((byte)(header >> 24) != 0xFF)
                    {
                        continue;
                    }

                    int sector = (byte)(header >> 8);
                    if (sector < 0 || sector >= AdfImage.SectorsPerTrack)
                    {
                        continue;
                    }

                    uint checksum = AdfMfmEncoder.DecodeOddEvenLong(
                        ReadMfmLong(captured, index + 25),
                        ReadMfmLong(captured, index + 27));
                    for (int longIndex = 0; longIndex < 128; longIndex++)
                    {
                        checksum ^= ReadMfmLong(captured, index + 29 + (longIndex * 2));
                        checksum ^= ReadMfmLong(captured, index + 285 + (longIndex * 2));
                    }

                    Check.Equal(0u, checksum & 0x55555555u, "Captured sector data checksum should match AmigaDOS MFM data.");
                    sectors.Add(sector);
                }

                Check.Equal(AdfImage.SectorsPerTrack, sectors.Count, "A 0x1900-word read after WORDSYNC should capture all 11 sectors.");
            }
            finally
            {
                TryDeleteFile(tempPath);
            }
        }

        /// <summary>
        /// Reads one big-endian MFM long from two captured 16-bit words.
        /// </summary>
        private static uint ReadMfmLong(IList<ushort> words, int wordOffset)
        {
            return ((uint)words[wordOffset] << 16) | words[wordOffset + 1];
        }

        /// <summary>
        /// Verifies that STEP pulses affect only selected DF0 and that CIAB DIR low moves inward.
        /// </summary>
        private static void TestSelectedDriveStepDirection()
        {
            var controller = new FloppyController();

            controller.ApplyCiaBPort(0xFF, 0xFF);
            controller.ApplyCiaBPort(0xFE, 0xFF);
            controller.ApplyCiaBPort(0xFF, 0xFF);
            Check.Equal(0, controller.Drive0.Track, "Unselected DF0 must ignore shared STEP pulses.");

            controller.ApplyCiaBPort(0x74, 0xFF);
            Check.Equal(0, controller.Drive0.Track, "DF0 should wait until the low STEP pulse is released.");
            controller.ApplyCiaBPort(0x75, 0xFF);
            Check.Equal(1, controller.Drive0.Track, "DIR low should move DF0 one track inward.");

            controller.ApplyCiaBPort(0x76, 0xFF);
            Check.Equal(1, controller.Drive0.Track, "DF0 should not step at the start of the outward STEP pulse.");
            controller.ApplyCiaBPort(0x77, 0xFF);
            Check.Equal(0, controller.Drive0.Track, "DIR high should move DF0 one track outward.");
        }

        /// <summary>
        /// Verifies that non-present external drives do not actively assert status lines.
        /// </summary>
        private static void TestAbsentExternalDriveStatusLines()
        {
            var controller = new FloppyController();

            controller.ApplyCiaBPort(0xEF, 0xFF);
            Check.True(controller.Drive1.Selected, "DF1 should be selected when CIA-B SEL1 is low.");
            Check.Equal(FloppyController.CiaAStatusMask, controller.BuildCiaAStatusBits(), "Absent DF1 must leave disk status bits inactive.");

            controller.ApplyCiaBPort(0xDF, 0xFF);
            Check.True(controller.Drive2.Selected, "DF2 should be selected when CIA-B SEL2 is low.");
            Check.Equal(FloppyController.CiaAStatusMask, controller.BuildCiaAStatusBits(), "Absent DF2 must leave disk status bits inactive.");

            controller.ApplyCiaBPort(0x77, 0xFF);
            Check.True(controller.Drive0.Selected, "DF0 should still be modeled as the built-in drive.");
            Check.Equal(
                FloppyController.CiaAStatusMask & ~FloppyController.CiaADiskChangeBit & ~FloppyController.CiaATrack0Bit,
                controller.BuildCiaAStatusBits(),
                "Empty DF0 should still assert disk-change and track-zero.");
        }

        /// <summary>
        /// Verifies that externally mounted floppy media participates in the shared drive bus.
        /// </summary>
        private static void TestExternalDriveMediaSelection()
        {
            string adfPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "amiga_tests_df1_" + Guid.NewGuid().ToString("N") + ".adf");
            System.IO.File.WriteAllBytes(adfPath, new byte[AdfImage.StandardSize]);
            try
            {
                var controller = new FloppyController();
                controller.InsertDrive(1, AdfImage.Load(adfPath), true);

                controller.ApplyCiaBPort(0x6F, 0xFF);
                Check.True(controller.Drive1.Selected, "DF1 should be selected when CIA-B SEL1 is low.");
                Check.True(controller.Drive1.MotorOn, "DF1 motor should follow the shared MOTOR line.");
                Check.True(controller.Drive1.HasDisk, "DF1 should contain the mounted external media.");
                Check.True(!controller.Drive0.Selected, "DF0 must remain deselected while DF1 is selected.");
                Check.Equal(controller.Drive1, controller.SelectedDrive, "The selected-drive bus view should expose DF1.");
                Check.Equal(
                    FloppyController.CiaAStatusMask & ~FloppyController.CiaADiskChangeBit & ~FloppyController.CiaAWriteProtectBit & ~FloppyController.CiaATrack0Bit,
                    controller.BuildCiaAStatusBits(),
                    "Freshly mounted write-protected DF1 should assert disk-change, write-protect, and track-zero.");
            }
            finally
            {
                TryDeleteFile(adfPath);
            }
        }

        /// <summary>
        /// Verifies that external drives can trigger the shared CIAB FLAG/index pulse.
        /// </summary>
        private static void TestExternalDriveIndexPulseReachesController()
        {
            string adfPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "amiga_tests_df1_index_" + Guid.NewGuid().ToString("N") + ".adf");
            System.IO.File.WriteAllBytes(adfPath, new byte[AdfImage.StandardSize]);
            try
            {
                var controller = new FloppyController();
                controller.InsertDrive(1, AdfImage.Load(adfPath), true);
                controller.ApplyCiaBPort(0x6F, 0xFF);

                bool sawIndex = false;
                for (int cycle = 0; cycle < 800000; cycle++)
                {
                    controller.Tick();
                    if (controller.IndexPulsePending)
                    {
                        sawIndex = true;
                        break;
                    }
                }

                Check.True(sawIndex, "A selected rotating DF1 must expose its index pulse on the shared floppy bus.");
            }
            finally
            {
                TryDeleteFile(adfPath);
            }
        }

        /// <summary>
        /// Verifies that replacing a disk does not reset the physical drive head or CIA lines.
        /// </summary>
        private static void TestMediaSwapPreservesDriveMechanics()
        {
            string firstPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "amiga_tests_swap_a_" + Guid.NewGuid().ToString("N") + ".adf");
            string secondPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "amiga_tests_swap_b_" + Guid.NewGuid().ToString("N") + ".adf");
            System.IO.File.WriteAllBytes(firstPath, new byte[AdfImage.StandardSize]);
            System.IO.File.WriteAllBytes(secondPath, new byte[AdfImage.StandardSize]);
            try
            {
                var controller = new FloppyController();
                controller.InsertDrive0(AdfImage.Load(firstPath), true);
                controller.ApplyCiaBPort(0x73, 0xFF);
                controller.Drive0.Step(36);

                Check.True(controller.Drive0.Selected, "DF0 should be selected before media replacement.");
                Check.True(controller.Drive0.MotorOn, "DF0 motor should be on before media replacement.");
                Check.Equal(36, controller.Drive0.Track, "DF0 should be positioned away from track zero before media replacement.");
                Check.Equal(1, controller.Drive0.Side, "DF0 side should follow CIA-B before media replacement.");

                controller.InsertDrive0(AdfImage.Load(secondPath), true);

                Check.True(controller.Drive0.HasDisk, "DF0 should contain the replacement media.");
                Check.True(controller.Drive0.DiskChanged, "DF0 should assert disk-change after media replacement.");
                Check.True(controller.Drive0.Selected, "DF0 selection should survive media replacement.");
                Check.True(controller.Drive0.MotorOn, "DF0 motor state should survive media replacement.");
                Check.Equal(36, controller.Drive0.Track, "DF0 head position should survive media replacement.");
                Check.Equal(1, controller.Drive0.Side, "DF0 side select should survive media replacement.");

                controller.EjectDrive0();

                Check.True(!controller.Drive0.HasDisk, "DF0 should be empty after eject.");
                Check.True(controller.Drive0.DiskChanged, "DF0 should assert disk-change after eject.");
                Check.True(controller.Drive0.Selected, "DF0 selection should survive eject.");
                Check.True(controller.Drive0.MotorOn, "DF0 motor state should survive eject.");
                Check.Equal(36, controller.Drive0.Track, "DF0 head position should survive eject.");
                Check.Equal(1, controller.Drive0.Side, "DF0 side select should survive eject.");
            }
            finally
            {
                TryDeleteFile(firstPath);
                TryDeleteFile(secondPath);
            }
        }

        /// <summary>
        /// Runs Test Disk Write Dma Timing.
        /// </summary>
        private static void TestDiskWriteDmaTiming()
        {
            var controller = new FloppyController();
            byte[] bytes = new byte[AdfImage.StandardSize];
            string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "amiga_tests_write_dma_" + Guid.NewGuid().ToString("N") + ".adf");
            System.IO.File.WriteAllBytes(tempPath, bytes);
            try
            {
                controller.InsertDrive0(AdfImage.Load(tempPath), false);
                controller.Drive0.ApplySelection(true, true);
                WaitForDriveReady(controller.Drive0);
                var chipWords = new ushort[] { 0x4489, 0xAAAA };
                var paula = new Paula();
                paula.AttachDisk(controller, null, address => chipWords[(address >> 1) & 1], delegate(CustomInterruptSource _) { }, () => 0x0210, () => 0);
                paula.NotifyCustomRegisterWordWrite(0x020, 0x0000);
                paula.NotifyCustomRegisterWordWrite(0x022, 0x0000);
                paula.NotifyCustomRegisterWordWrite(0x024, 0xC002);
                paula.NotifyCustomRegisterWordWrite(0x024, 0xC002);

                paula.Tick(true, false);
                Check.Equal(1, paula.RemainingDiskWords, "First disk DMA slot should write one word.");
                paula.Tick(true, false);
                Check.Equal(0, paula.RemainingDiskWords, "Second disk DMA slot should finish the two-word write.");
                Check.Equal(2, controller.Drive0.CapturedWriteWordCount, "Drive should capture both write-DMA MFM words.");
            }
            finally
            {
                try
                {
                    System.IO.File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// Verifies that stopping disk DMA clears stale DSKBYTR write-status bits.
        /// </summary>
        private static void TestDiskStopClearsWriteStatus()
        {
            var paula = new Paula();
            ushort status;

            paula.NotifyCustomRegisterWordWrite(0x024, 0xC002);
            paula.NotifyCustomRegisterWordWrite(0x024, 0xC002);
            Check.True(paula.TryReadCustomRegisterWord(0x01A, out status), "Paula should expose DSKBYTR.");
            Check.Equal(0x6000, status & 0x6000, "Active write DMA should set DSKBYTR DMA and write bits.");

            paula.NotifyCustomRegisterWordWrite(0x024, 0x4000);
            Check.True(paula.TryReadCustomRegisterWord(0x01A, out status), "Paula should expose DSKBYTR after DSKLEN stop.");
            Check.Equal(0x0000, status & 0x6000, "Inactive DSKLEN stop must clear stale DMA/write status.");
        }

        /// <summary>
        /// Verifies that WORDSYNC arms disk DMA on the first sync and transfers the following word.
        /// </summary>
        private static void TestWordSyncReadDmaStartsAfterMatchingSyncWord()
        {
            string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "amiga_tests_wordsync_read_" + Guid.NewGuid().ToString("N") + ".adf");
            System.IO.File.WriteAllBytes(tempPath, new byte[AdfImage.StandardSize]);
            try
            {
                var controller = new FloppyController();
                controller.InsertDrive0(AdfImage.Load(tempPath), true);
                controller.Drive0.ApplySelection(true, true);
                WaitForDriveReady(controller.Drive0);

                var captured = new List<ushort>();
                var paula = new Paula();
                paula.AttachDisk(
                    controller,
                    (address, word) => captured.Add(word),
                    null,
                    delegate(CustomInterruptSource _) { },
                    () => 0x0210,
                    () => 0x0400);
                paula.NotifyCustomRegisterWordWrite(0x020, 0x0000);
                paula.NotifyCustomRegisterWordWrite(0x022, 0x0000);
                paula.NotifyCustomRegisterWordWrite(0x024, 0x8004);
                paula.NotifyCustomRegisterWordWrite(0x024, 0x8004);

                for (int cycle = 0; cycle < 150000 && captured.Count < 2; cycle++)
                {
                    controller.Tick();
                    paula.Tick(true, false);
                }

                Check.True(captured.Count > 1, "WORDSYNC read DMA should eventually transfer after the first track sync.");
                Check.Equal(AdfMfmEncoder.SyncWord, captured[0], "WORDSYNC read DMA should start with the word after the matched sync, preserving the paired sync.");
                Check.True(captured[1] != AdfMfmEncoder.SyncWord, "WORDSYNC read DMA must not include the matched sync word before the paired sync.");
            }
            finally
            {
                TryDeleteFile(tempPath);
            }
        }

        /// <summary>
        /// Verifies that WORDSYNC detects the live stream even when no DMA slot has drained the short read queue.
        /// </summary>
        private static void TestWordSyncLiveMatchSurvivesFullQueue()
        {
            string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "amiga_tests_wordsync_full_queue_" + Guid.NewGuid().ToString("N") + ".adf");
            System.IO.File.WriteAllBytes(tempPath, new byte[AdfImage.StandardSize]);
            try
            {
                var controller = new FloppyController();
                controller.InsertDrive0(AdfImage.Load(tempPath), true);
                controller.Drive0.ApplySelection(true, true);
                WaitForDriveReady(controller.Drive0);

                var captured = new List<ushort>();
                var paula = new Paula();
                paula.AttachDisk(
                    controller,
                    (address, word) => captured.Add(word),
                    null,
                    delegate(CustomInterruptSource _) { },
                    () => 0x0210,
                    () => 0x0400);
                paula.NotifyCustomRegisterWordWrite(0x020, 0x0000);
                paula.NotifyCustomRegisterWordWrite(0x022, 0x0000);
                paula.NotifyCustomRegisterWordWrite(0x024, 0x8004);
                paula.NotifyCustomRegisterWordWrite(0x024, 0x8004);

                for (int cycle = 0; cycle < 800000; cycle++)
                {
                    controller.Tick();
                    paula.Tick(false, false);
                }

                Check.Equal(0, captured.Count, "The setup phase should not transfer without disk DMA slots.");
                for (int cycle = 0; cycle < 20000 && captured.Count < 2; cycle++)
                {
                    controller.Tick();
                    paula.Tick(true, false);
                }

                Check.True(captured.Count > 0, "WORDSYNC should have armed DMA from the live stream despite a previously full queue.");
                Check.True(captured[0] != AdfMfmEncoder.GapWord, "DMA should resume with post-sync stream data, not stale pre-sync gap words.");
            }
            finally
            {
                TryDeleteFile(tempPath);
            }
        }

        /// <summary>
        /// Verifies that an active DSKLEN block does not consume WORDSYNC while disk DMA is disabled.
        /// </summary>
        private static void TestWordSyncWaitsForEnabledDiskDmaSlots()
        {
            string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "amiga_tests_wordsync_dmacon_gate_" + Guid.NewGuid().ToString("N") + ".adf");
            System.IO.File.WriteAllBytes(tempPath, new byte[AdfImage.StandardSize]);
            try
            {
                var controller = new FloppyController();
                controller.InsertDrive0(AdfImage.Load(tempPath), true);
                controller.Drive0.ApplySelection(true, true);
                WaitForDriveReady(controller.Drive0);

                ushort dmacon = 0x0000;
                var captured = new List<ushort>();
                var paula = new Paula();
                paula.AttachDisk(
                    controller,
                    (address, word) => captured.Add(word),
                    null,
                    delegate(CustomInterruptSource _) { },
                    () => dmacon,
                    () => 0x0400);
                paula.NotifyCustomRegisterWordWrite(0x020, 0x0000);
                paula.NotifyCustomRegisterWordWrite(0x022, 0x0000);
                paula.NotifyCustomRegisterWordWrite(0x024, 0x8004);
                paula.NotifyCustomRegisterWordWrite(0x024, 0x8004);

                for (int cycle = 0; cycle < 800000; cycle++)
                {
                    controller.Tick();
                    paula.Tick(false, false);
                }

                Check.Equal(0, captured.Count, "Disabled disk DMA must not transfer any queued words.");
                Check.True(!paula.GetDiskDebugInfo().Contains("wordSyncMatched:True"), "Disabled disk DMA must not consume the WORDSYNC match.");

                dmacon = 0x0210;
                for (int cycle = 0; cycle < 800000 && captured.Count < 2; cycle++)
                {
                    controller.Tick();
                    paula.Tick(true, false);
                }

                Check.True(captured.Count > 0, "Enabling disk DMA should let the next WORDSYNC start the block.");
                Check.True(captured[0] != AdfMfmEncoder.GapWord, "DMA should start with post-sync data rather than stale disabled-DMA gap words.");
            }
            finally
            {
                TryDeleteFile(tempPath);
            }
        }

        /// <summary>
        /// Verifies that passive sync words on the disk stream do not create stale level-5 interrupts.
        /// </summary>
        private static void TestPassiveDiskSyncDoesNotRaiseDiskSyncInterrupt()
        {
            string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "amiga_tests_passive_dsksyn_" + Guid.NewGuid().ToString("N") + ".adf");
            System.IO.File.WriteAllBytes(tempPath, new byte[AdfImage.StandardSize]);
            try
            {
                var controller = new FloppyController();
                controller.InsertDrive0(AdfImage.Load(tempPath), true);
                controller.Drive0.ApplySelection(true, true);
                WaitForDriveReady(controller.Drive0);

                int diskSyncInterrupts = 0;
                var paula = new Paula();
                paula.AttachDisk(
                    controller,
                    delegate(uint _, ushort __) { },
                    null,
                    source =>
                    {
                        if (source == CustomInterruptSource.DiskSync)
                        {
                            diskSyncInterrupts++;
                        }
                    },
                    () => 0x0000,
                    () => 0x0000);

                for (int cycle = 0; cycle < 800000; cycle++)
                {
                    controller.Tick();
                    paula.Tick(false, false);
                }

                Check.True(paula.GetDiskDebugInfo().Contains("syncs:"), "The disk stream should still be observed for polling diagnostics.");
                Check.Equal(0, diskSyncInterrupts, "Passive DSKSYNC matches must not raise level-5 DSKSYN interrupts.");
            }
            finally
            {
                TryDeleteFile(tempPath);
            }
        }

        /// <summary>
        /// Verifies that captured MFM write data decodes back into the expected ADF sector.
        /// </summary>
        private static void TestCapturedMfmWriteSectorDecodesToAdf()
        {
            string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "amiga_tests_mfm_write_" + Guid.NewGuid().ToString("N") + ".adf");
            System.IO.File.WriteAllBytes(tempPath, new byte[AdfImage.StandardSize]);
            try
            {
                byte[] sectorData = new byte[AdfImage.SectorSize];
                for (int index = 0; index < sectorData.Length; index++)
                {
                    sectorData[index] = (byte)((index * 17) ^ 0x5A);
                }

                ushort[] words = AdfMfmEncoder.EncodeSectorWords(sectorData, 0, 3, 8);
                Check.True(AdfMfmEncoder.TryDecodeSectorWords(words, 0, out AdfDecodedSector decoded), "Encoded sector should decode.");
                Check.Equal(3, decoded.Sector, "Decoded sector number should match the MFM header.");
                for (int index = 0; index < sectorData.Length; index++)
                {
                    Check.Equal(sectorData[index], decoded.Data[index], "Decoded sector payload should match at byte " + index + ".");
                }

                AdfImage image = AdfImage.Load(tempPath);
                var drive = new FloppyDrive();
                drive.Insert(image, false);
                drive.ApplySelection(true, true);
                WaitForDriveReady(drive);
                for (int index = 0; index < words.Length; index++)
                {
                    Check.True(drive.TryCaptureWrittenMfmWord(words[index]), "Writable selected drive should capture MFM write word " + index + ".");
                }

                Check.Equal(1, drive.DecodedWriteSectorCount, "Drive should decode one captured write sector.");
                byte[] readBack = new byte[AdfImage.SectorSize];
                image.ReadSector(0, 0, 3, readBack, 0);
                for (int index = 0; index < readBack.Length; index++)
                {
                    Check.Equal(sectorData[index], readBack[index], "Written ADF sector should match at byte " + index + ".");
                }
            }
            finally
            {
                try
                {
                    System.IO.File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// Verifies that dirty ADF sectors are persisted back to the host file by Flush.
        /// </summary>
        private static void TestAdfSectorWritesFlushToHostFile()
        {
            string tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "amiga_tests_adf_flush_" + Guid.NewGuid().ToString("N") + ".adf");
            string backupPath = tempPath + ".bak";
            string tempWritePath = tempPath + ".tmp";
            System.IO.File.WriteAllBytes(tempPath, new byte[AdfImage.StandardSize]);
            try
            {
                byte[] sectorData = new byte[AdfImage.SectorSize];
                for (int index = 0; index < sectorData.Length; index++)
                {
                    sectorData[index] = (byte)(0xA5 ^ index);
                }

                AdfImage image = AdfImage.Load(tempPath);
                Check.True(image.WriteSector(0, 0, 0, sectorData, 0), "Sector write should succeed for a standard ADF.");
                Check.True(image.IsDirty, "ADF should become dirty after a sector write.");
                byte[] fileBytesBeforeFlush = System.IO.File.ReadAllBytes(tempPath);
                Check.Equal(0, fileBytesBeforeFlush[0], "Host ADF file should not change before Flush.");

                image.Flush();
                Check.True(!image.IsDirty, "Flush should clear the dirty flag.");
                byte[] fileBytesAfterFlush = System.IO.File.ReadAllBytes(tempPath);
                for (int index = 0; index < sectorData.Length; index++)
                {
                    Check.Equal(sectorData[index], fileBytesAfterFlush[index], "Flushed ADF sector should match at byte " + index + ".");
                }
            }
            finally
            {
                TryDeleteFile(tempPath);
                TryDeleteFile(backupPath);
                TryDeleteFile(tempWritePath);
            }
        }

        /// <summary>
        /// Deletes a test file while ignoring cleanup failures.
        /// </summary>
        private static void TryDeleteFile(string path)
        {
            try
            {
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Advances a standalone floppy drive until its motor has reached ready speed.
        /// </summary>
        private static void WaitForDriveReady(FloppyDrive drive)
        {
            for (int cycle = 0; cycle < 2_000_000 && !drive.Ready; cycle++)
            {
                drive.Tick();
            }

            Check.True(drive.Ready, "Selected drive should become ready after motor spin-up.");
        }
    }

    /// <summary>
    /// Provides Integration Tests functionality.
    /// </summary>
    internal static class IntegrationTests
    {
        /// <summary>
        /// Builds the Build result.
        /// </summary>
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return new ChipTestCase(
                "Integration",
                "CPU custom-register write to BPL1DAT reaches Denise bitplane latch",
                "Amiga bus custom register map plus Denise BPLxDAT latch side effect",
                TestCpuBusBplDatSideEffect);
            yield return new ChipTestCase(
                "Integration",
                "Kickstart overlay writes do not corrupt underlying chip RAM",
                "A500 ROM overlay behavior at address zero",
                TestKickstartOverlayWriteDoesNotTouchChipRam);
            yield return new ChipTestCase(
                "Integration",
                "bus arbitration contends Chip RAM, Slow RAM, and custom registers",
                "A500 Gary bus arbitration for CPU accesses during custom-chip and Slow RAM DMA windows",
                TestBusArbitrationRegions);
            yield return new ChipTestCase(
                "Integration",
                "fast accuracy keeps chip-bus arbitration relaxed",
                "Frontend compatibility profile used for games with sensitive mid-scanline bitplane updates",
                TestFastAccuracyKeepsChipBusRelaxed);
            yield return new ChipTestCase(
                "Integration",
                "2MB Chip RAM plus 8MB Fast RAM profile maps both writable regions",
                "A500 memory expansion profiles and Zorro II Fast RAM map",
                TestTwoMbChipEightMbFastRamProfile);
            yield return new ChipTestCase(
                "Integration",
                "all menu RAM profiles expose their advertised memory regions",
                "Amiga emulator RAM profile menu, Chip RAM, Slow RAM, and Fast RAM maps",
                TestAllMenuRamProfilesExposeAdvertisedRegions);
            yield return new ChipTestCase(
                "Integration",
                "default frontend RAM profile exposes 1MB Chip RAM for OCS game loaders",
                "Amiga emulator settings default RAM profile",
                TestDefaultRamProfileIsOneMbChip);
            yield return new ChipTestCase(
                "Integration",
                "INTREQ is readable before the delayed CPU IPL pin observes it",
                "OCS INTREQ register visibility versus delayed 68000 IPL sampling",
                TestInterruptRequestPrecedesCpuIplPins);
        }

        /// <summary>
        /// Runs Test Cpu Bus Bpl Dat Side Effect.
        /// </summary>
        private static void TestCpuBusBplDatSideEffect()
        {
            using (var system = new AmigaSystem(AmigaModel.A500Pal, AmigaAccuracyOptions.Fast.Clone()))
            {
                system.Bus.WriteWord(0x00DFF100, 0x1000);
                system.Bus.WriteWord(0x00DFF110, 0x8000);
                system.Agnus.Bitplanes.RefreshFromRegisters();
                Check.Equal(1, system.Agnus.Bitplanes.RenderColorIndex(0, BitplaneUnit.PalVisibleTop), "Bus write should update the BPL1DAT latch used by Denise.");
            }
        }

        /// <summary>
        /// Runs Test Kickstart Overlay Write Does Not Touch Chip Ram.
        /// </summary>
        private static void TestKickstartOverlayWriteDoesNotTouchChipRam()
        {
            var bus = new AmigaBus(AmigaModel.A500Pal, new CustomRegisterMap(), null, null, null);
            byte[] rom = new byte[512 * 1024];
            rom[0] = 0xAA;
            rom[1] = 0x55;
            bus.LoadKickstart(rom);
            bus.RomOverlayAtZero = true;
            Check.Equal(0xAA55, bus.ReadWord(0), "Overlay should initially expose Kickstart at zero.");
            bus.WriteWord(0, 0x1234);
            bus.RomOverlayAtZero = false;
            Check.Equal(0x0000, bus.ReadWord(0), "Writes while ROM overlay is active must not modify hidden Chip RAM.");
        }

        /// <summary>
        /// Verifies that only Agnus-visible regions are reported as chip-bus contended.
        /// </summary>
        private static void TestBusArbitrationRegions()
        {
            var bus = new AmigaBus(AmigaModel.A500Pal, new CustomRegisterMap(), null, null, null);
            byte[] rom = new byte[512 * 1024];
            rom[0] = 0xAA;
            bus.LoadKickstart(rom);
            bus.RomOverlayAtZero = true;

            Check.True(!bus.RequiresChipBus(0x000000, 2), "ROM overlay at zero should not be treated as Chip RAM contention.");
            bus.RomOverlayAtZero = false;
            Check.True(bus.RequiresChipBus(0x000000, 2), "Chip RAM should be contended by custom-chip DMA.");
            Check.True(bus.RequiresChipBus(0x00DFF180, 2), "Custom registers should be on the custom-chip bus.");
            Check.True(!bus.RequiresChipBus(0x00BFE001, 1), "CIA-A access should not wait for Agnus DMA slots.");
            Check.True(!bus.RequiresChipBus(0x00F80000, 2), "Kickstart ROM access should not wait for Agnus DMA slots.");

            var slowBus = new AmigaBus(AmigaModel.A500Pal512Slow512, new CustomRegisterMap(), null, null, null);
            Check.True(slowBus.RequiresChipBus(MemoryMap.SlowRamStart, 2), "Trapdoor Slow RAM should use chip-bus timing even though chipset DMA cannot target it directly.");
        }

        /// <summary>
        /// Verifies that the interactive Fast profile remains the compatibility-oriented timing mode.
        /// </summary>
        private static void TestFastAccuracyKeepsChipBusRelaxed()
        {
            AmigaAccuracyOptions options = AmigaAccuracyOptions.Fast.Clone();

            Check.True(!options.StrictChipBusArbitration, "Fast accuracy should leave strict chip-bus contention to Compatible and Strict.");
            Check.True(!options.EnableCpuTrace, "Fast accuracy should not enable CPU trace overhead.");
            Check.True(!options.EnableDmaSlotTrace, "Fast accuracy should not enable DMA slot trace overhead.");
        }

        /// <summary>
        /// Verifies that the expanded machine profile exposes 2MB Chip RAM and 8MB Fast RAM.
        /// </summary>
        private static void TestTwoMbChipEightMbFastRamProfile()
        {
            var bus = new AmigaBus(AmigaModel.A500Pal2MbChipFast8Mb, new CustomRegisterMap(), null, null, null);

            Check.Equal(AmigaAddressRegion.ChipRam, bus.GetRegion(0x001FFFFF), "2MB profile should map the final Chip RAM byte.");
            Check.Equal(AmigaAddressRegion.FastRam, bus.GetRegion(MemoryMap.FastRamStart), "Fast RAM should start at the Zorro II Fast RAM base.");
            Check.Equal(AmigaAddressRegion.FastRam, bus.GetRegion(MemoryMap.FastRamEnd), "8MB Fast RAM should extend through the supported Zorro II range.");

            bus.RomOverlayAtZero = false;
            bus.WriteByte(0x001FFFFF, 0x5A);
            Check.Equal(0x5A, bus.ReadByte(0x001FFFFF), "Final 2MB Chip RAM byte should be writable.");

            bus.WriteWord(MemoryMap.FastRamStart, 0xCAFE);
            bus.WriteByte(MemoryMap.FastRamEnd, 0x7B);
            Check.Equal(0xCAFE, bus.ReadWord(MemoryMap.FastRamStart), "Fast RAM base should be writable.");
            Check.Equal(0x7B, bus.ReadByte(MemoryMap.FastRamEnd), "Final 8MB Fast RAM byte should be writable.");
            Check.True(!bus.RequiresChipBus(MemoryMap.FastRamStart, 2), "Fast RAM should not be contended by custom-chip DMA.");
        }

        /// <summary>
        /// Verifies all RAM profiles offered by the menu catalog against the emulator memory map.
        /// </summary>
        private static void TestAllMenuRamProfilesExposeAdvertisedRegions()
        {
            CheckRamProfile(AmigaRamProfile.Chip512K, AmigaModel.A500Pal);
            CheckRamProfile(AmigaRamProfile.Chip512KSlow512K, AmigaModel.A500Pal512Slow512);
            CheckRamProfile(AmigaRamProfile.Chip1Mb, AmigaModel.A500Pal1MbChip);
            CheckRamProfile(AmigaRamProfile.Chip2Mb, AmigaModel.A500Pal2MbChip);
            CheckRamProfile(AmigaRamProfile.Chip2MbFast8Mb, AmigaModel.A500Pal2MbChipFast8Mb);
        }

        /// <summary>
        /// Checks one advertised RAM profile against the model and bus implementation.
        /// </summary>
        private static void CheckRamProfile(AmigaRamProfile profile, AmigaModel model)
        {
            AmigaRamProfileInfo info = AmigaOverlaySettingCatalog.GetRamProfileInfo(profile);
            Check.Equal(model.ChipRamBytes, info.ChipRamBytes, "Menu Chip RAM label should match the model for " + profile + ".");
            Check.Equal(model.SlowRamBytes, info.SlowRamBytes, "Menu Slow RAM label should match the model for " + profile + ".");
            Check.Equal(model.FastRamBytes, info.FastRamBytes, "Menu Fast RAM label should match the model for " + profile + ".");

            var bus = new AmigaBus(model, new CustomRegisterMap(), null, null, null);
            bus.RomOverlayAtZero = false;
            Check.Equal(AmigaAddressRegion.ChipRam, bus.GetRegion((uint)model.ChipRamBytes - 1u), "Final Chip RAM byte should be mapped for " + profile + ".");
            if (model.SlowRamBytes > 0)
            {
                Check.Equal(AmigaAddressRegion.SlowRam, bus.GetRegion(MemoryMap.SlowRamStart), "Slow RAM base should be mapped for " + profile + ".");
                Check.Equal(AmigaAddressRegion.SlowRam, bus.GetRegion(MemoryMap.SlowRamStart + (uint)model.SlowRamBytes - 1u), "Final Slow RAM byte should be mapped for " + profile + ".");
            }

            if (model.FastRamBytes > 0)
            {
                Check.Equal(AmigaAddressRegion.FastRam, bus.GetRegion(MemoryMap.FastRamStart), "Fast RAM base should be mapped for " + profile + ".");
                Check.Equal(AmigaAddressRegion.FastRam, bus.GetRegion(MemoryMap.FastRamStart + (uint)model.FastRamBytes - 1u), "Final Fast RAM byte should be mapped for " + profile + ".");
            }
        }

        /// <summary>
        /// Verifies that the default frontend profile gives OCS loaders the common 1MB Chip RAM map.
        /// </summary>
        private static void TestDefaultRamProfileIsOneMbChip()
        {
            var settings = new AmigaEmulatorSettings();
            Check.Equal(AmigaRamProfile.Chip1Mb, settings.RamProfile, "New settings should default to the 1MB Chip RAM profile.");
            Check.Equal(4, settings.Version, "Settings version should track the 1MB Chip RAM default migration.");
        }

        /// <summary>
        /// Verifies that custom interrupt requests become readable before the CPU-visible IPL pins change.
        /// </summary>
        private static void TestInterruptRequestPrecedesCpuIplPins()
        {
            using (var system = new AmigaSystem(AmigaModel.A500Pal, AmigaAccuracyOptions.Fast.Clone()))
            {
                system.Bus.WriteWord(0x00DFF09A, 0xC020);
                system.Agnus.CustomRegisters.RequestInterrupt(CustomInterruptSource.VerticalBlank);
                Check.True((system.Bus.ReadWord(0x00DFF01E) & 0x0020) != 0, "INTREQR should expose VERTB immediately.");

                system.RunChipCycles(1);
                Check.Equal(0, ReadPrivateInt(system, "_cpuVisibleInterruptLevel"), "CPU IPL pins should not update in the same chipcycle as INTREQ.");

                system.RunChipCycles(8);
                Check.Equal(3, ReadPrivateInt(system, "_cpuVisibleInterruptLevel"), "Delayed CPU IPL pins should observe the level 3 request.");
            }
        }

        /// <summary>
        /// Reads one private integer field from AmigaSystem for timing pipeline regression tests.
        /// </summary>
        private static int ReadPrivateInt(AmigaSystem system, string fieldName)
        {
            FieldInfo field = typeof(AmigaSystem).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Check.True(field != null, "Missing private field " + fieldName + ".");
            return (int)field.GetValue(system);
        }
    }

    /// <summary>
    /// Provides savestate roundtrip tests for the full Amiga machine state.
    /// </summary>
    internal static class SaveStateTests
    {
        /// <summary>
        /// Builds the Build result.
        /// </summary>
        public static IEnumerable<ChipTestCase> Build()
        {
            yield return new ChipTestCase(
                "SaveState",
                "full machine state roundtrips through the Amiga savestate file format",
                "Amiga SaveStateFile plus AmigaSystem component SaveState/LoadState",
                TestFullMachineRoundtrip);
            yield return new ChipTestCase(
                "SaveState",
                "future or unknown savestate profiles are marked incompatible",
                "Amiga SaveStateFile metadata compatibility validation",
                TestUnknownProfileMetadataIsIncompatible);
            yield return new ChipTestCase(
                "SaveState",
                "floppy CIA control lines are re-applied after loading a savestate",
                "A500 CIA-B floppy select, motor, side, and CIA-A ready restore ordering",
                TestFloppyControlLinesRefreshAfterLoad);
            yield return new ChipTestCase(
                "SaveState",
                "mounted ADF metadata is reattached to the recorded floppy drive",
                "Savestate metadata media restore used by multi-disk trackloaders",
                TestMountedAdfMetadataReattachesOnLoad);
            yield return new ChipTestCase(
                "CPU",
                "68000 prefetched instruction word survives savestate restore",
                "Self-modifying trackloaders depend on the 68000 sequential instruction prefetch",
                TestCpuPrefetchSurvivesSavestateRestore);
            yield return new ChipTestCase(
                "CPU",
                "68000 control flow invalidates stale fall-through prefetch",
                "Jumps, returns, and exceptions must not consume a prefetched fall-through word at the target PC",
                TestCpuControlFlowInvalidatesPrefetch);
            yield return new ChipTestCase(
                "SaveState",
                "flat savestates migrate into mounted-media subdirectories",
                "C64-style savestate browser groups timestamped saves by mounted media",
                TestFlatSaveMigrationUsesMountedMediaDirectory);
        }

        /// <summary>
        /// Runs Test Full Machine Roundtrip.
        /// </summary>
        private static void TestFullMachineRoundtrip()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "amiga-savestate-roundtrip-" + Guid.NewGuid().ToString("N") + ".amigasav");
            try
            {
                using (var system = new AmigaSystem(AmigaModel.A500Pal, AmigaAccuracyOptions.Fast.Clone()))
                {
                    system.Bus.RomOverlayAtZero = false;
                    system.Bus.WriteWord(0x000010, 0xCAFE);
                    system.Agnus.CustomRegisters.WriteWord(0x096, 0x8200);
                    system.PresentedFrameBuffer.Pixels[0] = 0xFF123456;
                    var screenshot = new uint[] { 0xFF010203, 0xFF040506 };

                    SaveStateFile.Write(
                        path,
                        system,
                        MountedMediaInfo.ForFloppy(0, "TestDisk.adf", "C:\\Temp\\TestDisk.adf", true),
                        AmigaKickstart.Auto,
                        AmigaRamProfile.Chip512K,
                        AmigaAccuracyProfile.Fast,
                        screenshot,
                        2,
                        1);

                    system.Bus.WriteWord(0x000010, 0x0000);
                    system.PresentedFrameBuffer.Pixels[0] = 0;
                    system.Agnus.CustomRegisters.WriteWord(0x096, 0x0200);

                    SaveStateSlotMetadata metadata = SaveStateFile.ReadMetadata(path, 3);
                    Check.Equal(SaveStateSlotStatus.Occupied, metadata.Status, "Savestate metadata should mark the slot occupied.");
                    Check.Equal(2, metadata.ScreenshotWidth, "Savestate metadata should preserve screenshot width.");
                    Check.Equal(1, metadata.ScreenshotHeight, "Savestate metadata should preserve screenshot height.");
                    Check.Equal(0xFF010203u, metadata.ScreenshotPixels[0], "Savestate metadata should preserve screenshot pixels.");
                    Check.Equal("TestDisk.adf", metadata.MountedMedia.DisplayName, "Savestate metadata should preserve mounted media display name.");

                    SaveStateFile.Load(path, system);
                    Check.Equal(0xCAFE, system.Bus.ReadWord(0x000010), "Chip RAM should be restored by savestate load.");
                    Check.Equal(0xFF123456u, system.PresentedFrameBuffer.Pixels[0], "Presented framebuffer should be restored by savestate load.");
                    Check.True((system.Agnus.CustomRegisters.DmaControl & 0x0200) != 0, "DMA master bit should be restored by savestate load.");
                }
            }
            finally
            {
                try
                {
                    System.IO.File.Delete(path);
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// Verifies that unknown metadata enum values are not advertised as loadable slots.
        /// </summary>
        private static void TestUnknownProfileMetadataIsIncompatible()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "amiga-savestate-incompatible-" + Guid.NewGuid().ToString("N") + ".amigasav");
            try
            {
                using (var system = new AmigaSystem(AmigaModel.A500Pal, AmigaAccuracyOptions.Fast.Clone()))
                {
                    SaveStateFile.Write(
                        path,
                        system,
                        MountedMediaInfo.None,
                        AmigaKickstart.Auto,
                        (AmigaRamProfile)999,
                        AmigaAccuracyProfile.Fast,
                        Array.Empty<uint>(),
                        0,
                        0);
                }

                SaveStateSlotMetadata metadata = SaveStateFile.ReadMetadata(path, 0);
                Check.Equal(SaveStateSlotStatus.Incompatible, metadata.Status, "Unknown RAM profiles should be marked incompatible.");
                Check.True(!metadata.HasState, "Incompatible slots should not be treated as loadable states.");
            }
            finally
            {
                try
                {
                    System.IO.File.Delete(path);
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// Verifies that flat savestates migrate into per-media directories without losing metadata.
        /// </summary>
        private static void TestFlatSaveMigrationUsesMountedMediaDirectory()
        {
            string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "amiga-save-migration-" + Guid.NewGuid().ToString("N"));
            string flatPath = System.IO.Path.Combine(directory, "save-20260620-120000.amigasav");
            try
            {
                System.IO.Directory.CreateDirectory(directory);
                using (var system = new AmigaSystem(AmigaModel.A500Pal, AmigaAccuracyOptions.Fast.Clone()))
                {
                    SaveStateFile.Write(
                        flatPath,
                        system,
                        MountedMediaInfo.ForFloppy(0, "North & South.adf", "C:\\Games\\North & South.adf", true),
                        AmigaKickstart.Auto,
                        AmigaRamProfile.Chip512K,
                        AmigaAccuracyProfile.Fast,
                        Array.Empty<uint>(),
                        0,
                        0);
                }

                int moved = SaveStateMigration.MigrateFlatSaves(directory, TextWriter.Null);
                string migratedPath = System.IO.Path.Combine(directory, "North & South", System.IO.Path.GetFileName(flatPath));

                Check.Equal(1, moved, "Migration should move the flat savestate into the mounted-media directory.");
                Check.True(!System.IO.File.Exists(flatPath), "The original flat savestate should be moved away.");
                Check.True(System.IO.File.Exists(migratedPath), "The migrated savestate should exist under the media directory.");
                SaveStateSlotMetadata metadata = SaveStateFile.ReadMetadata(migratedPath);
                Check.Equal("North & South.adf", metadata.MountedMedia.DisplayName, "Migration must not damage mounted media metadata.");
            }
            finally
            {
                try
                {
                    if (System.IO.Directory.Exists(directory))
                    {
                        System.IO.Directory.Delete(directory, true);
                    }
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// Verifies that restored CIA-B port state is applied back to DF0 after bus state is loaded.
        /// </summary>
        private static void TestFloppyControlLinesRefreshAfterLoad()
        {
            string adfPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "amiga-savestate-floppy-lines-" + Guid.NewGuid().ToString("N") + ".adf");
            System.IO.File.WriteAllBytes(adfPath, new byte[AdfImage.StandardSize]);
            try
            {
                byte[] stateBytes;
                using (var system = new AmigaSystem(AmigaModel.A500Pal, AmigaAccuracyOptions.Fast.Clone()))
                {
                    system.Bus.FloppyController.InsertDrive0(AdfImage.Load(adfPath), true);
                    system.Bus.WriteByte(0x00BFD300, 0xFF);
                    system.Bus.WriteByte(0x00BFD100, 0x73);
                    system.RunChipCycles(2_000_000);

                    Check.True(system.Bus.FloppyController.Drive0.Selected, "DF0 should be selected before saving.");
                    Check.True(system.Bus.FloppyController.Drive0.MotorOn, "DF0 motor should be on before saving.");
                    Check.Equal(1, system.Bus.FloppyController.Drive0.Side, "DF0 side should be restored from CIA-B side bit.");
                    Check.True(system.Bus.FloppyController.Drive0.Ready, "DF0 should be ready before saving.");

                    using (var stream = new MemoryStream())
                    using (var writer = new BinaryWriter(stream))
                    {
                        system.SaveState(writer);
                        writer.Flush();
                        stateBytes = stream.ToArray();
                    }
                }

                using (var restored = new AmigaSystem(AmigaModel.A500Pal, AmigaAccuracyOptions.Fast.Clone()))
                using (var stream = new MemoryStream(stateBytes))
                using (var reader = new BinaryReader(stream))
                {
                    restored.LoadState(reader);

                    Check.True(restored.Bus.FloppyController.Drive0.Selected, "LoadState should reselect DF0 from restored CIA-B PRB/DDRB.");
                    Check.True(restored.Bus.FloppyController.Drive0.MotorOn, "LoadState should restore the selected DF0 motor line.");
                    Check.Equal(1, restored.Bus.FloppyController.Drive0.Side, "LoadState should restore the DF0 side select line.");
                    Check.True(restored.Bus.FloppyController.Drive0.Ready, "LoadState should rebuild CIA/drive ready state after restoring CIA-B.");
                }
            }
            finally
            {
                try
                {
                    System.IO.File.Delete(adfPath);
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// Verifies that SaveStateFile.Load reattaches host ADF media recorded in metadata.
        /// </summary>
        private static void TestMountedAdfMetadataReattachesOnLoad()
        {
            string adfPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "amiga-savestate-mounted-adf-" + Guid.NewGuid().ToString("N") + ".adf");
            string savePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "amiga-savestate-mounted-adf-" + Guid.NewGuid().ToString("N") + ".amigasav");
            System.IO.File.WriteAllBytes(adfPath, new byte[AdfImage.StandardSize]);
            try
            {
                using (var system = new AmigaSystem(AmigaModel.A500Pal, AmigaAccuracyOptions.Fast.Clone()))
                {
                    system.Bus.FloppyController.GetDrive(1).Step(7);
                    SaveStateFile.Write(
                        savePath,
                        system,
                        MountedMediaInfo.ForFloppy(1, System.IO.Path.GetFileName(adfPath), adfPath, true),
                        AmigaKickstart.Auto,
                        AmigaRamProfile.Chip512K,
                        AmigaAccuracyProfile.Fast,
                        Array.Empty<uint>(),
                        0,
                        0);
                }

                using (var restored = new AmigaSystem(AmigaModel.A500Pal, AmigaAccuracyOptions.Fast.Clone()))
                {
                    SaveStateFile.Load(savePath, restored);

                    Check.True(!restored.Bus.FloppyController.Drive0.HasDisk, "SaveStateFile.Load should not reattach DF1 metadata to DF0.");
                    Check.True(restored.Bus.FloppyController.Drive1.HasDisk, "SaveStateFile.Load should reattach the metadata ADF to DF1.");
                    Check.True(restored.Bus.FloppyController.Drive1.WriteProtected, "Restored DF1 media should keep metadata write-protection.");
                    Check.Equal(7, restored.Bus.FloppyController.Drive1.Track, "Reattaching metadata media must not reset the saved drive track.");
                }
            }
            finally
            {
                try
                {
                    System.IO.File.Delete(adfPath);
                }
                catch
                {
                }

                try
                {
                    System.IO.File.Delete(savePath);
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// Verifies that savestates keep the 68000 instruction prefetch used by self-modifying code.
        /// </summary>
        private static void TestCpuPrefetchSurvivesSavestateRestore()
        {
            byte[] stateBytes;
            using (var system = new AmigaSystem(AmigaModel.A500Pal1MbChip, AmigaAccuracyOptions.Fast.Clone()))
            {
                system.Bus.RomOverlayAtZero = false;
                system.Bus.WriteWord(0x000100, 0x33FC); // MOVE.W #$FFFF,$00000108
                system.Bus.WriteWord(0x000102, 0xFFFF);
                system.Bus.WriteWord(0x000104, 0x0000);
                system.Bus.WriteWord(0x000106, 0x0108);
                system.Bus.WriteWord(0x000108, 0x4E71); // NOP prefetched before the write retires.
                system.Bus.WriteWord(0x00010A, 0x7007); // MOVEQ #7,D0
                system.Cpu.SetProgramCounter(0x000100);

                system.Cpu.Step();
                Check.Equal(0xFFFF, system.Bus.ReadWord(0x000108), "Self-modifying setup should overwrite the next instruction in RAM.");

                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream))
                {
                    system.SaveState(writer);
                    writer.Flush();
                    stateBytes = stream.ToArray();
                }
            }

            using (var restored = new AmigaSystem(AmigaModel.A500Pal1MbChip, AmigaAccuracyOptions.Fast.Clone()))
            using (var stream = new MemoryStream(stateBytes))
            using (var reader = new BinaryReader(stream))
            {
                restored.LoadState(reader);
                restored.Cpu.Step();
                Check.Equal(0x00010Au, restored.Cpu.PC, "Restored CPU should execute the prefetched NOP instead of the overwritten $FFFF word.");

                restored.Cpu.Step();
                Check.Equal(7u, restored.Cpu.D0, "Execution should continue after the restored prefetched instruction.");
            }
        }

        /// <summary>
        /// Verifies that explicit control flow rereads the target instruction instead of reusing fall-through prefetch data.
        /// </summary>
        private static void TestCpuControlFlowInvalidatesPrefetch()
        {
            using (var system = new AmigaSystem(AmigaModel.A500Pal1MbChip, AmigaAccuracyOptions.Fast.Clone()))
            {
                system.Bus.RomOverlayAtZero = false;
                system.Bus.WriteWord(0x000100, 0x33FC); // MOVE.W #$7007,$0000010E
                system.Bus.WriteWord(0x000102, 0x7007);
                system.Bus.WriteWord(0x000104, 0x0000);
                system.Bus.WriteWord(0x000106, 0x010E);
                system.Bus.WriteWord(0x000108, 0x4EF9); // JMP $0000010E
                system.Bus.WriteWord(0x00010A, 0x0000);
                system.Bus.WriteWord(0x00010C, 0x010E);
                system.Bus.WriteWord(0x00010E, 0x4E71); // stale fall-through prefetch before the write retires
                system.Cpu.SetProgramCounter(0x000100);

                system.Cpu.Step();
                Check.Equal(0x7007, system.Bus.ReadWord(0x00010E), "Self-modifying setup should overwrite the future jump target.");

                system.Cpu.Step();
                Check.Equal(0x00010Eu, system.Cpu.PC, "JMP should land on the overwritten target address.");

                system.Cpu.Step();
                Check.Equal(7u, system.Cpu.D0, "Control-flow target should be fetched from RAM, not from stale prefetched NOP data.");
            }
        }
    }

    /// <summary>
    /// Provides optional cdhooper/amiga_devtest boot-ADF integration checks.
    /// </summary>
    internal static class AmigaDevtestTests
    {
        /// <summary>
        /// Builds the Amiga devtest integration tests.
        /// </summary>
        public static IEnumerable<ChipTestCase> Build(TestOptions options)
        {
            yield return new ChipTestCase(
                "AmigaDevtest",
                "bootable devtest ADF reaches a visible trackdisk.device run",
                "cdhooper/amiga_devtest optional AmigaOS block-device integration",
                delegate { TestBootableDevtestAdf(options); });
        }

        /// <summary>
        /// Boots a prepared AmigaDOS ADF that runs devtest from S:startup-sequence.
        /// </summary>
        private static void TestBootableDevtestAdf(TestOptions options)
        {
            string adfPath = FindDevtestAdf(options);
            if (string.IsNullOrWhiteSpace(adfPath))
            {
                Check.Skip(
                    "No bootable amiga_devtest ADF found. Expected external-tests\\amiga_devtest\\amiga_devtest_trackdisk.adf, " +
                    "external-tests\\amiga_devtest\\devtest_trackdisk.adf, external-tests\\amiga_devtest\\devtest.adf, " +
                    "or pass --amiga-devtest-adf <path>.");
            }

            if (!KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out KickstartRomInfo romInfo))
            {
                Check.Skip("Default Kickstart ROM not found.");
            }

            using (var system = new AmigaSystem(AmigaModel.A500Pal, AmigaAccuracyOptions.Fast.Clone()))
            {
                system.LoadKickstart(romBytes);
                system.Bus.FloppyController.InsertDrive0(AdfImage.Load(adfPath), true);
                long startsBefore = system.Paula.DiskDmaStarts;
                long doneBefore = system.Paula.DiskDmaCompletedBlocks;

                system.RunChipCycles(options.AmigaDevtestCycles);

                long starts = system.Paula.DiskDmaStarts - startsBefore;
                long completed = system.Paula.DiskDmaCompletedBlocks - doneBefore;
                uint[] pixels = system.PresentedFrameBuffer.Pixels;
                int uniqueColors = CountUniqueColors(pixels, 16);
                int differentPixels = CountDifferentPixels(pixels);

                Check.True(system.FrameCounter > 10, "devtest ADF did not advance enough frames. ROM=" + romInfo.Path);
                Check.True(starts > 0, "devtest ADF did not start disk DMA. DF0=" + system.Bus.FloppyController.GetDebugInfo());
                Check.True(completed > 0, "devtest ADF did not complete disk DMA. Paula=" + system.Paula.GetDiskDebugInfo());
                Check.True(uniqueColors >= 2 && differentPixels > 128, "devtest ADF frame stayed blank or uniform. Colors=" + uniqueColors);
            }
        }

        /// <summary>
        /// Locates the optional bootable amiga_devtest ADF artifact.
        /// </summary>
        private static string FindDevtestAdf(TestOptions options)
        {
            if (!string.IsNullOrWhiteSpace(options.AmigaDevtestAdfPath))
            {
                string explicitPath = System.IO.Path.GetFullPath(options.AmigaDevtestAdfPath);
                return System.IO.File.Exists(explicitPath) ? explicitPath : null;
            }

            string root = FindRepositoryRoot();
            if (string.IsNullOrWhiteSpace(root))
            {
                return null;
            }

            string[] candidates = new string[]
            {
                System.IO.Path.Combine(root, "external-tests", "amiga_devtest", "amiga_devtest_trackdisk.adf"),
                System.IO.Path.Combine(root, "external-tests", "amiga_devtest", "devtest_trackdisk.adf"),
                System.IO.Path.Combine(root, "external-tests", "amiga_devtest", "devtest.adf")
            };
            for (int index = 0; index < candidates.Length; index++)
            {
                if (System.IO.File.Exists(candidates[index]))
                {
                    return candidates[index];
                }
            }

            return null;
        }

        /// <summary>
        /// Finds the repository root from the current test assembly location.
        /// </summary>
        private static string FindRepositoryRoot()
        {
            string current = AppDomain.CurrentDomain.BaseDirectory;
            for (int depth = 0; depth < 8 && !string.IsNullOrWhiteSpace(current); depth++)
            {
                if (System.IO.Directory.Exists(System.IO.Path.Combine(current, "external-tests")) &&
                    System.IO.File.Exists(System.IO.Path.Combine(current, "AmigaEmulator.sln")))
                {
                    return current;
                }

                current = System.IO.Directory.GetParent(current) != null
                    ? System.IO.Directory.GetParent(current).FullName
                    : null;
            }

            return null;
        }

        /// <summary>
        /// Counts visible color diversity in a frame with an upper bound.
        /// </summary>
        private static int CountUniqueColors(uint[] pixels, int limit)
        {
            var colors = new HashSet<uint>();
            for (int index = 0; index < pixels.Length && colors.Count < limit; index++)
            {
                colors.Add(pixels[index]);
            }

            return colors.Count;
        }

        /// <summary>
        /// Counts pixels that differ from the first frame color.
        /// </summary>
        private static int CountDifferentPixels(uint[] pixels)
        {
            if (pixels.Length == 0)
            {
                return 0;
            }

            uint first = pixels[0];
            int different = 0;
            for (int index = 0; index < pixels.Length; index++)
            {
                if (pixels[index] != first)
                {
                    different++;
                }
            }

            return different;
        }
    }

    /// <summary>
    /// Provides Golden ADF Tests functionality.
    /// </summary>
    internal static class GoldenAdfTests
    {
        /// <summary>
        /// Builds the Build result.
        /// </summary>
        public static IEnumerable<ChipTestCase> Build(TestOptions options)
        {
            yield return new ChipTestCase(
                "GoldenADF",
                "Kickstart 1.3 plus Turrican III reaches a nonblank boot/trainer frame",
                "Golden regression using local Kickstart ROM and Documents\\AmigaEmulator\\TURRI3-1.ADF",
                /// <summary>
                /// Runs Test Adf Non Blank.
                /// </summary>
                delegate { TestAdfNonBlank("TURRI3-1.ADF", 90_000_000); });
            yield return new ChipTestCase(
                "GoldenADF",
                "Kickstart 1.3 plus North & South reaches a stable intro/language frame without fetch stripes",
                "Golden regression using local Kickstart ROM and Documents\\AmigaEmulator\\North & South.adf",
                TestNorthSouthFrameQuality);
            yield return new ChipTestCase(
                "GoldenADF",
                "North & South accepts mouse language selection and reaches the main menu",
                "Golden regression using local Kickstart ROM and Documents\\AmigaEmulator\\North & South.adf",
                TestNorthSouthLanguageSelectionToMainMenu);
            yield return new ChipTestCase(
                "GoldenADF",
                "Amiga Test Kit 1.21 boot disk reaches a nonblank diagnostic frame",
                "keirf/amiga-stuff release testkit-v1.21 AmigaTestKit.adf",
                TestAmigaTestKitBootFrame);
        }

        /// <summary>
        /// Runs Test Adf Non Blank.
        /// </summary>
        private static void TestAdfNonBlank(string fileName, int cycles)
        {
            WithAdfFrame(fileName, cycles, delegate(AmigaSystem system, KickstartRomInfo romInfo)
            {
                CheckNonBlank(system.PresentedFrameBuffer.Pixels, romInfo);
            });
        }

        /// <summary>
        /// Runs Test North South Frame Quality.
        /// </summary>
        private static void TestNorthSouthFrameQuality()
        {
            WithAdfFrame("North & South.adf", 240_000_000, delegate(AmigaSystem system, KickstartRomInfo romInfo)
            {
                CheckNonBlank(system.PresentedFrameBuffer.Pixels, romInfo);
                int darkColumns = CountMostlyDarkColumns(system.PresentedFrameBuffer.Pixels, system.PresentedFrameBuffer.Width, system.PresentedFrameBuffer.Height, 30, 180);
                Check.True(darkColumns < 80, "North & South frame has too many mostly black vertical columns, likely a bitplane fetch/prefetch stripe regression. Columns=" + darkColumns);
            });
        }

        /// <summary>
        /// Runs Test North South Language Selection To Main Menu.
        /// </summary>
        private static void TestNorthSouthLanguageSelectionToMainMenu()
        {
            WithAdfFrame("North & South.adf", 260_000_000, delegate(AmigaSystem system, KickstartRomInfo romInfo)
            {
                long startsBeforeClick = system.Paula.DiskDmaStarts;
                MoveMouseGradually(system, AmigaControlPort.Port0, 100, 180, 120_000);
                system.RunChipCycles(200_000);
                system.Input.SetMouseButton(AmigaControlPort.Port0, AmigaJoystickButtons.Fire, true);
                system.RunChipCycles(2_000_000);
                system.Input.SetMouseButton(AmigaControlPort.Port0, AmigaJoystickButtons.Fire, false);
                system.RunChipCycles(160_000_000);

                Check.True(system.Paula.DiskDmaStarts > startsBeforeClick + 20, "North & South language click did not trigger main-menu disk loading. Paula=" + system.Paula.GetDiskDebugInfo());
                Check.True(system.Bus.FloppyController.Drive0.Track >= 30, "North & South main-menu load did not reach the expected later disk tracks. DF0=" + system.Bus.FloppyController.GetDebugInfo());
                Check.True(CountUniqueColors(system.PresentedFrameBuffer.Pixels, 32) >= 12, "North & South main menu frame did not develop enough colors. ROM=" + (romInfo != null ? romInfo.Path : string.Empty));
            });
        }

        /// <summary>
        /// Feeds relative mouse motion in small timed deltas so games polling JOYxDAT see real motion.
        /// </summary>
        private static void MoveMouseGradually(AmigaSystem system, AmigaControlPort port, int dx, int dy, int cyclesPerStep)
        {
            int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
            if (steps == 0)
            {
                return;
            }

            int lastX = 0;
            int lastY = 0;
            for (int step = 1; step <= steps; step++)
            {
                int nextX = (dx * step) / steps;
                int nextY = (dy * step) / steps;
                system.Input.MoveMouse(port, nextX - lastX, nextY - lastY);
                system.RunChipCycles(cyclesPerStep);
                lastX = nextX;
                lastY = nextY;
            }
        }

        /// <summary>
        /// Runs Test Amiga Test Kit Boot Frame.
        /// </summary>
        private static void TestAmigaTestKitBootFrame()
        {
            string adfPath = FindExternalAmigaTestKitAdf();
            Check.True(System.IO.File.Exists(adfPath), "Amiga Test Kit ADF not found: " + adfPath);
            Check.True(KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out KickstartRomInfo romInfo), "Default Kickstart ROM not found.");

            using (var system = new AmigaSystem(AmigaModel.A500Pal, AmigaAccuracyOptions.Compatible.Clone()))
            {
                system.Bus.FloppyController.InsertDrive0(AdfImage.Load(adfPath), true);
                system.LoadKickstart(romBytes);
                system.RunChipCycles(180_000_000);
                uint[] pixels = system.PresentedFrameBuffer.Pixels;
                CheckNonBlank(pixels, romInfo);
                Check.True(CountUniqueColors(pixels, 32) >= 4, "Amiga Test Kit frame did not develop enough visible diagnostic colors.");
            }
        }

        /// <summary>
        /// Runs With Adf Frame.
        /// </summary>
        private static void WithAdfFrame(string fileName, int cycles, Action<AmigaSystem, KickstartRomInfo> inspect)
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string adfPath = System.IO.Path.Combine(docs, "AmigaEmulator", fileName);
            Check.True(System.IO.File.Exists(adfPath), "ADF not found: " + adfPath);
            Check.True(KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out KickstartRomInfo romInfo), "Default Kickstart ROM not found.");

            using (var system = new AmigaSystem(AmigaModel.A500Pal, AmigaAccuracyOptions.Fast.Clone()))
            {
                system.Bus.FloppyController.InsertDrive0(AdfImage.Load(adfPath), true);
                system.LoadKickstart(romBytes);
                system.RunChipCycles(cycles);
                inspect(system, romInfo);
            }
        }

        /// <summary>
        /// Checks Non Blank for test validation.
        /// </summary>
        private static void CheckNonBlank(uint[] pixels, KickstartRomInfo romInfo)
        {
            uint first = pixels.Length > 0 ? pixels[0] : 0;
            int different = 0;
            for (int index = 0; index < pixels.Length; index++)
            {
                if (pixels[index] != first)
                {
                    different++;
                    if (different > 128)
                    {
                        break;
                    }
                }
            }

            Check.True(different > 128, "Golden ADF frame stayed blank or uniform. ROM=" + (romInfo != null ? romInfo.Path : string.Empty));
        }

        /// <summary>
        /// Runs Count Mostly Dark Columns.
        /// </summary>
        private static int CountMostlyDarkColumns(uint[] pixels, int width, int height, int top, int bottom)
        {
            int clampedTop = Math.Max(0, top);
            int clampedBottom = Math.Min(height, bottom);
            int badColumns = 0;
            int totalRows = Math.Max(1, clampedBottom - clampedTop);
            for (int x = 0; x < width; x++)
            {
                int dark = 0;
                for (int y = clampedTop; y < clampedBottom; y++)
                {
                    uint argb = pixels[(y * width) + x];
                    int red = (int)((argb >> 16) & 0xFF);
                    int green = (int)((argb >> 8) & 0xFF);
                    int blue = (int)(argb & 0xFF);
                    if (red + green + blue < 12)
                    {
                        dark++;
                    }
                }

                if ((dark * 100) / totalRows > 85)
                {
                    badColumns++;
                }
            }

            return badColumns;
        }

        /// <summary>
        /// Runs Count Unique Colors.
        /// </summary>
        private static int CountUniqueColors(uint[] pixels, int limit)
        {
            var colors = new HashSet<uint>();
            for (int index = 0; index < pixels.Length && colors.Count < limit; index++)
            {
                colors.Add(pixels[index]);
            }

            return colors.Count;
        }

        /// <summary>
        /// Finds External Amiga Test Kit ADF in the available emulator data.
        /// </summary>
        private static string FindExternalAmigaTestKitAdf()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string current = baseDir;
            for (int depth = 0; depth < 8 && !string.IsNullOrEmpty(current); depth++)
            {
                string candidate = System.IO.Path.Combine(
                    current,
                    "external-tests",
                    "amiga-test-kit-1.21",
                    "AmigaTestKit-1.21",
                    "AmigaTestKit.adf");
                if (System.IO.File.Exists(candidate))
                {
                    return candidate;
                }

                current = System.IO.Directory.GetParent(current)?.FullName;
            }

            return System.IO.Path.Combine(
                baseDir,
                "external-tests",
                "amiga-test-kit-1.21",
                "AmigaTestKit-1.21",
                "AmigaTestKit.adf");
        }
    }

    /// <summary>
    /// Provides V Amiga Ts Tests functionality.
    /// </summary>
    internal static class VAmigaTsTests
    {
        /// <summary>
        /// Stores Smoke Cases.
        /// </summary>
        private static readonly VAmigaTsCase[] SmokeCases =
        {
            /// <summary>
            /// Runs V Amiga Ts Case.
            /// </summary>
            new VAmigaTsCase("Agnus/Blitter", "line zero flag reference", @"Agnus\Blitter\line\zero1\zero1.adf", 3, 512),
            /// <summary>
            /// Runs V Amiga Ts Case.
            /// </summary>
            new VAmigaTsCase("Agnus/Copper", "COPWAIT timing reference", @"Agnus\Copper\Wait\copwait1\copwait1.adf", 3, 512),
            /// <summary>
            /// Runs V Amiga Ts Case.
            /// </summary>
            new VAmigaTsCase("Agnus/Beam", "VPOS register reference", @"Agnus\Registers\VPOS\vprobe4\vprobe4.adf", 3, 512),
            /// <summary>
            /// Runs V Amiga Ts Case.
            /// </summary>
            new VAmigaTsCase("Denise/Sprite", "manual sprite register reference", @"Denise\Sprites\manual\manual1\manual1.adf", 4, 512),
            /// <summary>
            /// Runs V Amiga Ts Case.
            /// </summary>
            new VAmigaTsCase("Denise/Bitplane", "BPLCON2 dual playfield reference", @"Denise\Registers\BPLCON2\dualpf1\dualpf1.adf", 3, 512),
            /// <summary>
            /// Runs V Amiga Ts Case.
            /// </summary>
            new VAmigaTsCase("Paula", "ADKCON register reference", @"Paula\Registers\ADKCON\adkcon1\adkcon1.adf", 2, 128),
            /// <summary>
            /// Runs V Amiga Ts Case.
            /// </summary>
            new VAmigaTsCase("CIA", "TOD alarm reference", @"CIA\TOD\todvice\0alarm\0alarm.adf", 2, 128),
            /// <summary>
            /// Runs V Amiga Ts Case.
            /// </summary>
            new VAmigaTsCase("Memory", "Kickstart mirror reference", @"Memory\Rom\mirror\kickmirror\kickmirror1.adf", 2, 128),
            /// <summary>
            /// Runs V Amiga Ts Case.
            /// </summary>
            new VAmigaTsCase("Mainboard/Input", "JOY0DAT port reference", @"Mainboard\Ports\JOYxDAT\joy0dat\joy0dat.adf", 2, 128)
        };

        /// <summary>
        /// Builds the Build result.
        /// </summary>
        public static IEnumerable<ChipTestCase> Build(TestOptions options)
        {
            VAmigaTsCase[] cases = options.VAmigaTsAll
                ? DiscoverAllCases()
                : SmokeCases;
            int limit = options.VAmigaTsLimit > 0
                ? Math.Min(options.VAmigaTsLimit, cases.Length)
                : cases.Length;
            for (int index = 0; index < limit; index++)
            {
                VAmigaTsCase testCase = cases[index];
                yield return new ChipTestCase(
                    "vAmigaTS/" + testCase.Category,
                    testCase.Name,
                    "dirkwhoffmann/vAmigaTS " + testCase.RelativeAdfPath,
                    /// <summary>
                    /// Runs the Smoke Case routine.
                    /// </summary>
                    delegate { RunSmokeCase(testCase, options.VAmigaTsCycles); });
            }
        }

        /// <summary>
        /// Runs Discover All Cases.
        /// </summary>
        private static VAmigaTsCase[] DiscoverAllCases()
        {
            string suiteRoot = FindSuiteRoot();
            if (!System.IO.Directory.Exists(suiteRoot))
            {
                return Array.Empty<VAmigaTsCase>();
            }

            string[] adfs = System.IO.Directory.GetFiles(suiteRoot, "*.adf", System.IO.SearchOption.AllDirectories);
            Array.Sort(adfs, StringComparer.OrdinalIgnoreCase);
            var cases = new List<VAmigaTsCase>();
            for (int index = 0; index < adfs.Length; index++)
            {
                string adf = adfs[index];
                string raw = FindReferenceRaw(adf);
                string image = FindReferenceImage(adf);
                if (!System.IO.File.Exists(raw) && !System.IO.File.Exists(image))
                {
                    continue;
                }

                string relative = GetRelativePath(suiteRoot, adf);
                string[] parts = relative.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                string category = parts.Length >= 2 ? parts[0] + "/" + parts[1] : "Misc";
                string name = System.IO.Path.GetFileNameWithoutExtension(adf) + " reference";
                cases.Add(new VAmigaTsCase(category, name, relative, 2, 128));
            }

            return cases.ToArray();
        }

        /// <summary>
        /// Runs the Smoke Case routine.
        /// </summary>
        private static void RunSmokeCase(VAmigaTsCase testCase, int cycles)
        {
            string suiteRoot = FindSuiteRoot();
            string adfPath = System.IO.Path.Combine(suiteRoot, testCase.RelativeAdfPath);
            Check.True(System.IO.File.Exists(adfPath), "vAmigaTS ADF not found: " + adfPath);
            string rawPath = FindReferenceRaw(adfPath);
            if (System.IO.File.Exists(rawPath))
            {
                Check.Equal(716 * 285 * 3, (int)new System.IO.FileInfo(rawPath).Length, "Unexpected vAmigaTS RAW size.");
            }
            else
            {
                string imagePath = FindReferenceImage(adfPath);
                Check.True(System.IO.File.Exists(imagePath), "vAmigaTS reference image not found: " + imagePath);
            }

            Check.True(KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out KickstartRomInfo romInfo), "Default Kickstart ROM not found.");

            using (var system = new AmigaSystem(AmigaModel.A500Pal, AmigaAccuracyOptions.Fast.Clone()))
            {
                system.LoadKickstart(romBytes);
                system.Bus.FloppyController.InsertDrive0(AdfImage.Load(adfPath), true);
                system.RunChipCycles(cycles);
                uint[] pixels = system.PresentedFrameBuffer.Pixels;
                Check.True(system.FrameCounter > 10, "vAmigaTS case did not advance frames. ROM=" + romInfo.Path);
                if (System.IO.File.Exists(rawPath) && TryReadUniformRawColor(rawPath, out uint expectedArgb))
                {
                    Check.True(
                        DominantColorMatches(pixels, expectedArgb, 24, 60),
                        "vAmigaTS uniform reference color mismatch. Expected=" + expectedArgb.ToString("X8") + " CPU=" + system.GetCpuDebugInfo());
                }
                else
                {
                    int different = CountPixelsDifferentFromFirst(pixels);
                    int unique = CountUniqueColors(pixels, 64);
                    Check.True(different >= testCase.MinDifferentPixels, "vAmigaTS frame stayed too uniform. Different=" + different + " CPU=" + system.GetCpuDebugInfo());
                    Check.True(unique >= testCase.MinUniqueColors, "vAmigaTS frame has too few colors. Unique=" + unique + " CPU=" + system.GetCpuDebugInfo());
                }
            }
        }

        /// <summary>
        /// Finds Suite Root in the available emulator data.
        /// </summary>
        private static string FindSuiteRoot()
        {
            string current = AppDomain.CurrentDomain.BaseDirectory;
            for (int depth = 0; depth < 10 && !string.IsNullOrEmpty(current); depth++)
            {
                string candidate = System.IO.Path.Combine(current, "external-tests", "vAmigaTS");
                if (System.IO.Directory.Exists(candidate))
                {
                    return candidate;
                }

                current = System.IO.Directory.GetParent(current)?.FullName;
            }

            return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "external-tests", "vAmigaTS");
        }

        /// <summary>
        /// Returns the Relative Path.
        /// </summary>
        private static string GetRelativePath(string root, string path)
        {
            string fullRoot = System.IO.Path.GetFullPath(root);
            string fullPath = System.IO.Path.GetFullPath(path);
            if (!fullRoot.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                fullRoot += System.IO.Path.DirectorySeparatorChar;
            }

            if (fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.Substring(fullRoot.Length);
            }

            return path;
        }

        /// <summary>
        /// Finds Reference Raw in the available emulator data.
        /// </summary>
        private static string FindReferenceRaw(string adfPath)
        {
            string directory = System.IO.Path.GetDirectoryName(adfPath) ?? string.Empty;
            string name = System.IO.Path.GetFileNameWithoutExtension(adfPath);
            string[] candidates =
            {
                System.IO.Path.Combine(directory, name + ".raw"),
                System.IO.Path.Combine(directory, name + "_ocs.raw"),
                System.IO.Path.Combine(directory, name + "_OCS.raw"),
                System.IO.Path.Combine(directory, name + "_A500_OCS.raw"),
                System.IO.Path.Combine(directory, name + "_ecs.raw"),
                System.IO.Path.Combine(directory, name + "_ECS.raw")
            };
            for (int index = 0; index < candidates.Length; index++)
            {
                if (System.IO.File.Exists(candidates[index]))
                {
                    return candidates[index];
                }
            }

            string[] wildcard = System.IO.Directory.GetFiles(directory, name + "_*.raw");
            if (wildcard.Length > 0)
            {
                Array.Sort(wildcard, StringComparer.OrdinalIgnoreCase);
                return wildcard[0];
            }

            return candidates[0];
        }

        /// <summary>
        /// Finds Reference Image in the available emulator data.
        /// </summary>
        private static string FindReferenceImage(string adfPath)
        {
            string directory = System.IO.Path.GetDirectoryName(adfPath) ?? string.Empty;
            string name = System.IO.Path.GetFileNameWithoutExtension(adfPath);
            string[] patterns =
            {
                name + ".JPG",
                name + ".jpg",
                name + "_*.JPG",
                name + "_*.jpg",
                name + "_*.jpeg",
                name + "_*.png"
            };
            for (int patternIndex = 0; patternIndex < patterns.Length; patternIndex++)
            {
                string[] matches = System.IO.Directory.GetFiles(directory, patterns[patternIndex]);
                if (matches.Length > 0)
                {
                    Array.Sort(matches, StringComparer.OrdinalIgnoreCase);
                    return matches[0];
                }
            }

            return System.IO.Path.Combine(directory, name + ".JPG");
        }

        /// <summary>
        /// Runs Count Pixels Different From First.
        /// </summary>
        private static int CountPixelsDifferentFromFirst(uint[] pixels)
        {
            uint first = pixels.Length > 0 ? pixels[0] : 0;
            int different = 0;
            for (int index = 0; index < pixels.Length; index++)
            {
                if (pixels[index] != first)
                {
                    different++;
                }
            }

            return different;
        }

        /// <summary>
        /// Runs Count Unique Colors.
        /// </summary>
        private static int CountUniqueColors(uint[] pixels, int limit)
        {
            var colors = new HashSet<uint>();
            for (int index = 0; index < pixels.Length && colors.Count < limit; index++)
            {
                colors.Add(pixels[index]);
            }

            return colors.Count;
        }

        /// <summary>
        /// Attempts to read a vAmigaTS RAW frame that intentionally contains one solid RGB color.
        /// </summary>
        private static bool TryReadUniformRawColor(string rawPath, out uint argb)
        {
            argb = 0;
            byte[] raw = System.IO.File.ReadAllBytes(rawPath);
            if (raw.Length < 3 || (raw.Length % 3) != 0)
            {
                return false;
            }

            byte red = raw[0];
            byte green = raw[1];
            byte blue = raw[2];
            for (int index = 3; index < raw.Length; index += 3)
            {
                if (raw[index] != red || raw[index + 1] != green || raw[index + 2] != blue)
                {
                    return false;
                }
            }

            argb = 0xFF000000u | ((uint)red << 16) | ((uint)green << 8) | blue;
            return true;
        }

        /// <summary>
        /// Returns whether the dominant emulator output color matches the expected reference color.
        /// </summary>
        private static bool DominantColorMatches(uint[] pixels, uint expectedArgb, int tolerance, int minimumPercent)
        {
            if (pixels.Length == 0)
            {
                return false;
            }

            int matches = 0;
            for (int index = 0; index < pixels.Length; index++)
            {
                if (ColorDistanceWithinTolerance(pixels[index], expectedArgb, tolerance))
                {
                    matches++;
                }
            }

            return matches * 100 >= pixels.Length * minimumPercent;
        }

        /// <summary>
        /// Returns whether two ARGB colors are close enough for 12-bit Amiga palette expansion comparisons.
        /// </summary>
        private static bool ColorDistanceWithinTolerance(uint actualArgb, uint expectedArgb, int tolerance)
        {
            int actualRed = (int)((actualArgb >> 16) & 0xFF);
            int actualGreen = (int)((actualArgb >> 8) & 0xFF);
            int actualBlue = (int)(actualArgb & 0xFF);
            int expectedRed = (int)((expectedArgb >> 16) & 0xFF);
            int expectedGreen = (int)((expectedArgb >> 8) & 0xFF);
            int expectedBlue = (int)(expectedArgb & 0xFF);
            return Math.Abs(actualRed - expectedRed) <= tolerance &&
                Math.Abs(actualGreen - expectedGreen) <= tolerance &&
                Math.Abs(actualBlue - expectedBlue) <= tolerance;
        }

        /// <summary>
        /// Provides V Amiga Ts Case functionality.
        /// </summary>
        private sealed class VAmigaTsCase
        {
            /// <summary>
            /// Initializes a new instance of the VAmigaTsCase class.
            /// </summary>
            public VAmigaTsCase(string category, string name, string relativeAdfPath, int minUniqueColors, int minDifferentPixels)
            {
                Category = category;
                Name = name;
                RelativeAdfPath = relativeAdfPath;
                MinUniqueColors = minUniqueColors;
                MinDifferentPixels = minDifferentPixels;
            }

            /// <summary>
            /// Gets or sets Category.
            /// </summary>
            public string Category { get; private set; }
            /// <summary>
            /// Gets or sets Name.
            /// </summary>
            public string Name { get; private set; }
            /// <summary>
            /// Gets or sets Relative ADF Path.
            /// </summary>
            public string RelativeAdfPath { get; private set; }
            /// <summary>
            /// Gets or sets Min Unique Colors.
            /// </summary>
            public int MinUniqueColors { get; private set; }
            /// <summary>
            /// Gets or sets Min Different Pixels.
            /// </summary>
            public int MinDifferentPixels { get; private set; }
        }
    }
}
