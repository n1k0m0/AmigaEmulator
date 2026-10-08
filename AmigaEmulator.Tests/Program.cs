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
            tests.AddRange(StorageExpansionTests.Build());
            if (options.IncludeAdf)
            {
                tests.AddRange(GoldenAdfTests.Build(options));
            }
            if (options.IncludeLocalSiedlerGolden)
            {
                tests.AddRange(LocalSiedlerGoldenTests.Build(options));
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
        /// Enables chip-bus contention in external vAmigaTS and local savestate checks.
        /// </summary>
        public bool StrictBus { get; private set; }

        public AmigaAccuracyOptions CreateExternalAccuracyOptions()
        {
            var accuracy = AmigaAccuracyOptions.Fast.Clone();
            accuracy.StrictChipBusArbitration = StrictBus;
            accuracy.Name = StrictBus ? "StrictNoTrace" : "Fast";
            return accuracy;
        }
        /// <summary>
        /// Gets or sets Include V Amiga Ts.
        /// </summary>
        public bool IncludeVAmigaTs { get; private set; }
        /// <summary>
        /// Gets or sets Include Amiga Devtest.
        /// </summary>
        public bool IncludeAmigaDevtest { get; private set; }
        /// <summary>
        /// Gets or sets Include Local Siedler Golden.
        /// </summary>
        public bool IncludeLocalSiedlerGolden { get; private set; }
        /// <summary>
        /// Gets or sets Siedler Save Path.
        /// </summary>
        public string SiedlerSavePath { get; private set; }
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
                if (string.Equals(arg, "--strict-bus", StringComparison.OrdinalIgnoreCase))
                {
                    options.StrictBus = true;
                }
                else if (string.Equals(arg, "--include-adf", StringComparison.OrdinalIgnoreCase))
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
                else if (string.Equals(arg, "--include-local-siedler-golden", StringComparison.OrdinalIgnoreCase))
                {
                    options.IncludeLocalSiedlerGolden = true;
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
                else if (string.Equals(arg, "--siedler-save", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
                {
                    options.SiedlerSavePath = args[++index];
                    options.IncludeLocalSiedlerGolden = true;
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
            yield return new ChipTestCase("CIA/Serial", "output serial interrupt follows sixteen Timer-A underflows",
                "Amiga HRM Appendix F SDR output at half the Timer-A underflow rate", TestSerialOutputTiming);
            yield return new ChipTestCase("CIA/Serial", "buffered SDR write continues without restarting active output",
                "Amiga HRM Appendix F double-buffered serial transmitter", TestSerialOutputBuffer);
            yield return new ChipTestCase("CIA/Serial", "serial output resumes exactly across new and legacy savestates",
                "CIA serial transfer state and compatibility with pre-output snapshots", TestSerialOutputSave);
            yield return SelfTest("Input", "existing keyboard/joystick/mouse regression self-test", "Amiga HRM input register descriptions; existing emulator regression suite", AmigaInputSelfTest.Run);
        }

        /// <summary>
        /// Runs Self Test.
        /// </summary>
        private static Cia8520 CreateSerialOutput()
        {
            var cia = new Cia8520("CIA-A");
            cia.WriteByte(0x0400, 1);
            cia.WriteByte(0x0500, 0);
            cia.WriteByte(0x0D00, 0x88);
            cia.WriteByte(0x0E00, 0x41);
            cia.WriteByte(0x0C00, 0xA5);
            return cia;
        }

        private static void TestSerialOutputTiming()
        {
            var cia = CreateSerialOutput();
            cia.QueueSerialInputByte(0x5A);
            for (int i = 0; i < 15; i++) cia.Tick();
            Check.Equal(0, cia.PeekByte(0x0D00) & 8, "No completion before eight CNT pulses.");
            Check.Equal((byte)0xA5, cia.SerialData, "Incoming keyboard bytes must not overwrite SDR in output mode.");
            cia.Tick();
            Check.Equal(0x88, cia.ReadByte(0x0D00) & 0x88, "The final pulse must raise the masked serial interrupt.");
            cia.WriteByte(0x0E00, 0x01);
            for (int i = 0; i < 8; i++) cia.Tick();
            Check.Equal((byte)0x5A, cia.SerialData, "Queued external input resumes after returning to input mode.");
            Check.Equal(8, cia.ReadByte(0x0D00) & 8, "Received input has its own completion interrupt.");
            cia = CreateSerialOutput();
            cia.WriteByte(0x0E00, 0x40);
            for (int i = 0; i < 100; i++) cia.Tick();
            Check.Equal(0, cia.PeekByte(0x0D00) & 8, "A stopped Timer A cannot clock serial output.");
        }

        private static void TestSerialOutputBuffer()
        {
            var cia = CreateSerialOutput();
            for (int i = 0; i < 5; i++) cia.Tick();
            cia.WriteByte(0x0C00, 0x3C);
            for (int i = 0; i < 10; i++) cia.Tick();
            Check.Equal(0, cia.PeekByte(0x0D00) & 8, "SDR refill must not interrupt the current byte early.");
            cia.Tick();
            Check.Equal(8, cia.ReadByte(0x0D00) & 8, "First byte completes without restart.");
            for (int i = 0; i < 15; i++) cia.Tick();
            Check.Equal(0, cia.PeekByte(0x0D00) & 8, "Second byte requires another eight pulses.");
            cia.Tick();
            Check.Equal(8, cia.ReadByte(0x0D00) & 8, "Buffered byte completes automatically.");
            for (int i = 0; i < 32; i++) cia.Tick();
            Check.Equal(0, cia.PeekByte(0x0D00) & 8, "Idle transmitter does not generate repeated completion interrupts.");
        }

        private static void TestSerialOutputSave()
        {
            var cia = CreateSerialOutput();
            for (int i = 0; i < 5; i++) cia.Tick();
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) cia.SaveState(writer);
            stream.Position = 0;
            var restored = new Cia8520("CIA-A");
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true)) restored.LoadState(reader);
            for (int i = 0; i < 10; i++) restored.Tick();
            Check.Equal(0, restored.PeekByte(0x0D00) & 8, "Restoring must preserve the remaining clocks.");
            restored.Tick();
            Check.Equal(8, restored.ReadByte(0x0D00) & 8, "Restored completion occurs at the original boundary.");
            stream.SetLength(0); stream.Position = 0;
            cia = CreateSerialOutput();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                StateSerializer.WriteObjectFields(writer, cia, "_serialOutputHalfClocks", "_serialOutputBufferPending");
            stream.Position = 0;
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true)) restored.LoadState(reader);
            for (int i = 0; i < 16; i++) restored.Tick();
            Check.Equal(8, restored.ReadByte(0x0D00) & 8, "Legacy output-mode snapshot must not remain permanently stuck.");
            cia = CreateSerialOutput();
            for (int i = 0; i < 5; i++) cia.Tick();
            cia.Reset();
            stream.SetLength(0); stream.Position = 0;
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) cia.SaveState(writer);
            using var freshStream = new MemoryStream();
            using (var writer = new BinaryWriter(freshStream, Encoding.UTF8, true)) new Cia8520("CIA-A").SaveState(writer);
            Check.True(stream.ToArray().SequenceEqual(freshStream.ToArray()), "Reset must clear pending serial output as completely as a fresh CIA.");
        }

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
                "production bitplane DMA hides incomplete fetch groups",
                "Current lowres compatibility path; per-slot hardware fetch remains an open audit item",
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

            int cyclesToFirstFetch = (BitplaneUnit.PalVisibleTop * AmigaModel.A500Pal.ChipCyclesPerLine) + 0x39;
            for (int index = 0; index < cyclesToFirstFetch; index++)
            {
                agnus.Tick();
            }

            agnus.Tick();
            Check.Equal(DmaOwner.Bitplane, agnus.LastDmaSlot.Owner, "Compact lowres slot $39 reserves BPL2 in a two-plane display.");
            Check.Equal(0, agnus.Bitplanes.RenderColorIndex(0, BitplaneUnit.PalVisibleTop), "First split slot must not expose an incomplete two-plane fetch group.");

            for (int index = 0; index < 2; index++)
            {
                agnus.Tick();
            }

            Check.Equal(DmaOwner.Bitplane, agnus.LastDmaSlot.Owner, "Compact lowres slot $3B completes BPL1 and complete the group.");
            Check.Equal(3, agnus.Bitplanes.RenderColorIndex(4, BitplaneUnit.PalVisibleTop), "After both split slots commit, the first fetched pixel should remain visible after the OCS lowres fetch lead-in.");
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
            Check.Equal(DmaOwner.Sprite, scheduler.GetSlot(0, 25, BitplaneUnit.PalVisibleTop).Owner, "Sprite slot should not be stolen by Copper or Blitter.");
            Check.Equal(DmaOwner.Copper, scheduler.GetSlot(0, 26, BitplaneUnit.PalVisibleTop).Owner, "The cycle between sprite words remains available to Copper.");
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

            for (int slot = 3; slot <= 9; slot += 2)
                Check.Equal(DmaOwner.Refresh, scheduler.GetSlot(0, slot, 0).Owner, "Four interleaved refresh cycles must be reserved.");
            for (int slot = 11; slot <= 15; slot += 2)
                Check.Equal(DmaOwner.Disk, scheduler.GetSlot(0, slot, 0).Owner, "Three interleaved disk cycles must be reserved.");
            for (int channel = 0; channel < 4; channel++)
            {
                var audioSlot = scheduler.GetSlot(0, 17 + channel * 2, 0);
                Check.Equal(DmaOwner.Audio, audioSlot.Owner, "Each audio channel reserves one word, not two.");
                Check.Equal(channel, audioSlot.AudioChannelIndex, "Audio channel order must be preserved.");
            }
            for (int word = 0; word < 16; word++)
            {
                var spriteSlot = scheduler.GetSlot(0, 25 + word * 2, 0);
                Check.Equal(DmaOwner.Sprite, spriteSlot.Owner, "Sixteen interleaved sprite word cycles must be reserved.");
                Check.Equal(word / 2, spriteSlot.SpriteIndex, "Sprite channel order must be preserved.");
                Check.Equal(word % 2, spriteSlot.SpriteSlotPhase, "Sprite control/data word order must be preserved.");
            }
            for (int slot = 4; slot < 56; slot += 2)
                Check.Equal(DmaOwner.Cpu, scheduler.GetSlot(0, slot, 0).Owner, "Fixed DMA must leave every intervening cycle free.");
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

            Check.Equal(DmaOwner.Disk, scheduler.GetSlot(0, 15, BitplaneUnit.PalVisibleTop).Owner, "Bitplane fetch timing must not override the fixed disk DMA slot.");
            Check.Equal(DmaOwner.Sprite, scheduler.GetSlot(0, 25, BitplaneUnit.PalVisibleTop).Owner, "Bitplane fetch timing must not override the fixed sprite DMA slot.");
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
            Check.Equal(DmaOwner.Blitter, scheduler.GetSlot(0, 41, 0).Owner, "The interleaved memory cycle remains available to the Blitter.");
            Check.Equal(DmaOwner.Copper, scheduler.GetSlot(0, 42, 0).Owner, "The Copper must fetch its second instruction word two cycles later, rather than waiting four cycles and missing sprite reuse positions.");
            Check.Equal(DmaOwner.Copper, scheduler.GetSlot(0, 4, 0).Owner, "Copper can run during horizontal blanking after refresh.");
            Check.Equal(DmaOwner.Copper, scheduler.GetSlot(0, 8, 0).Owner, "Disabled audio leaves its slot available to Copper.");
            Check.Equal(DmaOwner.Copper, scheduler.GetSlot(0, 16, 0).Owner, "An idle disk leaves its slot available to Copper.");
            registers.WriteWord(Dmacon, 0x8021);
            Check.Equal(DmaOwner.Audio, scheduler.GetSlot(0, 17, 0).Owner, "Enabled audio retains priority.");
            Check.Equal(DmaOwner.Copper, scheduler.GetSlot(0, 18, 0).Owner, "The cycle between audio words remains available to Copper.");
            scheduler.AttachSpriteBusDemand((sprite, phase, line) => false);
            Check.Equal(DmaOwner.Copper, scheduler.GetSlot(0, 20, 0).Owner, "An inactive sprite leaves its slot available to Copper.");
            scheduler.AttachSpriteBusDemand((sprite, phase, line) => true);
            Check.Equal(DmaOwner.Sprite, scheduler.GetSlot(0, 25, 0).Owner, "A fetching sprite retains priority over Copper.");

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
                "three-plane lowres DMA uses the current compact fetch group",
                "Current compatibility path for lowres screens with up to four planes",
                TestThreePlaneLowresUsesCompactFetchGroup);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "OCS lowres six-plane DMA follows hardware fetch slot order",
                "WinUAE/HRM OCS lowres bitplane DMA sequence 8,4,6,2,7,3,5,1",
                TestOcsLowresSixPlaneFetchSequence);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "BPLCON1 applies odd and even scroll nibbles to the matching planes",
                "Amiga HRM BPLCON1 PF1/odd uses bits 0-3 and PF2/even uses bits 4-7",
                TestBplcon1OddEvenScrollNibbles);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "BPLCON1 fine scroll keeps the shifted right edge visible",
                "OCS bitplane shifter output extends past the raw DDF fetch width by the active BPLCON1 fine scroll",
                TestBplcon1FineScrollExtendsRightEdge);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "mid-frame DDF change keeps per-line presentation apertures",
                "Settlers-style bottom panels can widen DDF mid-frame without losing the stable playfield aperture",
                TestMidFrameDdfChangeKeepsPerLinePresentationApertures);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "revisited DMA rows invalidate previously fetched words",
                "A backward raster fetch must disable the forward-row cache fast path until a new frame",
                TestRevisitedDmaRowsInvalidateWords);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "line-adaptive presentation keeps bottom panels horizontally registered",
                "Settlers-style bottom panels must keep their widened right DDF edge aligned with the monitor edge",
                TestLineAdaptivePresentationKeepsBottomPanelRegistered);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "oversized bottom panel includes widened left content edge",
                "Settlers-style bottom panels must keep the negative left content visible without exposing fine-scroll blank lead-in",
                TestOversizedBottomPanelIncludesWidenedLeftContentEdge);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "line tail renders the final widened DDF word",
                "Settlers-style bottom panels need post-beam tail pixels after all line DMA data has arrived",
                TestLineTailRendersFinalWidenedDdfWord);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "line tail renders right border color",
                "Settlers-style wooden side frames use COLOR00 border pixels beyond the last normal beam sample",
                TestLineTailRendersRightBorderColor);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "line lead-in renders the widened DDF left edge",
                "Settlers-style bottom panels need left overscan pixels before the normal beam aperture",
                TestLineLeadInRendersWidenedDdfLeftEdge);
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
                "four-plane lowres DDF advances by the visible OCS fetch span",
                "OCS 4-plane lowres DMA advances one 20-word standard DDF span per bitplane scanline",
                TestFourPlaneLowresDdfAdvancesByVisibleFetchSpan);
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
                "wide PAL overscan uses the stable monitor viewport",
                "PAL overscan screens such as Settlers must keep side-frame edges while avoiding vertical resampling",
                TestWidePalOverscanUsesFixedMonitorViewport);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "standard HIRES DDF keeps PAL presentation width",
                "OCS hires fetches start at $3C on a normal PAL screen and must not be horizontally resampled",
                TestStandardHiresDdfReportsStablePresentationWidth);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "vertical DIW overscan reports the full presentation source height",
                "Amiga HRM DIWSTRT/DIWSTOP can open the display window above the standard PAL view",
                TestVerticalDiwOverscanReportsPresentationSourceHeight);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "small vertical overscan keeps the game aperture",
                "PAL monitor presentation should scale the register-selected display window without inventing border rows",
                TestSmallVerticalOverscanCropsInsteadOfScaling);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "standard PAL black backgrounds stay inside the game aperture",
                "Black title-screen rows inside a normal 640x256 PAL display are scene content once selected by DIW/DDF",
                TestStandardPalBlackBackgroundIsNotLetterboxCropped);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "PAL vertical overscan keeps register-selected geometry",
                "Moderate vertical overscan at normal PAL width must not use content-dependent side trimming",
                TestPalVerticalOverscanKeepsRegisterSelectedSourceWindowStable);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "PAL aperture stays fixed when scene brightness changes",
                "Display registers select geometry independently of black scene content",
                TestPalApertureIgnoresSceneBrightness);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "PAL presentation preserves asymmetric dark scene edges",
                "Host presentation must copy the selected aperture without content-based shifts",
                TestPalPresentationPreservesDarkEdges);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "bitplane frame start invalidates previous-frame fetched rows",
                "Amiga HRM bitplane DMA reloads display data every frame",
                TestFrameStartInvalidatesFetchedRows);
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "sparse top-edge artwork survives frame presentation",
                "Sparse scene content must not be erased as transition noise",
                delegate { TestTopArtworkIsPreserved(8, 1); });
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "wide sparse top-edge artwork survives frame presentation",
                "Wide sparse scene content must be preserved in the selected aperture",
                delegate { TestTopArtworkIsPreserved(320, 4); });
            yield return new ChipTestCase(
                "Denise/Bitplane",
                "dense top-edge artwork is preserved in the presented frame",
                "PAL frame presentation must preserve dense title and HUD artwork",
                delegate { TestTopArtworkIsPreserved(600, 10); });
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
            yield return new ChipTestCase("Denise/Bitplane", "hires DMA renders every bit without periodic missing word prefixes",
                "Beam-ordered DMA word completion precedes shifter pixel output", TestHiresBeamOrderedDma);
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
            yield return new ChipTestCase("Denise/Bitplane", "sprites cannot contaminate HAM color hold within or across render spans",
                "HAM playfield color precedes sprite composition", TestHamIgnoresSpriteColor);
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
            yield return new ChipTestCase(
                "Denise/Collision",
                "CLXCON matches odd and even bitplane groups independently",
                "Amiga Hardware Reference Manual, CLXCON/CLXDAT register summary",
                TestCollisionPlayfieldGroups);
            yield return new ChipTestCase(
                "Denise/Collision",
                "disabled collision planes match zero pixels and reset after read",
                "Amiga Hardware Reference Manual: disabled planes cannot prevent collisions",
                TestCollisionDisabledPlanes);

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
            Check.Equal(1, bitplanes.RenderColorIndex(4, BitplaneUnit.PalVisibleTop), "Fetched DMA word bit 15 should render after the OCS lowres fetch lead-in.");
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

            bitplanes.FetchDmaPlaneWord(0x39, BitplaneUnit.PalVisibleTop);
            Check.Equal(0, bitplanes.RenderColorIndex(0, BitplaneUnit.PalVisibleTop), "A half-fetched split group must not become visible.");

            bitplanes.FetchDmaPlaneWord(0x3B, BitplaneUnit.PalVisibleTop);
            Check.Equal(3, bitplanes.RenderColorIndex(4, BitplaneUnit.PalVisibleTop), "After both plane words commit, the group should be visible after the OCS lowres fetch lead-in.");
        }

        /// <summary>
        /// Verifies the current compact lowres fetch compatibility path for three-plane displays.
        /// </summary>
        private static void TestThreePlaneLowresUsesCompactFetchGroup()
        {
            var registers = new CustomRegisterMap();
            ushort[] memory = new ushort[6];
            memory[0] = 0x8000;
            memory[1] = 0x8000;
            memory[2] = 0x8000;
            var bitplanes = new BitplaneUnit(registers, address => memory[(address >> 1) % memory.Length]);
            registers.WriteWord(Bplcon0, 0x3000);
            registers.WriteWord(Dmacon, 0x8300);
            registers.WriteWord(Diwstrt, 0x2C81);
            registers.WriteWord(Diwstop, 0x2D81);
            registers.WriteWord(Ddfstrt, 0x0038);
            registers.WriteWord(Ddfstop, 0x0038);
            for (int plane = 0; plane < 3; plane++)
            {
                registers.WriteWord((uint)(Bpl1pth + (plane * 4)), 0x0000);
                registers.WriteWord((uint)(Bpl1ptl + (plane * 4)), (ushort)(plane * 2));
            }

            bitplanes.FetchDmaPlaneWord(0x39, BitplaneUnit.PalVisibleTop);
            bitplanes.FetchDmaPlaneWord(0x3A, BitplaneUnit.PalVisibleTop);
            Check.Equal(0, bitplanes.RenderColorIndex(4, BitplaneUnit.PalVisibleTop), "A three-plane word remains hidden until the compatibility fetch group completes.");
            bitplanes.FetchDmaPlaneWord(0x3B, BitplaneUnit.PalVisibleTop);
            Check.True(!bitplanes.IsPlaneFetchSlot(0x3F, BitplaneUnit.PalVisibleTop), "The current compact path leaves slot $3F idle.");
            Check.Equal(7, bitplanes.RenderColorIndex(4, BitplaneUnit.PalVisibleTop), "The completed group renders all three bits after the lead-in.");
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

            Check.Equal(1, bitplanes.RenderColorIndex(0, BitplaneUnit.PalVisibleTop), "Upper BPLCON1 nibble delays even BPL2; odd BPL1 stays at the left edge.");
            registers.WriteWord(Bplcon1, 0x0001);
            bitplanes.RefreshFromRegisters();
            Check.Equal(2, bitplanes.RenderColorIndex(0, BitplaneUnit.PalVisibleTop), "Lower BPLCON1 nibble delays odd BPL1; even BPL2 stays at the left edge.");
        }

        /// <summary>
        /// Verifies that BPLCON1 fine scroll does not clip the last shifted bitplane pixels.
        /// </summary>
        private static void TestBplcon1FineScrollExtendsRightEdge()
        {
            var registers = new CustomRegisterMap();
            ushort[] memory = new ushort[32];
            memory[19] = 0x0001;
            var bitplanes = new BitplaneUnit(registers, address => memory[(address >> 1) & 31]);
            registers.WriteWord(Bplcon0, 0x1000);
            registers.WriteWord(Bplcon1, 0x000F);
            registers.WriteWord(Dmacon, 0x8300);
            registers.WriteWord(Diwstrt, 0x1871);
            registers.WriteWord(Diwstop, 0x10D1);
            registers.WriteWord(Ddfstrt, 0x0030);
            registers.WriteWord(Ddfstop, 0x00C8);
            registers.WriteWord(Bpl1pth, 0x0000);
            registers.WriteWord(Bpl1ptl, 0x0000);

            for (int beamX = 0x30; beamX < 0xD0; beamX++)
            {
                if (bitplanes.IsPlaneFetchSlot(beamX, BitplaneUnit.PalVisibleTop))
                {
                    bitplanes.FetchDmaPlaneWord(beamX, BitplaneUnit.PalVisibleTop);
                }
            }

            Check.Equal(1, bitplanes.RenderColorIndex(640, BitplaneUnit.PalVisibleTop), "The final shifted lowres bit must remain visible beyond the raw DDF fetch width.");
            Check.Equal(1, bitplanes.RenderColorIndex(641, BitplaneUnit.PalVisibleTop), "Lowres pixels duplicate horizontally at the shifted right edge.");
            Check.Equal(0, bitplanes.RenderColorIndex(642, BitplaneUnit.PalVisibleTop), "Fine-scroll extension must stop after the last shifted bitplane pixel.");
        }

        /// <summary>
        /// Verifies that Siedler-style bottom-panel DDF changes are tracked per beam line.
        /// </summary>
        private static void TestMidFrameDdfChangeKeepsPerLinePresentationApertures()
        {
            var registers = new CustomRegisterMap();
            var bitplanes = new BitplaneUnit(registers, null);
            registers.WriteWord(Dmacon, 0x8300);
            registers.WriteWord(Bplcon1, 0x00FF);
            registers.WriteWord(Diwstrt, 0x1871);
            registers.WriteWord(Diwstop, 0x10D1);
            registers.WriteWord(Bplcon0, 0x6200);
            registers.WriteWord(Ddfstrt, 0x0030);
            registers.WriteWord(Ddfstop, 0x00C8);

            bitplanes.RefreshFromRegisters();
            bitplanes.ObservePresentationWindowForBeamLine(BitplaneUnit.PalVisibleTop);
            Check.True(bitplanes.TryGetLinePresentationSourceWindow(BitplaneUnit.PalVisibleTop, out int playfieldLeft, out int playfieldWidth), "The playfield line should record its presentation aperture.");
            Check.Equal(-32, playfieldLeft, "The 6-plane playfield starts at the expected left overscan position.");
            Check.Equal(672, playfieldWidth, "The 6-plane playfield aperture should include BPLCON1 fine-scroll coverage.");

            registers.WriteWord(Bplcon0, 0x4200);
            registers.WriteWord(Diwstop, 0x31D1);
            registers.WriteWord(Ddfstrt, 0x002C);
            registers.WriteWord(Ddfstop, 0x00D1);

            int panelBeamY = BitplaneUnit.PalVisibleTop + 236;
            bitplanes.RefreshFromRegisters();
            bitplanes.ObservePresentationWindowForBeamLine(panelBeamY);
            Check.True(bitplanes.TryGetLinePresentationSourceWindow(panelBeamY, out int panelLeft, out int panelWidth), "The bottom-panel line should record its widened presentation aperture.");
            Check.Equal(-48, panelLeft, "The 4-plane bottom panel starts further into left overscan.");
            Check.Equal(734, panelWidth, "The 4-plane bottom panel should keep the shifted right edge selected by DDFSTOP and BPLCON1.");
            Check.Equal(-48, bitplanes.FramePresentationSourceLeft, "The frame-wide aperture should include the widest observed left edge.");
            Check.Equal(734, bitplanes.FramePresentationSourceWidth, "The frame-wide aperture should include the widest observed panel line.");
        }

        /// <summary>
        /// Verifies that per-line presentation metadata keeps the widened right edge stable.
        /// </summary>
        private static void TestLineAdaptivePresentationKeepsBottomPanelRegistered()
        {
            using (var system = new AmigaSystem(AmigaModel.A500Pal, AmigaAccuracyOptions.Fast.Clone()))
            {
                const int sourceLeft = 92;
                const int sourceTop = 44;
                const int sourceWidth = 722;
                const int sourceHeight = 288;
                const int topMarkerSourceX = 797;
                const int panelMarkerSourceX = 813;
                const int topTargetY = 16;
                const int panelTargetY = 236;
                const uint topMarker = 0xFFFF2222u;
                const uint panelMarker = 0xFF22FF22u;
                int topSourceY = sourceTop + topTargetY;
                int panelSourceY = sourceTop + panelTargetY;

                system.FrameBuffer.Clear(0xFF000000u);
                system.PresentedFrameBuffer.Clear(0xFF000000u);
                system.FrameBuffer.Pixels[(topSourceY * system.FrameBuffer.Width) + topMarkerSourceX] = topMarker;
                system.FrameBuffer.Pixels[(panelSourceY * system.FrameBuffer.Width) + panelMarkerSourceX] = panelMarker;

                CustomRegisterMap registers = system.Agnus.CustomRegisters;
                registers.WriteWord(Dmacon, 0x8300);
                registers.WriteWord(Bplcon1, 0x00FF);
                registers.WriteWord(Diwstrt, 0x1871);
                registers.WriteWord(Diwstop, 0x10D1);
                registers.WriteWord(Bplcon0, 0x6200);
                registers.WriteWord(Ddfstrt, 0x0030);
                registers.WriteWord(Ddfstop, 0x00C8);
                system.Agnus.Bitplanes.RefreshFromRegisters();
                system.Agnus.Bitplanes.ObservePresentationWindowForBeamLine(topSourceY);
                system.Agnus.Bitplanes.IncludeSpritePresentationWindowForBeamLine(topSourceY, -34, 704);

                registers.WriteWord(Bplcon0, 0x4200);
                registers.WriteWord(Diwstop, 0x31D1);
                registers.WriteWord(Ddfstrt, 0x002C);
                registers.WriteWord(Ddfstop, 0x00D1);
                system.Agnus.Bitplanes.RefreshFromRegisters();
                system.Agnus.Bitplanes.ObservePresentationWindowForBeamLine(panelSourceY);

                MethodInfo copy = typeof(AmigaSystem).GetMethod("CopyLineAdaptivePresentationWindow", BindingFlags.Instance | BindingFlags.NonPublic);
                Check.True(copy != null, "Line-adaptive presentation copy method should exist.");
                copy.Invoke(system, new object[] { sourceLeft, sourceTop, sourceWidth, sourceHeight });

                // The same hardware X must stay fixed when the pointer crosses the panel seam.
                const int spriteSourceX = 400;
                const uint cursor = 0xFFFFFFFFu;
                const uint behindCursor = 0xFF445566u;
                foreach (int sourceY in new[] { topSourceY, panelSourceY })
                {
                    int index = sourceY * system.FrameBuffer.Width + spriteSourceX;
                    system.FrameBuffer.Pixels[index] = cursor;
                    system.FrameBuffer.RecordSpritePixel(index, behindCursor, cursor);
                }
                copy.Invoke(system, new object[] { sourceLeft, sourceTop, sourceWidth, sourceHeight });
                int topCursorX = FindColorInPresentedRow(system, topTargetY, cursor);
                int panelCursorX = FindColorInPresentedRow(system, panelTargetY, cursor);
                Check.Equal(306, topCursorX, "The sprite uses the frame aperture origin at raw X94.");
                Check.Equal(topCursorX, panelCursorX, "A hardware sprite must not jump sideways at the menu seam.");
                Check.Equal(behindCursor, system.PresentedFrameBuffer.Pixels[panelTargetY * 704 + 290], "The old panel-relative cursor location must reveal its background without a duplicate pointer.");

                int topX = FindColorInPresentedRow(system, topTargetY, topMarker);
                int panelX = FindLastColorInPresentedRow(system, panelTargetY, panelMarker);
                Check.True(topX >= 0, "Top marker should be visible after presentation copy.");
                Check.True(panelX >= 0, "Bottom-panel marker should be visible after presentation copy.");
                Check.True(Math.Abs(topX - panelX) <= 1, "The widened panel right edge must stay registered with the monitor edge. top=" + topX + " panel=" + panelX);
            }
        }

        /// <summary>
        /// Verifies that Siedler-style oversized bottom panels keep their widened left content edge visible.
        /// </summary>
        private static void TestOversizedBottomPanelIncludesWidenedLeftContentEdge()
        {
            using (var system = new AmigaSystem(AmigaModel.A500Pal, AmigaAccuracyOptions.Fast.Clone()))
            {
                const int sourceLeft = 92;
                const int sourceTop = 44;
                const int sourceWidth = 722;
                const int sourceHeight = 288;
                const int panelLeftSourceX = 110;
                const int panelRightSourceX = 813;
                const int panelTargetY = 236;
                const uint leftMarker = 0xFF44AAFFu;
                const uint rightMarker = 0xFFFFAA44u;
                int panelSourceY = sourceTop + panelTargetY;

                system.FrameBuffer.Clear(0xFF000000u);
                system.PresentedFrameBuffer.Clear(0xFF000000u);
                system.FrameBuffer.Pixels[(panelSourceY * system.FrameBuffer.Width) + panelLeftSourceX] = leftMarker;
                system.FrameBuffer.Pixels[(panelSourceY * system.FrameBuffer.Width) + panelRightSourceX] = rightMarker;

                CustomRegisterMap registers = system.Agnus.CustomRegisters;
                registers.WriteWord(Dmacon, 0x8300);
                registers.WriteWord(Bplcon1, 0x00FF);
                registers.WriteWord(Diwstrt, 0x1871);
                registers.WriteWord(Bplcon0, 0x4200);
                registers.WriteWord(Diwstop, 0x31D1);
                registers.WriteWord(Ddfstrt, 0x002C);
                registers.WriteWord(Ddfstop, 0x00D1);
                system.Agnus.Bitplanes.RefreshFromRegisters();
                system.Agnus.Bitplanes.ObservePresentationWindowForBeamLine(panelSourceY);

                MethodInfo copy = typeof(AmigaSystem).GetMethod("CopyLineAdaptivePresentationWindow", BindingFlags.Instance | BindingFlags.NonPublic);
                Check.True(copy != null, "Line-adaptive presentation copy method should exist.");
                copy.Invoke(system, new object[] { sourceLeft, sourceTop, sourceWidth, sourceHeight });

                int leftX = FindColorInPresentedRow(system, panelTargetY, leftMarker);
                int rightX = FindLastColorInPresentedRow(system, panelTargetY, rightMarker);
                Check.True(leftX >= 0 && leftX <= 1, "The bottom panel's widened left content edge should begin at the monitor edge. left=" + leftX);
                Check.True(rightX >= system.PresentedFrameBuffer.Width - 2, "The widened bottom panel should keep its right edge visible. right=" + rightX);
            }
        }

        /// <summary>
        /// Verifies that a widened DDF line renders pixels past the final normal beam sample.
        /// </summary>
        private static void TestLineTailRendersFinalWidenedDdfWord()
        {
            var registers = new CustomRegisterMap();
            ushort[] memory = new ushort[0x1000];
            const int panelBeamY = BitplaneUnit.PalVisibleTop + 236;
            const int frameBufferXOffset = 128;
            const int tailFrameX = 813;
            const uint expectedColor = 0xFFFFFFFFu;
            var bitplanes = new BitplaneUnit(registers, address => memory[(address >> 1) & (memory.Length - 1)]);
            var sprites = new SpriteUnit(registers, address => 0);
            var frameBuffer = new AmigaFrameBuffer(896, 312);
            var denise = new Denise(frameBuffer, bitplanes, sprites, null, frameBufferXOffset, 0);

            registers.WriteWord(Dmacon, 0x8300);
            registers.WriteWord(Bplcon0, 0x4200);
            registers.WriteWord(Bplcon1, 0x00FF);
            registers.WriteWord(Diwstrt, 0x1871);
            registers.WriteWord(Diwstop, 0x31D1);
            registers.WriteWord(Ddfstrt, 0x002C);
            registers.WriteWord(Ddfstop, 0x00D1);
            registers.WriteWord(0x180 + (15u * 2u), 0x0FFF);
            for (int plane = 0; plane < 4; plane++)
            {
                ushort pointer = (ushort)(plane * 0x100);
                registers.WriteWord((uint)(Bpl1pth + (plane * 4)), 0x0000);
                registers.WriteWord((uint)(Bpl1ptl + (plane * 4)), pointer);
                memory[(pointer >> 1) + 21] = 0x0001;
            }

            frameBuffer.Clear(0xFF000000u);
            bitplanes.RefreshFromRegisters();
            bitplanes.ObservePresentationWindowForBeamLine(panelBeamY);
            for (int beamX = 0; beamX < AmigaModel.A500Pal.ChipCyclesPerLine; beamX++)
            {
                if (bitplanes.IsPlaneFetchSlot(beamX, panelBeamY))
                {
                    bitplanes.FetchDmaPlaneWord(beamX, panelBeamY);
                }
            }

            int row = (panelBeamY - BitplaneUnit.PalVisibleTop) * frameBuffer.Width;
            Check.Equal(0xFF000000u, frameBuffer.Pixels[row + tailFrameX], "The tail pixel starts as untouched background.");
            denise.FinishLineTail(new BeamTraceEntry { BeamX = AmigaModel.A500Pal.ChipCyclesPerLine - 1, BeamY = panelBeamY, Frame = 1 });
            Check.Equal(expectedColor, frameBuffer.Pixels[row + tailFrameX], "The final widened DDF word should render beyond the last normal beam pixel.");
        }

        /// <summary>
        /// Verifies that COLOR00 border pixels are rendered past the final normal beam sample.
        /// </summary>
        private static void TestLineTailRendersRightBorderColor()
        {
            var registers = new CustomRegisterMap();
            const int frameBufferXOffset = 128;
            const int beamY = BitplaneUnit.PalVisibleTop + 64;
            const int borderFrameX = 813;
            const uint expectedColor = 0xFFFFFFFFu;
            var bitplanes = new BitplaneUnit(registers, address => 0);
            var sprites = new SpriteUnit(registers, address => 0);
            var frameBuffer = new AmigaFrameBuffer(896, 312);
            var denise = new Denise(frameBuffer, bitplanes, sprites, null, frameBufferXOffset, 0);

            registers.WriteWord(Dmacon, 0x8300);
            registers.WriteWord(Bplcon0, 0x6200);
            registers.WriteWord(Bplcon1, 0x00FF);
            registers.WriteWord(Diwstrt, 0x1871);
            registers.WriteWord(Diwstop, 0x10D1);
            registers.WriteWord(Ddfstrt, 0x0030);
            registers.WriteWord(Ddfstop, 0x00C8);
            registers.WriteWord(Color00, 0x0FFF);

            frameBuffer.Clear(0xFF000000u);
            bitplanes.RefreshFromRegisters();
            bitplanes.ObservePresentationWindowForBeamLine(beamY);

            int row = (beamY - BitplaneUnit.PalVisibleTop) * frameBuffer.Width;
            Check.Equal(0xFF000000u, frameBuffer.Pixels[row + borderFrameX], "The right border tail pixel starts as untouched background.");
            denise.FinishLineTail(new BeamTraceEntry { BeamX = AmigaModel.A500Pal.ChipCyclesPerLine - 1, BeamY = beamY, Frame = 1 });
            Check.Equal(expectedColor, frameBuffer.Pixels[row + borderFrameX], "COLOR00 should fill the right border tail beyond the final normal beam pixel.");
        }

        /// <summary>
        /// Verifies that a widened DDF line renders pixels before the normal beam aperture.
        /// </summary>
        private static void TestLineLeadInRendersWidenedDdfLeftEdge()
        {
            var registers = new CustomRegisterMap();
            ushort[] memory = new ushort[0x1000];
            const int panelBeamY = BitplaneUnit.PalVisibleTop + 236;
            const int frameBufferXOffset = 128;
            const int leadFrameX = 110;
            const uint expectedColor = 0xFFFFFFFFu;
            var bitplanes = new BitplaneUnit(registers, address => memory[(address >> 1) & (memory.Length - 1)]);
            var sprites = new SpriteUnit(registers, address => 0);
            var frameBuffer = new AmigaFrameBuffer(896, 312);
            var denise = new Denise(frameBuffer, bitplanes, sprites, null, frameBufferXOffset, 0);

            registers.WriteWord(Dmacon, 0x8300);
            registers.WriteWord(Bplcon0, 0x4200);
            registers.WriteWord(Bplcon1, 0x00FF);
            registers.WriteWord(Diwstrt, 0x1871);
            registers.WriteWord(Diwstop, 0x31D1);
            registers.WriteWord(Ddfstrt, 0x002C);
            registers.WriteWord(Ddfstop, 0x00D1);
            registers.WriteWord(0x180 + (15u * 2u), 0x0FFF);
            for (int plane = 0; plane < 4; plane++)
            {
                ushort pointer = (ushort)(plane * 0x100);
                registers.WriteWord((uint)(Bpl1pth + (plane * 4)), 0x0000);
                registers.WriteWord((uint)(Bpl1ptl + (plane * 4)), pointer);
                memory[pointer >> 1] = 0x8000;
            }

            frameBuffer.Clear(0xFF000000u);
            bitplanes.RefreshFromRegisters();
            bitplanes.ObservePresentationWindowForBeamLine(panelBeamY);
            for (int beamX = 0; beamX < AmigaModel.A500Pal.ChipCyclesPerLine; beamX++)
            {
                if (bitplanes.IsPlaneFetchSlot(beamX, panelBeamY))
                {
                    bitplanes.FetchDmaPlaneWord(beamX, panelBeamY);
                }
            }

            int row = (panelBeamY - BitplaneUnit.PalVisibleTop) * frameBuffer.Width;
            Check.Equal(0xFF000000u, frameBuffer.Pixels[row + leadFrameX], "The left lead-in pixel starts as untouched background.");
            denise.FinishLineTail(new BeamTraceEntry { BeamX = AmigaModel.A500Pal.ChipCyclesPerLine - 1, BeamY = panelBeamY, Frame = 1 });
            Check.Equal(expectedColor, frameBuffer.Pixels[row + leadFrameX], "The widened DDF left edge should render before the normal beam aperture.");
        }

        /// <summary>
        /// Finds one ARGB color in a presented framebuffer row.
        /// </summary>
        private static int FindColorInPresentedRow(AmigaSystem system, int y, uint color)
        {
            int row = y * system.PresentedFrameBuffer.Width;
            for (int x = 0; x < system.PresentedFrameBuffer.Width; x++)
            {
                if (system.PresentedFrameBuffer.Pixels[row + x] == color)
                {
                    return x;
                }
            }

            return -1;
        }

        /// <summary>
        /// Finds the last occurrence of one ARGB color in a presented framebuffer row.
        /// </summary>
        private static int FindLastColorInPresentedRow(AmigaSystem system, int y, uint color)
        {
            int row = y * system.PresentedFrameBuffer.Width;
            for (int x = system.PresentedFrameBuffer.Width - 1; x >= 0; x--)
            {
                if (system.PresentedFrameBuffer.Pixels[row + x] == color)
                {
                    return x;
                }
            }

            return -1;
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

            Check.Equal(1, bitplanes.RenderColorIndex(642, BitplaneUnit.PalVisibleTop), "The final fetched DDF word must reach the rightmost lowres pixel pair after the fetch lead-in.");
            Check.Equal(1, bitplanes.RenderColorIndex(643, BitplaneUnit.PalVisibleTop), "The duplicated lowres pixel must also reach the final hires pixel after the fetch lead-in.");
        }

        /// <summary>
        /// Verifies that 4-plane lowres DMA advances by the standard 20-word DDF span.
        /// </summary>
        private static void TestFourPlaneLowresDdfAdvancesByVisibleFetchSpan()
        {
            var registers = new CustomRegisterMap();
            ushort[] memory = new ushort[0x400];
            memory[0] = 0xFFFF;
            memory[20] = 0x0000;
            memory[40] = 0xFFFF;
            var bitplanes = new BitplaneUnit(registers, address => memory[(address >> 1) & 0x3FF]);
            registers.WriteWord(Bplcon0, 0x4000);
            registers.WriteWord(Dmacon, 0x8300);
            registers.WriteWord(Diwstrt, 0x2C81);
            registers.WriteWord(Diwstop, 0x2EC1);
            registers.WriteWord(Ddfstrt, 0x0038);
            registers.WriteWord(Ddfstop, 0x00D0);
            for (int plane = 0; plane < 4; plane++)
            {
                registers.WriteWord((uint)(Bpl1pth + (plane * 4)), 0x0000);
                registers.WriteWord((uint)(Bpl1ptl + (plane * 4)), (ushort)(plane * 0x100));
            }

            for (int beamX = 0x38; beamX < 0xD8; beamX++)
            {
                if (bitplanes.IsPlaneFetchSlot(beamX, BitplaneUnit.PalVisibleTop))
                {
                    bitplanes.FetchDmaPlaneWord(beamX, BitplaneUnit.PalVisibleTop);
                }
            }

            Check.Equal(1, bitplanes.RenderColorIndex(4, BitplaneUnit.PalVisibleTop), "The first line should render the first visible word after the lowres fetch lead-in.");

            for (int beamX = 0x38; beamX < 0xD8; beamX++)
            {
                if (bitplanes.IsPlaneFetchSlot(beamX, BitplaneUnit.PalVisibleTop + 1))
                {
                    bitplanes.FetchDmaPlaneWord(beamX, BitplaneUnit.PalVisibleTop + 1);
                }
            }

            Check.Equal(0, bitplanes.RenderColorIndex(4, BitplaneUnit.PalVisibleTop + 1), "The second line should start after the 20-word standard lowres fetch span, not skip ahead to word 40.");
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

            Check.Equal(1, bitplanes.RenderColorIndex(4, 0xBD), "The first active DMA line must fetch from BPL1PT, not from an offset for skipped DIW rows.");
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

            bitplanes.FetchDmaPlaneWord(0x3B, 0x30);
            Check.Equal(1, bitplanes.RenderColorIndex(4, 0x30), "The first fetched line should render the first memory word after the OCS lowres fetch lead-in.");

            registers.WriteWord(Diwstrt, 0x2081);
            registers.WriteWord(Diwstop, 0x5081);
            bitplanes.FetchDmaPlaneWord(0x3B, 0x31);

            Check.Equal(0, bitplanes.RenderColorIndex(4, 0x31), "The second physical beam line must not refetch the first word after DIWSTRT changes.");
            Check.Equal(1, bitplanes.RenderColorIndex(6, 0x31), "The second physical beam line should continue to the next DMA word.");
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

            Check.Equal(1, bitplanes.RenderColorIndex(4, BitplaneUnit.PalVisibleTop), "The first post-lead-in visible pixel should show the word fetched after the offscreen DDF lead-in.");
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

            Check.Equal(-28, bitplanes.PresentationSourceLeft, "Presentation should still preserve the left overscan source while accounting for the OCS lowres fetch lead-in.");
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
            TestPlaneCountIncreaseReloadsSplitPointers();
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
        /// Verifies that a plane-count increase completes a raster split's pointer reload.
        /// </summary>
        private static void TestPlaneCountIncreaseReloadsSplitPointers()
        {
            TestCompactGroupPlaneCountIncreasePreservesFetchPositions();
            var registers = new CustomRegisterMap();
            ushort[] memory = new ushort[0x100];
            memory[0x40] = memory[0x42] = 0xFFFF;
            var bitplanes = new BitplaneUnit(registers, address => memory[(address >> 1) % memory.Length]);
            registers.WriteWord(Bplcon0, 0x1000);
            registers.WriteWord(Dmacon, 0x8300);
            registers.WriteWord(Diwstrt, 0x2C81);
            registers.WriteWord(Diwstop, 0x30C1);
            registers.WriteWord(Ddfstrt, 0x0038);
            registers.WriteWord(Ddfstop, 0x0038);
            bitplanes.FetchDmaWords(0x38, BitplaneUnit.PalVisibleTop);
            registers.WriteWord(Bpl1ptl, 0x0080);
            registers.WriteWord(Bpl2ptl, 0x0082);
            bitplanes.FetchDmaWords(0x38, BitplaneUnit.PalVisibleTop + 1);
            registers.WriteWord(Bpl2ptl, 0x0084);
            registers.WriteWord(Bplcon0, 0x2000);
            bitplanes.RefreshFromRegisters();
            bitplanes.FetchDmaWords(0x38, BitplaneUnit.PalVisibleTop + 2);
            Check.Equal(3, bitplanes.RenderColorIndex(4, BitplaneUnit.PalVisibleTop + 2),
                "A raster split enabling another plane must reload its completed pointer set on the next line.");
        }

        private static void TestRevisitedDmaRowsInvalidateWords()
        {
            var registers = new CustomRegisterMap();
            ushort data = 0xFFFF;
            var bitplanes = new BitplaneUnit(registers, address => data);
            registers.WriteWord(Bplcon0, 0x1000);
            registers.WriteWord(Dmacon, 0x8300);
            registers.WriteWord(Diwstrt, 0x2C81);
            registers.WriteWord(Diwstop, 0x3081);
            registers.WriteWord(Ddfstrt, 0x0038);
            registers.WriteWord(Ddfstop, 0x0038);
            Fetch(BitplaneUnit.PalVisibleTop);
            Fetch(BitplaneUnit.PalVisibleTop + 1);
            Check.Equal(1, bitplanes.RenderColorIndex(4, BitplaneUnit.PalVisibleTop + 1), "Initial row must contain its fetched data.");
            data = 0;
            Fetch(BitplaneUnit.PalVisibleTop);
            Fetch(BitplaneUnit.PalVisibleTop + 1);
            Check.Equal(0, bitplanes.RenderColorIndex(4, BitplaneUnit.PalVisibleTop + 1), "Returning forward after a backward fetch must not reuse the previous row's cached words.");
            void Fetch(int row)
            {
                for (int x = 0x38; x < 0x40; x++)
                    bitplanes.FetchDmaPlaneWord(x, row);
            }
        }

        private static void TestCompactGroupPlaneCountIncreasePreservesFetchPositions()
        {
            var registers = new CustomRegisterMap();
            var reads = new List<uint>();
            var bitplanes = new BitplaneUnit(registers, address => { reads.Add(address); return 0xFFFF; });
            registers.WriteWord(Bplcon0, 0x3000);
            registers.WriteWord(Dmacon, 0x8300);
            registers.WriteWord(Diwstrt, 0x2C81);
            registers.WriteWord(Diwstop, 0x30C1);
            registers.WriteWord(Ddfstrt, 0x0030);
            registers.WriteWord(Ddfstop, 0x0040);
            for (int plane = 0; plane < 6; plane++)
                registers.WriteWord((uint)(Bpl1ptl + plane * 4), (ushort)(plane * 0x100));
            for (int x = 0x30; x < 0x3C; x++)
                bitplanes.FetchDmaPlaneWord(x, BitplaneUnit.PalVisibleTop);
            registers.WriteWord(Bplcon0, 0x6000);
            for (int x = 0x3C; x < 0x48; x++)
                bitplanes.FetchDmaPlaneWord(x, BitplaneUnit.PalVisibleTop);
            for (int plane = 0; plane < 3; plane++)
                Check.Equal(1, reads.Count(address => address == plane * 0x100u + 2),
                    "A compact word already latched before the split must not be fetched again.");
            for (int plane = 0; plane < 6; plane++)
                Check.True(reads.Contains((uint)(plane * 0x100 + 4)),
                    "Every plane must fetch the matching word position after the split.");
            Check.Equal(63, bitplanes.RenderColorIndex(40, BitplaneUnit.PalVisibleTop),
                "The first complete group following the split must contain all six aligned planes.");
        }

        /// <summary>Verifies that wide DDF fetches preserve the right overscan content.</summary>
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

            Check.Equal(-28, bitplanes.PresentationSourceLeft, "A $30 DDF start begins 32 hires pixels left of the standard PAL presentation edge plus the OCS lowres fetch lead-in.");
            Check.Equal(704, bitplanes.PresentationSourceWidth, "A $30..$D8 lowres fetch should preserve the complete overscan source instead of subtracting the left lead-in.");

            registers.WriteWord(Ddfstop, 0x00C8);
            Check.Equal(668, bitplanes.PresentationSourceWidth, "A $30..$C8 fetch must still include the standard right PAL edge when left overscan is visible.");
        }

        /// <summary>
        /// Verifies that a large PAL overscan window is preserved in the fixed monitor viewport.
        /// </summary>
        private static void TestWidePalOverscanUsesFixedMonitorViewport()
        {
            using (var system = new AmigaSystem(AmigaModel.A500Pal, AmigaAccuracyOptions.Fast.Clone()))
            {
                CustomRegisterMap registers = system.Agnus.CustomRegisters;
                registers.WriteWord(Bplcon0, 0x0200);
                registers.WriteWord(Dmacon, 0x8300);
                registers.WriteWord(Diwstrt, 0x1871);
                registers.WriteWord(Diwstop, 0x31D1);
                registers.WriteWord(Ddfstrt, 0x002C);
                registers.WriteWord(Ddfstop, 0x00D1);

                string info = system.GetPresentationDebugInfo();
                Check.True(info.IndexOf("presented:704x288", StringComparison.Ordinal) >= 0, "PAL monitor presentation should use the fixed overscan viewport. " + info);
                Check.True(info.IndexOf("source:92,21+704x288", StringComparison.Ordinal) >= 0, "Wide PAL overscan should preserve both side-frame edges in a stable full-height monitor aperture. " + info);
            }
        }

        /// <summary>
        /// Verifies that normal PAL hires fetches are not treated as horizontal overscan.
        /// </summary>
        private static void TestStandardHiresDdfReportsStablePresentationWidth()
        {
            var registers = new CustomRegisterMap();
            var bitplanes = new BitplaneUnit(registers, null);
            registers.WriteWord(Bplcon0, 0xA000);
            registers.WriteWord(Dmacon, 0x8300);
            registers.WriteWord(Diwstrt, 0x2C81);
            registers.WriteWord(Diwstop, 0x2DC1);
            registers.WriteWord(Ddfstrt, 0x003C);
            registers.WriteWord(Ddfstop, 0x00D4);

            Check.Equal(4, bitplanes.PresentationSourceLeft, "A standard hires DDF start includes the DMA-to-shifter lead-in.");
            Check.Equal(640, bitplanes.PresentationSourceWidth, "A standard $3C..$D4 hires fetch should map directly to the stable PAL presentation width.");
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
        /// Verifies that tiny vertical overscan differences keep the register-selected aperture.
        /// </summary>
        private static void TestSmallVerticalOverscanCropsInsteadOfScaling()
        {
            using (var system = new AmigaSystem(AmigaModel.A500Pal, AmigaAccuracyOptions.Fast.Clone()))
            {
                CustomRegisterMap registers = system.Agnus.CustomRegisters;
                registers.WriteWord(Bplcon0, 0x2200);
                registers.WriteWord(Dmacon, 0x8300);
                registers.WriteWord(Diwstrt, 0x2B8E);
                registers.WriteWord(Diwstop, 0x2DB3);
                registers.WriteWord(Ddfstrt, 0x0038);
                registers.WriteWord(Ddfstop, 0x00D0);

                string info = system.GetPresentationDebugInfo();
                Check.True(info.IndexOf("source:132,44+640x256", StringComparison.Ordinal) >= 0, "Two-line vertical overscan should keep the game aperture instead of adding monitor border rows. " + info);
            }
        }

        /// <summary>
        /// Verifies that black rows in a normal-height PAL title screen are preserved as artwork/background.
        /// </summary>
        private static void TestStandardPalBlackBackgroundIsNotLetterboxCropped()
        {
            using (var system = new AmigaSystem(AmigaModel.A500Pal, AmigaAccuracyOptions.Fast.Clone()))
            {
                CustomRegisterMap registers = system.Agnus.CustomRegisters;
                registers.WriteWord(Bplcon0, 0x5200);
                registers.WriteWord(Dmacon, 0x8300);
                registers.WriteWord(Diwstrt, 0x2C81);
                registers.WriteWord(Diwstop, 0xF4C1);
                registers.WriteWord(Ddfstrt, 0x0038);
                registers.WriteWord(Ddfstop, 0x00D0);

                for (int y = 100; y < 200; y++)
                {
                    for (int x = 160; x < 736; x++)
                    {
                        system.FrameBuffer.Pixels[(y * system.FrameBuffer.Width) + x] = 0xFFFFFFFFu;
                    }
                }

                string info = system.GetPresentationDebugInfo();
                Check.True(info.IndexOf("source:132,44+640x256", StringComparison.Ordinal) >= 0, "Normal-height PAL title screens should present the register-selected game aperture without adding host letterbox rows. " + info);
            }
        }

        /// <summary>
        /// Verifies that normal-width vertical overscan is not cropped based on framebuffer contents.
        /// </summary>
        private static void TestPalVerticalOverscanKeepsRegisterSelectedSourceWindowStable()
        {
            using (var system = new AmigaSystem(AmigaModel.A500Pal, AmigaAccuracyOptions.Fast.Clone()))
            {
                CustomRegisterMap registers = system.Agnus.CustomRegisters;
                registers.WriteWord(Bplcon0, 0x4302);
                registers.WriteWord(Dmacon, 0x8300);
                registers.WriteWord(Diwstrt, 0x0581);
                registers.WriteWord(Diwstop, 0x40C1);
                registers.WriteWord(Ddfstrt, 0x0038);
                registers.WriteWord(Ddfstop, 0x00D0);
                system.FrameBuffer.Clear(0xFF000000u);

                for (int y = 100; y < 170; y++)
                {
                    for (int x = 256; x < 640; x++)
                    {
                        system.FrameBuffer.Pixels[(y * system.FrameBuffer.Width) + x] = 0xFFFFFFFFu;
                    }
                }

                string info = system.GetPresentationDebugInfo();
                Check.True(info.IndexOf("source:132,14+640x288", StringComparison.Ordinal) >= 0, "Moderate normal-width PAL overscan should keep register-selected vertical geometry without content-based side trimming. " + info);
            }
        }

        /// <summary>
        /// Verifies that scene brightness cannot change the register-selected aperture.
        /// </summary>
        private static void TestPalApertureIgnoresSceneBrightness()
        {
            using (var system = new AmigaSystem(AmigaModel.A500Pal, AmigaAccuracyOptions.Fast.Clone()))
            {
                CustomRegisterMap registers = system.Agnus.CustomRegisters;
                registers.WriteWord(Bplcon0, 0x4302);
                registers.WriteWord(Dmacon, 0x8300);
                registers.WriteWord(Diwstrt, 0x0581);
                registers.WriteWord(Diwstop, 0x40C1);
                registers.WriteWord(Ddfstrt, 0x0038);
                registers.WriteWord(Ddfstop, 0x00D0);
                system.FrameBuffer.Clear(0xFFFFFFFFu);
                string brightGeometry = system.GetPresentationDebugInfo();
                for (int y = 0; y < 38; y++)
                {
                    Array.Fill(system.FrameBuffer.Pixels, 0xFF000000u, y * system.FrameBuffer.Width, system.FrameBuffer.Width);
                }
                Check.Equal(brightGeometry, system.GetPresentationDebugInfo(), "Black scene rows must not change the register-selected aperture.");
            }
        }

        private static void TestPalPresentationPreservesDarkEdges()
        {
            using (var system = new AmigaSystem(AmigaModel.A500Pal, AmigaAccuracyOptions.Fast.Clone()))
            {
                const int left = 132;
                const int top = 14;
                const int width = 640;
                const int height = 288;
                MethodInfo copy = typeof(AmigaSystem).GetMethod("CopyPresentationWindow", BindingFlags.Instance | BindingFlags.NonPublic);
                Check.True(copy != null, "Presentation copy method should exist.");
                var expected = new AmigaFrameBuffer(704, 288);
                for (int edge = 0; edge < 3; edge++)
                {
                    system.FrameBuffer.Clear(0xFF334455u);
                    for (int y = top; y < top + height; y++)
                    {
                        for (int x = left; x < left + width; x++)
                        {
                            if ((edge == 0 && x < left + 32) ||
                                (edge == 1 && x >= left + width - 32) ||
                                (edge == 2 && y >= top + height - 16))
                            {
                                system.FrameBuffer.Pixels[y * system.FrameBuffer.Width + x] = 0xFF000000u;
                            }
                        }
                    }
                    expected.CopyScaledWindowFrom(system.FrameBuffer, left, top, width, height);
                    copy.Invoke(system, new object[] { left, top, width, height });
                    Check.True(expected.Pixels.SequenceEqual(system.PresentedFrameBuffer.Pixels), "Dark scene edge " + edge + " must retain its width and position.");
                }
            }
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
            Check.Equal(1, bitplanes.RenderColorIndex(4, BitplaneUnit.PalVisibleTop), "The first frame should render the fetched bit after the OCS lowres fetch lead-in.");

            memory[0] = 0x0000;
            bitplanes.BeginFrame();
            Check.Equal(0, bitplanes.RenderColorIndex(4, BitplaneUnit.PalVisibleTop), "Before the next frame fetches, previous-frame row data must be invalid.");

            bitplanes.FetchDmaWords(0x38, BitplaneUnit.PalVisibleTop);
            Check.Equal(0, bitplanes.RenderColorIndex(4, BitplaneUnit.PalVisibleTop), "After the next fetch, the new frame should render the updated word.");
        }

        /// <summary>
        /// Exercises the end-of-frame path with sparse and dense scene artwork near the top edge.
        /// </summary>
        private static void TestTopArtworkIsPreserved(int artworkWidth, int artworkHeight)
        {
            using (var system = new AmigaSystem(AmigaModel.A500Pal, AmigaAccuracyOptions.Fast.Clone()))
            {
                int frameCycles = system.Model.ChipCyclesPerLine * system.Model.LinesPerFrame;
                system.RunChipCycles(frameCycles - 1);
                system.FrameBuffer.Clear(0xFF000000u);
                MethodInfo aperture = typeof(AmigaSystem).GetMethod("GetPresentationSourceWindow", BindingFlags.Instance | BindingFlags.NonPublic);
                Check.True(aperture != null, "Register-selected aperture method should exist.");
                object[] geometry = { 0, 0, 0, 0 };
                aperture.Invoke(system, geometry);
                int left = (int)geometry[0];
                int top = (int)geometry[1];
                int width = (int)geometry[2];
                int height = (int)geometry[3];
                for (int y = top + 4; y < top + 4 + artworkHeight; y++)
                {
                    for (int x = left + 16; x < left + 16 + artworkWidth; x++)
                    {
                        system.FrameBuffer.SetPixel(x, y, 0xFF4422EEu);
                    }
                }
                var expected = new AmigaFrameBuffer(704, 288);
                expected.CopyScaledWindowFrom(system.FrameBuffer, left, top, width, height);
                int expectedPixels = expected.Pixels.Count(color => color == 0xFF4422EEu);
                Check.True(expectedPixels > 0, "Top artwork must be inside the aperture.");
                system.RunChipCycles(1);
                Check.Equal(expectedPixels, system.PresentedFrameBuffer.Pixels.Count(color => color == 0xFF4422EEu), "End-of-frame presentation must preserve all top artwork pixels.");
            }
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
        private static void TestHiresBeamOrderedDma()
        {
            var registers = new CustomRegisterMap();
            var bitplanes = new BitplaneUnit(registers, address => address < 512 ? (ushort)0xFFFF : (ushort)0);
            var frame = new AmigaFrameBuffer(704, 256);
            var denise = new Denise(frame, bitplanes, new SpriteUnit(registers, null));
            registers.WriteWord(Bplcon0, 0xA000);
            registers.WriteWord(Dmacon, 0x8300);
            registers.WriteWord(Diwstrt, 0x2C00);
            registers.WriteWord(Diwstop, 0x2DFF);
            registers.WriteWord(Ddfstrt, 0x003C);
            registers.WriteWord(Ddfstop, 0x00D4);
            registers.WriteWord(Bpl2ptl, 512);
            registers.WriteWord(Color01, 0x0FFF);
            for (int x = 0; x < 227; x++)
            {
                if (bitplanes.IsPlaneFetchSlot(x, 44)) bitplanes.FetchDmaPlaneWord(x, 44);
                denise.Tick(new BeamTraceEntry { BeamX = x, BeamY = 44 });
            }
            for (int x = 4; x < 644; x++)
                Check.Equal(0xFFFFFFFFu, frame.Pixels[x], "Hires DMA must not lose the leading bits of successive words at X=" + x);
        }

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
            registers.WriteWord(Ddfstrt, 0x003C);
            registers.WriteWord(Ddfstop, 0x003C);
            registers.WriteWord(Bpl1pth, 0x0000);
            registers.WriteWord(Bpl1ptl, 0x0000);

            bitplanes.FetchDmaWords(0x3C, BitplaneUnit.PalVisibleTop);

            Check.Equal(1, bitplanes.RenderColorIndex(4, BitplaneUnit.PalVisibleTop), "HIRES bit 15 should affect only the first hires pixel.");
            Check.Equal(0, bitplanes.RenderColorIndex(5, BitplaneUnit.PalVisibleTop), "HIRES must not duplicate the first bit into the second pixel.");
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

            Check.Equal(1, bitplanes.RenderColorIndex(643, BitplaneUnit.PalVisibleTop), "The final word of a $3C..$D0 two-plane hires fetch must not be dropped.");
        }

        /// <summary>
        /// Verifies HAM6 base colors and component modify operations.
        /// </summary>
        private static void TestHamIgnoresSpriteColor()
        {
            foreach (int coveredPixels in new[] { 1, 2 })
            {
                var registers = new CustomRegisterMap();
                var bitplanes = new BitplaneUnit(registers, null);
                var frame = new AmigaFrameBuffer(640, 256);
                var sprites = new SpriteUnit(registers, null);
                var denise = new Denise(frame, bitplanes, sprites);
                registers.WriteWord(Bplcon0, 0x6800);
                registers.WriteWord(Bplcon2, 0x0038);
                registers.WriteWord(Color01, 0x0F00);
                registers.WriteWord(0x1A2, 0x00F0);
                for (int plane = 0; plane < 6; plane++)
                {
                    ushort bits = 0;
                    for (int pixel = 0; pixel < 4; pixel++)
                    {
                        int code = pixel < coveredPixels ? 1 : 0x1A;
                        bits |= (ushort)(((code >> plane) & 1) << (15 - pixel));
                    }
                    bitplanes.NotifyCustomRegisterWordWrite(0x110u + (uint)plane * 2, bits);
                }
                registers.WriteWord(0x140, 0x2C40);
                registers.WriteWord(0x142, 0x2D00);
                registers.WriteWord(0x144, coveredPixels == 1 ? (ushort)0x8000 : (ushort)0xC000);
                denise.Tick(new BeamTraceEntry { BeamX = 0x3E, BeamY = 44 });
                denise.Tick(new BeamTraceEntry { BeamX = 0x3F, BeamY = 44 });
                Check.Equal(0xFF00FF00u, frame.Pixels[0], "The sprite must cover the red palette pixel.");
                Check.Equal(0xFFFF00AAu, frame.Pixels[coveredPixels * 2], "HAM blue modification must retain playfield red, independently of the green sprite.");
            }
        }

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
        private static void TestCollisionPlayfieldGroups()
        {
            var collisions = new CollisionUnit { Control = 0x00C1 };
            collisions.ObservePixel(0x03, 0x01);
            Check.Equal(0x0002, collisions.ReadAndClear(), "BPL2 mismatch must not prevent the matched odd playfield from colliding with sprite 0.");
            collisions.ObservePixel(0x01, 0x01);
            Check.Equal(0x0023, collisions.ReadAndClear(), "BPL2 zero must satisfy its zero-match collision while BPL1 satisfies the odd playfield match.");
        }

        private static void TestCollisionDisabledPlanes()
        {
            var collisions = new CollisionUnit { Control = 0x003F };
            collisions.ObservePixel(0, 0);
            Check.Equal(0x0001, collisions.ReadAndClear(), "Disabled bitplanes must always match, even on zero pixels and with nonzero match values.");
            collisions.Control = 0xF000;
            collisions.ObservePixel(0, 0xFF);
            Check.True(collisions.IsSaturated, "All included sprite pairs and disabled planes must latch all collision bits.");
            Check.Equal(0x7FFF, collisions.ReadAndClear(), "Bit 15 is unused; all remaining collision bits should be set.");
            Check.True(!collisions.IsSaturated, "Reading CLXDAT must re-enable collision observation.");
            collisions.ObservePixel(0, 0x01);
            Check.Equal(0x0023, collisions.ReadAndClear(), "After read-clear, sprite 0 must collide with both unmasked playfields again.");
        }

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
                "sprite POS reuses armed data and CTL disarms until DATA is written",
                "Amiga HRM Sprite Hardware Details horizontal comparator; Celtic Heart Copper reuse",
                TestSpritePositionWriteReusesArmedData);
            yield return new ChipTestCase("Denise/Sprite", "horizontal reuse cannot start DMA after a missed VSTART",
                "Amiga HRM sprite DMA vertical comparator; idle Copper-fed parallax channel", TestSpriteReuseDoesNotStartIdleDma);
            yield return new ChipTestCase("Denise/Sprite", "attachment selects pair palette with an inactive parked odd sprite",
                "Amiga HRM SPRxCTL ATT; Night Owl transparent attachment dummy", TestSpriteAttachmentWithParkedOddSprite);
            yield return new ChipTestCase(
                "Denise/Sprite",
                "sprite presentation aperture preserves the full latched word",
                "SPRxPOS/CTL and the 16 lowres pixel latch determine sprite geometry",
                TestSpritePresentationWindow);
            yield return new ChipTestCase(
                "Denise/Sprite",
                "sprite border tail survives beyond the bitplane and beam raster",
                "Sprite registers select a right border beyond the final regular beam pixel",
                TestSpriteBorderTail);
            yield return new ChipTestCase(
                "Denise/Sprite",
                "moving pointer cannot resize the attached wooden frame",
                "Independent sprites do not select the frame presentation aperture",
                TestMovingSpriteKeepsFrameWindow);


        }

        /// <summary>
        /// Runs Test Direct Sprite Register Rendering.
        /// </summary>
        private static void TestSpriteBorderTail()
        {
            var registers = new CustomRegisterMap();
            var frame = new AmigaFrameBuffer(896, 312);
            var bitplanes = new BitplaneUnit(registers, null);
            var sprites = new SpriteUnit(registers, null);
            var denise = new Denise(frame, bitplanes, sprites, null, 128, BitplaneUnit.PalVisibleTop);
            registers.WriteWord(0x100, 0x1000);
            registers.WriteWord(0x096, 0x8300);
            registers.WriteWord(0x08E, 0x2C81);
            registers.WriteWord(0x090, 0x2D81);
            registers.WriteWord(0x092, 0x0038);
            registers.WriteWord(0x094, 0x00D0);
            registers.WriteWord(Spr0pos, 0x2CDF);
            registers.WriteWord(Spr0ctl, 0x2D01);
            registers.WriteWord(Spr0data, 0x0001);
            registers.WriteWord(Spr0datb, 0x0000);
            registers.WriteWord(0x1A2, 0x00F0);
            bitplanes.RefreshFromRegisters();
            bitplanes.ObservePresentationWindowForBeamLine(BitplaneUnit.PalVisibleTop);
            denise.Tick(new BeamTraceEntry { BeamX = 0x3E, BeamY = BitplaneUnit.PalVisibleTop });
            var beam = new BeamTraceEntry { BeamX = 226, BeamY = BitplaneUnit.PalVisibleTop };
            denise.Tick(beam);
            denise.FinishLineTail(beam);
            Check.Equal(0xFF00FF00u, frame.Pixels[BitplaneUnit.PalVisibleTop * frame.Width + 796], "The sprite's final word must render beyond the regular beam raster instead of being cleared as border.");
            Check.Equal(0xFF00FF00u, frame.Pixels[BitplaneUnit.PalVisibleTop * frame.Width + 797], "Both hires halves of the last lowres sprite pixel must remain visible.");
            Check.Equal(0xFF000000u, frame.Pixels[BitplaneUnit.PalVisibleTop * frame.Width + 798], "The sprite tail must stop at its registered right edge.");
        }

        private static void TestMovingSpriteKeepsFrameWindow()
        {
            var registers = new CustomRegisterMap();
            var sprites = new SpriteUnit(registers, null);
            for (int sprite = 2; sprite <= 5; sprite++)
            {
                uint offset = 0x140u + (uint)sprite * 8;
                registers.WriteWord(offset, sprite < 4 ? (ushort)0x2C37 : (ushort)0x2CDF);
                registers.WriteWord(offset + 2, (ushort)(0x2D01 | ((sprite & 1) != 0 ? 0x80 : 0)));
                registers.WriteWord(offset + 4, 0xFFFF);
            }
            foreach (ushort cursorPos in new ushort[] { 0x2C00, 0x2C80, 0x2CFF })
            {
                registers.WriteWord(Spr0pos, cursorPos);
                registers.WriteWord(Spr0ctl, 0x2D00);
                registers.WriteWord(Spr0data, 0xFFFF);
                sprites.RefreshFromRegisters();
                sprites.NotifyCustomRegisterWordWrite(Spr0pos, cursorPos, 44);
                sprites.NotifyCustomRegisterWordWrite(Spr0data, 0xFFFF, 44);
                sprites.TryRenderPixel(0, 44, out uint _);
                Check.True(sprites.TryGetLineSpriteFrameWindow(out int left, out int width), "Attached borders must determine the frame.");
                Check.Equal(-34, left, "Pointer motion must not move the left wood aperture.");
                Check.Equal(704, width, "Pointer motion must not stretch the wooden frame.");
            }
        }

        private static void TestSpritePresentationWindow()
        {
            var registers = new CustomRegisterMap();
            var sprites = new SpriteUnit(registers, null);
            registers.WriteWord(Spr0pos, 0x2C46);
            registers.WriteWord(Spr0ctl, 0x2D00);
            registers.WriteWord(Spr0data, 0x00F0);
            registers.WriteWord(0x1A2, 0x0FFF);
            registers.WriteWord(Spr0datb, 0x0000);
            sprites.RefreshFromRegisters();
            sprites.TryRenderPixel(40, BitplaneUnit.PalVisibleTop, out uint _, out int _);
            Check.True(sprites.TryGetLinePresentationWindow(out int left, out int width), "Latched sprite must provide its rendered aperture.");
            Check.Equal(24, left, "The aperture starts at the registered sprite position, independently of pixel colors.");
            Check.Equal(32, width, "All sixteen latched lowres pixels must fit in the aperture.");
        }

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
        /// Verifies horizontal reuse and the horizontal comparator arming registers.
        /// </summary>
        private static void TestSpritePositionWriteReusesArmedData()
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
            Check.True(sprites.TryRenderPixel(0x18, BitplaneUnit.PalVisibleTop, out uint _, out int _), "POS must reuse the armed bitmap at the second horizontal position.");
            registers.WriteWord(Spr0ctl, 0x2D00);
            sprites.NotifyCustomRegisterWordWrite(Spr0ctl, 0x2D00, BitplaneUnit.PalVisibleTop);
            Check.True(!sprites.TryRenderPixel(0x18, BitplaneUnit.PalVisibleTop, out uint _, out int _), "Even an unchanged CTL write disarms the comparator.");
            sprites.NotifyCustomRegisterWordWrite(Spr0datb, 0xFFFF, BitplaneUnit.PalVisibleTop);
            Check.True(!sprites.TryRenderPixel(0x18, BitplaneUnit.PalVisibleTop, out uint _, out int _), "DATB must not arm a disabled comparator.");
            sprites.NotifyCustomRegisterWordWrite(Spr0data, 0x8000, BitplaneUnit.PalVisibleTop);
            Check.True(sprites.TryRenderPixel(0x18, BitplaneUnit.PalVisibleTop, out uint _, out int _), "DATA must rearm the comparator.");
            registers.WriteWord(Spr0pos, 0x7046);
            sprites.NotifyCustomRegisterWordWrite(Spr0pos, 0x7046, BitplaneUnit.PalVisibleTop);
            registers.WriteWord(Spr0ctl, 0x7100);
            sprites.NotifyCustomRegisterWordWrite(Spr0ctl, 0x7100, BitplaneUnit.PalVisibleTop);
            sprites.NotifyCustomRegisterWordWrite(Spr0data, 0x8000, BitplaneUnit.PalVisibleTop);
            Check.True(sprites.TryRenderPixel(0x18, BitplaneUnit.PalVisibleTop, out uint _, out int _), "Manual DATA arms Denise even before Agnus's VSTART DMA comparison.");
        }

        private static void TestSpriteAttachmentWithParkedOddSprite()
        {
            var registers = new CustomRegisterMap();
            registers.WriteWord(0x150, 0x2C40); // Sprite 2 at the visible origin.
            registers.WriteWord(0x152, 0x2D00);
            registers.WriteWord(0x154, 0x8000);
            registers.WriteWord(0x158, 0x2870); // Sprite 3 parked and vertically inactive.
            registers.WriteWord(0x15A, 0x2980); // ATT is a control latch, not a pixel.
            registers.WriteWord(0x1A2, 0x00F); // Attached index 17: blue.
            registers.WriteWord(0x1AA, 0xFF0); // Independent sprite 2 index 21: yellow.
            var sprites = new SpriteUnit(registers, null);
            sprites.RefreshFromRegisters();
            Check.True(sprites.TryRenderPixel(0, 44, out uint color, out int group), "Even sprite must render with an inactive odd partner.");
            Check.Equal(0xFF0000FFu, color, "ATT must select COLOR17 rather than the independent COLOR21.");
            Check.Equal(1, group, "Attachment must retain the pair's priority group.");
        }

        private static void TestSpriteReuseDoesNotStartIdleDma()
        {
            var registers = new CustomRegisterMap();
            int reads = 0;
            var sprites = new SpriteUnit(registers, address => { reads++; return 0; });
            registers.WriteWord(Dmacon, 0x8220);
            registers.WriteWord(0x122, 0x0100);
            sprites.RefreshFromRegisters();
            sprites.TickDmaSlot(0, 0, 0); sprites.TickDmaSlot(0, 1, 0);
            Check.Equal(2, reads, "Dummy list control pair.");
            sprites.HasRenderablePixelsOnLine(100);
            registers.WriteWord(Spr0pos, 0x0060);
            sprites.NotifyCustomRegisterWordWrite(Spr0pos, 0x0060, 100);
            sprites.NotifyCustomRegisterWordWrite(Spr0data, 0x8000, 100);
            sprites.NotifyCustomRegisterWordWrite(Spr0datb, 0, 100);
            Check.True(sprites.TryRenderPixel(128, 100, out uint _, out int _), "Manual Copper data must render.");
            registers.WriteWord(Spr0pos, 0x0068);
            sprites.NotifyCustomRegisterWordWrite(Spr0pos, 0x0068, 100);
            Check.True(sprites.TryRenderPixel(160, 100, out uint _, out int _), "Copper can reuse the same word.");
            sprites.TickDmaSlot(0, 0, 101); sprites.TickDmaSlot(0, 1, 101);
            Check.Equal(2, reads, "A late VSTART=0 write must not start idle sprite DMA.");
            using var buffer = new MemoryStream();
            using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, true)) sprites.SaveState(writer);
            buffer.Position = 0;
            using (var reader = new BinaryReader(buffer, System.Text.Encoding.UTF8, true)) sprites.LoadState(reader);
            int restoredReads = reads;
            sprites.TickDmaSlot(0, 0, 102); sprites.TickDmaSlot(0, 1, 102);
            Check.Equal(restoredReads, reads, "Restore cannot restart DMA into memory after the dummy list.");
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
            yield return new ChipTestCase("Paula/Audio", "manual AUDDAT samples survive while audio DMA stays disabled",
                "Manual audio data latches are independent of repeated unchanged DMACON state", delegate {
                    using var system = new AmigaSystem(AmigaModel.A500Pal, AmigaAccuracyOptions.Fast.Clone());
                    bool audible = false;
                    system.Paula.AudioSampleGenerated += (left, right) => audible |= left > 0.1f;
                    system.Bus.WriteWord(0xDFF0A6, 124);
                    system.Bus.WriteWord(0xDFF0A8, 64);
                    system.Bus.WriteWord(0xDFF0AA, 0x7F7F);
                    system.RunChipCycles(1000);
                    Check.True(audible, "Manually written audio samples must reach the output without Audio DMA enabled.");
                    Check.Equal(0L, system.Paula.AudioDmaWordsFetched, "Manual mode must not perform DMA fetches.");
                });
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
            yield return new ChipTestCase("Integration", "component profiling preserves full rendered machine output",
                "Profiling must execute line tails and presentation identically to ordinary chip cycles", TestProfilePreservesVideo);
            yield return new ChipTestCase("Integration", "compatible profile retains arbitration without continuous trace allocation",
                "Gameplay compatibility and diagnostic tracing are independent options", delegate {
                    Check.Equal(AmigaAccuracyProfile.Compatible, new AmigaEmulatorSettings().AccuracyProfile, "New frontend settings should prioritize gameplay compatibility.");
                    var options = AmigaAccuracyOptions.Compatible.Clone();
                    Check.True(options.StrictChipBusArbitration, "Compatible must retain chip contention.");
                    Check.True(!options.EnableCpuTrace && !options.EnableDmaSlotTrace, "Continuous traces are reserved for explicit Strict diagnostics.");
                    Check.True(AmigaAccuracyOptions.Strict.EnableCpuTrace && AmigaAccuracyOptions.Strict.EnableDmaSlotTrace, "Strict diagnostic tracing remains available.");
                });
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
        /// Verifies profiling equivalence for the Fast throughput profile.
        /// </summary>
        private static void TestProfilePreservesVideo()
        {
            using var ordinary = new AmigaSystem(AmigaModel.A500Pal, AmigaAccuracyOptions.Fast.Clone());
            using var profiled = new AmigaSystem(AmigaModel.A500Pal, AmigaAccuracyOptions.Fast.Clone());
            ordinary.Bus.WriteWord(0xDFF180, 0x0F00);
            profiled.Bus.WriteWord(0xDFF180, 0x0F00);
            int cycles = ordinary.Model.ChipCyclesPerLine * ordinary.Model.LinesPerFrame * 2;
            ordinary.RunChipCycles(cycles);
            profiled.ProfileChipCycles(cycles);
            Check.True(ordinary.PresentedFrameBuffer.Pixels.Any(pixel => (pixel & 0xFFFFFFu) != 0), "Fixture must produce visible presentation output.");
            Check.True(ordinary.FrameBuffer.Pixels.SequenceEqual(profiled.FrameBuffer.Pixels), "Profiled raw raster must match ordinary execution.");
            Check.True(ordinary.PresentedFrameBuffer.Pixels.SequenceEqual(profiled.PresentedFrameBuffer.Pixels), "Profiler must update the presented frame, not leave stale pixels.");
            Check.Equal(ordinary.GetCpuDebugInfo(), profiled.GetCpuDebugInfo(), "Profiling must preserve CPU state.");
        }

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
            Check.Equal(5, settings.Version, "Settings version should track the monitor aperture default migration.");
            Check.Equal(AmigaMonitorAperture.Crt, settings.MonitorAperture, "New settings should default to the CRT monitor aperture.");
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
                "in-flight disk DMA preserves rotating word phase across restore",
                "Paula byte sampling and DF0 physical rotation must resume in phase",
                TestDiskDmaRotationPhaseRoundtrip);
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
        /// Verifies that a restored rotating disk supplies exactly the same words to active DMA.
        /// </summary>
        private static void TestDiskDmaRotationPhaseRoundtrip()
        {
            string path = Path.Combine(Path.GetTempPath(), "amiga-disk-phase-" + Guid.NewGuid().ToString("N") + ".adf");
            var data = new byte[AdfImage.StandardSize];
            for (int i = 0; i < data.Length; i++) data[i] = (byte)(i * 37 + i / 256);
            File.WriteAllBytes(path, data);
            try
            {
                foreach (int phase in new[] { 17, 53, 91 })
                {
                    var controller = new FloppyController();
                    controller.InsertDrive0(AdfImage.Load(path), true);
                    controller.Drive0.ApplySelection(true, true);
                    for (int i = 0; i < 1_400_000; i++) controller.Tick();
                    var expected = new List<ushort>();
                    var paula = new Paula();
                    paula.AttachDisk(controller, (_, word) => expected.Add(word), null, _ => { }, () => 0x0210, () => 0);
                    paula.NotifyCustomRegisterWordWrite(0x024, 0x8800);
                    paula.NotifyCustomRegisterWordWrite(0x024, 0x8800);
                    for (int i = 0; i < 100_000 + phase; i++) { controller.Tick(); paula.Tick(true, false); }
                    Check.True(paula.DiskDmaActive, "Save must occur during a disk DMA block.");
                    using var stream = new MemoryStream();
                    using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
                    StateSerializer.WriteObjectFields(writer, controller);
                    paula.SaveState(writer);
                    writer.Flush();
                    var restoredController = new FloppyController();
                    var restoredPaula = new Paula();
                    stream.Position = 0;
                    using var reader = new BinaryReader(stream, Encoding.UTF8, true);
                    StateSerializer.ReadObjectFields(reader, restoredController);
                    restoredController.RestoreDrive0Media(AdfImage.Load(path), true);
                    restoredPaula.LoadState(reader);
                    var actual = new List<ushort>();
                    restoredPaula.AttachDisk(restoredController, (_, word) => actual.Add(word), null, _ => { }, () => 0x0210, () => 0);
                    expected.Clear();
                    for (int i = 0; i < 150_000; i++)
                    {
                        controller.Tick(); paula.Tick(true, false);
                        restoredController.Tick(); restoredPaula.Tick(true, false);
                    }
                    Check.True(expected.Count > 500, "Test must compare a substantial DMA transfer.");
                    Check.True(expected.SequenceEqual(actual), "Restored disk DMA changed MFM words at phase " + phase + ".");
                    Check.True(!paula.DiskDmaActive && !restoredPaula.DiskDmaActive, "Both DMA blocks should complete.");
                }
            }
            finally { File.Delete(path); }
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
    /// Provides local Settlers savestate presentation regression tests.
    /// </summary>
    internal static class LocalSiedlerGoldenTests
    {
        /// <summary>
        /// Builds the Build result.
        /// </summary>
        public static IEnumerable<ChipTestCase> Build(TestOptions options)
        {
            yield return new ChipTestCase(
                "LocalSiedlerGolden",
                "Die Siedler savestate keeps wooden frame and bottom menu aligned",
                "Local PDX Die Siedler savestate, no checked-in game assets",
                delegate { TestSiedlerSavestatePresentation(options.SiedlerSavePath, options.CreateExternalAccuracyOptions()); });
        }

        /// <summary>
        /// Verifies the visible Settlers gameplay frame and bottom command panel geometry.
        /// </summary>
        private static void TestSiedlerSavestatePresentation(string configuredSavePath, AmigaAccuracyOptions accuracy)
        {
            string savePath = ResolveSiedlerSavePath(configuredSavePath);
            Check.True(System.IO.File.Exists(savePath), "Siedler savestate not found. Pass --siedler-save <path>.");

            SaveStateSlotMetadata metadata = SaveStateFile.ReadMetadata(savePath);
            using (var system = new AmigaSystem(GetModelForRamProfile(metadata.RamProfile), accuracy))
            {
                SaveStateFile.Load(savePath, system);
                RunFrames(system, 12);

                AmigaEmulator.Core.Video.AmigaFrameBuffer frame = system.PresentedFrameBuffer;
                Check.Equal(704, frame.Width, "Siedler must use the stable PAL overscan presentation width.");
                Check.Equal(288, frame.Height, "Siedler must use the stable PAL overscan presentation height.");
                Check.True((frame.Pixels[100 * frame.Width] & 0xFFFFFFu) != 0, "The left wooden playfield border must meet the monitor edge.");
                Check.True((frame.Pixels[100 * frame.Width + frame.Width - 1] & 0xFFFFFFu) != 0, "The right wooden playfield border must align with the bottom panel, without a black strip.");
                Check.True(frame.Pixels.Take(frame.Width).Count(pixel => (pixel & 0xFFFFFFu) != 0) > frame.Width / 2, "The top wooden frame must remain visible instead of being preceded by unused raster rows.");


                SiedlerFrameMetrics metrics = AnalyzeSiedlerFrame(frame.Pixels, frame.Width, frame.Height);
                Check.True(metrics.UniqueColors >= 24, "Siedler frame has too few colors. " + metrics);
                Check.True(metrics.BottomPanelNonBlackPercent >= 55.0, "Siedler bottom menu appears blank or clipped. " + metrics);
                Check.True(metrics.BottomLeftWoodPixels >= 650, "Siedler bottom menu is clipped on the left edge. " + metrics);
                Check.True(metrics.BottomRightWoodPixels >= 650, "Siedler bottom menu is clipped on the right edge. " + metrics);
                Check.True(metrics.RightFrameWoodPixels >= 900, "Siedler right vertical wooden frame is missing or cropped. " + metrics);
                Check.True(metrics.LeftFrameWoodPixels >= 900, "Siedler left vertical wooden frame is missing or cropped. " + metrics);
                Check.True(metrics.TopRightFrameWoodPixels >= 100, "Siedler top-right frame corner is missing. " + metrics);
                Check.True(metrics.ConsecutiveLargeBottomBlackRunRows <= 8, "Siedler bottom menu contains a large black block. " + metrics);
                Check.True(metrics.BottomEdgeContentColumns >= 660, "Siedler bottom menu does not span the full monitor aperture. " + metrics);
                while (system.Agnus.Beam.BeamX != 0 || system.Agnus.Beam.BeamY != 0) system.TickChipCycle();
                Check.True(system.Agnus.Bitplanes.TryGetSpriteFrameHorizontalWindow(out int frameLeft, out int frameWidth), "Wood frame must select a stable sprite aperture.");
                foreach (var delta in new[] { (-400, 0), (800, 0), (-400, 300), (0, -600) })
                {
                    int steps = Math.Max(Math.Abs(delta.Item1), Math.Abs(delta.Item2)) / 8;
                    int lastX = 0, lastY = 0;
                    for (int step = 1; step <= steps; step++)
                    {
                        int x = delta.Item1 * step / steps;
                        int y = delta.Item2 * step / steps;
                        system.Input.MoveMouse(AmigaControlPort.Port0, x - lastX, y - lastY);
                        system.RunChipCycles(120_000);
                        lastX = x; lastY = y;
                    }
                    RunFrames(system, 4);
                    while (system.Agnus.Beam.BeamX != 0 || system.Agnus.Beam.BeamY != 0) system.TickChipCycle();
                    Check.True(system.Agnus.Bitplanes.TryGetSpriteFrameHorizontalWindow(out int movedLeft, out int movedWidth), "The wood aperture must survive pointer motion.");
                    Check.Equal(frameLeft, movedLeft, "Pointer motion must not shift the game frame.");
                    Check.Equal(frameWidth, movedWidth, "Pointer motion must not resize the game frame.");
                    for (int beamY = 44; beamY < 240; beamY++)
                    {
                        Check.True(system.Agnus.Bitplanes.TryGetLinePresentationContentSourceWindow(beamY, out int left, out int width), "Frame line metadata must be present.");
                        Check.Equal(frameLeft, left, "No individual wood scanline may move with the pointer at beam " + beamY + " after " + delta);
                        Check.Equal(frameWidth, width, "No individual wood scanline may stretch with the pointer at beam " + beamY + " after " + delta);
                    }
                }
            }
        }

        /// <summary>
        /// Advances by full PAL frames.
        /// </summary>
        private static void RunFrames(AmigaSystem system, int framesToRun)
        {
            int frameCycles = system.Model.ChipCyclesPerLine * system.Model.LinesPerFrame;
            for (int frame = 0; frame < framesToRun; frame++)
            {
                system.RunChipCycles(frameCycles);
            }
        }

        /// <summary>
        /// Resolves the explicit or latest local Settlers savestate.
        /// </summary>
        private static string ResolveSiedlerSavePath(string configuredSavePath)
        {
            if (!string.IsNullOrWhiteSpace(configuredSavePath) && System.IO.File.Exists(configuredSavePath))
            {
                return System.IO.Path.GetFullPath(configuredSavePath);
            }

            string root = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AmigaEmulator",
                "saves");
            if (!System.IO.Directory.Exists(root))
            {
                return configuredSavePath ?? string.Empty;
            }

            return System.IO.Directory.GetFiles(root, "*.amigasav", SearchOption.AllDirectories)
                .Where(path => path.IndexOf("Siedler", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    path.IndexOf("Settlers", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(path => System.IO.File.GetLastWriteTimeUtc(path))
                .FirstOrDefault() ?? configuredSavePath ?? string.Empty;
        }

        /// <summary>
        /// Analyzes visible frame/menu geometry using broad color and edge metrics.
        /// </summary>
        private static SiedlerFrameMetrics AnalyzeSiedlerFrame(uint[] pixels, int width, int height)
        {
            int bottomTop = Math.Max(0, height - 64);
            int playfieldTop = Math.Min(24, height - 1);
            int playfieldBottom = Math.Max(playfieldTop + 1, bottomTop - 4);
            var colors = new HashSet<uint>();
            int bottomPixels = 0;
            int bottomNonBlack = 0;
            int bottomLeftWood = 0;
            int bottomRightWood = 0;
            int leftFrameWood = 0;
            int rightFrameWood = 0;
            int topRightFrameWood = 0;
            int bottomEdgeContentColumns = 0;
            int largestBottomBlackRun = 0;
            int consecutiveLargeBlackRows = 0;
            int maxConsecutiveLargeBlackRows = 0;

            for (int index = 0; index < pixels.Length; index++)
            {
                if (colors.Count < 4096)
                {
                    colors.Add(pixels[index]);
                }
            }

            for (int y = bottomTop; y < height; y++)
            {
                int row = y * width;
                int blackRun = 0;
                int rowLargestBlackRun = 0;
                for (int x = 0; x < width; x++)
                {
                    uint color = pixels[row + x];
                    bottomPixels++;
                    if (!IsNearBlack(color))
                    {
                        bottomNonBlack++;
                    }

                    if (x < 80 && IsWoodLike(color))
                    {
                        bottomLeftWood++;
                    }

                    if (x >= width - 80 && IsWoodLike(color))
                    {
                        bottomRightWood++;
                    }

                    if (IsNearBlack(color))
                    {
                        blackRun++;
                        largestBottomBlackRun = Math.Max(largestBottomBlackRun, blackRun);
                        rowLargestBlackRun = Math.Max(rowLargestBlackRun, blackRun);
                    }
                    else
                    {
                        blackRun = 0;
                    }
                }

                if (rowLargestBlackRun >= 160)
                {
                    consecutiveLargeBlackRows++;
                    maxConsecutiveLargeBlackRows = Math.Max(maxConsecutiveLargeBlackRows, consecutiveLargeBlackRows);
                }
                else
                {
                    consecutiveLargeBlackRows = 0;
                }
            }

            for (int x = 0; x < width; x++)
            {
                int contentRows = 0;
                for (int y = bottomTop; y < height; y++)
                {
                    if (!IsNearBlack(pixels[(y * width) + x]))
                    {
                        contentRows++;
                    }
                }

                if (contentRows >= 4)
                {
                    bottomEdgeContentColumns++;
                }
            }

            for (int y = playfieldTop; y < playfieldBottom; y++)
            {
                int row = y * width;
                for (int x = 0; x < Math.Min(34, width); x++)
                {
                    if (IsWoodLike(pixels[row + x]))
                    {
                        leftFrameWood++;
                    }
                }

                for (int x = Math.Max(0, width - 34); x < width; x++)
                {
                    if (IsWoodLike(pixels[row + x]))
                    {
                        rightFrameWood++;
                    }
                }
            }

            for (int y = 0; y < Math.Min(28, height); y++)
            {
                int row = y * width;
                for (int x = Math.Max(0, width - 48); x < width; x++)
                {
                    if (IsWoodLike(pixels[row + x]))
                    {
                        topRightFrameWood++;
                    }
                }
            }

            return new SiedlerFrameMetrics
            {
                UniqueColors = colors.Count,
                BottomPanelNonBlackPercent = bottomPixels > 0 ? (bottomNonBlack * 100.0) / bottomPixels : 0.0,
                BottomLeftWoodPixels = bottomLeftWood,
                BottomRightWoodPixels = bottomRightWood,
                LeftFrameWoodPixels = leftFrameWood,
                RightFrameWoodPixels = rightFrameWood,
                TopRightFrameWoodPixels = topRightFrameWood,
                LargestBottomBlackRun = largestBottomBlackRun,
                ConsecutiveLargeBottomBlackRunRows = maxConsecutiveLargeBlackRows,
                BottomEdgeContentColumns = bottomEdgeContentColumns
            };
        }

        /// <summary>
        /// Returns whether a color is close to monitor black.
        /// </summary>
        private static bool IsNearBlack(uint color)
        {
            int red = (int)((color >> 16) & 0xFF);
            int green = (int)((color >> 8) & 0xFF);
            int blue = (int)(color & 0xFF);
            return red < 18 && green < 18 && blue < 18;
        }

        /// <summary>
        /// Returns whether a color is a broad wooden brown/orange frame color.
        /// </summary>
        private static bool IsWoodLike(uint color)
        {
            int red = (int)((color >> 16) & 0xFF);
            int green = (int)((color >> 8) & 0xFF);
            int blue = (int)(color & 0xFF);
            return red >= 28 &&
                red >= green &&
                green >= blue &&
                green >= 10 &&
                blue <= 120 &&
                red - blue >= 16;
        }

        /// <summary>
        /// Converts savestate RAM-profile metadata back to the matching emulator model.
        /// </summary>
        private static AmigaModel GetModelForRamProfile(AmigaRamProfile profile)
        {
            switch (profile)
            {
                case AmigaRamProfile.Chip512KSlow512K:
                    return AmigaModel.A500Pal512Slow512;
                case AmigaRamProfile.Chip1Mb:
                    return AmigaModel.A500Pal1MbChip;
                case AmigaRamProfile.Chip2Mb:
                    return AmigaModel.A500Pal2MbChip;
                case AmigaRamProfile.Chip2MbFast8Mb:
                    return AmigaModel.A500Pal2MbChipFast8Mb;
                default:
                    return AmigaModel.PalA500;
            }
        }

        /// <summary>
        /// Stores local Settlers visual metrics.
        /// </summary>
        private sealed class SiedlerFrameMetrics
        {
            public int UniqueColors;
            public double BottomPanelNonBlackPercent;
            public int BottomLeftWoodPixels;
            public int BottomRightWoodPixels;
            public int LeftFrameWoodPixels;
            public int RightFrameWoodPixels;
            public int TopRightFrameWoodPixels;
            public int LargestBottomBlackRun;
            public int ConsecutiveLargeBottomBlackRunRows;
            public int BottomEdgeContentColumns;

            public override string ToString()
            {
                return string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "colors={0} bottomNonBlack={1:F1}% bottomLeftWood={2} bottomRightWood={3} leftFrameWood={4} rightFrameWood={5} topRightWood={6} largestBottomBlackRun={7} largeBlackRows={8} bottomContentColumns={9}",
                    UniqueColors,
                    BottomPanelNonBlackPercent,
                    BottomLeftWoodPixels,
                    BottomRightWoodPixels,
                    LeftFrameWoodPixels,
                    RightFrameWoodPixels,
                    TopRightFrameWoodPixels,
                    LargestBottomBlackRun,
                    ConsecutiveLargeBottomBlackRunRows,
                    BottomEdgeContentColumns);
            }
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
                    delegate { RunSmokeCase(testCase, options.VAmigaTsCycles, options.CreateExternalAccuracyOptions()); });
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
        private static void RunSmokeCase(VAmigaTsCase testCase, int cycles, AmigaAccuracyOptions accuracy)
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

            using (var system = new AmigaSystem(AmigaModel.A500Pal, accuracy))
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
