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
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using AmigaEmulator.Core;
using AmigaEmulator.Core.Cia;
using AmigaEmulator.Core.Cpu;
using AmigaEmulator.Core.CustomChips;
using AmigaEmulator.Core.DevTools;
using AmigaEmulator.Core.Input;
using AmigaEmulator.Core.Media;
using AmigaEmulator.Core.Memory;
using AmigaEmulator.Core.SaveStates;

namespace AmigaEmulator
{
    /// <summary>
    /// Provides the command-line diagnostics, self-tests, and WinForms application entry point.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// Tracks the relaxed Bus Diagnostics.
        /// </summary>
        private static bool _relaxedBusDiagnostics;

        /// <summary>
        /// Executes the Main logic.
        /// </summary>
        [STAThread]
        private static void Main(string[] args)
        {
            _relaxedBusDiagnostics = HasOption(args, "--relaxed-bus");

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--check-roms", StringComparison.OrdinalIgnoreCase))
            {
                RunRomCheck(args);
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--self-test-cpu", StringComparison.OrdinalIgnoreCase))
            {
                RunCpuSelfTest();
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--self-test-blitter", StringComparison.OrdinalIgnoreCase))
            {
                RunBlitterSelfTest();
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--self-test-copper", StringComparison.OrdinalIgnoreCase))
            {
                RunCopperSelfTest();
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--self-test-disk", StringComparison.OrdinalIgnoreCase))
            {
                RunDiskSelfTest();
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--self-test-input", StringComparison.OrdinalIgnoreCase))
            {
                RunInputSelfTest();
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--self-test-cia", StringComparison.OrdinalIgnoreCase))
            {
                RunCiaSelfTest();
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--benchmark", StringComparison.OrdinalIgnoreCase))
            {
                int cycles = args.Length >= 2 && int.TryParse(args[1], out int parsedCycles) && parsedCycles > 0
                    ? parsedCycles
                    : 1000000;
                string profile = args.Length >= 3 ? args[2] : "fast";
                RunBenchmark(cycles, profile);
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--profile-components", StringComparison.OrdinalIgnoreCase))
            {
                int cycles = args.Length >= 2 && int.TryParse(args[1], out int parsedCycles) && parsedCycles > 0
                    ? parsedCycles
                    : 1000000;
                string profile = args.Length >= 3 ? args[2] : "fast";
                RunComponentProfile(cycles, profile);
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--trace-machine", StringComparison.OrdinalIgnoreCase))
            {
                int cycles = args.Length >= 2 && int.TryParse(args[1], out int parsedTraceCycles) && parsedTraceCycles > 0
                    ? parsedTraceCycles
                    : 2000;
                string logPath = args.Length >= 3
                    ? args[2]
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "amiga_trace.jsonl");
                MachineTraceExporter.Export(cycles, logPath);
                Console.WriteLine("Trace written: " + Path.GetFullPath(logPath));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--trace-register-value", StringComparison.OrdinalIgnoreCase))
            {
                string registerName = args.Length >= 2 ? args[1] : "A2";
                uint value = args.Length >= 3 ? Convert.ToUInt32(args[2].Replace("$", string.Empty), 16) : 0xFFFFFFFFu;
                int cycles = args.Length >= 4 && int.TryParse(args[3], out int parsedRegisterCycles) && parsedRegisterCycles > 0
                    ? parsedRegisterCycles
                    : 12000000;
                int maxHits = args.Length >= 5 && int.TryParse(args[4], out int parsedRegisterHits) && parsedRegisterHits > 0
                    ? parsedRegisterHits
                    : 20;
                int startCycle = args.Length >= 6 && int.TryParse(args[5], out int parsedRegisterStart) && parsedRegisterStart > 0
                    ? parsedRegisterStart
                    : 0;
                TraceRegisterValue(registerName, value, cycles, maxHits, startCycle, ParseModelArgument(args, 6));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--trace-until-fault", StringComparison.OrdinalIgnoreCase))
            {
                int cycles = args.Length >= 2 && int.TryParse(args[1], out int parsedFaultCycles) && parsedFaultCycles > 0
                    ? parsedFaultCycles
                    : 10000000;
                string logPath = args.Length >= 3
                    ? args[2]
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "amiga_fault_trace.jsonl");
                MachineTraceSample fault = MachineTraceExporter.ExportUntilSuspiciousInstruction(cycles, logPath);
                Console.WriteLine("Trace written: " + Path.GetFullPath(logPath));
                if (fault == null || fault.CpuTrace == null)
                {
                    Console.WriteLine("No suspicious CPU instruction found.");
                }
                else
                {
                    Console.WriteLine(string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "First suspicious instruction: cycle={0} pc=${1:X6} opcode=${2:X4} instruction={3}",
                        fault.GlobalChipCycle,
                        fault.CpuTrace.PcBefore,
                        fault.CpuTrace.Opcode,
                        fault.CpuTrace.Instruction));
                }

                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--find-suspicious-cpu", StringComparison.OrdinalIgnoreCase))
            {
                int cycles = args.Length >= 2 && int.TryParse(args[1], out int parsedSuspiciousCycles) && parsedSuspiciousCycles > 0
                    ? parsedSuspiciousCycles
                    : 12000000;
                FindSuspiciousCpuInstruction(cycles, ParseModelArgument(args, 2));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--dump-frame", StringComparison.OrdinalIgnoreCase))
            {
                int cycles = args.Length >= 2 && int.TryParse(args[1], out int parsedDumpCycles) && parsedDumpCycles > 0
                    ? parsedDumpCycles
                    : 12000000;
                string imagePath = args.Length >= 3
                    ? args[2]
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "amiga_frame.png");
                DumpFrame(cycles, imagePath, ParseModelArgument(args, 3));
                Console.WriteLine("Frame written: " + Path.GetFullPath(imagePath));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--dump-savestate-frame", StringComparison.OrdinalIgnoreCase))
            {
                string savePath = args.Length >= 2 ? args[1] : SaveStateFile.GetSlotPath(UserDataPaths.GetSaveDirectory(), 0);
                string imagePath = args.Length >= 3
                    ? args[2]
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "amiga_savestate_frame.png");
                int frames = args.Length >= 4 && int.TryParse(args[3], out int parsedSaveFrames) && parsedSaveFrames > 0
                    ? parsedSaveFrames
                    : 0;
                DumpSavestateFrame(savePath, imagePath, frames, ParseModelArgument(args, 4));
                Console.WriteLine("Savestate frame written: " + Path.GetFullPath(imagePath));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--dump-frame-poke-viewlist", StringComparison.OrdinalIgnoreCase))
            {
                int cycles = args.Length >= 2 && int.TryParse(args[1], out int parsedDumpPokeCycles) && parsedDumpPokeCycles > 0
                    ? parsedDumpPokeCycles
                    : 12000000;
                int pokeCycle = args.Length >= 3 && int.TryParse(args[2], out int parsedPokeCycle) && parsedPokeCycle > 0
                    ? parsedPokeCycle
                    : 6845000;
                string imagePath = args.Length >= 4
                    ? args[3]
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "amiga_frame_poke_viewlist.png");
                DumpFrameWithViewListPoke(cycles, pokeCycle, imagePath, ParseModelArgument(args, 4));
                Console.WriteLine("Frame written: " + Path.GetFullPath(imagePath));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--diagnose-adf-boot", StringComparison.OrdinalIgnoreCase))
            {
                string adfPath = args.Length >= 2 ? args[1] : string.Empty;
                int cycles = args.Length >= 3 && int.TryParse(args[2], out int parsedAdfCycles) && parsedAdfCycles > 0
                    ? parsedAdfCycles
                    : 120000000;
                string imagePath = args.Length >= 4
                    ? args[3]
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "amiga_adf_boot.png");
                DiagnoseAdfBoot(adfPath, cycles, imagePath, ParseModelArgument(args, 4));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--diagnose-adf-boot-fire", StringComparison.OrdinalIgnoreCase))
            {
                string adfPath = args.Length >= 2 ? args[1] : string.Empty;
                int preCycles = args.Length >= 3 && int.TryParse(args[2], out int parsedPreFireCycles) && parsedPreFireCycles > 0
                    ? parsedPreFireCycles
                    : 120000000;
                int postCycles = args.Length >= 4 && int.TryParse(args[3], out int parsedPostFireCycles) && parsedPostFireCycles > 0
                    ? parsedPostFireCycles
                    : 60000000;
                string imagePath = args.Length >= 5
                    ? args[4]
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "amiga_adf_boot_fire.png");
                DiagnoseAdfBootFire(adfPath, preCycles, postCycles, imagePath, ParseModelArgument(args, 5));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--diagnose-adf-bootblock", StringComparison.OrdinalIgnoreCase))
            {
                string adfPath = args.Length >= 2 ? args[1] : string.Empty;
                int cycles = args.Length >= 3 && int.TryParse(args[2], out int parsedBootblockCycles) && parsedBootblockCycles > 0
                    ? parsedBootblockCycles
                    : 120000000;
                DiagnoseAdfBootblock(adfPath, cycles, ParseModelArgument(args, 3));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--diagnose-adf-dma-block", StringComparison.OrdinalIgnoreCase))
            {
                string adfPath = args.Length >= 2 ? args[1] : string.Empty;
                int targetBlock = args.Length >= 3 && int.TryParse(args[2], out int parsedTargetBlock) && parsedTargetBlock > 0
                    ? parsedTargetBlock
                    : 1;
                int maxCycles = args.Length >= 4 && int.TryParse(args[3], out int parsedDmaBlockCycles) && parsedDmaBlockCycles > 0
                    ? parsedDmaBlockCycles
                    : 30000000;
                DiagnoseAdfDmaBlock(adfPath, targetBlock, maxCycles, ParseModelArgument(args, 4));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--trace-floppy-cia", StringComparison.OrdinalIgnoreCase))
            {
                string adfPath = args.Length >= 2 ? args[1] : string.Empty;
                int cycles = args.Length >= 3 && int.TryParse(args[2], out int parsedFloppyCiaCycles) && parsedFloppyCiaCycles > 0
                    ? parsedFloppyCiaCycles
                    : 15000000;
                int maxLines = args.Length >= 4 && int.TryParse(args[3], out int parsedFloppyCiaMax) && parsedFloppyCiaMax > 0
                    ? parsedFloppyCiaMax
                    : 256;
                TraceFloppyCia(adfPath, cycles, maxLines, ParseModelArgument(args, 4));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--trace-floppy-probe", StringComparison.OrdinalIgnoreCase))
            {
                string adfPath = args.Length >= 2 ? args[1] : string.Empty;
                int cycles = args.Length >= 3 && int.TryParse(args[2], out int parsedFloppyProbeCycles) && parsedFloppyProbeCycles > 0
                    ? parsedFloppyProbeCycles
                    : 15000000;
                int maxLines = args.Length >= 4 && int.TryParse(args[3], out int parsedFloppyProbeMax) && parsedFloppyProbeMax > 0
                    ? parsedFloppyProbeMax
                    : 256;
                TraceFloppyProbe(adfPath, cycles, maxLines, ParseModelArgument(args, 4));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--diagnose-adf-insert-after", StringComparison.OrdinalIgnoreCase))
            {
                string adfPath = args.Length >= 2 ? args[1] : string.Empty;
                int preCycles = args.Length >= 3 && int.TryParse(args[2], out int parsedPreCycles) && parsedPreCycles > 0
                    ? parsedPreCycles
                    : 20000000;
                int postCycles = args.Length >= 4 && int.TryParse(args[3], out int parsedPostCycles) && parsedPostCycles > 0
                    ? parsedPostCycles
                    : 60000000;
                string imagePath = args.Length >= 5
                    ? args[4]
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "amiga_adf_insert_after.png");
                DiagnoseAdfInsertAfter(adfPath, preCycles, postCycles, imagePath, ParseModelArgument(args, 5));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--trace-adf-until-fault", StringComparison.OrdinalIgnoreCase))
            {
                string adfPath = args.Length >= 2 ? args[1] : string.Empty;
                int cycles = args.Length >= 3 && int.TryParse(args[2], out int parsedAdfFaultCycles) && parsedAdfFaultCycles > 0
                    ? parsedAdfFaultCycles
                    : 120000000;
                string logPath = args.Length >= 4
                    ? args[3]
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "amiga_adf_fault_trace.jsonl");
                TraceAdfUntilFault(adfPath, cycles, logPath, ParseModelArgument(args, 4));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--trace-adf-copper", StringComparison.OrdinalIgnoreCase))
            {
                string adfPath = args.Length >= 2 ? args[1] : string.Empty;
                int cycles = args.Length >= 3 && int.TryParse(args[2], out int parsedAdfCopperCycles) && parsedAdfCopperCycles > 0
                    ? parsedAdfCopperCycles
                    : 30000000;
                int maxInstructions = args.Length >= 4 && int.TryParse(args[3], out int parsedAdfCopperMax) && parsedAdfCopperMax > 0
                    ? parsedAdfCopperMax
                    : 512;
                int startCycle = args.Length >= 5 && int.TryParse(args[4], out int parsedAdfCopperStart) && parsedAdfCopperStart > 0
                    ? parsedAdfCopperStart
                    : 0;
                TraceAdfCopper(adfPath, cycles, maxInstructions, startCycle, ParseModelArgument(args, 5));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--trace-adf-custom-regs", StringComparison.OrdinalIgnoreCase))
            {
                string adfPath = args.Length >= 2 ? args[1] : string.Empty;
                int cycles = args.Length >= 3 && int.TryParse(args[2], out int parsedAdfCustomCycles) && parsedAdfCustomCycles > 0
                    ? parsedAdfCustomCycles
                    : 30000000;
                int maxChanges = args.Length >= 4 && int.TryParse(args[3], out int parsedAdfCustomMax) && parsedAdfCustomMax > 0
                    ? parsedAdfCustomMax
                    : 512;
                int startCycle = args.Length >= 5 && int.TryParse(args[4], out int parsedAdfCustomStart) && parsedAdfCustomStart > 0
                    ? parsedAdfCustomStart
                    : 0;
                TraceAdfCustomRegisters(adfPath, cycles, maxChanges, startCycle, ParseModelArgument(args, 5));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--dump-adf-audio", StringComparison.OrdinalIgnoreCase))
            {
                string adfPath = args.Length >= 2 ? args[1] : string.Empty;
                int cycles = args.Length >= 3 && int.TryParse(args[2], out int parsedAudioCycles) && parsedAudioCycles > 0
                    ? parsedAudioCycles
                    : 120000000;
                string wavPath = args.Length >= 4
                    ? args[3]
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "amiga_audio.wav");
                DumpAdfAudio(adfPath, cycles, wavPath, ParseModelArgument(args, 4));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--trace-adf-writes", StringComparison.OrdinalIgnoreCase))
            {
                string adfPath = args.Length >= 2 ? args[1] : string.Empty;
                uint start = args.Length >= 3 ? Convert.ToUInt32(args[2].Replace("$", string.Empty), 16) : 0x00DFF020u;
                uint end = args.Length >= 4 ? Convert.ToUInt32(args[3].Replace("$", string.Empty), 16) : 0x00DFF09Fu;
                int cycles = args.Length >= 5 && int.TryParse(args[4], out int parsedAdfWriteCycles) && parsedAdfWriteCycles > 0
                    ? parsedAdfWriteCycles
                    : 30000000;
                int maxWrites = args.Length >= 6 && int.TryParse(args[5], out int parsedAdfWriteMax) && parsedAdfWriteMax > 0
                    ? parsedAdfWriteMax
                    : 256;
                int startCycle = args.Length >= 7 && int.TryParse(args[6], out int parsedAdfWriteStart) && parsedAdfWriteStart > 0
                    ? parsedAdfWriteStart
                    : 0;
                TraceAdfWrites(adfPath, start, end, cycles, maxWrites, startCycle, ParseModelArgument(args, 7));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--trace-adf-reads", StringComparison.OrdinalIgnoreCase))
            {
                string adfPath = args.Length >= 2 ? args[1] : string.Empty;
                uint start = args.Length >= 3 ? Convert.ToUInt32(args[2].Replace("$", string.Empty), 16) : 0x00DFF000u;
                uint end = args.Length >= 4 ? Convert.ToUInt32(args[3].Replace("$", string.Empty), 16) : 0x00DFF03Fu;
                int cycles = args.Length >= 5 && int.TryParse(args[4], out int parsedAdfReadCycles) && parsedAdfReadCycles > 0
                    ? parsedAdfReadCycles
                    : 30000000;
                int maxReads = args.Length >= 6 && int.TryParse(args[5], out int parsedAdfReadMax) && parsedAdfReadMax > 0
                    ? parsedAdfReadMax
                    : 256;
                int startCycle = args.Length >= 7 && int.TryParse(args[6], out int parsedAdfReadStart) && parsedAdfReadStart > 0
                    ? parsedAdfReadStart
                    : 0;
                TraceAdfReads(adfPath, start, end, cycles, maxReads, startCycle, ParseModelArgument(args, 7));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--adf-test-matrix", StringComparison.OrdinalIgnoreCase))
            {
                string adfDirectory = args.Length >= 2 ? args[1] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "AmigaEmulator");
                int cycles = args.Length >= 3 && int.TryParse(args[2], out int parsedMatrixCycles) && parsedMatrixCycles > 0
                    ? parsedMatrixCycles
                    : 30000000;
                string outputDirectory = args.Length >= 4
                    ? args[3]
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adf_matrix");
                int limit = ParseIntOption(args, "--limit", 0);
                AmigaModel modelFilter = HasOption(args, "--model") ? ParseModelArgument(args, 0) : null;
                RunAdfTestMatrix(adfDirectory, cycles, outputDirectory, limit, modelFilter);
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--dump-bitplane-memory-image", StringComparison.OrdinalIgnoreCase))
            {
                int cycles = args.Length >= 2 && int.TryParse(args[1], out int parsedBitplaneImageCycles) && parsedBitplaneImageCycles > 0
                    ? parsedBitplaneImageCycles
                    : 120000000;
                string imagePath = args.Length >= 3
                    ? args[2]
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "amiga_bitplanes.png");
                DumpBitplaneMemoryImage(cycles, imagePath, ParseModelArgument(args, 3));
                Console.WriteLine("Bitplane memory image written: " + Path.GetFullPath(imagePath));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--dump-core-state", StringComparison.OrdinalIgnoreCase))
            {
                int cycles = args.Length >= 2 && int.TryParse(args[1], out int parsedStateCycles) && parsedStateCycles > 0
                    ? parsedStateCycles
                    : 12000000;
                DumpCoreState(cycles, ParseModelArgument(args, 2));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--assert-kickstart-screen-path", StringComparison.OrdinalIgnoreCase))
            {
                int cycles = args.Length >= 2 && int.TryParse(args[1], out int parsedAssertCycles) && parsedAssertCycles > 0
                    ? parsedAssertCycles
                    : 12000000;
                AssertKickstartScreenPath(cycles, ParseModelArgument(args, 2));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--assert-kickstart-screen-path-slow", StringComparison.OrdinalIgnoreCase))
            {
                int cycles = args.Length >= 2 && int.TryParse(args[1], out int parsedAssertSlowCycles) && parsedAssertSlowCycles > 0
                    ? parsedAssertSlowCycles
                    : 12000000;
                AssertKickstartScreenPath(cycles, AmigaModel.A500Pal512Slow512);
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--dump-kickstart-structures", StringComparison.OrdinalIgnoreCase))
            {
                int cycles = args.Length >= 2 && int.TryParse(args[1], out int parsedStructureCycles) && parsedStructureCycles > 0
                    ? parsedStructureCycles
                    : 12000000;
                DumpKickstartStructures(cycles);
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--diagnose-kickstart-display", StringComparison.OrdinalIgnoreCase))
            {
                int cycles = args.Length >= 2 && int.TryParse(args[1], out int parsedDiagnoseCycles) && parsedDiagnoseCycles > 0
                    ? parsedDiagnoseCycles
                    : 12000000;
                DiagnoseKickstartDisplay(cycles, ParseModelArgument(args, 2));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--scan-graphics-objects", StringComparison.OrdinalIgnoreCase))
            {
                int cycles = args.Length >= 2 && int.TryParse(args[1], out int parsedScanGraphicsCycles) && parsedScanGraphicsCycles > 0
                    ? parsedScanGraphicsCycles
                    : 12000000;
                int maxHits = args.Length >= 3 && int.TryParse(args[2], out int parsedScanGraphicsMaxHits) && parsedScanGraphicsMaxHits > 0
                    ? parsedScanGraphicsMaxHits
                    : 32;
                ScanGraphicsObjects(cycles, maxHits, ParseModelArgument(args, 3));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--dump-words", StringComparison.OrdinalIgnoreCase))
            {
                uint address = args.Length >= 2 ? Convert.ToUInt32(args[1].Replace("$", string.Empty), 16) : 0x00FDE600u;
                int words = args.Length >= 3 && int.TryParse(args[2], out int parsedWords) && parsedWords > 0
                    ? parsedWords
                    : 64;
                int cycles = args.Length >= 4 && int.TryParse(args[3], out int parsedDumpWordsCycles) && parsedDumpWordsCycles > 0
                    ? parsedDumpWordsCycles
                    : 0;
                DumpWordsCommand(address, words, cycles, ParseModelArgument(args, 4));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--dump-chip-words", StringComparison.OrdinalIgnoreCase))
            {
                uint address = args.Length >= 2 ? Convert.ToUInt32(args[1].Replace("$", string.Empty), 16) : 0x00000420;
                int words = args.Length >= 3 && int.TryParse(args[2], out int parsedChipWords) && parsedChipWords > 0
                    ? parsedChipWords
                    : 64;
                int cycles = args.Length >= 4 && int.TryParse(args[3], out int parsedChipCycles) && parsedChipCycles > 0
                    ? parsedChipCycles
                    : 10000000;
                DumpChipWords(address, words, cycles, ParseModelArgument(args, 4));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--dump-bus-words", StringComparison.OrdinalIgnoreCase))
            {
                uint address = args.Length >= 2 ? Convert.ToUInt32(args[1].Replace("$", string.Empty), 16) : 0x00000420;
                int words = args.Length >= 3 && int.TryParse(args[2], out int parsedBusWords) && parsedBusWords > 0
                    ? parsedBusWords
                    : 64;
                int cycles = args.Length >= 4 && int.TryParse(args[3], out int parsedBusCycles) && parsedBusCycles > 0
                    ? parsedBusCycles
                    : 10000000;
                DumpBusWords(address, words, cycles, ParseModelArgument(args, 4));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--trace-writes", StringComparison.OrdinalIgnoreCase))
            {
                uint start = args.Length >= 2 ? Convert.ToUInt32(args[1].Replace("$", string.Empty), 16) : 0x00000000;
                uint end = args.Length >= 3 ? Convert.ToUInt32(args[2].Replace("$", string.Empty), 16) : start + 0xFFFFu;
                int cycles = args.Length >= 4 && int.TryParse(args[3], out int parsedWriteCycles) && parsedWriteCycles > 0
                    ? parsedWriteCycles
                    : 10000000;
                int maxWrites = args.Length >= 5 && int.TryParse(args[4], out int parsedMaxWrites) && parsedMaxWrites > 0
                    ? parsedMaxWrites
                    : 256;
                int startCycle = args.Length >= 6 && int.TryParse(args[5], out int parsedStartCycle) && parsedStartCycle > 0
                    ? parsedStartCycle
                    : 0;
                TraceWrites(start, end, cycles, maxWrites, startCycle, false, ParseModelArgument(args, 6));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--trace-writes-context", StringComparison.OrdinalIgnoreCase))
            {
                uint start = args.Length >= 2 ? Convert.ToUInt32(args[1].Replace("$", string.Empty), 16) : 0x00000000;
                uint end = args.Length >= 3 ? Convert.ToUInt32(args[2].Replace("$", string.Empty), 16) : start + 0xFFFFu;
                int cycles = args.Length >= 4 && int.TryParse(args[3], out int parsedWriteContextCycles) && parsedWriteContextCycles > 0
                    ? parsedWriteContextCycles
                    : 10000000;
                int maxWrites = args.Length >= 5 && int.TryParse(args[4], out int parsedMaxContextWrites) && parsedMaxContextWrites > 0
                    ? parsedMaxContextWrites
                    : 256;
                int startCycle = args.Length >= 6 && int.TryParse(args[5], out int parsedContextStartCycle) && parsedContextStartCycle > 0
                    ? parsedContextStartCycle
                    : 0;
                TraceWrites(start, end, cycles, maxWrites, startCycle, true, ParseModelArgument(args, 6));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--trace-long-value", StringComparison.OrdinalIgnoreCase))
            {
                uint value = args.Length >= 2 ? Convert.ToUInt32(args[1].Replace("$", string.Empty), 16) : 0x00004CE8u;
                int cycles = args.Length >= 3 && int.TryParse(args[2], out int parsedLongValueCycles) && parsedLongValueCycles > 0
                    ? parsedLongValueCycles
                    : 12000000;
                int maxHits = args.Length >= 4 && int.TryParse(args[3], out int parsedLongValueHits) && parsedLongValueHits > 0
                    ? parsedLongValueHits
                    : 80;
                int startCycle = args.Length >= 5 && int.TryParse(args[4], out int parsedLongValueStartCycle) && parsedLongValueStartCycle > 0
                    ? parsedLongValueStartCycle
                    : 0;
                TraceLongValue(value, cycles, maxHits, startCycle, ParseModelArgument(args, 5));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--scan-long-value", StringComparison.OrdinalIgnoreCase))
            {
                uint value = args.Length >= 2 ? Convert.ToUInt32(args[1].Replace("$", string.Empty), 16) : 0x00004CE8u;
                int cycles = args.Length >= 3 && int.TryParse(args[2], out int parsedScanCycles) && parsedScanCycles > 0
                    ? parsedScanCycles
                    : 12000000;
                int maxHits = args.Length >= 4 && int.TryParse(args[3], out int parsedScanHits) && parsedScanHits > 0
                    ? parsedScanHits
                    : 128;
                ScanLongValue(value, cycles, maxHits, ParseModelArgument(args, 4));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--trace-reads", StringComparison.OrdinalIgnoreCase))
            {
                uint start = args.Length >= 2 ? Convert.ToUInt32(args[1].Replace("$", string.Empty), 16) : 0x00000000;
                uint end = args.Length >= 3 ? Convert.ToUInt32(args[2].Replace("$", string.Empty), 16) : start + 0xFFFFu;
                int cycles = args.Length >= 4 && int.TryParse(args[3], out int parsedReadCycles) && parsedReadCycles > 0
                    ? parsedReadCycles
                    : 10000000;
                int maxReads = args.Length >= 5 && int.TryParse(args[4], out int parsedMaxReads) && parsedMaxReads > 0
                    ? parsedMaxReads
                    : 256;
                int startCycle = args.Length >= 6 && int.TryParse(args[5], out int parsedReadStartCycle) && parsedReadStartCycle > 0
                    ? parsedReadStartCycle
                    : 0;
                TraceReads(start, end, cycles, maxReads, startCycle, false, ParseModelArgument(args, 6));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--trace-reads-context", StringComparison.OrdinalIgnoreCase))
            {
                uint start = args.Length >= 2 ? Convert.ToUInt32(args[1].Replace("$", string.Empty), 16) : 0x00000000;
                uint end = args.Length >= 3 ? Convert.ToUInt32(args[2].Replace("$", string.Empty), 16) : start + 0xFFFFu;
                int cycles = args.Length >= 4 && int.TryParse(args[3], out int parsedReadContextCycles) && parsedReadContextCycles > 0
                    ? parsedReadContextCycles
                    : 10000000;
                int maxReads = args.Length >= 5 && int.TryParse(args[4], out int parsedMaxContextReads) && parsedMaxContextReads > 0
                    ? parsedMaxContextReads
                    : 256;
                int startCycle = args.Length >= 6 && int.TryParse(args[5], out int parsedReadContextStartCycle) && parsedReadContextStartCycle > 0
                    ? parsedReadContextStartCycle
                    : 0;
                TraceReads(start, end, cycles, maxReads, startCycle, true, ParseModelArgument(args, 6));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--trace-odd-accesses", StringComparison.OrdinalIgnoreCase))
            {
                int cycles = args.Length >= 2 && int.TryParse(args[1], out int parsedOddCycles) && parsedOddCycles > 0
                    ? parsedOddCycles
                    : 12000000;
                int maxHits = args.Length >= 3 && int.TryParse(args[2], out int parsedOddHits) && parsedOddHits > 0
                    ? parsedOddHits
                    : 80;
                TraceOddAddressAccesses(cycles, maxHits, ParseModelArgument(args, 3));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--trace-custom-regs", StringComparison.OrdinalIgnoreCase))
            {
                int cycles = args.Length >= 2 && int.TryParse(args[1], out int parsedCustomCycles) && parsedCustomCycles > 0
                    ? parsedCustomCycles
                    : 20000000;
                int maxChanges = args.Length >= 3 && int.TryParse(args[2], out int parsedMaxChanges) && parsedMaxChanges > 0
                    ? parsedMaxChanges
                    : 512;
                int startCycle = args.Length >= 4 && int.TryParse(args[3], out int parsedCustomStartCycle) && parsedCustomStartCycle > 0
                    ? parsedCustomStartCycle
                    : 0;
                TraceCustomRegisters(cycles, maxChanges, startCycle, ParseModelArgument(args, 4));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--trace-blits", StringComparison.OrdinalIgnoreCase))
            {
                int cycles = args.Length >= 2 && int.TryParse(args[1], out int parsedBlitCycles) && parsedBlitCycles > 0
                    ? parsedBlitCycles
                    : 100000000;
                int maxBlits = args.Length >= 3 && int.TryParse(args[2], out int parsedMaxBlits) && parsedMaxBlits > 0
                    ? parsedMaxBlits
                    : 256;
                int startCycle = args.Length >= 4 && int.TryParse(args[3], out int parsedBlitStartCycle) && parsedBlitStartCycle > 0
                    ? parsedBlitStartCycle
                    : 0;
                uint filterStart = args.Length >= 5 ? Convert.ToUInt32(args[4].Replace("$", string.Empty), 16) : 0u;
                uint filterEnd = args.Length >= 6 ? Convert.ToUInt32(args[5].Replace("$", string.Empty), 16) : 0u;
                TraceBlits(cycles, maxBlits, startCycle, filterStart, filterEnd);
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--trace-blit-words", StringComparison.OrdinalIgnoreCase))
            {
                int cycles = args.Length >= 2 && int.TryParse(args[1], out int parsedBlitWordCycles) && parsedBlitWordCycles > 0
                    ? parsedBlitWordCycles
                    : 100000000;
                int maxWords = args.Length >= 3 && int.TryParse(args[2], out int parsedMaxBlitWords) && parsedMaxBlitWords > 0
                    ? parsedMaxBlitWords
                    : 256;
                int startCycle = args.Length >= 4 && int.TryParse(args[3], out int parsedBlitWordStartCycle) && parsedBlitWordStartCycle > 0
                    ? parsedBlitWordStartCycle
                    : 0;
                uint filterStart = args.Length >= 5 ? Convert.ToUInt32(args[4].Replace("$", string.Empty), 16) : 0u;
                uint filterEnd = args.Length >= 6 ? Convert.ToUInt32(args[5].Replace("$", string.Empty), 16) : 0u;
                TraceBlitWords(cycles, maxWords, startCycle, filterStart, filterEnd);
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--trace-copper", StringComparison.OrdinalIgnoreCase))
            {
                int cycles = args.Length >= 2 && int.TryParse(args[1], out int parsedCopperCycles) && parsedCopperCycles > 0
                    ? parsedCopperCycles
                    : 20000000;
                int maxInstructions = args.Length >= 3 && int.TryParse(args[2], out int parsedMaxCopperInstructions) && parsedMaxCopperInstructions > 0
                    ? parsedMaxCopperInstructions
                    : 512;
                int startCycle = args.Length >= 4 && int.TryParse(args[3], out int parsedCopperStartCycle) && parsedCopperStartCycle > 0
                    ? parsedCopperStartCycle
                    : 0;
                TraceCopper(cycles, maxInstructions, startCycle, ParseModelArgument(args, 4));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--run-until-pc", StringComparison.OrdinalIgnoreCase))
            {
                uint pc = args.Length >= 2 ? Convert.ToUInt32(args[1].Replace("$", string.Empty), 16) : 0x00FC30DA;
                int cycles = args.Length >= 3 && int.TryParse(args[2], out int parsedUntilPcCycles) && parsedUntilPcCycles > 0
                    ? parsedUntilPcCycles
                    : 100000000;
                int hit = args.Length >= 4 && int.TryParse(args[3], out int parsedPcHit) && parsedPcHit > 0
                    ? parsedPcHit
                    : 1;
                RunUntilPc(pc, cycles, hit);
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--run-until-display-setup", StringComparison.OrdinalIgnoreCase))
            {
                int cycles = args.Length >= 2 && int.TryParse(args[1], out int parsedDisplayCycles) && parsedDisplayCycles > 0
                    ? parsedDisplayCycles
                    : 120000000;
                RunUntilDisplaySetup(cycles);
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--profile-pc", StringComparison.OrdinalIgnoreCase))
            {
                int cycles = args.Length >= 2 && int.TryParse(args[1], out int parsedProfileCycles) && parsedProfileCycles > 0
                    ? parsedProfileCycles
                    : 20000000;
                int top = args.Length >= 3 && int.TryParse(args[2], out int parsedTop) && parsedTop > 0
                    ? parsedTop
                    : 24;
                ProfileProgramCounter(cycles, top);
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--trace-after-pc", StringComparison.OrdinalIgnoreCase))
            {
                uint pc = args.Length >= 2 ? Convert.ToUInt32(args[1].Replace("$", string.Empty), 16) : 0x00FC237E;
                int hit = args.Length >= 3 && int.TryParse(args[2], out int parsedTraceHit) && parsedTraceHit > 0
                    ? parsedTraceHit
                    : 1;
                int cycles = args.Length >= 4 && int.TryParse(args[3], out int parsedTraceAfterCycles) && parsedTraceAfterCycles > 0
                    ? parsedTraceAfterCycles
                    : 2000;
                string logPath = args.Length >= 5
                    ? args[4]
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "amiga_trace_after_pc.jsonl");
                TraceAfterPc(pc, hit, cycles, logPath, ParseModelArgument(args, 5));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--trace-before-pc", StringComparison.OrdinalIgnoreCase))
            {
                uint pc = args.Length >= 2 ? Convert.ToUInt32(args[1].Replace("$", string.Empty), 16) : 0x00FC30C2;
                int cycles = args.Length >= 3 && int.TryParse(args[2], out int parsedTraceBeforeCycles) && parsedTraceBeforeCycles > 0
                    ? parsedTraceBeforeCycles
                    : 10000000;
                int samples = args.Length >= 4 && int.TryParse(args[3], out int parsedTraceBeforeSamples) && parsedTraceBeforeSamples > 0
                    ? parsedTraceBeforeSamples
                    : 80;
                int hit = args.Length >= 5 && int.TryParse(args[4], out int parsedTraceBeforeHit) && parsedTraceBeforeHit > 0
                    ? parsedTraceBeforeHit
                    : 1;
                TraceBeforePc(pc, cycles, samples, hit, ParseModelArgument(args, 5));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--trace-adf-before-pc", StringComparison.OrdinalIgnoreCase))
            {
                string adfPath = args.Length >= 2 ? args[1] : string.Empty;
                uint pc = args.Length >= 3 ? Convert.ToUInt32(args[2].Replace("$", string.Empty), 16) : 0x00FC0F94;
                int cycles = args.Length >= 4 && int.TryParse(args[3], out int parsedAdfTraceBeforeCycles) && parsedAdfTraceBeforeCycles > 0
                    ? parsedAdfTraceBeforeCycles
                    : 10000000;
                int samples = args.Length >= 5 && int.TryParse(args[4], out int parsedAdfTraceBeforeSamples) && parsedAdfTraceBeforeSamples > 0
                    ? parsedAdfTraceBeforeSamples
                    : 80;
                int hit = args.Length >= 6 && int.TryParse(args[5], out int parsedAdfTraceBeforeHit) && parsedAdfTraceBeforeHit > 0
                    ? parsedAdfTraceBeforeHit
                    : 1;
                TraceAdfBeforePc(adfPath, pc, cycles, samples, hit, ParseModelArgument(args, 6));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--trace-adf-until-pc-range", StringComparison.OrdinalIgnoreCase))
            {
                string adfPath = args.Length >= 2 ? args[1] : string.Empty;
                uint minPc = args.Length >= 3 ? Convert.ToUInt32(args[2].Replace("$", string.Empty), 16) : 0x00070000;
                uint maxPc = args.Length >= 4 ? Convert.ToUInt32(args[3].Replace("$", string.Empty), 16) : 0x0007FFFF;
                int cycles = args.Length >= 5 && int.TryParse(args[4], out int parsedAdfRangeCycles) && parsedAdfRangeCycles > 0
                    ? parsedAdfRangeCycles
                    : 10000000;
                int samples = args.Length >= 6 && int.TryParse(args[5], out int parsedAdfRangeSamples) && parsedAdfRangeSamples > 0
                    ? parsedAdfRangeSamples
                    : 120;
                TraceAdfUntilPcRange(adfPath, minPc, maxPc, cycles, samples, ParseModelArgument(args, 6));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--trace-until-pc-range", StringComparison.OrdinalIgnoreCase))
            {
                uint minPc = args.Length >= 2 ? Convert.ToUInt32(args[1].Replace("$", string.Empty), 16) : 0x00070000;
                uint maxPc = args.Length >= 3 ? Convert.ToUInt32(args[2].Replace("$", string.Empty), 16) : 0x0007FFFF;
                int cycles = args.Length >= 4 && int.TryParse(args[3], out int parsedRangeCycles) && parsedRangeCycles > 0
                    ? parsedRangeCycles
                    : 10000000;
                int samples = args.Length >= 5 && int.TryParse(args[4], out int parsedRangeSamples) && parsedRangeSamples > 0
                    ? parsedRangeSamples
                    : 120;
                TraceUntilPcRange(minPc, maxPc, cycles, samples, ParseModelArgument(args, 5));
                return;
            }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--run-until-overlay-off", StringComparison.OrdinalIgnoreCase))
            {
                int cycles = args.Length >= 2 && int.TryParse(args[1], out int parsedOverlayCycles) && parsedOverlayCycles > 0
                    ? parsedOverlayCycles
                    : 10000000;
                RunUntilOverlayOff(cycles);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (var window = new AmigaWindow(AmigaModel.A500Pal, "Amiga Emulator"))
            {
                window.Run();
            }
        }

        /// <summary>
        /// Runs the ROM Check routine.
        /// </summary>
        private static void RunRomCheck(string[] args)
        {
            string romPath = args.Length >= 2 ? args[1] : KickstartRomResolver.EnsureKickstart13InUserRomDirectory();
            KickstartRomInfo info = KickstartRomResolver.Probe(romPath);
            Console.WriteLine(KickstartRomResolver.BuildStatusReport(romPath, info));
            Environment.ExitCode = info.IsUsable ? 0 : 1;
        }

        /// <summary>
        /// Runs the CPU Self Test routine.
        /// </summary>
        private static void RunCpuSelfTest()
        {
            var log = new StringBuilder();
            int failures = M68kSelfTest.Run(log);
            Console.Write(log.ToString());
            Environment.ExitCode = failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// Runs the Blitter Self Test routine.
        /// </summary>
        private static void RunBlitterSelfTest()
        {
            var log = new StringBuilder();
            int failures = BlitterSelfTest.Run(log);
            Console.Write(log.ToString());
            Environment.ExitCode = failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// Runs the Copper Self Test routine.
        /// </summary>
        private static void RunCopperSelfTest()
        {
            var log = new StringBuilder();
            int failures = CopperSelfTest.Run(log);
            Console.Write(log.ToString());
            Environment.ExitCode = failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// Runs the Disk Self Test routine.
        /// </summary>
        private static void RunDiskSelfTest()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "amiga_disk_selftest.adf");
            byte[] bytes = new byte[AmigaEmulator.Core.Media.AdfImage.StandardSize];
            for (int index = 0; index < bytes.Length; index++)
            {
                bytes[index] = (byte)(index & 0xFF);
            }

            File.WriteAllBytes(tempPath, bytes);
            int failures = 0;
            try
            {
                using (var system = new AmigaSystem(AmigaModel.PalA500, AmigaAccuracyOptions.Fast.Clone()))
                {
                    var image = AmigaEmulator.Core.Media.AdfImage.Load(tempPath);
                    system.Bus.FloppyController.InsertDrive0(image, true);
                    system.Bus.FloppyController.Drive0.ApplySelection(true, true);

                    uint dmaAddress = 0x00001000u;
                    system.Bus.WriteWord(0x00DFF020, (ushort)(dmaAddress >> 16));
                    system.Bus.WriteWord(0x00DFF022, (ushort)(dmaAddress & 0xFFFF));
                    system.Bus.WriteWord(0x00DFF07E, AdfTrackStream.SyncWord);
                    system.Bus.WriteWord(0x00DFF096, 0x8210);
                    /// <summary>
                    /// Defines the DMA WORDS custom register offset or bit mask.
                    /// </summary>
                    const int dmaWords = 768;
                    system.Bus.WriteWord(0x00DFF024, 0x8000 | dmaWords);
                    system.Bus.WriteWord(0x00DFF024, 0x8000 | dmaWords);
                    system.RunChipCycles(1000000);

                    bool foundSync = false;
                    for (int word = 0; word < dmaWords; word++)
                    {
                        ushort value = system.Bus.ReadWord(dmaAddress + (uint)(word * 2));
                        if (value == AdfTrackStream.SyncWord)
                        {
                            foundSync = true;
                            break;
                        }
                    }

                    bool dskBlk = (system.Agnus.CustomRegisters.InterruptRequest & (ushort)CustomInterruptSource.DskBlk) != 0;
                    if (!foundSync)
                    {
                        failures++;
                        Console.WriteLine("FAIL disk DMA did not transfer MFM sync word");
                        int firstNonGap = -1;
                        for (int word = 0; word < dmaWords; word++)
                        {
                            if (system.Bus.ReadWord(dmaAddress + (uint)(word * 2)) != AdfMfmEncoder.GapWord)
                            {
                                firstNonGap = word;
                                break;
                            }
                        }

                        Console.WriteLine("FirstNonGapWordIndex=" + firstNonGap);
                        Console.WriteLine("PaulaDisk=" + system.Paula.GetDiskDebugInfo());
                        Console.WriteLine("DF0=" + system.Bus.FloppyController.GetDebugInfo());
                        var preview = new StringBuilder();
                        for (int word = 0; word < 16; word++)
                        {
                            if (word > 0)
                            {
                                preview.Append(' ');
                            }

                            preview.Append(system.Bus.ReadWord(dmaAddress + (uint)(word * 2)).ToString("X4"));
                        }

                        Console.WriteLine("FirstWords=" + preview);
                    }

                    if (!dskBlk)
                    {
                        failures++;
                        Console.WriteLine("FAIL disk DMA did not request DSKBLK interrupt");
                    }

                }

                using (var system = new AmigaSystem(AmigaModel.PalA500, AmigaAccuracyOptions.Fast.Clone()))
                {
                    var image = AmigaEmulator.Core.Media.AdfImage.Load(tempPath);
                    system.Bus.FloppyController.InsertDrive0(image, true);
                    system.Bus.FloppyController.Drive0.ApplySelection(true, true);

                    uint dmaAddress = 0x00002000u;
                    system.Bus.WriteWord(0x00DFF020, (ushort)(dmaAddress >> 16));
                    system.Bus.WriteWord(0x00DFF022, (ushort)(dmaAddress & 0xFFFF));
                    system.Bus.WriteWord(0x00DFF07E, AdfTrackStream.SyncWord);
                    system.Bus.WriteWord(0x00DFF096, 0x8200);
                    /// <summary>
                    /// Defines the DMA WORDS custom register offset or bit mask.
                    /// </summary>
                    const int dmaWords = 64;
                    system.Bus.WriteWord(0x00DFF024, 0x8000 | dmaWords);
                    system.Bus.WriteWord(0x00DFF024, 0x8000 | dmaWords);
                    system.RunChipCycles(20000);

                    bool wroteAny = false;
                    for (int word = 0; word < dmaWords; word++)
                    {
                        if (system.Bus.ReadWord(dmaAddress + (uint)(word * 2)) != 0)
                        {
                            wroteAny = true;
                            break;
                        }
                    }

                    bool dskBlk = (system.Agnus.CustomRegisters.InterruptRequest & (ushort)CustomInterruptSource.DskBlk) != 0;
                    if (wroteAny || dskBlk)
                    {
                        failures++;
                        Console.WriteLine("FAIL disk DMA transferred while DMACON.DSKEN was clear");
                    }
                }

                using (var system = new AmigaSystem(AmigaModel.PalA500, AmigaAccuracyOptions.Fast.Clone()))
                {
                    var image = AmigaEmulator.Core.Media.AdfImage.Load(tempPath);
                    var track = image.BuildTrackStream(0, 0);
                    ushort expectedFirstDmaWord = 0;
                    int expectedFirstDmaWordIndex = -1;
                    for (int word = 1; word < track.WordCount - 1; word++)
                    {
                        if (track.ReadWord(word - 1) == AdfTrackStream.SyncWord &&
                            track.ReadWord(word) == AdfTrackStream.SyncWord)
                        {
                            expectedFirstDmaWord = track.ReadWord(word + 1);
                            expectedFirstDmaWordIndex = word + 1;
                            break;
                        }
                    }

                    system.Bus.FloppyController.InsertDrive0(image, true);
                    system.Bus.FloppyController.Drive0.ApplySelection(true, true);

                    uint dmaAddress = 0x00003000u;
                    system.Bus.WriteWord(0x00DFF020, (ushort)(dmaAddress >> 16));
                    system.Bus.WriteWord(0x00DFF022, (ushort)(dmaAddress & 0xFFFF));
                    system.Bus.WriteWord(0x00DFF07E, AdfTrackStream.SyncWord);
                    system.Bus.WriteWord(0x00DFF09E, 0x8400);
                    system.Bus.WriteWord(0x00DFF096, 0x8210);
                    /// <summary>
                    /// Defines the DMA WORDS custom register offset or bit mask.
                    /// </summary>
                    const int dmaWords = 900;
                    system.Bus.WriteWord(0x00DFF024, 0x8000 | dmaWords);
                    system.Bus.WriteWord(0x00DFF024, 0x8000 | dmaWords);
                    system.RunChipCycles(1000000);

                    ushort firstWord = system.Bus.ReadWord(dmaAddress);
                    bool dskSync = (system.Agnus.CustomRegisters.InterruptRequest & (ushort)CustomInterruptSource.DiskSync) != 0;
                    if (expectedFirstDmaWord == 0 || firstWord == AdfTrackStream.SyncWord || firstWord != expectedFirstDmaWord)
                    {
                        failures++;
                        Console.WriteLine("FAIL WORDSYNC did not start DMA after sync words. First=$" + firstWord.ToString("X4") + " Expected=$" + expectedFirstDmaWord.ToString("X4"));
                    }
                    else
                    {
                        int mismatch = -1;
                        for (int word = 0; word < dmaWords; word++)
                        {
                            ushort actual = system.Bus.ReadWord(dmaAddress + (uint)(word * 2));
                            ushort expected = track.ReadWord(expectedFirstDmaWordIndex + word);
                            if (actual != expected)
                            {
                                mismatch = word;
                                break;
                            }
                        }

                        if (mismatch >= 0)
                        {
                            failures++;
                            ushort actual = system.Bus.ReadWord(dmaAddress + (uint)(mismatch * 2));
                            ushort expected = track.ReadWord(expectedFirstDmaWordIndex + mismatch);
                            Console.WriteLine("FAIL WORDSYNC DMA stream skipped or changed a post-start word at +" + mismatch + ". Actual=$" + actual.ToString("X4") + " Expected=$" + expected.ToString("X4"));
                        }
                    }

                    if (!dskSync)
                    {
                        failures++;
                        Console.WriteLine("FAIL WORDSYNC did not request DSKSYNC interrupt");
                    }
                }

                using (var system = new AmigaSystem(AmigaModel.PalA500, AmigaAccuracyOptions.Fast.Clone()))
                {
                    var image = AmigaEmulator.Core.Media.AdfImage.Load(tempPath);
                    system.Bus.FloppyController.InsertDrive0(image, true);
                    system.Bus.FloppyController.Drive0.ApplySelection(true, true);
                    system.RunChipCycles(60);

                    ushort first = system.Bus.ReadWord(0x00DFF01A);
                    ushort second = system.Bus.ReadWord(0x00DFF01A);
                    if ((first & 0x8000) == 0 || (second & 0x8000) != 0)
                    {
                        failures++;
                        Console.WriteLine("FAIL DSKBYTR byte-ready bit did not clear on read");
                    }
                }

                var controller = new AmigaEmulator.Core.Media.FloppyController();
                controller.ApplyCiaBPort(0xFF, 0xFF);
                if (controller.Drive0.Selected || controller.Drive0.MotorOn)
                {
                    failures++;
                    Console.WriteLine("FAIL disk motor latch initial state");
                }

                controller.ApplyCiaBPort(0x7F, 0xFF);
                if (controller.Drive0.Selected || controller.Drive0.MotorOn)
                {
                    failures++;
                    Console.WriteLine("FAIL disk motor latched without drive select");
                }

                controller.ApplyCiaBPort(0x77, 0xFF);
                if (!controller.Drive0.Selected || !controller.Drive0.MotorOn)
                {
                    failures++;
                    Console.WriteLine("FAIL disk motor did not latch on when selected with motor line low");
                }

                controller.ApplyCiaBPort(0xFF, 0xFF);
                if (controller.Drive0.Selected || !controller.Drive0.MotorOn)
                {
                    failures++;
                    Console.WriteLine("FAIL disk motor latch did not persist while deselected");
                }

                controller.ApplyCiaBPort(0xF7, 0xFF);
                if (!controller.Drive0.Selected || controller.Drive0.MotorOn)
                {
                    failures++;
                    Console.WriteLine("FAIL disk motor did not latch off when selected with motor line high");
                }

                var stepController = new AmigaEmulator.Core.Media.FloppyController();
                stepController.ApplyCiaBPort(0x75, 0xFF);
                stepController.ApplyCiaBPort(0x74, 0xFF);
                stepController.ApplyCiaBPort(0x75, 0xFF);
                if (stepController.Drive0.Track != 1)
                {
                    failures++;
                    Console.WriteLine("FAIL disk step direction low did not move inward. Track=" + stepController.Drive0.Track);
                }

                stepController.ApplyCiaBPort(0x77, 0xFF);
                stepController.ApplyCiaBPort(0x76, 0xFF);
                stepController.ApplyCiaBPort(0x77, 0xFF);
                if (stepController.Drive0.Track != 0)
                {
                    failures++;
                    Console.WriteLine("FAIL disk step direction high did not move outward. Track=" + stepController.Drive0.Track);
                }

                if (failures == 0)
                {
                    Console.WriteLine("Disk self-test OK");
                }
            }
            finally
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                }
            }

            Environment.ExitCode = failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// Runs the Input Self Test routine.
        /// </summary>
        private static void RunInputSelfTest()
        {
            var log = new StringBuilder();
            int failures = AmigaInputSelfTest.Run(log);
            Console.Write(log.ToString());
            Environment.ExitCode = failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// Runs the CIA Self Test routine.
        /// </summary>
        private static void RunCiaSelfTest()
        {
            var log = new StringBuilder();
            int failures = CiaSelfTest.Run(log);
            Console.Write(log.ToString());
            Environment.ExitCode = failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// Runs the Benchmark routine.
        /// </summary>
        private static void RunBenchmark(int cycles, string profile)
        {
            AmigaAccuracyOptions accuracyOptions = ParseBenchmarkAccuracyOptions(profile);
            using (var system = new AmigaSystem(AmigaModel.PalA500, accuracyOptions))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                var started = DateTime.UtcNow;
                system.RunCycles(cycles);
                double seconds = Math.Max(0.000001, (DateTime.UtcNow - started).TotalSeconds);
                Console.WriteLine("AMIGA BENCHMARK");
                Console.WriteLine("Profile=" + accuracyOptions.Name);
                Console.WriteLine("Cycles=" + cycles);
                Console.WriteLine("ElapsedSeconds=" + seconds.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));
                Console.WriteLine("ChipCyclesPerSecond=" + (cycles / seconds).ToString("F0", System.Globalization.CultureInfo.InvariantCulture));
                Console.WriteLine("TargetChipCyclesPerSecond=" + AmigaModel.PalA500.ChipClockHz.ToString("F0", System.Globalization.CultureInfo.InvariantCulture));
                Console.WriteLine("HostSpeedPercent=" + (((cycles / seconds) / AmigaModel.PalA500.ChipClockHz) * 100.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                Console.WriteLine("Frames=" + system.FrameCounter);
            }
        }

        /// <summary>
        /// Executes the Parse Benchmark Accuracy Options logic.
        /// </summary>
        private static AmigaAccuracyOptions ParseBenchmarkAccuracyOptions(string profile)
        {
            if (string.Equals(profile, "strict", StringComparison.OrdinalIgnoreCase))
            {
                return AmigaAccuracyOptions.Strict.Clone();
            }

            if (string.Equals(profile, "compatible", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(profile, "compat", StringComparison.OrdinalIgnoreCase))
            {
                return AmigaAccuracyOptions.Compatible.Clone();
            }

            return AmigaAccuracyOptions.Fast.Clone();
        }

        /// <summary>
        /// Runs the Component Profile routine.
        /// </summary>
        private static void RunComponentProfile(int cycles, string profile)
        {
            AmigaAccuracyOptions accuracyOptions = ParseBenchmarkAccuracyOptions(profile);
            using (var system = new AmigaSystem(AmigaModel.PalA500, accuracyOptions))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                AmigaSystemComponentProfile result = system.ProfileChipCycles(cycles);
                Console.WriteLine("AMIGA COMPONENT PROFILE");
                Console.WriteLine("Profile=" + accuracyOptions.Name);
                Console.WriteLine("Cycles=" + result.Cycles);
                PrintProfileLine("Agnus", result.AgnusTicks, result);
                PrintProfileLine("Paula", result.PaulaTicks, result);
                PrintProfileLine("CIA", result.CiaTicks, result);
                PrintProfileLine("CPU", result.CpuTicks, result);
                PrintProfileLine("Denise", result.DeniseTicks, result);
                PrintProfileLine("Trace", result.TraceTicks, result);
                PrintProfileLine("Total", result.TotalTicks, result);
            }
        }

        /// <summary>
        /// Executes the Print Profile Line logic.
        /// </summary>
        private static void PrintProfileLine(string name, long ticks, AmigaSystemComponentProfile profile)
        {
            double seconds = ticks / (double)profile.StopwatchFrequency;
            double percent = profile.TotalTicks > 0 ? (ticks * 100.0) / profile.TotalTicks : 0.0;
            Console.WriteLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0}={1:F6}s {2:F1}%",
                name,
                seconds,
                percent));
        }

        /// <summary>
        /// Dumps Frame for diagnostics.
        /// </summary>
        private static void DumpFrame(int cycles, string imagePath, AmigaModel model)
        {
            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                system.RunCycles(cycles);
                string directory = Path.GetDirectoryName(Path.GetFullPath(imagePath));
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                AmigaEmulator.Core.Video.AmigaFrameBuffer frameBuffer = system.PresentedFrameBuffer;
                using (var bitmap = new Bitmap(frameBuffer.Width, frameBuffer.Height, PixelFormat.Format32bppArgb))
                {
                    uint[] pixels = frameBuffer.Pixels;
                    for (int y = 0; y < frameBuffer.Height; y++)
                    {
                        for (int x = 0; x < frameBuffer.Width; x++)
                        {
                            bitmap.SetPixel(x, y, Color.FromArgb(unchecked((int)pixels[(y * frameBuffer.Width) + x])));
                        }
                    }

                    bitmap.Save(imagePath, ImageFormat.Png);
                }
            }
        }

        /// <summary>
        /// Dumps a frame after restoring an emulator savestate.
        /// </summary>
        private static void DumpSavestateFrame(string savePath, string imagePath, int framesToRun, AmigaModel model)
        {
            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500, CreateNoTraceAccuracyOptions()))
            {
                SaveStateFile.Load(savePath, system);
                int frameCycles = system.Model.ChipCyclesPerLine * system.Model.LinesPerFrame;
                for (int frame = 0; frame < framesToRun; frame++)
                {
                    system.RunChipCycles(frameCycles);
                }

                SaveFrameBuffer(system.PresentedFrameBuffer, imagePath);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                Console.WriteLine("Beam=" + system.GetBeamDebugInfo());
                Console.WriteLine("DMA=" + system.GetDmaDebugInfo());
                Console.WriteLine("Blitter=busy:" + system.Agnus.Blitter.Busy + " pending:" + system.Agnus.Blitter.DmaPending);
                DumpVideoRegisterSummary(system);
            }
        }

        /// <summary>
        /// Prints the display-related custom registers for savestate diagnostics.
        /// </summary>
        private static void DumpVideoRegisterSummary(AmigaSystem system)
        {
            var registers = system.Agnus.CustomRegisters;
            Console.WriteLine("DMACON=$" + registers.ReadWord(0x096).ToString("X4"));
            Console.WriteLine("BPLCON0=$" + registers.ReadWord(0x100).ToString("X4"));
            Console.WriteLine("BPLCON1=$" + registers.ReadWord(0x102).ToString("X4"));
            Console.WriteLine("BPLCON2=$" + registers.ReadWord(0x104).ToString("X4"));
            Console.WriteLine("DIWSTRT=$" + registers.ReadWord(0x08E).ToString("X4"));
            Console.WriteLine("DIWSTOP=$" + registers.ReadWord(0x090).ToString("X4"));
            Console.WriteLine("DDFSTRT=$" + registers.ReadWord(0x092).ToString("X4"));
            Console.WriteLine("DDFSTOP=$" + registers.ReadWord(0x094).ToString("X4"));
            uint cop1 = ((uint)registers.ReadWord(0x080) << 16) | registers.ReadWord(0x082);
            uint cop2 = ((uint)registers.ReadWord(0x084) << 16) | registers.ReadWord(0x086);
            Console.WriteLine("COPPC=$" + system.Agnus.Copper.ProgramCounter.ToString("X8"));
            Console.WriteLine("COP1LC=$" + cop1.ToString("X8"));
            Console.WriteLine("COP2LC=$" + cop2.ToString("X8"));
            for (int index = 0; index < 6; index++)
            {
                uint address = ((uint)registers.ReadWord((uint)(0x0E0 + (index * 4))) << 16) | registers.ReadWord((uint)(0x0E2 + (index * 4)));
                Console.WriteLine("BPL" + (index + 1) + "PT=$" + address.ToString("X8"));
            }

            DumpChipWords(system, "COP1", cop1, 80);
            DumpChipWords(system, "COPPC", system.Agnus.Copper.ProgramCounter, 80);
        }

        /// <summary>
        /// Prints words from Chip RAM for display-list diagnostics.
        /// </summary>
        private static void DumpChipWords(AmigaSystem system, string label, uint address, int count)
        {
            Console.Write(label + " WORDS=");
            for (int index = 0; index < count; index++)
            {
                uint current = address + (uint)(index * 2);
                ushort word = (ushort)((system.Bus.ChipRam.Read(current) << 8) | system.Bus.ChipRam.Read(current + 1u));
                if (index != 0)
                {
                    Console.Write(",");
                }

                Console.Write("$" + word.ToString("X4"));
            }

            Console.WriteLine();
        }

        /// <summary>
        /// Writes a frame buffer to a PNG file.
        /// </summary>
        private static void SaveFrameBuffer(AmigaEmulator.Core.Video.AmigaFrameBuffer frameBuffer, string imagePath)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(imagePath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (var bitmap = new Bitmap(frameBuffer.Width, frameBuffer.Height, PixelFormat.Format32bppArgb))
            {
                uint[] pixels = frameBuffer.Pixels;
                for (int y = 0; y < frameBuffer.Height; y++)
                {
                    for (int x = 0; x < frameBuffer.Width; x++)
                    {
                        bitmap.SetPixel(x, y, Color.FromArgb(unchecked((int)pixels[(y * frameBuffer.Width) + x])));
                    }
                }

                bitmap.Save(imagePath, ImageFormat.Png);
            }
        }

        /// <summary>
        /// Dumps Frame With View List Poke for diagnostics.
        /// </summary>
        private static void DumpFrameWithViewListPoke(int cycles, int pokeCycle, string imagePath, AmigaModel model)
        {
            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500, CreateNoTraceAccuracyOptions()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                int before = Math.Min(cycles, pokeCycle);
                system.RunChipCycles(before);
                if (before < cycles)
                {
                    system.Bus.WriteLong(0x000049C0u, 0x00004CE8u);
                    system.RunChipCycles(cycles - before);
                }

                Console.WriteLine("POKE DIAG ViewList $49C0 <= $4CE8 at cycle " + before);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                DumpDisplayRegisters(system);
                DumpKickstartStructureSummary(system);
                SaveFrame(system, imagePath);
            }
        }

        /// <summary>
        /// Executes the Diagnose ADF Boot logic.
        /// </summary>
        private static void DiagnoseAdfBoot(string adfPath, int cycles, string imagePath, AmigaModel model)
        {
            if (string.IsNullOrWhiteSpace(adfPath) || !File.Exists(adfPath))
            {
                Console.WriteLine("ADF not found: " + adfPath);
                Environment.ExitCode = 2;
                return;
            }

            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500, AmigaAccuracyOptions.Fast.Clone()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                var image = AdfImage.Load(adfPath);
                system.Bus.FloppyController.InsertDrive0(image, true);
                system.RunChipCycles(cycles);
                SaveFrame(system, imagePath);

                DumpAdfBootDiagnostics(system, adfPath, cycles);
                Console.WriteLine("Frame written: " + Path.GetFullPath(imagePath));
            }
        }

        /// <summary>
        /// Executes the Diagnose ADF Boot Fire logic.
        /// </summary>
        private static void DiagnoseAdfBootFire(string adfPath, int preCycles, int postCycles, string imagePath, AmigaModel model)
        {
            if (string.IsNullOrWhiteSpace(adfPath) || !File.Exists(adfPath))
            {
                Console.WriteLine("ADF not found: " + adfPath);
                Environment.ExitCode = 2;
                return;
            }

            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500, AmigaAccuracyOptions.Fast.Clone()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                system.Bus.FloppyController.InsertDrive0(AdfImage.Load(adfPath), true);
                system.RunChipCycles(preCycles);
                system.Input.SetMouseButton(AmigaControlPort.Port0, AmigaJoystickButtons.Fire, true);
                system.Input.SetJoystickButton(AmigaControlPort.Port1, AmigaJoystickButtons.Fire, true);
                system.RunChipCycles(400000);
                system.Input.SetMouseButton(AmigaControlPort.Port0, AmigaJoystickButtons.Fire, false);
                system.Input.SetJoystickButton(AmigaControlPort.Port1, AmigaJoystickButtons.Fire, false);
                system.RunChipCycles(postCycles);
                SaveFrame(system, imagePath);

                DumpAdfBootDiagnostics(system, adfPath, preCycles + 400000 + postCycles);
                Console.WriteLine("InjectedFireAt=" + preCycles);
                Console.WriteLine("Frame written: " + Path.GetFullPath(imagePath));
            }
        }

        /// <summary>
        /// Dumps ADF Boot Diagnostics for diagnostics.
        /// </summary>
        private static void DumpAdfBootDiagnostics(AmigaSystem system, string adfPath, int cycles)
        {
            Console.WriteLine("ADF BOOT DIAG");
            Console.WriteLine("ADF=" + Path.GetFileName(adfPath));
            Console.WriteLine("Cycles=" + cycles);
            Console.WriteLine("Model=" + system.Model.Name);
            Console.WriteLine("Frame=" + system.FrameCounter);
            Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
            Console.WriteLine("Beam=" + system.GetBeamDebugInfo());
            Console.WriteLine("DMA=" + system.GetDmaDebugInfo());
            Console.WriteLine("PaulaDisk=" + system.Paula.GetDiskDebugInfo());
            Console.WriteLine("PaulaAudio=" + system.Paula.GetAudioDebugInfo());
            Console.WriteLine("DF0=" + system.Bus.FloppyController.GetDebugInfo());
            Console.WriteLine("CIAA PRA=$" + system.CiaA.Pra.ToString("X2") + " DDRA=$" + system.CiaA.Ddra.ToString("X2") + " PRA(read)=$" + system.Bus.ReadByte(0x00BFE001).ToString("X2"));
            Console.WriteLine("CIAB PRB=$" + system.CiaB.Prb.ToString("X2") + " DDRB=$" + system.CiaB.Ddrb.ToString("X2") + " PRB(read)=$" + system.Bus.ReadByte(0x00BFD100).ToString("X2"));
            Console.WriteLine("CIAA=" + system.CiaA.GetDebugInfo());
            Console.WriteLine("CIAB=" + system.CiaB.GetDebugInfo());
            Console.WriteLine("INTENA=$" + system.Agnus.CustomRegisters.InterruptEnable.ToString("X4") + " LEVEL=" + system.Agnus.CustomRegisters.ActiveInterruptLevel);
            Console.WriteLine("INTREQ=$" + system.Agnus.CustomRegisters.InterruptRequest.ToString("X4"));
            Console.WriteLine("DMACON=$" + system.Agnus.CustomRegisters.DmaControl.ToString("X4"));
            DumpCpuRegisters(system);
            DumpWords(system, system.Cpu.PC >= 16 ? system.Cpu.PC - 16u : system.Cpu.PC, 16, "PCCTX");
            DumpDisplayRegisters(system);
        }

        /// <summary>
        /// Executes the Diagnose ADF Bootblock logic.
        /// </summary>
        private static void DiagnoseAdfBootblock(string adfPath, int cycles, AmigaModel model)
        {
            if (string.IsNullOrWhiteSpace(adfPath) || !File.Exists(adfPath))
            {
                Console.WriteLine("ADF not found: " + adfPath);
                Environment.ExitCode = 2;
                return;
            }

            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500, AmigaAccuracyOptions.Fast.Clone()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                var image = AdfImage.Load(adfPath);
                byte[] bootblock = new byte[AdfImage.SectorSize * 2];
                image.ReadBytes(0, bootblock, 0, bootblock.Length);

                system.Bus.FloppyController.InsertDrive0(image, true);
                system.RunChipCycles(cycles);

                Console.WriteLine("ADF BOOTBLOCK DIAG");
                Console.WriteLine("ADF=" + Path.GetFileName(adfPath));
                Console.WriteLine("Cycles=" + cycles);
                Console.WriteLine("Model=" + system.Model.Name);
                Console.WriteLine("SourceChecksum=$" + ComputeAmigaBootblockChecksum(bootblock).ToString("X8"));
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                Console.WriteLine("PaulaDisk=" + system.Paula.GetDiskDebugInfo());
                Console.WriteLine("PaulaAudio=" + system.Paula.GetAudioDebugInfo());
                Console.WriteLine("DF0=" + system.Bus.FloppyController.GetDebugInfo());
                Console.WriteLine("Source[0..31]=" + FormatBytes(bootblock, 0, 32));
                DecodeBootblockFromGeneratedTrack(image, bootblock);

                ScanBootblockRange(system, bootblock, 0, (uint)system.Model.ChipRamBytes, "CHIP");
                ScanMfmBootblockRange(system, bootblock, 0, (uint)system.Model.ChipRamBytes, "CHIP-MFM");
                if (system.Model.SlowRamBytes > 0)
                {
                    ScanBootblockRange(system, bootblock, MemoryMap.SlowRamStart, (uint)system.Model.SlowRamBytes, "SLOW");
                    ScanMfmBootblockRange(system, bootblock, MemoryMap.SlowRamStart, (uint)system.Model.SlowRamBytes, "SLOW-MFM");
                }
            }
        }

        /// <summary>
        /// Executes the Diagnose ADF DMA Block logic.
        /// </summary>
        private static void DiagnoseAdfDmaBlock(string adfPath, int targetBlock, int maxCycles, AmigaModel model)
        {
            if (string.IsNullOrWhiteSpace(adfPath) || !File.Exists(adfPath))
            {
                Console.WriteLine("ADF not found: " + adfPath);
                Environment.ExitCode = 2;
                return;
            }

            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500, AmigaAccuracyOptions.Fast.Clone()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                var image = AdfImage.Load(adfPath);
                byte[] bootblock = new byte[AdfImage.SectorSize * 2];
                image.ReadBytes(0, bootblock, 0, bootblock.Length);
                system.Bus.FloppyController.InsertDrive0(image, true);

                int cycles = 0;
                while (cycles < maxCycles && system.Paula.DiskDmaCompletedBlocks < targetBlock)
                {
                    system.TickChipCycle();
                    cycles++;
                }

                Console.WriteLine("ADF DMA BLOCK DIAG");
                Console.WriteLine("ADF=" + Path.GetFileName(adfPath));
                Console.WriteLine("TargetBlock=" + targetBlock);
                Console.WriteLine("Cycles=" + cycles + "/" + maxCycles);
                Console.WriteLine("Model=" + system.Model.Name);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                Console.WriteLine("PaulaDisk=" + system.Paula.GetDiskDebugInfo());
                Console.WriteLine("DF0=" + system.Bus.FloppyController.GetDebugInfo());
                Console.WriteLine("INTREQ=$" + system.Agnus.CustomRegisters.InterruptRequest.ToString("X4"));
                Console.WriteLine("DMAEndPointer=$" + system.Paula.DiskPointer.ToString("X8"));
                uint dmaStart = system.Paula.LastDiskBlockWords > 0 ? system.Paula.LastDiskBlockStart : system.Paula.CurrentDiskBlockStart;
                int dmaWords = system.Paula.LastDiskBlockWords > 0 ? system.Paula.LastDiskBlockWords : system.Paula.RemainingDiskWords;
                Console.WriteLine("LastDmaStart=$" + system.Paula.LastDiskBlockStart.ToString("X8") + " LastDmaEnd=$" + system.Paula.LastDiskBlockEnd.ToString("X8") + " LastDmaWords=" + system.Paula.LastDiskBlockWords);
                Console.WriteLine("CurrentDmaStart=$" + system.Paula.CurrentDiskBlockStart.ToString("X8") + " DmaDumpStart=$" + dmaStart.ToString("X8") + " DmaDumpWords=" + dmaWords);
                DumpDmaWords(system, dmaStart, 64, "DMA");
                PrintMfmCandidates(system, dmaStart, Math.Min((uint)system.Model.ChipRamBytes - dmaStart, 0x4000u), 12);
                PrintRawWordPattern(system, dmaStart, Math.Min((uint)system.Model.ChipRamBytes - dmaStart, 0x4000u), new ushort[] { 0x552A, 0xAAA5, 0x552A, 0xAAA9 }, "S0 header");
                ScanMfmBootblockRange(system, bootblock, 0, (uint)system.Model.ChipRamBytes, "CHIP-MFM");
                if (system.Model.SlowRamBytes > 0)
                {
                    ScanMfmBootblockRange(system, bootblock, MemoryMap.SlowRamStart, (uint)system.Model.SlowRamBytes, "SLOW-MFM");
                }
            }
        }

        /// <summary>
        /// Executes the Print Raw Word Pattern logic.
        /// </summary>
        private static void PrintRawWordPattern(AmigaSystem system, uint start, uint length, ushort[] pattern, string label)
        {
            uint end = start + length - (uint)(pattern.Length * 2);
            for (uint address = start; address <= end; address += 2)
            {
                bool match = true;
                for (int index = 0; index < pattern.Length; index++)
                {
                    if (system.Bus.ReadWord(address + (uint)(index * 2)) != pattern[index])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    Console.WriteLine("Pattern " + label + " @$" + address.ToString("X8") + " raw=" + FormatWords(system, address, 12));
                    return;
                }
            }

            Console.WriteLine("Pattern " + label + " none");
        }

        /// <summary>
        /// Dumps DMA Words for diagnostics.
        /// </summary>
        private static void DumpDmaWords(AmigaSystem system, uint address, int words, string label)
        {
            var builder = new StringBuilder();
            for (int index = 0; index < words; index++)
            {
                if (index > 0)
                {
                    builder.Append(index % 16 == 0 ? Environment.NewLine : " ");
                }

                if (index % 16 == 0)
                {
                    builder.Append(label).Append("+").Append((index * 2).ToString("X4")).Append(" $").Append((address + (uint)(index * 2)).ToString("X8")).Append(": ");
                }

                builder.Append(system.Bus.ReadWord(address + (uint)(index * 2)).ToString("X4"));
            }

            Console.WriteLine(builder.ToString());
        }

        /// <summary>
        /// Executes the Print MFM Candidates logic.
        /// </summary>
        private static void PrintMfmCandidates(AmigaSystem system, uint start, uint length, int maxCandidates)
        {
            int printed = 0;
            uint end = start + length - 8u;
            for (uint address = start; address <= end && printed < maxCandidates; address += 2)
            {
                bool hasSync = system.Bus.ReadWord(address) == AdfTrackStream.SyncWord &&
                    system.Bus.ReadWord(address + 2u) == AdfTrackStream.SyncWord;
                uint headerAddress = hasSync ? address + 4u : address;
                uint header = DecodeOddEvenMfmLong(
                    ((uint)system.Bus.ReadWord(headerAddress) << 16) | system.Bus.ReadWord(headerAddress + 2u),
                    ((uint)system.Bus.ReadWord(headerAddress + 4u) << 16) | system.Bus.ReadWord(headerAddress + 6u));
                if (hasSync || (byte)(header >> 24) == 0xFF)
                {
                    Console.WriteLine("MFM candidate @$" + address.ToString("X8") +
                        " sync=" + hasSync +
                        " header@$" + headerAddress.ToString("X8") +
                        " decoded=$" + header.ToString("X8") +
                        " raw=" + FormatWords(system, address, 12));
                    printed++;
                }
            }

            if (printed == 0)
            {
                Console.WriteLine("MFM candidate none in $" + start.ToString("X8") + "..$" + (start + length).ToString("X8"));
            }
        }

        /// <summary>
        /// Executes the Format Words logic.
        /// </summary>
        private static string FormatWords(AmigaSystem system, uint address, int words)
        {
            var builder = new StringBuilder(words * 5);
            for (int index = 0; index < words; index++)
            {
                if (index > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(system.Bus.ReadWord(address + (uint)(index * 2)).ToString("X4"));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Decodes Bootblock From Generated Track from Amiga-compatible data.
        /// </summary>
        private static void DecodeBootblockFromGeneratedTrack(AdfImage image, byte[] expectedBootblock)
        {
            var track = image.BuildTrackStream(0, 0);
            byte[] decoded = new byte[AdfImage.SectorSize * 2];
            bool found0 = false;
            bool found1 = false;
            for (int wordIndex = 0; wordIndex < track.WordCount - AdfMfmEncoder.SectorWords; wordIndex++)
            {
                bool hasSync = track.ReadWord(wordIndex) == AdfTrackStream.SyncWord &&
                    track.ReadWord(wordIndex + 1) == AdfTrackStream.SyncWord;
                if (hasSync && !found0 && !found1)
                {
                    Console.WriteLine("GENERATED-MFM first raw=" + FormatTrackWords(track, wordIndex, 12));
                }

                int headerIndex = hasSync ? wordIndex + 2 : wordIndex;
                if (TryDecodeAmigaDosSector(
                    offset => track.ReadWord(headerIndex + offset),
                    out byte logicalTrack,
                    out byte sector,
                    out byte[] data))
                {
                    if (logicalTrack == 0 && sector < 2)
                    {
                        Buffer.BlockCopy(data, 0, decoded, sector * AdfImage.SectorSize, AdfImage.SectorSize);
                        found0 |= sector == 0;
                        found1 |= sector == 1;
                    }
                }
            }

            int diff = found0 && found1 ? CountByteDifferences(decoded, expectedBootblock) : expectedBootblock.Length;
            Console.WriteLine("GENERATED-MFM S0=" + found0 + " S1=" + found1 + " Diff=" + diff +
                " Checksum=$" + ComputeAmigaBootblockChecksum(decoded).ToString("X8"));
        }

        /// <summary>
        /// Executes the Format Track Words logic.
        /// </summary>
        private static string FormatTrackWords(AdfTrackStream track, int startWord, int words)
        {
            var builder = new StringBuilder(words * 5);
            for (int index = 0; index < words; index++)
            {
                if (index > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(track.ReadWord(startWord + index).ToString("X4"));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Scans MFM Bootblock Range for diagnostic output.
        /// </summary>
        private static void ScanMfmBootblockRange(AmigaSystem system, byte[] expectedBootblock, uint start, uint length, string label)
        {
            if (length < AdfMfmEncoder.SectorWords * 2)
            {
                Console.WriteLine(label + " no range");
                return;
            }

            byte[] decoded = new byte[AdfImage.SectorSize * 2];
            uint sector0Address = 0;
            uint sector1Address = 0;
            bool found0 = false;
            bool found1 = false;
            uint end = start + length - (uint)(AdfMfmEncoder.SectorWords * 2);

            for (uint address = start; address <= end; address += 2)
            {
                uint headerAddress = address;
                bool hasSync = system.Bus.ReadWord(address) == AdfTrackStream.SyncWord &&
                    system.Bus.ReadWord(address + 2u) == AdfTrackStream.SyncWord;
                if (hasSync)
                {
                    headerAddress = address + 4u;
                }

                if (!TryDecodeAmigaDosSector(
                    offset => system.Bus.ReadWord(headerAddress + (uint)(offset * 2)),
                    out byte logicalTrack,
                    out byte sector,
                    out byte[] data))
                {
                    continue;
                }

                if (logicalTrack != 0 || sector > 1)
                {
                    continue;
                }

                Buffer.BlockCopy(data, 0, decoded, sector * AdfImage.SectorSize, AdfImage.SectorSize);
                if (sector == 0 && !found0)
                {
                    sector0Address = headerAddress;
                    found0 = true;
                }
                else if (sector == 1 && !found1)
                {
                    sector1Address = headerAddress;
                    found1 = true;
                }

                if (found0 && found1)
                {
                    break;
                }
            }

            int diff = found0 && found1 ? CountByteDifferences(decoded, expectedBootblock) : expectedBootblock.Length;
            Console.WriteLine(label + " S0=" + found0 + " @$" + sector0Address.ToString("X8") +
                " S1=" + found1 + " @$" + sector1Address.ToString("X8") +
                " Diff=" + diff +
                " Checksum=$" + ComputeAmigaBootblockChecksum(decoded).ToString("X8"));
            if (found0 || found1)
            {
                Console.WriteLine(label + " Decoded[0..31]=" + FormatBytes(decoded, 0, 32));
            }
        }

        /// <summary>
        /// Attempts to Decode Amiga Dos Sector and reports whether it succeeded.
        /// </summary>
        private static bool TryDecodeAmigaDosSector(Func<int, ushort> readWord, out byte logicalTrack, out byte sector, out byte[] data)
        {
            logicalTrack = 0;
            sector = 0;
            data = null;

            uint header = DecodeOddEvenMfmLong(ReadMfmLong(readWord, 0), ReadMfmLong(readWord, 2));
            if ((byte)(header >> 24) != 0xFF)
            {
                return false;
            }

            logicalTrack = (byte)(header >> 16);
            sector = (byte)(header >> 8);
            if (sector >= AdfImage.SectorsPerTrack)
            {
                return false;
            }

            uint storedHeaderChecksum = DecodeOddEvenMfmLong(ReadMfmLong(readWord, 20), ReadMfmLong(readWord, 22));
            uint storedDataChecksum = DecodeOddEvenMfmLong(ReadMfmLong(readWord, 24), ReadMfmLong(readWord, 26));
            uint computedHeaderChecksum = ComputeMfmChecksum(readWord, 0, 10);
            uint computedDataChecksum = ComputeMfmChecksum(readWord, 28, AdfImage.SectorSize / 2);
            if (storedHeaderChecksum != computedHeaderChecksum || storedDataChecksum != computedDataChecksum)
            {
                return false;
            }

            data = new byte[AdfImage.SectorSize];
            /// <summary>
            /// Defines the data Offset Words constant.
            /// </summary>
            const int dataOffsetWords = 28;
            /// <summary>
            /// Defines the data Longs constant.
            /// </summary>
            const int dataLongs = AdfImage.SectorSize / 4;
            /// <summary>
            /// Defines the even Data Offset Words constant.
            /// </summary>
            const int evenDataOffsetWords = dataOffsetWords + (dataLongs * 2);
            for (int index = 0; index < dataLongs; index++)
            {
                uint odd = ReadMfmLong(readWord, dataOffsetWords + (index * 2));
                uint even = ReadMfmLong(readWord, evenDataOffsetWords + (index * 2));
                uint value = DecodeOddEvenMfmLong(odd, even);
                int byteOffset = index * 4;
                data[byteOffset] = (byte)(value >> 24);
                data[byteOffset + 1] = (byte)(value >> 16);
                data[byteOffset + 2] = (byte)(value >> 8);
                data[byteOffset + 3] = (byte)value;
            }

            return true;
        }

        /// <summary>
        /// Executes the Compute MFM Checksum logic.
        /// </summary>
        private static uint ComputeMfmChecksum(Func<int, ushort> readWord, int offsetWords, int encodedLongCount)
        {
            uint checksum = 0;
            for (int index = 0; index < encodedLongCount; index++)
            {
                checksum ^= ReadMfmLong(readWord, offsetWords + (index * 2)) & 0x55555555u;
            }

            return checksum & 0x55555555u;
        }

        /// <summary>
        /// Reads two consecutive MFM words and combines them into one encoded longword.
        /// </summary>
        private static uint ReadMfmLong(Func<int, ushort> readWord, int offsetWords)
        {
            return ((uint)readWord(offsetWords) << 16) | readWord(offsetWords + 1);
        }

        /// <summary>
        /// Decodes Odd Even MFM Long from Amiga-compatible data.
        /// </summary>
        private static uint DecodeOddEvenMfmLong(uint oddMfmLong, uint evenMfmLong)
        {
            return ((oddMfmLong & 0x55555555u) << 1) | (evenMfmLong & 0x55555555u);
        }

        /// <summary>
        /// Scans Bootblock Range for diagnostic output.
        /// </summary>
        private static void ScanBootblockRange(AmigaSystem system, byte[] bootblock, uint start, uint length, string label)
        {
            if (length < bootblock.Length)
            {
                Console.WriteLine(label + " no range");
                return;
            }

            uint bestAddress = start;
            int bestPrefix = -1;
            int bestWindowDiff = bootblock.Length + 1;
            uint exactAddress = 0;
            bool exact = false;
            uint end = start + length - (uint)bootblock.Length;

            for (uint address = start; address <= end; address += 2)
            {
                int prefix = CountMatchingPrefix(system, address, bootblock);
                if (prefix == bootblock.Length)
                {
                    exactAddress = address;
                    exact = true;
                    bestAddress = address;
                    bestPrefix = prefix;
                    bestWindowDiff = 0;
                    break;
                }

                if (prefix > bestPrefix)
                {
                    bestPrefix = prefix;
                    bestAddress = address;
                    bestWindowDiff = CountDifferences(system, address, bootblock);
                }
            }

            if (!exact && bestPrefix < 8)
            {
                uint dosAddress;
                if (TryFindAscii(system, start, length, "DOS", out dosAddress))
                {
                    int prefix = CountMatchingPrefix(system, dosAddress, bootblock);
                    int diff = CountDifferences(system, dosAddress, bootblock);
                    if (prefix > bestPrefix || (prefix == bestPrefix && diff < bestWindowDiff))
                    {
                        bestAddress = dosAddress;
                        bestPrefix = prefix;
                        bestWindowDiff = diff;
                    }
                }
            }

            Console.WriteLine(label + " Exact=" + exact + " Address=$" + (exact ? exactAddress : bestAddress).ToString("X8") +
                " Prefix=" + bestPrefix + " Diff=" + bestWindowDiff +
                " Checksum=$" + ComputeAmigaBootblockChecksum(ReadBusBlock(system, exact ? exactAddress : bestAddress, bootblock.Length)).ToString("X8"));
            Console.WriteLine(label + " RAM[0..31]=" + FormatBytes(ReadBusBlock(system, exact ? exactAddress : bestAddress, 32), 0, 32));
        }

        /// <summary>
        /// Attempts to Find Ascii and reports whether it succeeded.
        /// </summary>
        private static bool TryFindAscii(AmigaSystem system, uint start, uint length, string ascii, out uint address)
        {
            address = 0;
            if (string.IsNullOrEmpty(ascii) || length < ascii.Length)
            {
                return false;
            }

            uint end = start + length - (uint)ascii.Length;
            for (uint current = start; current <= end; current++)
            {
                bool match = true;
                for (int index = 0; index < ascii.Length; index++)
                {
                    if (system.Bus.ReadByte(current + (uint)index) != (byte)ascii[index])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    address = current;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Executes the Count Matching Prefix logic.
        /// </summary>
        private static int CountMatchingPrefix(AmigaSystem system, uint address, byte[] expected)
        {
            for (int index = 0; index < expected.Length; index++)
            {
                if (system.Bus.ReadByte(address + (uint)index) != expected[index])
                {
                    return index;
                }
            }

            return expected.Length;
        }

        /// <summary>
        /// Executes the Count Differences logic.
        /// </summary>
        private static int CountDifferences(AmigaSystem system, uint address, byte[] expected)
        {
            int differences = 0;
            for (int index = 0; index < expected.Length; index++)
            {
                if (system.Bus.ReadByte(address + (uint)index) != expected[index])
                {
                    differences++;
                }
            }

            return differences;
        }

        /// <summary>
        /// Executes the Count Byte Differences logic.
        /// </summary>
        private static int CountByteDifferences(byte[] actual, byte[] expected)
        {
            int length = Math.Min(actual.Length, expected.Length);
            int differences = Math.Abs(actual.Length - expected.Length);
            for (int index = 0; index < length; index++)
            {
                if (actual[index] != expected[index])
                {
                    differences++;
                }
            }

            return differences;
        }

        /// <summary>
        /// Captures a contiguous block of bytes through the emulated Amiga bus.
        /// </summary>
        private static byte[] ReadBusBlock(AmigaSystem system, uint address, int length)
        {
            byte[] data = new byte[length];
            for (int index = 0; index < data.Length; index++)
            {
                data[index] = system.Bus.ReadByte(address + (uint)index);
            }

            return data;
        }

        /// <summary>
        /// Executes the Compute Amiga Bootblock Checksum logic.
        /// </summary>
        private static uint ComputeAmigaBootblockChecksum(byte[] bytes)
        {
            uint sum = 0;
            int length = Math.Min(bytes.Length, AdfImage.SectorSize * 2);
            length &= ~3;
            for (int offset = 0; offset < length; offset += 4)
            {
                uint word = ((uint)bytes[offset] << 24) |
                    ((uint)bytes[offset + 1] << 16) |
                    ((uint)bytes[offset + 2] << 8) |
                    bytes[offset + 3];
                uint previous = sum;
                sum += word;
                if (sum < previous)
                {
                    sum++;
                }
            }

            return sum;
        }

        /// <summary>
        /// Executes the Format Bytes logic.
        /// </summary>
        private static string FormatBytes(byte[] bytes, int offset, int count)
        {
            var builder = new StringBuilder(count * 3);
            int end = Math.Min(bytes.Length, offset + count);
            for (int index = offset; index < end; index++)
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(bytes[index].ToString("X2"));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Traces Floppy CIA for diagnostics.
        /// </summary>
        private static void TraceFloppyCia(string adfPath, int cycles, int maxLines, AmigaModel model)
        {
            bool hasAdf = !string.IsNullOrWhiteSpace(adfPath) && adfPath != "-";
            if (hasAdf && !File.Exists(adfPath))
            {
                Console.WriteLine("ADF not found: " + adfPath);
                Environment.ExitCode = 2;
                return;
            }

            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500, AmigaAccuracyOptions.Fast.Clone()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                if (hasAdf)
                {
                    system.Bus.FloppyController.InsertDrive0(AdfImage.Load(adfPath), true);
                }

                int lines = 0;
                system.Bus.WriteObserved += (address, size, value, region) =>
                {
                    if (lines >= maxLines)
                    {
                        return;
                    }

                    uint offset = address & 0x00000F00u;
                    bool traceCiaBFloppy = region == AmigaAddressRegion.CiaB && (offset == 0x0100u || offset == 0x0300u);
                    bool traceCiaControl = (region == AmigaAddressRegion.CiaA || region == AmigaAddressRegion.CiaB) &&
                        (offset == 0x0400u || offset == 0x0500u || offset == 0x0600u || offset == 0x0700u || offset == 0x0D00u || offset == 0x0E00u || offset == 0x0F00u);
                    bool traceInterruptRegs = region == AmigaAddressRegion.CustomRegisters &&
                        ((address & 0x000001FEu) == 0x009Au || (address & 0x000001FEu) == 0x009Cu);
                    if (!traceCiaBFloppy && !traceCiaControl && !traceInterruptRegs)
                    {
                        return;
                    }

                    lines++;
                    string register = GetTraceRegisterName(region, offset, address);
                    byte ciaaPra = system.Bus.ReadByte(0x00BFE001);
                    Console.WriteLine(string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{0,10} F{1,4} {2,3},{3,3} {4}.{5} <= ${6:X4} INTENA=${7:X4} INTREQ=${8:X4} L{9} CIAA=${10:X2} {11} {12} DF0={13} Paula={14}",
                        system.Agnus.Beam.GlobalChipCycle,
                        system.Agnus.Beam.Frame,
                        system.Agnus.Beam.BeamX,
                        system.Agnus.Beam.BeamY,
                        region,
                        register,
                        value,
                        system.Agnus.CustomRegisters.InterruptEnable,
                        system.Agnus.CustomRegisters.InterruptRequest,
                        system.Agnus.CustomRegisters.ActiveInterruptLevel,
                        ciaaPra,
                        system.CiaA.GetDebugInfo(),
                        system.CiaB.GetDebugInfo(),
                        system.Bus.FloppyController.GetDebugInfo(),
                        system.Paula.GetDiskShortDebugInfo()));
                };

                system.RunChipCycles(cycles);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                Console.WriteLine("PaulaDisk=" + system.Paula.GetDiskDebugInfo());
                Console.WriteLine("DF0=" + system.Bus.FloppyController.GetDebugInfo());
                Console.WriteLine("CIAA PRA(read)=$" + system.Bus.ReadByte(0x00BFE001).ToString("X2"));
                Console.WriteLine("CIAB PRB=$" + system.CiaB.Prb.ToString("X2") + " DDRB=$" + system.CiaB.Ddrb.ToString("X2"));
                Console.WriteLine("CIAA=" + system.CiaA.GetDebugInfo());
                Console.WriteLine("CIAB=" + system.CiaB.GetDebugInfo());
                Console.WriteLine("INTENA=$" + system.Agnus.CustomRegisters.InterruptEnable.ToString("X4") + " INTREQ=$" + system.Agnus.CustomRegisters.InterruptRequest.ToString("X4") + " LEVEL=" + system.Agnus.CustomRegisters.ActiveInterruptLevel);
            }
        }

        /// <summary>
        /// Traces Floppy Probe for diagnostics.
        /// </summary>
        private static void TraceFloppyProbe(string adfPath, int cycles, int maxLines, AmigaModel model)
        {
            bool hasAdf = !string.IsNullOrWhiteSpace(adfPath) && adfPath != "-";
            if (hasAdf && !File.Exists(adfPath))
            {
                Console.WriteLine("ADF not found: " + adfPath);
                Environment.ExitCode = 2;
                return;
            }

            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500, AmigaAccuracyOptions.Fast.Clone()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                if (hasAdf)
                {
                    system.Bus.FloppyController.InsertDrive0(AdfImage.Load(adfPath), true);
                }

                int lines = 0;
                byte lastCiaARead = 0xFF;
                byte lastPrb = system.CiaB.Prb;
                bool lastSelected = system.Bus.FloppyController.Drive0.Selected;
                bool lastMotor = system.Bus.FloppyController.Drive0.MotorOn;

                system.Bus.WriteObserved += delegate(uint address, int size, uint value, AmigaAddressRegion region)
                {
                    if (lines >= maxLines || region != AmigaAddressRegion.CiaB)
                    {
                        return;
                    }

                    uint offset = address & 0x00000F00u;
                    if (offset != 0x0100u && offset != 0x0300u)
                    {
                        return;
                    }

                    bool selected = system.Bus.FloppyController.Drive0.Selected;
                    bool motor = system.Bus.FloppyController.Drive0.MotorOn;
                    if (system.CiaB.Prb == lastPrb && selected == lastSelected && motor == lastMotor)
                    {
                        return;
                    }

                    lastPrb = system.CiaB.Prb;
                    lastSelected = selected;
                    lastMotor = motor;
                    PrintFloppyProbeLine(system, "W", region, address, value, ref lines);
                };

                system.Bus.ReadObserved += delegate(uint address, int size, uint value, AmigaAddressRegion region)
                {
                    if (lines >= maxLines || region != AmigaAddressRegion.CiaA)
                    {
                        return;
                    }

                    uint offset = address & 0x00000F00u;
                    if (offset != 0x0000u)
                    {
                        return;
                    }

                    byte readValue = (byte)(value & 0xFF);
                    bool selected = system.Bus.FloppyController.Drive0.Selected;
                    if (!selected && readValue == lastCiaARead)
                    {
                        return;
                    }

                    lastCiaARead = readValue;
                    PrintFloppyProbeLine(system, "R", region, address, readValue, ref lines);
                };

                system.RunChipCycles(cycles);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                Console.WriteLine("PaulaDisk=" + system.Paula.GetDiskDebugInfo());
                Console.WriteLine("DF0=" + system.Bus.FloppyController.GetDebugInfo());
                Console.WriteLine("CIAA=" + system.CiaA.GetDebugInfo());
                Console.WriteLine("CIAB=" + system.CiaB.GetDebugInfo());
                Console.WriteLine("INTENA=$" + system.Agnus.CustomRegisters.InterruptEnable.ToString("X4") + " INTREQ=$" + system.Agnus.CustomRegisters.InterruptRequest.ToString("X4") + " LEVEL=" + system.Agnus.CustomRegisters.ActiveInterruptLevel);
            }
        }

        /// <summary>
        /// Executes the Print Floppy Probe Line logic.
        /// </summary>
        private static void PrintFloppyProbeLine(AmigaSystem system, string access, AmigaAddressRegion region, uint address, uint value, ref int lines)
        {
            lines++;
            Console.WriteLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0,10} F{1,4} {2,3},{3,3} {4} PC ${5:X6} OP ${6:X4} {7} ${8:X6}=${9:X2} PRB=${10:X2} DDRB=${11:X2} CIAA=${12:X2} DF0={13} Paula={14} {15}",
                system.Agnus.Beam.GlobalChipCycle,
                system.Agnus.Beam.Frame,
                system.Agnus.Beam.BeamX,
                system.Agnus.Beam.BeamY,
                access,
                system.Cpu.CurrentInstructionAddress,
                system.Cpu.CurrentOpcode,
                region,
                address,
                value & 0xFF,
                system.CiaB.Prb,
                system.CiaB.Ddrb,
                system.Bus.FloppyController.BuildCiaAStatusBits(),
                system.Bus.FloppyController.GetDebugInfo(),
                system.Paula.GetDiskShortDebugInfo(),
                system.Cpu.LastInstruction ?? string.Empty));
        }

        /// <summary>
        /// Returns the Trace Register Name.
        /// </summary>
        private static string GetTraceRegisterName(AmigaAddressRegion region, uint ciaOffset, uint address)
        {
            if (region == AmigaAddressRegion.CustomRegisters)
            {
                uint customOffset = address & 0x000001FEu;
                if (customOffset == 0x009Au)
                {
                    return "INTENA";
                }

                if (customOffset == 0x009Cu)
                {
                    return "INTREQ";
                }

                return "$" + customOffset.ToString("X3");
            }

            switch (ciaOffset)
            {
                case 0x0000u:
                    return "PRA";
                case 0x0100u:
                    return "PRB";
                case 0x0200u:
                    return "DDRA";
                case 0x0300u:
                    return "DDRB";
                case 0x0400u:
                    return "TALO";
                case 0x0500u:
                    return "TAHI";
                case 0x0600u:
                    return "TBLO";
                case 0x0700u:
                    return "TBHI";
                case 0x0D00u:
                    return "ICR";
                case 0x0E00u:
                    return "CRA";
                case 0x0F00u:
                    return "CRB";
                default:
                    return "$" + ciaOffset.ToString("X4");
            }
        }

        /// <summary>
        /// Executes the Diagnose ADF Insert After logic.
        /// </summary>
        private static void DiagnoseAdfInsertAfter(string adfPath, int preCycles, int postCycles, string imagePath, AmigaModel model)
        {
            if (string.IsNullOrWhiteSpace(adfPath) || !File.Exists(adfPath))
            {
                Console.WriteLine("ADF not found: " + adfPath);
                Environment.ExitCode = 2;
                return;
            }

            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500, AmigaAccuracyOptions.Fast.Clone()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                system.RunChipCycles(preCycles);
                system.Bus.FloppyController.InsertDrive0(AdfImage.Load(adfPath), true);
                system.RunChipCycles(postCycles);
                SaveFrame(system, imagePath);

                Console.WriteLine("ADF INSERT-AFTER DIAG");
                Console.WriteLine("ADF=" + Path.GetFileName(adfPath));
                Console.WriteLine("PreCycles=" + preCycles);
                Console.WriteLine("PostCycles=" + postCycles);
                Console.WriteLine("Model=" + system.Model.Name);
                Console.WriteLine("Frame=" + system.FrameCounter);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                Console.WriteLine("Beam=" + system.GetBeamDebugInfo());
                Console.WriteLine("DMA=" + system.GetDmaDebugInfo());
                Console.WriteLine("PaulaDisk=" + system.Paula.GetDiskDebugInfo());
                Console.WriteLine("PaulaAudio=" + system.Paula.GetAudioDebugInfo());
                Console.WriteLine("DF0=" + system.Bus.FloppyController.GetDebugInfo());
                Console.WriteLine("CIAA PRA=$" + system.CiaA.Pra.ToString("X2") + " DDRA=$" + system.CiaA.Ddra.ToString("X2") + " PRA(read)=$" + system.Bus.ReadByte(0x00BFE001).ToString("X2"));
                Console.WriteLine("CIAB PRB=$" + system.CiaB.Prb.ToString("X2") + " DDRB=$" + system.CiaB.Ddrb.ToString("X2") + " PRB(read)=$" + system.Bus.ReadByte(0x00BFD100).ToString("X2"));
                Console.WriteLine("INTREQ=$" + system.Agnus.CustomRegisters.InterruptRequest.ToString("X4"));
                Console.WriteLine("DMACON=$" + system.Agnus.CustomRegisters.DmaControl.ToString("X4"));
                DumpDisplayRegisters(system);
                Console.WriteLine("Frame written: " + Path.GetFullPath(imagePath));
            }
        }

        /// <summary>
        /// Traces ADF Until Fault for diagnostics.
        /// </summary>
        private static void TraceAdfUntilFault(string adfPath, int cycles, string logPath, AmigaModel model)
        {
            if (string.IsNullOrWhiteSpace(adfPath) || !File.Exists(adfPath))
            {
                Console.WriteLine("ADF not found: " + adfPath);
                Environment.ExitCode = 2;
                return;
            }

            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500, AmigaAccuracyOptions.Compatible.Clone()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                system.Bus.FloppyController.InsertDrive0(AdfImage.Load(adfPath), true);
                MachineTraceSample fault = MachineTraceExporter.ExportJsonLinesUntilSuspiciousInstruction(system, cycles, logPath);
                Console.WriteLine("ADF FAULT TRACE");
                Console.WriteLine("ADF=" + Path.GetFileName(adfPath));
                Console.WriteLine("Cycles=" + cycles);
                Console.WriteLine("Model=" + system.Model.Name);
                Console.WriteLine("Trace written: " + Path.GetFullPath(logPath));
                if (fault == null || fault.CpuTrace == null)
                {
                    Console.WriteLine("No suspicious CPU instruction found.");
                }
                else
                {
                    Console.WriteLine(string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "First suspicious instruction: cycle={0} pc=${1:X6} opcode=${2:X4} instruction={3}",
                        fault.GlobalChipCycle,
                        fault.CpuTrace.PcBefore,
                        fault.CpuTrace.Opcode,
                        fault.CpuTrace.Instruction));
                }

                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                Console.WriteLine("PaulaDisk=" + system.Paula.GetDiskDebugInfo());
                Console.WriteLine("DF0=" + system.Bus.FloppyController.GetDebugInfo());
                DumpWords(system, system.Cpu.PC >= 16 ? system.Cpu.PC - 16u : system.Cpu.PC, 16, "PCCTX");
                DumpDisplayRegisters(system);
            }
        }

        /// <summary>
        /// Runs the ADF Test Matrix routine.
        /// </summary>
        private static void RunAdfTestMatrix(string adfDirectory, int cycles, string outputDirectory, int limit, AmigaModel modelFilter)
        {
            if (string.IsNullOrWhiteSpace(adfDirectory) || !Directory.Exists(adfDirectory))
            {
                Console.WriteLine("ADF directory not found: " + adfDirectory);
                Environment.ExitCode = 2;
                return;
            }

            Directory.CreateDirectory(outputDirectory);
            string adfRoot = Path.GetFullPath(adfDirectory);
            string csvPath = Path.Combine(outputDirectory, "adf_matrix.csv");
            string[] adfs = Directory.GetFiles(adfRoot, "*.adf", SearchOption.AllDirectories);
            Array.Sort(adfs, StringComparer.OrdinalIgnoreCase);
            if (limit > 0 && adfs.Length > limit)
            {
                Array.Resize(ref adfs, limit);
            }

            AmigaModel[] models = modelFilter != null
                ? new[] { modelFilter }
                : new[]
                {
                    AmigaModel.A500Pal,
                    AmigaModel.A500Pal512Slow512,
                    AmigaModel.A500Pal1MbChip
                };

            using (var writer = new StreamWriter(csvPath, false, Encoding.UTF8))
            {
                writer.WriteLine("adf,path,model,cycles,frame,pc,opcode,sr,category,image_activity,unique_colors,non_background_pixels,frame_hash,dominant_color,dominant_percent,edge_non_background_pixels,edge_non_background_percent,glitch_flags,df0,paula_disk,paula_audio,dmacon,intena,intreq,bplcon0,bplcon1,cop1lc,bpl1pt,bpl2pt,screenshot");
                foreach (string adf in adfs)
                {
                    foreach (AmigaModel model in models)
                    {
                        AdfMatrixResult result = RunAdfMatrixCase(adf, adfRoot, model, cycles, outputDirectory);
                        writer.WriteLine(result.ToCsvLine());
                        Console.WriteLine(result.AdfName + " | " + result.ModelName + " | " + result.Category + " | PC=$" + result.Pc.ToString("X6") + " | " + result.ImageActivity + " | hash=$" + result.FrameHash.ToString("X16") + " | " + result.GlitchFlags);
                    }
                }
            }

            Console.WriteLine("ADF matrix written: " + Path.GetFullPath(csvPath));
        }

        /// <summary>
        /// Runs the ADF Matrix Case routine.
        /// </summary>
        private static AdfMatrixResult RunAdfMatrixCase(string adfPath, string adfRoot, AmigaModel model, int cycles, string outputDirectory)
        {
            using (var system = new AmigaSystem(model, AmigaAccuracyOptions.Fast.Clone()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                system.Bus.FloppyController.InsertDrive0(AdfImage.Load(adfPath), true);
                system.RunChipCycles(cycles);

                string adfName = Path.GetFileName(adfPath);
                string imageName = SanitizeFileName(Path.GetFileNameWithoutExtension(adfPath)) + "_" + SanitizeFileName(GetMatrixModelKey(model)) + ".png";
                string imagePath = Path.Combine(outputDirectory, imageName);
                SaveFrame(system, imagePath);

                FrameImageMetrics metrics = AnalyzeFrame(system);
                var registers = system.Agnus.CustomRegisters;
                uint cop1lc = ReadCustomPointer(system, 0x080);
                uint bpl1pt = ReadCustomPointer(system, 0x0E0);
                uint bpl2pt = ReadCustomPointer(system, 0x0E4);

                return new AdfMatrixResult
                {
                    AdfName = adfName,
                    AdfPath = MakeRelativePath(adfRoot, adfPath),
                    ModelName = model.Name,
                    Cycles = cycles,
                    Frame = system.FrameCounter,
                    Pc = system.Cpu.PC,
                    Opcode = system.Cpu.CurrentOpcode,
                    Sr = system.Cpu.SR,
                    Category = ClassifyAdfMatrixCase(system, metrics),
                    ImageActivity = metrics.Activity,
                    UniqueColors = metrics.UniqueColors,
                    NonBackgroundPixels = metrics.NonBackgroundPixels,
                    FrameHash = metrics.FrameHash,
                    DominantColor = metrics.DominantColor,
                    DominantPercent = metrics.DominantPercent,
                    EdgeNonBackgroundPixels = metrics.EdgeNonBackgroundPixels,
                    EdgeNonBackgroundPercent = metrics.EdgeNonBackgroundPercent,
                    GlitchFlags = metrics.GlitchFlags,
                    Df0 = system.Bus.FloppyController.GetDebugInfo(),
                    Paula = system.Paula.GetDiskDebugInfo(),
                    PaulaAudio = system.Paula.GetAudioDebugInfo(),
                    Dmacon = registers.ReadWord(0x096),
                    Intena = registers.ReadWord(0x09A),
                    Intreq = registers.ReadWord(0x09C),
                    Bplcon0 = registers.ReadWord(0x100),
                    Bplcon1 = registers.ReadWord(0x102),
                    Cop1Lc = cop1lc,
                    Bpl1Pt = bpl1pt,
                    Bpl2Pt = bpl2pt,
                    Screenshot = imagePath
                };
            }
        }

        /// <summary>
        /// Executes the Analyze Frame logic.
        /// </summary>
        private static FrameImageMetrics AnalyzeFrame(AmigaSystem system)
        {
            AmigaEmulator.Core.Video.AmigaFrameBuffer frameBuffer = system.PresentedFrameBuffer;
            uint[] pixels = frameBuffer.Pixels;
            var colors = new HashSet<uint>();
            var counts = new Dictionary<uint, int>();
            uint background = pixels.Length > 0 ? pixels[0] : 0;
            ulong frameHash = 14695981039346656037UL;
            int nonBackground = 0;
            for (int index = 0; index < pixels.Length; index++)
            {
                uint color = pixels[index];
                MixFrameHash(ref frameHash, color);
                if (colors.Count < 512)
                {
                    colors.Add(color);
                }

                if (counts.TryGetValue(color, out int count))
                {
                    counts[color] = count + 1;
                }
                else
                {
                    counts[color] = 1;
                }

                if (color != background)
                {
                    nonBackground++;
                }
            }

            uint dominantColor = 0;
            int dominantPixels = 0;
            foreach (KeyValuePair<uint, int> entry in counts)
            {
                if (entry.Value > dominantPixels)
                {
                    dominantColor = entry.Key;
                    dominantPixels = entry.Value;
                }
            }

            const int edgeThickness = 4;
            int edgePixels = 0;
            int edgeNonBackground = 0;
            int width = frameBuffer.Width;
            int height = frameBuffer.Height;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (x >= edgeThickness && y >= edgeThickness && x < width - edgeThickness && y < height - edgeThickness)
                    {
                        continue;
                    }

                    edgePixels++;
                    if (pixels[(y * width) + x] != background)
                    {
                        edgeNonBackground++;
                    }
                }
            }

            double dominantPercent = pixels.Length > 0 ? (dominantPixels * 100.0) / pixels.Length : 100.0;
            double edgeNonBackgroundPercent = edgePixels > 0 ? (edgeNonBackground * 100.0) / edgePixels : 0.0;
            string activity;
            if (nonBackground < 16)
            {
                activity = "blank";
            }
            else if (nonBackground < 500)
            {
                activity = "tiny";
            }
            else if (colors.Count <= 2)
            {
                activity = "mono";
            }
            else
            {
                activity = "visible";
            }

            return new FrameImageMetrics
            {
                Activity = activity,
                UniqueColors = colors.Count,
                NonBackgroundPixels = nonBackground,
                FrameHash = frameHash,
                DominantColor = dominantColor,
                DominantPercent = dominantPercent,
                EdgeNonBackgroundPixels = edgeNonBackground,
                EdgeNonBackgroundPercent = edgeNonBackgroundPercent,
                GlitchFlags = BuildGlitchFlags(activity, colors.Count, nonBackground, dominantPercent, edgeNonBackgroundPercent)
            };
        }

        /// <summary>
        /// Mixes one presented-frame pixel into the deterministic matrix hash.
        /// </summary>
        private static void MixFrameHash(ref ulong hash, uint color)
        {
            for (int shift = 0; shift < 32; shift += 8)
            {
                hash ^= (byte)(color >> shift);
                hash *= 1099511628211UL;
            }
        }

        /// <summary>
        /// Builds a compact list of visual warning flags for automated ADF runs.
        /// </summary>
        private static string BuildGlitchFlags(string activity, int uniqueColors, int nonBackgroundPixels, double dominantPercent, double edgeNonBackgroundPercent)
        {
            var flags = new List<string>();
            if (string.Equals(activity, "blank", StringComparison.OrdinalIgnoreCase))
            {
                flags.Add("blank");
            }
            else if (string.Equals(activity, "tiny", StringComparison.OrdinalIgnoreCase))
            {
                flags.Add("tiny-output");
            }

            if (uniqueColors <= 2)
            {
                flags.Add("low-entropy");
            }

            if (dominantPercent >= 98.0)
            {
                flags.Add("dominant-fill");
            }

            if (nonBackgroundPixels > 500 && edgeNonBackgroundPercent >= 20.0)
            {
                flags.Add("edge-noise");
            }

            return flags.Count == 0 ? "none" : string.Join("|", flags);
        }

        /// <summary>
        /// Executes the Classify ADF Matrix Case logic.
        /// </summary>
        private static string ClassifyAdfMatrixCase(AmigaSystem system, FrameImageMetrics metrics)
        {
            uint pc = system.Cpu.PC;
            ushort dmacon = system.Agnus.CustomRegisters.DmaControl;
            ushort bplcon0 = system.Agnus.CustomRegisters.ReadWord(0x100);
            bool inKickstart = pc >= 0x00F80000u;
            bool stopped = system.GetCpuDebugInfo().IndexOf("STOP", StringComparison.OrdinalIgnoreCase) >= 0;
            bool diskActive = system.Paula.DiskDmaActive;
            bool displayEnabled = ((bplcon0 >> 12) & 0x07) != 0 && (dmacon & 0x0100) != 0;

            if (inKickstart && stopped && diskActive)
            {
                return "kickstart-disk-wait";
            }

            if (!inKickstart && metrics.Activity == "visible" && displayEnabled)
            {
                return "bootcode-visible";
            }

            if (!inKickstart && metrics.Activity != "blank")
            {
                return "bootcode-corrupt-video";
            }

            if (!inKickstart)
            {
                return "bootcode-no-video";
            }

            if (metrics.Activity == "visible")
            {
                return "kickstart-visible";
            }

            return "unknown";
        }

        /// <summary>
        /// Returns the Matrix Model Key.
        /// </summary>
        private static string GetMatrixModelKey(AmigaModel model)
        {
            if (model == AmigaModel.A500Pal512Slow512)
            {
                return "slow512";
            }

            if (model == AmigaModel.A500Pal1MbChip)
            {
                return "chip1mb";
            }

            return "chip512";
        }

        /// <summary>
        /// Provides Frame Image Metrics behavior used by the emulator.
        /// </summary>
        private sealed class FrameImageMetrics
        {
            /// <summary>
            /// Stores the Activity.
            /// </summary>
            public string Activity;
            /// <summary>
            /// Stores the Unique Colors.
            /// </summary>
            public int UniqueColors;
            /// <summary>
            /// Stores the Non Background Pixels.
            /// </summary>
            public int NonBackgroundPixels;
            /// <summary>
            /// Stores the deterministic hash of the presented frame.
            /// </summary>
            public ulong FrameHash;
            /// <summary>
            /// Stores the dominant ARGB color.
            /// </summary>
            public uint DominantColor;
            /// <summary>
            /// Stores the dominant ARGB color percentage.
            /// </summary>
            public double DominantPercent;
            /// <summary>
            /// Stores the non-background pixel count in the outer frame border.
            /// </summary>
            public int EdgeNonBackgroundPixels;
            /// <summary>
            /// Stores the non-background percentage in the outer frame border.
            /// </summary>
            public double EdgeNonBackgroundPercent;
            /// <summary>
            /// Stores compact visual warning flags.
            /// </summary>
            public string GlitchFlags;
        }

        /// <summary>
        /// Provides ADF Matrix Result behavior used by the emulator.
        /// </summary>
        private sealed class AdfMatrixResult
        {
            /// <summary>
            /// Stores the ADF Name.
            /// </summary>
            public string AdfName;
            /// <summary>
            /// Stores the relative or absolute ADF path.
            /// </summary>
            public string AdfPath;
            /// <summary>
            /// Stores the Model Name.
            /// </summary>
            public string ModelName;
            /// <summary>
            /// Stores the Cycles.
            /// </summary>
            public int Cycles;
            /// <summary>
            /// Stores the Frame.
            /// </summary>
            public long Frame;
            /// <summary>
            /// Stores the PC.
            /// </summary>
            public uint Pc;
            /// <summary>
            /// Stores the Opcode.
            /// </summary>
            public ushort Opcode;
            /// <summary>
            /// Stores the SR.
            /// </summary>
            public ushort Sr;
            /// <summary>
            /// Stores the Category.
            /// </summary>
            public string Category;
            /// <summary>
            /// Stores the Image Activity.
            /// </summary>
            public string ImageActivity;
            /// <summary>
            /// Stores the Unique Colors.
            /// </summary>
            public int UniqueColors;
            /// <summary>
            /// Stores the Non Background Pixels.
            /// </summary>
            public int NonBackgroundPixels;
            /// <summary>
            /// Stores the deterministic hash of the presented frame.
            /// </summary>
            public ulong FrameHash;
            /// <summary>
            /// Stores the dominant ARGB color in the presented frame.
            /// </summary>
            public uint DominantColor;
            /// <summary>
            /// Stores the percentage covered by the dominant color.
            /// </summary>
            public double DominantPercent;
            /// <summary>
            /// Stores the non-background pixel count in the outer frame border.
            /// </summary>
            public int EdgeNonBackgroundPixels;
            /// <summary>
            /// Stores the non-background percentage in the outer frame border.
            /// </summary>
            public double EdgeNonBackgroundPercent;
            /// <summary>
            /// Stores the visual warning flags derived from the presented frame.
            /// </summary>
            public string GlitchFlags;
            /// <summary>
            /// Stores the Df0.
            /// </summary>
            public string Df0;
            /// <summary>
            /// Stores the Paula.
            /// </summary>
            public string Paula;
            /// <summary>
            /// Stores the Paula Audio.
            /// </summary>
            public string PaulaAudio;
            /// <summary>
            /// Stores the Dmacon.
            /// </summary>
            public ushort Dmacon;
            /// <summary>
            /// Stores the Intena.
            /// </summary>
            public ushort Intena;
            /// <summary>
            /// Stores the Intreq.
            /// </summary>
            public ushort Intreq;
            /// <summary>
            /// Stores the Bplcon0.
            /// </summary>
            public ushort Bplcon0;
            /// <summary>
            /// Stores the Bplcon1.
            /// </summary>
            public ushort Bplcon1;
            /// <summary>
            /// Stores the Cop1 Lc.
            /// </summary>
            public uint Cop1Lc;
            /// <summary>
            /// Stores the Bpl1 Pt.
            /// </summary>
            public uint Bpl1Pt;
            /// <summary>
            /// Stores the Bpl2 Pt.
            /// </summary>
            public uint Bpl2Pt;
            /// <summary>
            /// Stores the Screenshot.
            /// </summary>
            public string Screenshot;

            /// <summary>
            /// Executes the To Csv Line logic.
            /// </summary>
            public string ToCsvLine()
            {
                string[] columns =
                {
                    AdfName,
                    AdfPath,
                    ModelName,
                    Cycles.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Frame.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "$" + Pc.ToString("X6"),
                    "$" + Opcode.ToString("X4"),
                    "$" + Sr.ToString("X4"),
                    Category,
                    ImageActivity,
                    UniqueColors.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    NonBackgroundPixels.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "$" + FrameHash.ToString("X16"),
                    "$" + DominantColor.ToString("X8"),
                    DominantPercent.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
                    EdgeNonBackgroundPixels.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    EdgeNonBackgroundPercent.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
                    GlitchFlags,
                    Df0,
                    Paula,
                    PaulaAudio,
                    "$" + Dmacon.ToString("X4"),
                    "$" + Intena.ToString("X4"),
                    "$" + Intreq.ToString("X4"),
                    "$" + Bplcon0.ToString("X4"),
                    "$" + Bplcon1.ToString("X4"),
                    "$" + Cop1Lc.ToString("X8"),
                    "$" + Bpl1Pt.ToString("X8"),
                    "$" + Bpl2Pt.ToString("X8"),
                    Screenshot
                };

                for (int index = 0; index < columns.Length; index++)
                {
                    columns[index] = CsvEscape(columns[index]);
                }

                return string.Join(",", columns);
            }
        }

        /// <summary>
        /// Saves Frame from emulator state.
        /// </summary>
        private static void SaveFrame(AmigaSystem system, string imagePath)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(imagePath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            AmigaEmulator.Core.Video.AmigaFrameBuffer frameBuffer = system.PresentedFrameBuffer;
            using (var bitmap = new Bitmap(frameBuffer.Width, frameBuffer.Height, PixelFormat.Format32bppArgb))
            {
                uint[] pixels = frameBuffer.Pixels;
                for (int y = 0; y < frameBuffer.Height; y++)
                {
                    for (int x = 0; x < frameBuffer.Width; x++)
                    {
                        bitmap.SetPixel(x, y, Color.FromArgb(unchecked((int)pixels[(y * frameBuffer.Width) + x])));
                    }
                }

                bitmap.Save(imagePath, ImageFormat.Png);
            }
        }

        /// <summary>
        /// Dumps Bitplane Memory Image for diagnostics.
        /// </summary>
        private static void DumpBitplaneMemoryImage(int cycles, string imagePath, AmigaModel model)
        {
            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500, CreateNoTraceAccuracyOptions()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                system.RunCycles(cycles);
                var registers = system.Agnus.CustomRegisters;
                ushort bplcon0 = registers.ReadWord(0x100);
                int planes = Math.Min(6, (bplcon0 >> 12) & 0x07);
                ushort ddfStart = registers.ReadWord(0x092);
                ushort ddfStop = registers.ReadWord(0x094);
                int start = ddfStart == 0 ? 0x38 : ddfStart & 0x00FE;
                int stop = ddfStop == 0 ? 0xD0 : ddfStop & 0x00FE;
                int wordsPerLine = Math.Max(1, ((stop - start) / 8) + 1);
                int width = Math.Min(system.FrameBuffer.Width, wordsPerLine * 16);
                int height = system.FrameBuffer.Height;
                int oddModulo = (short)registers.ReadWord(0x108);
                int evenModulo = (short)registers.ReadWord(0x10A);
                uint[] pointers = new uint[6];
                for (int plane = 0; plane < pointers.Length; plane++)
                {
                    pointers[plane] = ReadCustomPointer(registers, 0x0E0 + ((uint)plane * 4u));
                }

                string directory = Path.GetDirectoryName(Path.GetFullPath(imagePath));
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using (var bitmap = new Bitmap(system.FrameBuffer.Width, system.FrameBuffer.Height, PixelFormat.Format32bppArgb))
                {
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < system.FrameBuffer.Width; x++)
                        {
                            int colorIndex = 0;
                            if (x < width)
                            {
                                int wordIndex = x >> 4;
                                int bit = 15 - (x & 0x0F);
                                for (int plane = 0; plane < planes; plane++)
                                {
                                    int modulo = (plane & 1) == 0 ? oddModulo : evenModulo;
                                    uint address = (uint)((int)pointers[plane] + (y * ((wordsPerLine * 2) + modulo)) + (wordIndex * 2)) & 0x00FFFFFEu;
                                    ushort word = address + 1 < system.Model.ChipRamBytes ? system.Bus.ReadWord(address) : (ushort)0;
                                    colorIndex |= ((word >> bit) & 1) << plane;
                                }
                            }

                            bitmap.SetPixel(x, y, Color.FromArgb(unchecked((int)ReadColorRegister(registers, colorIndex))));
                        }
                    }

                    bitmap.Save(imagePath, ImageFormat.Png);
                }

                Console.WriteLine(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "Planes={0} WordsPerLine={1} BPL1=${2:X6} BPL2=${3:X6} MOD {4}/{5}",
                    planes,
                    wordsPerLine,
                    pointers[0],
                    pointers[1],
                    oddModulo,
                    evenModulo));
            }
        }

        /// <summary>
        /// Converts one Amiga custom color register into a 32-bit ARGB value for diagnostics.
        /// </summary>
        private static uint ReadColorRegister(CustomRegisterMap registers, int index)
        {
            ushort color = registers.ReadWord(0x180 + ((uint)(index & 0x1F) * 2u));
            byte red = (byte)(((color >> 8) & 0x0F) * 0x11);
            byte green = (byte)(((color >> 4) & 0x0F) * 0x11);
            byte blue = (byte)((color & 0x0F) * 0x11);
            return 0xFF000000u | ((uint)red << 16) | ((uint)green << 8) | blue;
        }

        /// <summary>
        /// Dumps Core State for diagnostics.
        /// </summary>
        private static void DumpCoreState(int cycles)
        {
            DumpCoreState(cycles, AmigaModel.PalA500);
        }

        /// <summary>
        /// Dumps Core State for diagnostics.
        /// </summary>
        private static void DumpCoreState(int cycles, AmigaModel model)
        {
            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                system.RunCycles(cycles);
                var registers = system.Agnus.CustomRegisters;
                Console.WriteLine("AMIGA CORE STATE");
                Console.WriteLine("Cycles=" + cycles);
                Console.WriteLine("Frame=" + system.FrameCounter);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                DumpCpuRegisters(system);
                Console.WriteLine("Beam=" + system.GetBeamDebugInfo());
                Console.WriteLine("DMA=" + system.GetDmaDebugInfo());
                Console.WriteLine("DMACON=$" + registers.ReadWord(0x096).ToString("X4"));
                Console.WriteLine("INTENA=$" + registers.ReadWord(0x09A).ToString("X4"));
                Console.WriteLine("INTREQ=$" + registers.ReadWord(0x09C).ToString("X4"));
                Console.WriteLine("BPLCON0=$" + registers.ReadWord(0x100).ToString("X4"));
                Console.WriteLine("BPLCON1=$" + registers.ReadWord(0x102).ToString("X4"));
                Console.WriteLine("DIWSTRT=$" + registers.ReadWord(0x08E).ToString("X4"));
                Console.WriteLine("DIWSTOP=$" + registers.ReadWord(0x090).ToString("X4"));
                Console.WriteLine("DDFSTRT=$" + registers.ReadWord(0x092).ToString("X4"));
                Console.WriteLine("DDFSTOP=$" + registers.ReadWord(0x094).ToString("X4"));
                Console.WriteLine("BLTCON0=$" + registers.ReadWord(0x040).ToString("X4"));
                Console.WriteLine("BLTCON1=$" + registers.ReadWord(0x042).ToString("X4"));
                Console.WriteLine("BLTAFWM=$" + registers.ReadWord(0x044).ToString("X4"));
                Console.WriteLine("BLTALWM=$" + registers.ReadWord(0x046).ToString("X4"));
                Console.WriteLine("BLTAMOD=$" + registers.ReadWord(0x064).ToString("X4"));
                Console.WriteLine("BLTBMOD=$" + registers.ReadWord(0x062).ToString("X4"));
                Console.WriteLine("BLTCMOD=$" + registers.ReadWord(0x060).ToString("X4"));
                Console.WriteLine("BLTDMOD=$" + registers.ReadWord(0x066).ToString("X4"));
                Console.WriteLine("BLTADAT=$" + registers.ReadWord(0x074).ToString("X4"));
                Console.WriteLine("BLTBDAT=$" + registers.ReadWord(0x072).ToString("X4"));
                Console.WriteLine("BLTCDAT=$" + registers.ReadWord(0x070).ToString("X4"));
                Console.WriteLine("BLTAPT=$" + (((uint)registers.ReadWord(0x050) << 16) | registers.ReadWord(0x052)).ToString("X8"));
                Console.WriteLine("BLTBPT=$" + (((uint)registers.ReadWord(0x04C) << 16) | registers.ReadWord(0x04E)).ToString("X8"));
                Console.WriteLine("BLTCPT=$" + (((uint)registers.ReadWord(0x048) << 16) | registers.ReadWord(0x04A)).ToString("X8"));
                Console.WriteLine("BLTDPT=$" + (((uint)registers.ReadWord(0x054) << 16) | registers.ReadWord(0x056)).ToString("X8"));
                Console.WriteLine("COPPC=$" + system.Agnus.Copper.ProgramCounter.ToString("X8"));
                Console.WriteLine("CIAA PRA=$" + system.CiaA.Pra.ToString("X2") + " DDRA=$" + system.CiaA.Ddra.ToString("X2"));
                Console.WriteLine("CIAA PRA(read)=$" + system.Bus.ReadByte(0x00BFE001).ToString("X2"));
                Console.WriteLine("CIAB PRB=$" + system.CiaB.Prb.ToString("X2") + " DDRB=$" + system.CiaB.Ddrb.ToString("X2"));
                Console.WriteLine("CIAB PRB(read)=$" + system.Bus.ReadByte(0x00BFD100).ToString("X2"));
                Console.WriteLine("CIAA=" + system.CiaA.GetDebugInfo());
                Console.WriteLine("CIAB=" + system.CiaB.GetDebugInfo());
                Console.WriteLine("DF0=" + system.Bus.FloppyController.GetDebugInfo());
                Console.WriteLine("COP1LC=$" + (((uint)registers.ReadWord(0x080) << 16) | registers.ReadWord(0x082)).ToString("X8"));
                Console.WriteLine("COP2LC=$" + (((uint)registers.ReadWord(0x084) << 16) | registers.ReadWord(0x086)).ToString("X8"));
                for (int index = 0; index < 6; index++)
                {
                    uint address = ((uint)registers.ReadWord((uint)(0x0E0 + (index * 4))) << 16) | registers.ReadWord((uint)(0x0E2 + (index * 4)));
                    Console.WriteLine("BPL" + (index + 1) + "PT=$" + address.ToString("X8"));
                }

                for (int index = 0; index < 8; index++)
                {
                    uint address = ((uint)registers.ReadWord((uint)(0x120 + (index * 4))) << 16) | registers.ReadWord((uint)(0x122 + (index * 4)));
                    Console.WriteLine("SPR" + index + "PT=$" + address.ToString("X8"));
                }

                for (int index = 0; index < 32; index++)
                {
                    Console.WriteLine("COLOR" + index.ToString("00") + "=$" + registers.ReadWord((uint)(0x180 + (index * 2))).ToString("X4"));
                }
            }
        }

        /// <summary>
        /// Asserts the Kickstart Screen Path diagnostic condition.
        /// </summary>
        private static void AssertKickstartScreenPath(int cycles, AmigaModel model)
        {
            using (var system = new AmigaSystem(model, CreateNoTraceAccuracyOptions()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                system.RunCycles(cycles);

                var registers = system.Agnus.CustomRegisters;
                uint cop1lc = ((uint)registers.ReadWord(0x080) << 16) | registers.ReadWord(0x082);
                uint cop2lc = ((uint)registers.ReadWord(0x084) << 16) | registers.ReadWord(0x086);
                uint view = 0x000049A6u;
                uint viewPort = system.Bus.ReadLong(view);
                uint lofCprList = system.Bus.ReadLong(view + 4u);
                uint shfCprList = system.Bus.ReadLong(view + 8u);
                bool hasViewPort = IsValidChipPointer(system, viewPort, 4);
                bool hasCprList = IsValidChipPointer(system, lofCprList, 4) || IsValidChipPointer(system, shfCprList, 4);
                bool hasBitplanePointer = HasBitplanePointer(system);
                bool hasCopperList = cop1lc != 0 || cop2lc != 0;
                bool cop1LooksRaw = IsLikelyRawCopperList(system, cop1lc);
                bool cop2LooksRaw = IsLikelyRawCopperList(system, cop2lc);
                bool hasRawCopperList = hasCopperList && (cop1LooksRaw || cop2LooksRaw);
                ushort dmacon = registers.DmaControl;
                ushort bplcon0 = registers.ReadWord(0x100);
                bool bitplaneDmaEnabled = (dmacon & 0x0300) == 0x0300;
                bool twoPlaneLowres = (bplcon0 & 0x7000) >= 0x2000 && (bplcon0 & 0x8000) == 0;
                bool hasVisibleBootPalette = registers.ReadWord(0x180) == 0x0FFF &&
                    registers.ReadWord(0x182) == 0x0000 &&
                    registers.ReadWord(0x184) != 0x0000;
                bool pass = hasBitplanePointer && hasRawCopperList && bitplaneDmaEnabled && twoPlaneLowres && hasVisibleBootPalette;

                Console.WriteLine("KICKSTART SCREEN PATH ASSERTION");
                Console.WriteLine("Cycles=" + cycles);
                Console.WriteLine("Model=" + model.Name);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                Console.WriteLine("View=$" + view.ToString("X8") + " DiagnosticOnly");
                Console.WriteLine("ViewPort=$" + viewPort.ToString("X8") + " " + FormatPass(hasViewPort));
                Console.WriteLine("LOFCprList=$" + lofCprList.ToString("X8"));
                Console.WriteLine("SHFCprList=$" + shfCprList.ToString("X8") + " " + FormatPass(hasCprList) + " DiagnosticOnly");
                Console.WriteLine("COP1LC=$" + cop1lc.ToString("X8") + " Raw=" + FormatPass(cop1LooksRaw));
                Console.WriteLine("COP2LC=$" + cop2lc.ToString("X8") + " Raw=" + FormatPass(cop2LooksRaw));
                Console.WriteLine("BPL1-BPL5 Pointer=" + FormatPass(hasBitplanePointer));
                Console.WriteLine("BitplaneDMA=" + FormatPass(bitplaneDmaEnabled));
                Console.WriteLine("TwoPlaneLowres=" + FormatPass(twoPlaneLowres));
                Console.WriteLine("BootPalette=" + FormatPass(hasVisibleBootPalette));
                DumpKickstartStructureSummary(system);
                DumpDisplayRegisters(system);
                Console.WriteLine(pass ? "PASS" : "FAIL");
                Environment.ExitCode = pass ? 0 : 1;
            }
        }

        /// <summary>
        /// Dumps Kickstart Structures for diagnostics.
        /// </summary>
        private static void DumpKickstartStructures(int cycles)
        {
            using (var system = new AmigaSystem(AmigaModel.PalA500, CreateNoTraceAccuracyOptions()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                system.RunCycles(cycles);
                Console.WriteLine("KICKSTART STRUCTURES");
                Console.WriteLine("Cycles=" + cycles);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                DumpKickstartStructureSummary(system);
                DumpDisplayRegisters(system);
            }
        }

        /// <summary>
        /// Executes the Diagnose Kickstart Display logic.
        /// </summary>
        private static void DiagnoseKickstartDisplay(int cycles, AmigaModel model)
        {
            using (var system = new AmigaSystem(model, CreateNoTraceAccuracyOptions()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                system.RunCycles(cycles);
                Console.WriteLine("KICKSTART DISPLAY DIAGNOSIS");
                Console.WriteLine("Cycles=" + cycles);
                Console.WriteLine("Model=" + model.Name);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                DumpDisplayRegisters(system);
                DumpKickstartStructureSummary(system);
                DumpGraphicsBaseCopperState(system);
                ScanLikelyViews(system, 16);
                DumpCopperListAnalysis(system, "COP1LC", ReadCustomPointer(system, 0x080));
                DumpCopperListAnalysis(system, "COP2LC", ReadCustomPointer(system, 0x084));
                DumpCopperListAnalysis(system, "GraphicsBase.COP1", system.Bus.ReadLong(0x00002246));
                DumpCopperListAnalysis(system, "GraphicsBase.COP2A", system.Bus.ReadLong(0x00002250));
                DumpCopperListAnalysis(system, "GraphicsBase.COP2B", system.Bus.ReadLong(0x00002254));
            }
        }

        /// <summary>
        /// Dumps Graphics Base Copper State for diagnostics.
        /// </summary>
        private static void DumpGraphicsBaseCopperState(AmigaSystem system)
        {
            uint graphicsBase = 0x0000221Eu;
            Console.WriteLine("GRAPHICS BASE COPPER STATE");
            Console.WriteLine("Base=$" + graphicsBase.ToString("X8"));
            DumpStructureLong(system, graphicsBase + 0x0028u, "GB+0028");
            DumpStructureLong(system, graphicsBase + 0x002Eu, "GB+002E");
            DumpStructureLong(system, graphicsBase + 0x0032u, "GB+0032 COP2A");
            DumpStructureLong(system, graphicsBase + 0x0036u, "GB+0036 COP2B");
            DumpStructureLong(system, graphicsBase + 0x008Eu, "GB+008E");
            DumpStructureLong(system, graphicsBase + 0x0096u, "GB+0096");
            DumpStructureLong(system, graphicsBase + 0x009Cu, "GB+009C");
        }

        /// <summary>
        /// Scans Likely Views for diagnostic output.
        /// </summary>
        private static void ScanLikelyViews(AmigaSystem system, int maxHits)
        {
            Console.WriteLine("LIKELY VIEW STRUCTURES");
            int hits = 0;
            ScanLikelyViewsInRange(system, 0, (uint)system.Model.ChipRamBytes, maxHits, ref hits);
            if (system.Model.SlowRamBytes > 0 && hits < maxHits)
            {
                ScanLikelyViewsInRange(system, 0x00C00000u, (uint)system.Model.SlowRamBytes, maxHits, ref hits);
            }

            Console.WriteLine("LikelyViews=" + hits);
        }

        /// <summary>
        /// Scans Likely Views In Range for diagnostic output.
        /// </summary>
        private static void ScanLikelyViewsInRange(AmigaSystem system, uint start, uint bytes, int maxHits, ref int hits)
        {
            if (bytes < 0x12u)
            {
                return;
            }

            uint end = start + bytes - 0x12u;
            for (uint address = start; address <= end && hits < maxHits; address += 2u)
            {
                ushort dy = system.Bus.ReadWord(address + 0x0Cu);
                ushort dx = system.Bus.ReadWord(address + 0x0Eu);
                ushort modes = system.Bus.ReadWord(address + 0x10u);
                if (dy != 0x002Cu || dx != 0x0081u || (modes & 0x4000u) == 0)
                {
                    continue;
                }

                uint viewPort = system.Bus.ReadLong(address);
                uint lof = system.Bus.ReadLong(address + 4u);
                uint shf = system.Bus.ReadLong(address + 8u);
                Console.WriteLine(
                    "$" + address.ToString("X8") +
                    " VP=$" + viewPort.ToString("X8") + " " + FormatPass(IsValidRamPointer(system, viewPort, 4)) +
                    " LOF=$" + lof.ToString("X8") + " " + FormatPass(IsLikelyRawCopperList(system, lof)) +
                    " SHF=$" + shf.ToString("X8") + " " + FormatPass(IsLikelyRawCopperList(system, shf)) +
                    " Modes=$" + modes.ToString("X4"));
                hits++;
            }
        }

        /// <summary>
        /// Scans Graphics Objects for diagnostic output.
        /// </summary>
        private static void ScanGraphicsObjects(int cycles, int maxHits, AmigaModel model)
        {
            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500, CreateNoTraceAccuracyOptions()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                system.RunCycles(cycles);
                Console.WriteLine("GRAPHICS OBJECT SCAN");
                Console.WriteLine("Cycles=" + cycles);
                Console.WriteLine("Model=" + system.Model.Name);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                DumpDisplayRegisters(system);
                ScanViews(system, maxHits);
                ScanViewPorts(system, maxHits);
                ScanRasInfos(system, maxHits);
                ScanBitMaps(system, maxHits);
            }
        }

        /// <summary>
        /// Scans Views for diagnostic output.
        /// </summary>
        private static void ScanViews(AmigaSystem system, int maxHits)
        {
            Console.WriteLine("SCAN View");
            int hits = 0;
            ScanRange(system, 0, (uint)system.Model.ChipRamBytes, maxHits, ref hits, TryPrintView);
            if (system.Model.SlowRamBytes > 0 && hits < maxHits)
            {
                ScanRange(system, 0x00C00000u, (uint)system.Model.SlowRamBytes, maxHits, ref hits, TryPrintView);
            }

            Console.WriteLine("ViewHits=" + hits);
        }

        /// <summary>
        /// Scans View Ports for diagnostic output.
        /// </summary>
        private static void ScanViewPorts(AmigaSystem system, int maxHits)
        {
            Console.WriteLine("SCAN ViewPort");
            int hits = 0;
            ScanRange(system, 0, (uint)system.Model.ChipRamBytes, maxHits, ref hits, TryPrintViewPort);
            if (system.Model.SlowRamBytes > 0 && hits < maxHits)
            {
                ScanRange(system, 0x00C00000u, (uint)system.Model.SlowRamBytes, maxHits, ref hits, TryPrintViewPort);
            }

            Console.WriteLine("ViewPortHits=" + hits);
        }

        /// <summary>
        /// Scans Ras Infos for diagnostic output.
        /// </summary>
        private static void ScanRasInfos(AmigaSystem system, int maxHits)
        {
            Console.WriteLine("SCAN RasInfo");
            int hits = 0;
            ScanRange(system, 0, (uint)system.Model.ChipRamBytes, maxHits, ref hits, TryPrintRasInfo);
            if (system.Model.SlowRamBytes > 0 && hits < maxHits)
            {
                ScanRange(system, 0x00C00000u, (uint)system.Model.SlowRamBytes, maxHits, ref hits, TryPrintRasInfo);
            }

            Console.WriteLine("RasInfoHits=" + hits);
        }

        /// <summary>
        /// Scans Bit Maps for diagnostic output.
        /// </summary>
        private static void ScanBitMaps(AmigaSystem system, int maxHits)
        {
            Console.WriteLine("SCAN BitMap");
            int hits = 0;
            ScanRange(system, 0, (uint)system.Model.ChipRamBytes, maxHits, ref hits, TryPrintBitMap);
            if (system.Model.SlowRamBytes > 0 && hits < maxHits)
            {
                ScanRange(system, 0x00C00000u, (uint)system.Model.SlowRamBytes, maxHits, ref hits, TryPrintBitMap);
            }

            Console.WriteLine("BitMapHits=" + hits);
        }

        /// <summary>
        /// Executes the Graphics Object Printer logic.
        /// </summary>
        private delegate bool GraphicsObjectPrinter(AmigaSystem system, uint address);

        /// <summary>
        /// Scans Range for diagnostic output.
        /// </summary>
        private static void ScanRange(AmigaSystem system, uint start, uint bytes, int maxHits, ref int hits, GraphicsObjectPrinter printer)
        {
            if (bytes < 0x40u)
            {
                return;
            }

            uint end = start + bytes - 0x40u;
            for (uint address = start; address <= end && hits < maxHits; address += 2u)
            {
                if (printer(system, address))
                {
                    hits++;
                }
            }
        }

        /// <summary>
        /// Attempts to Print View and reports whether it succeeded.
        /// </summary>
        private static bool TryPrintView(AmigaSystem system, uint address)
        {
            ushort dy = system.Bus.ReadWord(address + 0x0Cu);
            ushort dx = system.Bus.ReadWord(address + 0x0Eu);
            ushort modes = system.Bus.ReadWord(address + 0x10u);
            uint viewPort = system.Bus.ReadLong(address);
            uint lof = system.Bus.ReadLong(address + 4u);
            uint shf = system.Bus.ReadLong(address + 8u);

            bool offsetsLookRight = dy <= 0x0200 && dx <= 0x0200;
            bool modeLooksRight = modes == 0 || (modes & 0xBFFFu) == 0 || (modes & 0x4000u) != 0;
            bool hasUsefulPointer = IsValidRamPointer(system, viewPort, 0x2C) ||
                IsLikelyRawCopperList(system, lof) ||
                IsLikelyRawCopperList(system, shf);
            if (!offsetsLookRight || !modeLooksRight || !hasUsefulPointer)
            {
                return false;
            }

            Console.WriteLine(
                "$" + address.ToString("X8") +
                " VP=$" + viewPort.ToString("X8") + " " + FormatPass(IsValidRamPointer(system, viewPort, 0x2C)) +
                " LOF=$" + lof.ToString("X8") + " Raw=" + FormatPass(IsLikelyRawCopperList(system, lof)) +
                " SHF=$" + shf.ToString("X8") + " Raw=" + FormatPass(IsLikelyRawCopperList(system, shf)) +
                " Dy=$" + dy.ToString("X4") +
                " Dx=$" + dx.ToString("X4") +
                " Modes=$" + modes.ToString("X4"));
            if (IsValidRamPointer(system, viewPort, 0x2C))
            {
                TryPrintViewPort(system, viewPort);
            }

            return true;
        }

        /// <summary>
        /// Attempts to Print View Port and reports whether it succeeded.
        /// </summary>
        private static bool TryPrintViewPort(AmigaSystem system, uint address)
        {
            uint next = system.Bus.ReadLong(address);
            uint colorMap = system.Bus.ReadLong(address + 4u);
            uint dspIns = system.Bus.ReadLong(address + 8u);
            uint sprIns = system.Bus.ReadLong(address + 0x0Cu);
            uint clrIns = system.Bus.ReadLong(address + 0x10u);
            uint uCopIns = system.Bus.ReadLong(address + 0x14u);
            ushort dWidth = system.Bus.ReadWord(address + 0x18u);
            ushort dHeight = system.Bus.ReadWord(address + 0x1Au);
            short dx = unchecked((short)system.Bus.ReadWord(address + 0x1Cu));
            short dy = unchecked((short)system.Bus.ReadWord(address + 0x1Eu));
            ushort modes = system.Bus.ReadWord(address + 0x20u);
            uint rasInfo = system.Bus.ReadLong(address + 0x26u);

            bool sizeLooksRight = dWidth >= 16 && dWidth <= 768 && dHeight >= 16 && dHeight <= 320;
            bool rasInfoLooksRight = IsValidRamPointer(system, rasInfo, 0x0C) && LooksLikeRasInfo(system, rasInfo);
            bool instructionsLookRight = IsValidRamPointer(system, dspIns, 4) ||
                IsValidRamPointer(system, sprIns, 4) ||
                IsValidRamPointer(system, clrIns, 4) ||
                IsValidRamPointer(system, uCopIns, 4);
            if (!sizeLooksRight || (!rasInfoLooksRight && !instructionsLookRight))
            {
                return false;
            }

            Console.WriteLine(
                "  VP $" + address.ToString("X8") +
                " Next=$" + next.ToString("X8") +
                " CM=$" + colorMap.ToString("X8") +
                " Dsp=$" + dspIns.ToString("X8") +
                " Spr=$" + sprIns.ToString("X8") +
                " Clr=$" + clrIns.ToString("X8") +
                " UCop=$" + uCopIns.ToString("X8") +
                " Size=" + dWidth + "x" + dHeight +
                " Offset=" + dx + "," + dy +
                " Modes=$" + modes.ToString("X4") +
                " RI=$" + rasInfo.ToString("X8") + " " + FormatPass(rasInfoLooksRight));
            if (rasInfoLooksRight)
            {
                TryPrintRasInfo(system, rasInfo);
            }

            return true;
        }

        /// <summary>
        /// Attempts to Print Ras Info and reports whether it succeeded.
        /// </summary>
        private static bool TryPrintRasInfo(AmigaSystem system, uint address)
        {
            if (!LooksLikeRasInfo(system, address))
            {
                return false;
            }

            uint next = system.Bus.ReadLong(address);
            uint bitMap = system.Bus.ReadLong(address + 4u);
            short rx = unchecked((short)system.Bus.ReadWord(address + 8u));
            short ry = unchecked((short)system.Bus.ReadWord(address + 0x0Au));
            Console.WriteLine(
                "    RI $" + address.ToString("X8") +
                " Next=$" + next.ToString("X8") +
                " BM=$" + bitMap.ToString("X8") +
                " Scroll=" + rx + "," + ry);
            TryPrintBitMap(system, bitMap);
            return true;
        }

        /// <summary>
        /// Executes the Looks Like Ras Info logic.
        /// </summary>
        private static bool LooksLikeRasInfo(AmigaSystem system, uint address)
        {
            uint bitMap = system.Bus.ReadLong(address + 4u);
            short rx = unchecked((short)system.Bus.ReadWord(address + 8u));
            short ry = unchecked((short)system.Bus.ReadWord(address + 0x0Au));
            return rx > -2048 && rx < 2048 &&
                ry > -2048 && ry < 2048 &&
                IsValidRamPointer(system, bitMap, 0x28) &&
                LooksLikeBitMap(system, bitMap);
        }

        /// <summary>
        /// Attempts to Print Bit Map and reports whether it succeeded.
        /// </summary>
        private static bool TryPrintBitMap(AmigaSystem system, uint address)
        {
            if (!LooksLikeBitMap(system, address))
            {
                return false;
            }

            ushort bytesPerRow = system.Bus.ReadWord(address);
            ushort rows = system.Bus.ReadWord(address + 2u);
            byte flags = system.Bus.ReadByte(address + 4u);
            byte depth = system.Bus.ReadByte(address + 5u);
            var planes = new StringBuilder();
            for (int index = 0; index < Math.Min(8, (int)depth); index++)
            {
                if (index > 0)
                {
                    planes.Append(",");
                }

                planes.Append("$");
                planes.Append(system.Bus.ReadLong(address + 8u + (uint)(index * 4)).ToString("X8"));
            }

            Console.WriteLine(
                "      BM $" + address.ToString("X8") +
                " BPR=" + bytesPerRow +
                " Rows=" + rows +
                " Flags=$" + flags.ToString("X2") +
                " Depth=" + depth +
                " Planes=" + planes);
            return true;
        }

        /// <summary>
        /// Executes the Looks Like Bit Map logic.
        /// </summary>
        private static bool LooksLikeBitMap(AmigaSystem system, uint address)
        {
            if (!IsValidRamPointer(system, address, 0x28))
            {
                return false;
            }

            ushort bytesPerRow = system.Bus.ReadWord(address);
            ushort rows = system.Bus.ReadWord(address + 2u);
            byte depth = system.Bus.ReadByte(address + 5u);
            if (bytesPerRow < 2 || bytesPerRow > 512 || (bytesPerRow & 1) != 0 ||
                rows < 8 || rows > 1024 ||
                depth < 1 || depth > 6)
            {
                return false;
            }

            int validPlanes = 0;
            for (int index = 0; index < depth; index++)
            {
                uint plane = system.Bus.ReadLong(address + 8u + (uint)(index * 4));
                if (IsValidChipPointer(system, plane, Math.Min(2, (int)bytesPerRow * (int)rows)))
                {
                    validPlanes++;
                }
            }

            return validPlanes == depth;
        }

        /// <summary>
        /// Dumps Copper List Analysis for diagnostics.
        /// </summary>
        private static void DumpCopperListAnalysis(AmigaSystem system, string label, uint address)
        {
            address &= 0x00FFFFFEu;
            bool valid = IsValidChipPointer(system, address, 4);
            Console.WriteLine(label + "=$" + address.ToString("X8") + " " + FormatPass(valid));
            if (!valid)
            {
                return;
            }

            int moves = 0;
            int waits = 0;
            int skips = 0;
            int blockedLikeMoves = 0;
            int bitplaneMoves = 0;
            for (int index = 0; index < 24; index++)
            {
                uint current = address + (uint)(index * 4);
                ushort first = system.Bus.ReadWord(current);
                ushort second = system.Bus.ReadWord(current + 2u);
                if (first == 0xFFFF && second == 0xFFFE)
                {
                    Console.WriteLine("  +" + (index * 4).ToString("X2") + " END");
                    break;
                }

                if ((first & 1) == 0)
                {
                    uint registerOffset = (uint)(first & 0x01FE);
                    bool protectedLike = registerOffset < 0x040;
                    if (protectedLike)
                    {
                        blockedLikeMoves++;
                    }

                    if (registerOffset >= 0x0E0 && registerOffset <= 0x0F6)
                    {
                        bitplaneMoves++;
                    }

                    moves++;
                    Console.WriteLine("  +" + (index * 4).ToString("X2") + " MOVE $" + registerOffset.ToString("X3") + " <= $" + second.ToString("X4") + (protectedLike ? " protected" : string.Empty));
                    continue;
                }

                if ((second & 1) != 0)
                {
                    skips++;
                    Console.WriteLine("  +" + (index * 4).ToString("X2") + " SKIP $" + first.ToString("X4") + " $" + second.ToString("X4"));
                }
                else
                {
                    waits++;
                    Console.WriteLine("  +" + (index * 4).ToString("X2") + " WAIT $" + first.ToString("X4") + " $" + second.ToString("X4"));
                }
            }

            Console.WriteLine("  Summary Moves=" + moves + " Waits=" + waits + " Skips=" + skips + " ProtectedLike=" + blockedLikeMoves + " BitplaneMoves=" + bitplaneMoves);
        }

        /// <summary>
        /// Reads a custom-chip pointer from high and low register words through the running system.
        /// </summary>
        private static uint ReadCustomPointer(AmigaSystem system, uint highOffset)
        {
            var registers = system.Agnus.CustomRegisters;
            return (((uint)registers.ReadWord(highOffset) << 16) | registers.ReadWord(highOffset + 2u)) & 0x00FFFFFEu;
        }

        /// <summary>
        /// Dumps Kickstart Structure Summary for diagnostics.
        /// </summary>
        private static void DumpKickstartStructureSummary(AmigaSystem system)
        {
            uint bootDisplayBase = 0x00004984u;
            uint view = bootDisplayBase + 0x22u;
            uint displayListHead = bootDisplayBase + 0x3Cu;
            uint viewPort = system.Bus.ReadLong(view);
            uint lofCprList = system.Bus.ReadLong(view + 4u);
            uint shfCprList = system.Bus.ReadLong(view + 8u);
            Console.WriteLine("BootDisplayBase=$" + bootDisplayBase.ToString("X8"));
            DumpStructureLong(system, bootDisplayBase + 0x0000u, "BootDisplayBase+0000");
            DumpStructureLong(system, bootDisplayBase + 0x0004u, "BootDisplayBase+0004");
            DumpStructureLong(system, bootDisplayBase + 0x001Cu, "BootDisplayBase+001C");
            DumpStructureLong(system, bootDisplayBase + 0x0022u, "BootView.ViewPort");
            DumpStructureLong(system, bootDisplayBase + 0x0026u, "BootView.LOFCprList");
            DumpStructureLong(system, bootDisplayBase + 0x002Au, "BootView.SHFCprList");
            DumpStructureWord(system, bootDisplayBase + 0x002Eu, "BootView.DyOffset");
            DumpStructureWord(system, bootDisplayBase + 0x0030u, "BootView.DxOffset");
            DumpStructureWord(system, bootDisplayBase + 0x0032u, "BootView.Modes");
            DumpStructureLong(system, displayListHead, "BootDisplayListHead");
            DumpStructureLong(system, bootDisplayBase + 0x0040u, "BootDisplayBase+0040");
            DumpStructureLong(system, bootDisplayBase + 0x0054u, "BootDisplayBase+0054");
            DumpStructureLong(system, bootDisplayBase + 0x0062u, "BootDisplayBase+0062");
            DumpStructureLong(system, bootDisplayBase + 0x0066u, "BootDisplayBase+0066");
            DumpStructureLong(system, bootDisplayBase + 0x006Au, "BootDisplayBase+006A");
            Console.WriteLine("View=$" + view.ToString("X8") +
                " ViewPort=$" + viewPort.ToString("X8") +
                " LOF=$" + lofCprList.ToString("X8") +
                " SHF=$" + shfCprList.ToString("X8"));

            DumpCandidateStructure(system, 0x00004CE8u, "Candidate@$4CE8");
            DumpCandidateStructure(system, 0x00004EE8u, "Candidate@$4EE8");
            DumpCandidateStructure(system, 0x00005A68u, "Candidate@$5A68");
        }

        /// <summary>
        /// Dumps Candidate Structure for diagnostics.
        /// </summary>
        private static void DumpCandidateStructure(AmigaSystem system, uint address, string label)
        {
            Console.WriteLine(label);
            for (int offset = 0; offset < 0x20; offset += 4)
            {
                DumpStructureLong(system, address + (uint)offset, label + "+" + offset.ToString("X2"));
            }
        }

        /// <summary>
        /// Dumps Structure Long for diagnostics.
        /// </summary>
        private static void DumpStructureLong(AmigaSystem system, uint address, string label)
        {
            Console.WriteLine(label + " $" + address.ToString("X8") + "=$" + system.Bus.ReadLong(address).ToString("X8"));
        }

        /// <summary>
        /// Dumps Structure Word for diagnostics.
        /// </summary>
        private static void DumpStructureWord(AmigaSystem system, uint address, string label)
        {
            Console.WriteLine(label + " $" + address.ToString("X8") + "=$" + system.Bus.ReadWord(address).ToString("X4"));
        }

        /// <summary>
        /// Dumps Chip Words for diagnostics.
        /// </summary>
        private static void DumpChipWords(uint address, int words, int cycles)
        {
            DumpChipWords(address, words, cycles, AmigaModel.PalA500);
        }

        /// <summary>
        /// Dumps Chip Words for diagnostics.
        /// </summary>
        private static void DumpChipWords(uint address, int words, int cycles, AmigaModel model)
        {
            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500, CreateNoTraceAccuracyOptions()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                system.RunCycles(cycles);
                Console.WriteLine("CHIP WORDS");
                Console.WriteLine("Cycles=" + cycles);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                Console.WriteLine("Base=$" + address.ToString("X8"));
                for (int index = 0; index < words; index++)
                {
                    uint current = (address + (uint)(index * 2)) & 0x00FFFFFEu;
                    ushort value = (ushort)((system.Bus.ChipRam.Read(current) << 8) | system.Bus.ChipRam.Read(current + 1u));
                    Console.WriteLine("$" + current.ToString("X8") + "=$" + value.ToString("X4"));
                }
            }
        }

        /// <summary>
        /// Dumps Words Command for diagnostics.
        /// </summary>
        private static void DumpWordsCommand(uint address, int words, int cycles)
        {
            DumpWordsCommand(address, words, cycles, AmigaModel.PalA500);
        }

        /// <summary>
        /// Dumps Words Command for diagnostics.
        /// </summary>
        private static void DumpWordsCommand(uint address, int words, int cycles, AmigaModel model)
        {
            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500, CreateNoTraceAccuracyOptions()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                if (cycles > 0)
                {
                    system.RunCycles(cycles);
                }

                Console.WriteLine("WORDS");
                Console.WriteLine("Cycles=" + cycles);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                Console.WriteLine("Base=$" + address.ToString("X8"));
                DumpWords(system, address, words, "MEM");
            }
        }

        /// <summary>
        /// Dumps Bus Words for diagnostics.
        /// </summary>
        private static void DumpBusWords(uint address, int words, int cycles)
        {
            DumpBusWords(address, words, cycles, AmigaModel.PalA500);
        }

        /// <summary>
        /// Dumps Bus Words for diagnostics.
        /// </summary>
        private static void DumpBusWords(uint address, int words, int cycles, AmigaModel model)
        {
            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500, CreateNoTraceAccuracyOptions()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                system.RunCycles(cycles);
                Console.WriteLine("BUS WORDS");
                Console.WriteLine("Cycles=" + cycles);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                Console.WriteLine("Base=$" + address.ToString("X8"));
                DumpWords(system, address, words, "MEM");
            }
        }

        /// <summary>
        /// Traces Writes for diagnostics.
        /// </summary>
        private static void TraceWrites(uint start, uint end, int cycles, int maxWrites, int startCycle)
        {
            TraceWrites(start, end, cycles, maxWrites, startCycle, false, AmigaModel.PalA500);
        }

        /// <summary>
        /// Traces Writes for diagnostics.
        /// </summary>
        private static void TraceWrites(uint start, uint end, int cycles, int maxWrites, int startCycle, bool includeContext)
        {
            TraceWrites(start, end, cycles, maxWrites, startCycle, includeContext, AmigaModel.PalA500);
        }

        /// <summary>
        /// Traces Writes for diagnostics.
        /// </summary>
        private static void TraceWrites(uint start, uint end, int cycles, int maxWrites, int startCycle, bool includeContext, AmigaModel model)
        {
            if (end < start)
            {
                uint swap = start;
                start = end;
                end = swap;
            }

            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500, CreateNoTraceAccuracyOptions()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                int writes = 0;
                system.Bus.WriteObserved += delegate(uint address, int size, uint value, AmigaAddressRegion region)
                {
                    if (writes >= maxWrites || system.Agnus.Beam.GlobalChipCycle < startCycle || address < start || address > end)
                    {
                        return;
                    }

                    PrintWriteTrace(system, "CPU", region.ToString(), address, size, value);
                    if (includeContext)
                    {
                        PrintTraceRegisterContext(system);
                    }

                    writes++;
                };

                system.Agnus.Blitter.ChipWordWritten += delegate(uint address, ushort value)
                {
                    if (writes >= maxWrites || system.Agnus.Beam.GlobalChipCycle < startCycle || address < start || address > end)
                    {
                        return;
                    }

                    PrintWriteTrace(system, "BLT", "ChipRam", address, 2, value);
                    if (includeContext)
                    {
                        PrintTraceRegisterContext(system);
                    }

                    writes++;
                };

                for (int cycle = 0; cycle < cycles && writes < maxWrites; cycle++)
                {
                    system.TickChipCycle();
                }

                Console.WriteLine("Writes=" + writes);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                DumpDisplayRegisters(system);
            }
        }

        /// <summary>
        /// Traces Reads for diagnostics.
        /// </summary>
        private static void TraceReads(uint start, uint end, int cycles, int maxReads, int startCycle)
        {
            TraceReads(start, end, cycles, maxReads, startCycle, false, AmigaModel.PalA500);
        }

        /// <summary>
        /// Traces Reads for diagnostics.
        /// </summary>
        private static void TraceReads(uint start, uint end, int cycles, int maxReads, int startCycle, bool includeContext)
        {
            TraceReads(start, end, cycles, maxReads, startCycle, includeContext, AmigaModel.PalA500);
        }

        /// <summary>
        /// Traces Reads for diagnostics.
        /// </summary>
        private static void TraceReads(uint start, uint end, int cycles, int maxReads, int startCycle, bool includeContext, AmigaModel model)
        {
            if (end < start)
            {
                uint swap = start;
                start = end;
                end = swap;
            }

            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500, CreateNoTraceAccuracyOptions()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                int reads = 0;
                system.Bus.ReadObserved += delegate(uint address, int size, uint value, AmigaAddressRegion region)
                {
                    if (reads >= maxReads || system.Agnus.Beam.GlobalChipCycle < startCycle || address < start || address > end)
                    {
                        return;
                    }

                    PrintBusTrace(system, "RD", region.ToString(), address, size, value);
                    if (includeContext)
                    {
                        PrintTraceRegisterContext(system);
                    }

                    reads++;
                };

                for (int cycle = 0; cycle < cycles && reads < maxReads; cycle++)
                {
                    system.TickChipCycle();
                }

                Console.WriteLine("Reads=" + reads);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                DumpDisplayRegisters(system);
            }
        }

        /// <summary>
        /// Traces Odd Address Accesses for diagnostics.
        /// </summary>
        private static void TraceOddAddressAccesses(int cycles, int maxHits, AmigaModel model)
        {
            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500, CreateNoTraceAccuracyOptions()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                int hits = 0;
                system.Cpu.OddAddressAccessed += delegate(uint address, int size, bool write, uint pc, ushort opcode)
                {
                    if (hits >= maxHits)
                    {
                        return;
                    }

                    Console.WriteLine(string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{0,9} F{1,4} {2,3},{3,3} {4} size {5} @${6:X6} PC ${7:X6} OP ${8:X4} SR ${9:X4}",
                        system.Agnus.Beam.GlobalChipCycle,
                        system.FrameCounter,
                        system.Agnus.Beam.BeamX,
                        system.Agnus.Beam.BeamY,
                        write ? "WR" : "RD",
                        size,
                        address,
                        pc,
                        opcode,
                        system.Cpu.SR));
                    DumpCpuRegisters(system);
                    hits++;
                };

                for (int cycle = 0; cycle < cycles && hits < maxHits; cycle++)
                {
                    system.TickChipCycle();
                }

                Console.WriteLine("OddAccesses=" + hits);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                DumpDisplayRegisters(system);
            }
        }

        /// <summary>
        /// Traces Long Value for diagnostics.
        /// </summary>
        private static void TraceLongValue(uint watchedValue, int cycles, int maxHits, int startCycle)
        {
            TraceLongValue(watchedValue, cycles, maxHits, startCycle, AmigaModel.PalA500);
        }

        /// <summary>
        /// Traces Long Value for diagnostics.
        /// </summary>
        private static void TraceLongValue(uint watchedValue, int cycles, int maxHits, int startCycle, AmigaModel model)
        {
            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500, CreateNoTraceAccuracyOptions()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                var seen = new HashSet<uint>();
                int hits = 0;
                system.Bus.WriteObserved += delegate(uint address, int size, uint value, AmigaAddressRegion region)
                {
                    if (hits >= maxHits ||
                        system.Agnus.Beam.GlobalChipCycle < startCycle ||
                        region != AmigaAddressRegion.ChipRam)
                    {
                        return;
                    }

                    uint first = address >= 3 ? address - 3u : 0u;
                    for (uint candidate = first; candidate <= address; candidate++)
                    {
                        uint aligned = candidate & 0x00FFFFFEu;
                        if (seen.Contains(aligned))
                        {
                            continue;
                        }

                        if (system.Bus.ReadLong(aligned) != watchedValue)
                        {
                            continue;
                        }

                        seen.Add(aligned);
                        Console.WriteLine(string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "{0,9} F{1,4} {2,3},{3,3} PC ${4:X6} LONG ${5:X8} at ${6:X8} after write ${7:X8} size {8} value ${9:X8}",
                            system.Agnus.Beam.GlobalChipCycle,
                            system.FrameCounter,
                            system.Agnus.Beam.BeamX,
                            system.Agnus.Beam.BeamY,
                            system.Cpu.PC,
                            watchedValue,
                            aligned,
                            address,
                            size,
                            value));
                        PrintTraceRegisterContext(system);
                        hits++;
                        if (hits >= maxHits)
                        {
                            break;
                        }
                    }
                };

                for (int cycle = 0; cycle < cycles && hits < maxHits; cycle++)
                {
                    system.TickChipCycle();
                }

                Console.WriteLine("LongValueHits=" + hits + " value=$" + watchedValue.ToString("X8"));
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                DumpKickstartStructureSummary(system);
                DumpDisplayRegisters(system);
            }
        }

        /// <summary>
        /// Traces Register Value for diagnostics.
        /// </summary>
        private static void TraceRegisterValue(string registerName, uint watchedValue, int cycles, int maxHits, int startCycle)
        {
            TraceRegisterValue(registerName, watchedValue, cycles, maxHits, startCycle, AmigaModel.PalA500);
        }

        /// <summary>
        /// Traces Register Value for diagnostics.
        /// </summary>
        private static void TraceRegisterValue(string registerName, uint watchedValue, int cycles, int maxHits, int startCycle, AmigaModel model)
        {
            using (var system = new AmigaSystem(model, CreateNoTraceAccuracyOptions()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                uint previous = ReadCpuRegister(system.Cpu, registerName);
                uint lastInstructionAddress = system.Cpu.CurrentInstructionAddress;
                int hits = 0;
                for (int cycle = 0; cycle < cycles && hits < maxHits; cycle++)
                {
                    system.TickChipCycle();
                    if (system.Cpu.CurrentInstructionAddress == lastInstructionAddress)
                    {
                        continue;
                    }

                    lastInstructionAddress = system.Cpu.CurrentInstructionAddress;
                    uint current = ReadCpuRegister(system.Cpu, registerName);
                    if (system.Agnus.Beam.GlobalChipCycle >= startCycle && current == watchedValue && previous != watchedValue)
                    {
                        Console.WriteLine(string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "{0,9} F{1,4} {2,3},{3,3} PC ${4:X6} IP ${5:X6} OP ${6:X4} {7}=${8:X8}",
                            system.Agnus.Beam.GlobalChipCycle,
                            system.FrameCounter,
                            system.Agnus.Beam.BeamX,
                            system.Agnus.Beam.BeamY,
                            system.Cpu.PC,
                            system.Cpu.CurrentInstructionAddress,
                            system.Cpu.CurrentOpcode,
                            registerName.ToUpperInvariant(),
                            current));
                        PrintTraceRegisterContext(system);
                        hits++;
                    }

                    previous = current;
                }

                Console.WriteLine("RegisterHits=" + hits + " " + registerName.ToUpperInvariant() + "=$" + watchedValue.ToString("X8"));
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                DumpDisplayRegisters(system);
            }
        }

        /// <summary>
        /// Returns the requested CPU register value for command-line diagnostics.
        /// </summary>
        private static uint ReadCpuRegister(M68kCpu cpu, string registerName)
        {
            if (cpu == null)
            {
                return 0;
            }

            string normalized = (registerName ?? string.Empty).Trim().ToUpperInvariant();
            if (normalized.Length == 2 && normalized[0] == 'D' && normalized[1] >= '0' && normalized[1] <= '7')
            {
                return cpu.GetD(normalized[1] - '0');
            }

            if (normalized.Length == 2 && normalized[0] == 'A' && normalized[1] >= '0' && normalized[1] <= '7')
            {
                return cpu.GetA(normalized[1] - '0');
            }

            if (normalized == "PC")
            {
                return cpu.PC;
            }

            if (normalized == "SR")
            {
                return cpu.SR;
            }

            throw new ArgumentException("Unsupported register: " + registerName, nameof(registerName));
        }

        /// <summary>
        /// Scans Long Value for diagnostic output.
        /// </summary>
        private static void ScanLongValue(uint watchedValue, int cycles, int maxHits)
        {
            ScanLongValue(watchedValue, cycles, maxHits, AmigaModel.PalA500);
        }

        /// <summary>
        /// Scans Long Value for diagnostic output.
        /// </summary>
        private static void ScanLongValue(uint watchedValue, int cycles, int maxHits, AmigaModel model)
        {
            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500, CreateNoTraceAccuracyOptions()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                system.RunCycles(cycles);
                int hits = 0;
                uint end = (uint)system.Model.ChipRamBytes - 4u;
                Console.WriteLine("SCAN LONG VALUE");
                Console.WriteLine("Cycles=" + cycles);
                Console.WriteLine("Value=$" + watchedValue.ToString("X8"));
                for (uint address = 0; address <= end && hits < maxHits; address += 2u)
                {
                    if (system.Bus.ReadLong(address) != watchedValue)
                    {
                        continue;
                    }

                    Console.WriteLine("$" + address.ToString("X8"));
                    hits++;
                }

                Console.WriteLine("Hits=" + hits);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                DumpKickstartStructureSummary(system);
                DumpDisplayRegisters(system);
            }
        }

        /// <summary>
        /// Executes the Print Write Trace logic.
        /// </summary>
        private static void PrintWriteTrace(AmigaSystem system, string source, string region, uint address, int size, uint value)
        {
            PrintBusTrace(system, source, region, address, size, value);
        }

        /// <summary>
        /// Executes the Print Bus Trace logic.
        /// </summary>
        private static void PrintBusTrace(AmigaSystem system, string source, string region, uint address, int size, uint value)
        {
            Console.WriteLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0,9} F{1,4} {2,3},{3,3} {4,-3} IP ${5:X6} OP ${6:X4} PC ${7:X6} {8,-15} ${9:X8} size {10} <= ${11:X8} {12}",
                system.Agnus.Beam.GlobalChipCycle,
                system.FrameCounter,
                system.Agnus.Beam.BeamX,
                system.Agnus.Beam.BeamY,
                source,
                system.Cpu.CurrentInstructionAddress,
                system.Cpu.CurrentOpcode,
                system.Cpu.PC,
                region,
                address,
                size,
                value,
                system.Cpu.LastInstruction ?? string.Empty));
        }

        /// <summary>
        /// Executes the Print Trace Register Context logic.
        /// </summary>
        private static void PrintTraceRegisterContext(AmigaSystem system)
        {
            Console.WriteLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "          D0={0:X8} D1={1:X8} D2={2:X8} D3={3:X8} D4={4:X8} D5={5:X8} D6={6:X8} D7={7:X8}",
                system.Cpu.GetD(0),
                system.Cpu.GetD(1),
                system.Cpu.GetD(2),
                system.Cpu.GetD(3),
                system.Cpu.GetD(4),
                system.Cpu.GetD(5),
                system.Cpu.GetD(6),
                system.Cpu.GetD(7)));
            Console.WriteLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "          A0={0:X8} A1={1:X8} A2={2:X8} A3={3:X8} A4={4:X8} A5={5:X8} A6={6:X8} A7={7:X8}",
                system.Cpu.GetA(0),
                system.Cpu.GetA(1),
                system.Cpu.GetA(2),
                system.Cpu.GetA(3),
                system.Cpu.GetA(4),
                system.Cpu.GetA(5),
                system.Cpu.GetA(6),
                system.Cpu.GetA(7)));
        }

        /// <summary>
        /// Traces Custom Registers for diagnostics.
        /// </summary>
        private static void TraceCustomRegisters(int cycles, int maxChanges, int startCycle)
        {
            TraceCustomRegisters(cycles, maxChanges, startCycle, AmigaModel.PalA500);
        }

        /// <summary>
        /// Traces Custom Registers for diagnostics.
        /// </summary>
        private static void TraceCustomRegisters(int cycles, int maxChanges, int startCycle, AmigaModel model)
        {
            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500, CreateNoTraceAccuracyOptions()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                var registers = system.Agnus.CustomRegisters;
                var watched = BuildWatchedCustomRegisterList();
                ushort[] previous = new ushort[watched.Count];
                for (int index = 0; index < watched.Count; index++)
                {
                    previous[index] = registers.ReadWord(watched[index].Address);
                }

                int changes = 0;
                for (int cycle = 0; cycle < cycles && changes < maxChanges; cycle++)
                {
                    system.TickChipCycle();
                    if (cycle < startCycle)
                    {
                        for (int index = 0; index < watched.Count; index++)
                        {
                            previous[index] = registers.ReadWord(watched[index].Address);
                        }

                        continue;
                    }

                    for (int index = 0; index < watched.Count && changes < maxChanges; index++)
                    {
                        ushort current = registers.ReadWord(watched[index].Address);
                        if (current == previous[index])
                        {
                            continue;
                        }

                        MachineTraceSample sample = system.LastTraceSample;
                        uint pc = sample != null && sample.CpuTrace != null ? sample.CpuTrace.PcBefore : system.Cpu.PC;
                        Console.WriteLine(string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "{0,9} F{1,4} {2,3},{3,3} PC ${4:X6} {5,-8} ${6:X3}: ${7:X4} -> ${8:X4}",
                            cycle,
                            system.FrameCounter,
                            system.Agnus.Beam.BeamX,
                            system.Agnus.Beam.BeamY,
                            pc,
                            watched[index].Name,
                            watched[index].Address,
                            previous[index],
                            current));
                        previous[index] = current;
                        changes++;
                    }
                }

                Console.WriteLine("Changes=" + changes);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                DumpDisplayRegisters(system);
            }
        }

        /// <summary>
        /// Traces ADF Custom Registers for diagnostics.
        /// </summary>
        private static void TraceAdfCustomRegisters(string adfPath, int cycles, int maxChanges, int startCycle, AmigaModel model)
        {
            if (string.IsNullOrWhiteSpace(adfPath) || !File.Exists(adfPath))
            {
                Console.WriteLine("ADF not found: " + adfPath);
                Environment.ExitCode = 2;
                return;
            }

            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500, CreateNoTraceAccuracyOptions()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                system.Bus.FloppyController.InsertDrive0(AdfImage.Load(adfPath), true);
                var registers = system.Agnus.CustomRegisters;
                var watched = BuildWatchedCustomRegisterList();
                ushort[] previous = new ushort[watched.Count];
                for (int index = 0; index < watched.Count; index++)
                {
                    previous[index] = registers.ReadWord(watched[index].Address);
                }

                int changes = 0;
                for (int cycle = 0; cycle < cycles && changes < maxChanges; cycle++)
                {
                    system.TickChipCycle();
                    if (cycle < startCycle)
                    {
                        for (int index = 0; index < watched.Count; index++)
                        {
                            previous[index] = registers.ReadWord(watched[index].Address);
                        }

                        continue;
                    }

                    for (int index = 0; index < watched.Count && changes < maxChanges; index++)
                    {
                        ushort current = registers.ReadWord(watched[index].Address);
                        if (current == previous[index])
                        {
                            continue;
                        }

                        MachineTraceSample sample = system.LastTraceSample;
                        uint pc = sample != null && sample.CpuTrace != null ? sample.CpuTrace.PcBefore : system.Cpu.PC;
                        Console.WriteLine(string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "{0,9} F{1,4} {2,3},{3,3} PC ${4:X6} {5,-8} ${6:X3}: ${7:X4} -> ${8:X4}",
                            cycle,
                            system.FrameCounter,
                            system.Agnus.Beam.BeamX,
                            system.Agnus.Beam.BeamY,
                            pc,
                            watched[index].Name,
                            watched[index].Address,
                            previous[index],
                            current));
                        previous[index] = current;
                        changes++;
                    }
                }

                Console.WriteLine("ADF=" + Path.GetFileName(adfPath));
                Console.WriteLine("Model=" + system.Model.Name);
                Console.WriteLine("Changes=" + changes);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                Console.WriteLine("PaulaDisk=" + system.Paula.GetDiskDebugInfo());
                Console.WriteLine("PaulaAudio=" + system.Paula.GetAudioDebugInfo());
                Console.WriteLine("DF0=" + system.Bus.FloppyController.GetDebugInfo());
                DumpDisplayRegisters(system);
            }
        }

        /// <summary>
        /// Dumps ADF Audio for diagnostics.
        /// </summary>
        private static void DumpAdfAudio(string adfPath, int cycles, string wavPath, AmigaModel model)
        {
            if (string.IsNullOrWhiteSpace(adfPath) || !File.Exists(adfPath))
            {
                Console.WriteLine("ADF not found: " + adfPath);
                Environment.ExitCode = 2;
                return;
            }

            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500, CreateNoTraceAccuracyOptions()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                var pcm = new List<short>();
                system.Paula.AudioSampleGenerated += delegate(float left, float right)
                {
                    pcm.Add(ClampToPcm16(left));
                    pcm.Add(ClampToPcm16(right));
                };

                system.Bus.FloppyController.InsertDrive0(AdfImage.Load(adfPath), true);
                system.RunChipCycles(cycles);
                WriteStereoPcm16Wav(wavPath, pcm, 48000);

                Console.WriteLine("ADF AUDIO DUMP");
                Console.WriteLine("ADF=" + Path.GetFileName(adfPath));
                Console.WriteLine("Cycles=" + cycles);
                Console.WriteLine("Model=" + system.Model.Name);
                Console.WriteLine("Frames=" + (pcm.Count / 2));
                Console.WriteLine("PaulaAudio=" + system.Paula.GetAudioDebugInfo());
                Console.WriteLine("WAV written: " + Path.GetFullPath(wavPath));
            }
        }

        /// <summary>
        /// Executes the Clamp To Pcm16 logic.
        /// </summary>
        private static short ClampToPcm16(float value)
        {
            if (value > 1.0f)
            {
                value = 1.0f;
            }
            else if (value < -1.0f)
            {
                value = -1.0f;
            }

            return (short)(value * 32767.0f);
        }

        /// <summary>
        /// Writes captured stereo PCM16 samples to a WAV file.
        /// </summary>
        private static void WriteStereoPcm16Wav(string wavPath, List<short> samples, int sampleRate)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(wavPath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            int dataBytes = samples.Count * 2;
            using (var stream = File.Create(wavPath))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataBytes);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)2);
                writer.Write(sampleRate);
                writer.Write(sampleRate * 4);
                writer.Write((short)4);
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataBytes);
                for (int index = 0; index < samples.Count; index++)
                {
                    writer.Write(samples[index]);
                }
            }
        }

        /// <summary>
        /// Traces ADF Writes for diagnostics.
        /// </summary>
        private static void TraceAdfWrites(string adfPath, uint start, uint end, int cycles, int maxWrites, int startCycle, AmigaModel model)
        {
            if (string.IsNullOrWhiteSpace(adfPath) || !File.Exists(adfPath))
            {
                Console.WriteLine("ADF not found: " + adfPath);
                Environment.ExitCode = 2;
                return;
            }

            if (end < start)
            {
                uint swap = start;
                start = end;
                end = swap;
            }

            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500, CreateNoTraceAccuracyOptions()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                system.Bus.FloppyController.InsertDrive0(AdfImage.Load(adfPath), true);
                int writes = 0;
                system.Bus.WriteObserved += delegate(uint address, int size, uint value, AmigaAddressRegion region)
                {
                    if (writes >= maxWrites || system.Agnus.Beam.GlobalChipCycle < startCycle)
                    {
                        return;
                    }

                    uint normalized = address & 0x00FFFFFEu;
                    if (normalized < start || normalized > end)
                    {
                        return;
                    }

                    Console.WriteLine(string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{0,9} F{1,4} {2,3},{3,3} PC ${4:X6} OP ${5:X4} {6} ${7:X6} size {8} <= ${9:X8} {10}",
                        system.Agnus.Beam.GlobalChipCycle,
                        system.FrameCounter,
                        system.Agnus.Beam.BeamX,
                        system.Agnus.Beam.BeamY,
                        system.Cpu.CurrentInstructionAddress,
                        system.Cpu.CurrentOpcode,
                        region,
                        address,
                        size,
                        value,
                        system.Cpu.LastInstruction ?? string.Empty));
                    writes++;
                };

                for (int cycle = 0; cycle < cycles && writes < maxWrites; cycle++)
                {
                    system.TickChipCycle();
                }

                Console.WriteLine("ADF=" + Path.GetFileName(adfPath));
                Console.WriteLine("Writes=" + writes);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                Console.WriteLine("PaulaDisk=" + system.Paula.GetDiskDebugInfo());
                Console.WriteLine("PaulaAudio=" + system.Paula.GetAudioDebugInfo());
                Console.WriteLine("DF0=" + system.Bus.FloppyController.GetDebugInfo());
                DumpDisplayRegisters(system);
            }
        }

        /// <summary>
        /// Traces ADF Reads for diagnostics.
        /// </summary>
        private static void TraceAdfReads(string adfPath, uint start, uint end, int cycles, int maxReads, int startCycle, AmigaModel model)
        {
            if (string.IsNullOrWhiteSpace(adfPath) || !File.Exists(adfPath))
            {
                Console.WriteLine("ADF not found: " + adfPath);
                Environment.ExitCode = 2;
                return;
            }

            if (end < start)
            {
                uint swap = start;
                start = end;
                end = swap;
            }

            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500, CreateNoTraceAccuracyOptions()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                system.Bus.FloppyController.InsertDrive0(AdfImage.Load(adfPath), true);
                int reads = 0;
                system.Bus.ReadObserved += delegate(uint address, int size, uint value, AmigaAddressRegion region)
                {
                    if (reads >= maxReads || system.Agnus.Beam.GlobalChipCycle < startCycle)
                    {
                        return;
                    }

                    uint normalized = address & 0x00FFFFFEu;
                    if (normalized < start || normalized > end)
                    {
                        return;
                    }

                    Console.WriteLine(string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{0,9} F{1,4} {2,3},{3,3} PC ${4:X6} OP ${5:X4} {6} ${7:X6} size {8} => ${9:X8} {10}",
                        system.Agnus.Beam.GlobalChipCycle,
                        system.FrameCounter,
                        system.Agnus.Beam.BeamX,
                        system.Agnus.Beam.BeamY,
                        system.Cpu.CurrentInstructionAddress,
                        system.Cpu.CurrentOpcode,
                        region,
                        address,
                        size,
                        value,
                        system.Cpu.LastInstruction ?? string.Empty));
                    reads++;
                };

                for (int cycle = 0; cycle < cycles && reads < maxReads; cycle++)
                {
                    system.TickChipCycle();
                }

                Console.WriteLine("ADF=" + Path.GetFileName(adfPath));
                Console.WriteLine("Reads=" + reads);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                Console.WriteLine("PaulaDisk=" + system.Paula.GetDiskDebugInfo());
                Console.WriteLine("DF0=" + system.Bus.FloppyController.GetDebugInfo());
                DumpDisplayRegisters(system);
            }
        }

        /// <summary>
        /// Traces Blits for diagnostics.
        /// </summary>
        private static void TraceBlits(int cycles, int maxBlits, int startCycle, uint filterStart, uint filterEnd)
        {
            if (filterEnd < filterStart)
            {
                uint swap = filterStart;
                filterStart = filterEnd;
                filterEnd = swap;
            }

            bool useFilter = filterStart != 0 || filterEnd != 0;
            filterStart &= 0x00FFFFFEu;
            filterEnd &= 0x00FFFFFEu;

            using (var system = new AmigaSystem(AmigaModel.PalA500, CreateNoTraceAccuracyOptions()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                int blits = 0;
                system.Bus.WriteObserved += delegate(uint address, int size, uint value, AmigaAddressRegion region)
                {
                    if (blits >= maxBlits ||
                        system.Agnus.Beam.GlobalChipCycle < startCycle ||
                        region != AmigaAddressRegion.CustomRegisters)
                    {
                        return;
                    }

                    uint offset = address & 0x01FFu;
                    bool startsBlit = (size == 2 && (offset & 0x01FEu) == 0x058u) ||
                        (size == 1 && offset == 0x059u);
                    if (!startsBlit)
                    {
                        return;
                    }

                    var registers = system.Agnus.CustomRegisters;
                    ushort bltcon0 = registers.ReadWord(0x040);
                    ushort bltcon1 = registers.ReadWord(0x042);
                    ushort sizeValue = registers.ReadWord(0x058);
                    int widthWords = sizeValue & 0x003F;
                    int heightLines = (sizeValue >> 6) & 0x03FF;
                    if (widthWords == 0)
                    {
                        widthWords = 64;
                    }

                    if (heightLines == 0)
                    {
                        heightLines = 1024;
                    }

                    uint dpt = ReadCustomPointer(registers, 0x054);
                    bool usesD = (bltcon0 & 0x0100) != 0;
                    bool descending = (bltcon1 & 0x0002) != 0;
                    uint byteCount = (uint)Math.Max(2, widthWords * heightLines * 2);
                    uint targetStart = dpt;
                    uint targetEnd = dpt;
                    if (usesD)
                    {
                        if (descending)
                        {
                            targetStart = (dpt >= byteCount - 2u ? dpt - byteCount + 2u : 0u) & 0x00FFFFFEu;
                            targetEnd = dpt;
                        }
                        else
                        {
                            targetStart = dpt;
                            targetEnd = (dpt + byteCount - 2u) & 0x00FFFFFEu;
                        }
                    }

                    if (useFilter && (!usesD || targetEnd < filterStart || targetStart > filterEnd))
                    {
                        return;
                    }

                    PrintBlitTrace(system, blits + 1, widthWords, heightLines, targetStart, targetEnd);
                    blits++;
                };

                for (int cycle = 0; cycle < cycles && blits < maxBlits; cycle++)
                {
                    system.TickChipCycle();
                }

                Console.WriteLine("Blits=" + blits);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                DumpDisplayRegisters(system);
            }
        }

        /// <summary>
        /// Executes the Print blitter Trace logic.
        /// </summary>
        private static void PrintBlitTrace(AmigaSystem system, int index, int widthWords, int heightLines, uint targetStart, uint targetEnd)
        {
            var registers = system.Agnus.CustomRegisters;
            ushort bltcon0 = registers.ReadWord(0x040);
            ushort bltcon1 = registers.ReadWord(0x042);
            ushort bltafwm = registers.ReadWord(0x044);
            ushort bltalwm = registers.ReadWord(0x046);
            ushort bltsize = registers.ReadWord(0x058);
            uint apt = ReadCustomPointer(registers, 0x050);
            uint bpt = ReadCustomPointer(registers, 0x04C);
            uint cpt = ReadCustomPointer(registers, 0x048);
            uint dpt = ReadCustomPointer(registers, 0x054);
            string use = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0}{1}{2}{3}",
                (bltcon0 & 0x0800) != 0 ? "A" : "-",
                (bltcon0 & 0x0400) != 0 ? "B" : "-",
                (bltcon0 & 0x0200) != 0 ? "C" : "-",
                (bltcon0 & 0x0100) != 0 ? "D" : "-");
            Console.WriteLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0,9} F{1,4} {2,3},{3,3} BLIT #{4,4} IP ${5:X6} OP ${6:X4} PC ${7:X6} size ${8:X4} {9}x{10} CON0 ${11:X4} CON1 ${12:X4} use {13} min ${14:X2} target ${15:X6}-${16:X6} {17}",
                system.Agnus.Beam.GlobalChipCycle,
                system.FrameCounter,
                system.Agnus.Beam.BeamX,
                system.Agnus.Beam.BeamY,
                index,
                system.Cpu.CurrentInstructionAddress,
                system.Cpu.CurrentOpcode,
                system.Cpu.PC,
                bltsize,
                widthWords,
                heightLines,
                bltcon0,
                bltcon1,
                use,
                bltcon0 & 0x00FF,
                targetStart,
                targetEnd,
                system.Cpu.LastInstruction ?? string.Empty));
            Console.WriteLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "          A=${0:X6} B=${1:X6} C=${2:X6} D=${3:X6} MOD A={4} B={5} C={6} D={7} MASK ${8:X4}/${9:X4} DAT A=${10:X4} B=${11:X4} C=${12:X4}",
                apt,
                bpt,
                cpt,
                dpt,
                (short)registers.ReadWord(0x064),
                (short)registers.ReadWord(0x062),
                (short)registers.ReadWord(0x060),
                (short)registers.ReadWord(0x066),
                bltafwm,
                bltalwm,
                registers.ReadWord(0x074),
                registers.ReadWord(0x072),
                registers.ReadWord(0x070)));
            Console.WriteLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "          MEM A:{0} B:{1} C:{2} D:{3}",
                FormatChipWordSample(system, apt),
                FormatChipWordSample(system, bpt),
                FormatChipWordSample(system, cpt),
                FormatChipWordSample(system, dpt)));
        }

        /// <summary>
        /// Traces blitter Words for diagnostics.
        /// </summary>
        private static void TraceBlitWords(int cycles, int maxWords, int startCycle, uint filterStart, uint filterEnd)
        {
            if (filterEnd < filterStart)
            {
                uint swap = filterStart;
                filterStart = filterEnd;
                filterEnd = swap;
            }

            bool useFilter = filterStart != 0 || filterEnd != 0;
            filterStart &= 0x00FFFFFEu;
            filterEnd &= 0x00FFFFFEu;

            using (var system = new AmigaSystem(AmigaModel.PalA500, CreateNoTraceAccuracyOptions()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                int words = 0;
                system.Agnus.Blitter.WordExecuted += delegate(BlitterWordTraceEntry entry)
                {
                    long cycle = system.Agnus.Beam.GlobalChipCycle;
                    if (words >= maxWords || cycle < startCycle)
                    {
                        return;
                    }

                    uint d = entry.DAddress & 0x00FFFFFEu;
                    if (useFilter && (d < filterStart || d > filterEnd))
                    {
                        return;
                    }

                    PrintBlitWordTrace(system, words + 1, entry);
                    words++;
                };

                for (int cycle = 0; cycle < cycles && words < maxWords; cycle++)
                {
                    system.TickChipCycle();
                }

                Console.WriteLine("BlitWords=" + words);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                DumpDisplayRegisters(system);
            }
        }

        /// <summary>
        /// Executes the Print blitter Word Trace logic.
        /// </summary>
        private static void PrintBlitWordTrace(AmigaSystem system, int index, BlitterWordTraceEntry entry)
        {
            Console.WriteLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0,9} F{1,4} {2,3},{3,3} BWORD #{4,4} {5} L{6,3} W{7,3} PC ${8:X6} D=${9:X6} -> ${10:X4} min ${11:X2} use {12}{13}{14}{15}",
                system.Agnus.Beam.GlobalChipCycle,
                system.FrameCounter,
                system.Agnus.Beam.BeamX,
                system.Agnus.Beam.BeamY,
                index,
                entry.LineMode ? "LINE " : "BLOCK",
                entry.Line,
                entry.Word,
                system.Cpu.CurrentInstructionAddress,
                entry.DAddress,
                entry.Result,
                entry.Minterm,
                entry.UseA ? "A" : "-",
                entry.UseB ? "B" : "-",
                entry.UseC ? "C" : "-",
                entry.UseD ? "D" : "-"));
            Console.WriteLine(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "          A@${0:X6} raw ${1:X4} sh ${2:X4}  B@${3:X6} raw ${4:X4} sh ${5:X4}  C@${6:X6} raw ${7:X4}  mask ${8:X4} pre ${9:X4}",
                entry.AAddress,
                entry.RawA,
                entry.ShiftedA,
                entry.BAddress,
                entry.RawB,
                entry.ShiftedB,
                entry.CAddress,
                entry.RawC,
                entry.Mask,
                entry.ResultBeforeFill));
            if (entry.FillInclusive || entry.FillExclusive)
            {
                Console.WriteLine(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "          fill inc={0} exc={1} carry {2}->{3}",
                    entry.FillInclusive,
                    entry.FillExclusive,
                    entry.FillCarryIn,
                    entry.FillCarryOut));
            }

            if (entry.LineMode)
            {
                Console.WriteLine(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "          line x={0} y={1} err={2} drawn={3}",
                    entry.LineX,
                    entry.LineY,
                    entry.LineError,
                    entry.Drawn));
            }
        }

        /// <summary>
        /// Reads a custom-chip pointer directly from paired register-map words.
        /// </summary>
        private static uint ReadCustomPointer(CustomRegisterMap registers, uint offset)
        {
            return (((uint)registers.ReadWord(offset) << 16) | registers.ReadWord(offset + 2)) & 0x00FFFFFEu;
        }

        /// <summary>
        /// Executes the Format Chip Word Sample logic.
        /// </summary>
        private static string FormatChipWordSample(AmigaSystem system, uint address)
        {
            address &= 0x00FFFFFEu;
            if (address + 6u >= (uint)system.Model.ChipRamBytes)
            {
                return "$------";
            }

            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "${0:X6}={1:X4},{2:X4},{3:X4},{4:X4}",
                address,
                system.Bus.ReadWord(address),
                system.Bus.ReadWord(address + 2),
                system.Bus.ReadWord(address + 4),
                system.Bus.ReadWord(address + 6));
        }

        /// <summary>
        /// Traces Copper for diagnostics.
        /// </summary>
        private static void TraceCopper(int cycles, int maxInstructions, int startCycle)
        {
            TraceCopper(cycles, maxInstructions, startCycle, AmigaModel.PalA500);
        }

        /// <summary>
        /// Traces Copper for diagnostics.
        /// </summary>
        private static void TraceCopper(int cycles, int maxInstructions, int startCycle, AmigaModel model)
        {
            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500, CreateNoTraceAccuracyOptions()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                int instructions = 0;
                system.Agnus.Copper.InstructionExecuted += delegate(CopperTraceEntry entry)
                {
                    if (instructions >= maxInstructions || entry.GlobalChipCycle < startCycle)
                    {
                        return;
                    }

                    Console.WriteLine(string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{0,9} F{1,4} {2,3},{3,3} COP ${4:X6}: ${5:X4} ${6:X4} {7,-11} reg ${8:X3} val ${9:X4} target ${10:X6}{11}",
                        entry.GlobalChipCycle,
                        system.FrameCounter,
                        entry.BeamX,
                        entry.BeamY,
                        entry.ProgramCounter,
                        entry.FirstWord,
                        entry.SecondWord,
                        entry.Kind,
                        entry.RegisterOffset,
                        entry.Value,
                        entry.Target,
                        entry.Waiting ? " waiting" : string.Empty));
                    instructions++;
                };

                for (int cycle = 0; cycle < cycles && instructions < maxInstructions; cycle++)
                {
                    system.TickChipCycle();
                }

                Console.WriteLine("CopperInstructions=" + instructions);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                DumpDisplayRegisters(system);
            }
        }

        /// <summary>
        /// Traces ADF Copper for diagnostics.
        /// </summary>
        private static void TraceAdfCopper(string adfPath, int cycles, int maxInstructions, int startCycle, AmigaModel model)
        {
            if (string.IsNullOrWhiteSpace(adfPath) || !File.Exists(adfPath))
            {
                Console.WriteLine("ADF not found: " + adfPath);
                Environment.ExitCode = 2;
                return;
            }

            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500, CreateNoTraceAccuracyOptions()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                system.Bus.FloppyController.InsertDrive0(AdfImage.Load(adfPath), true);
                int instructions = 0;
                system.Agnus.Copper.InstructionExecuted += delegate(CopperTraceEntry entry)
                {
                    if (instructions >= maxInstructions || entry.GlobalChipCycle < startCycle)
                    {
                        return;
                    }

                    Console.WriteLine(string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "{0,9} F{1,4} {2,3},{3,3} COP ${4:X6}: ${5:X4} ${6:X4} {7,-11} reg ${8:X3} val ${9:X4} target ${10:X6}{11}",
                        entry.GlobalChipCycle,
                        system.FrameCounter,
                        entry.BeamX,
                        entry.BeamY,
                        entry.ProgramCounter,
                        entry.FirstWord,
                        entry.SecondWord,
                        entry.Kind,
                        entry.RegisterOffset,
                        entry.Value,
                        entry.Target,
                        entry.Waiting ? " waiting" : string.Empty));
                    instructions++;
                };

                for (int cycle = 0; cycle < cycles && instructions < maxInstructions; cycle++)
                {
                    system.TickChipCycle();
                }

                Console.WriteLine("ADF=" + Path.GetFileName(adfPath));
                Console.WriteLine("Model=" + system.Model.Name);
                Console.WriteLine("CopperInstructions=" + instructions);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                Console.WriteLine("Beam=" + system.GetBeamDebugInfo());
                Console.WriteLine("DMA=" + system.GetDmaDebugInfo());
                Console.WriteLine("PaulaDisk=" + system.Paula.GetDiskDebugInfo());
                Console.WriteLine("DF0=" + system.Bus.FloppyController.GetDebugInfo());
                DumpDisplayRegisters(system);
            }
        }

        /// <summary>
        /// Stores Watched Custom Register data used by the emulator.
        /// </summary>
        private struct WatchedCustomRegister
        {
            /// <summary>
            /// Stores the Address.
            /// </summary>
            public uint Address;
            /// <summary>
            /// Stores the Name.
            /// </summary>
            public string Name;
        }

        /// <summary>
        /// Builds the Watched Custom Register List.
        /// </summary>
        private static List<WatchedCustomRegister> BuildWatchedCustomRegisterList()
        {
            var list = new List<WatchedCustomRegister>();
            AddWatched(list, 0x002, "DMACONR");
            AddWatched(list, 0x01C, "INTENAR");
            AddWatched(list, 0x01E, "INTREQR");
            AddWatched(list, 0x020, "DSKPTH");
            AddWatched(list, 0x022, "DSKPTL");
            AddWatched(list, 0x024, "DSKLEN");
            AddWatched(list, 0x026, "DSKDAT");
            AddWatched(list, 0x07E, "DSKSYNC");
            AddWatched(list, 0x040, "BLTCON0");
            AddWatched(list, 0x042, "BLTCON1");
            AddWatched(list, 0x044, "BLTAFWM");
            AddWatched(list, 0x046, "BLTALWM");
            AddWatched(list, 0x048, "BLTCPTH");
            AddWatched(list, 0x04A, "BLTCPTL");
            AddWatched(list, 0x04C, "BLTBPTH");
            AddWatched(list, 0x04E, "BLTBPTL");
            AddWatched(list, 0x050, "BLTAPTH");
            AddWatched(list, 0x052, "BLTAPTL");
            AddWatched(list, 0x054, "BLTDPTH");
            AddWatched(list, 0x056, "BLTDPTL");
            AddWatched(list, 0x058, "BLTSIZE");
            AddWatched(list, 0x060, "BLTCMOD");
            AddWatched(list, 0x062, "BLTBMOD");
            AddWatched(list, 0x064, "BLTAMOD");
            AddWatched(list, 0x066, "BLTDMOD");
            AddWatched(list, 0x070, "BLTCDAT");
            AddWatched(list, 0x072, "BLTBDAT");
            AddWatched(list, 0x074, "BLTADAT");
            AddWatched(list, 0x080, "COP1LCH");
            AddWatched(list, 0x082, "COP1LCL");
            AddWatched(list, 0x084, "COP2LCH");
            AddWatched(list, 0x086, "COP2LCL");
            AddWatched(list, 0x08E, "DIWSTRT");
            AddWatched(list, 0x090, "DIWSTOP");
            AddWatched(list, 0x092, "DDFSTRT");
            AddWatched(list, 0x094, "DDFSTOP");
            AddWatched(list, 0x096, "DMACON");
            AddWatched(list, 0x09A, "INTENA");
            AddWatched(list, 0x09C, "INTREQ");
            AddWatched(list, 0x09E, "ADKCON");
            AddWatched(list, 0x100, "BPLCON0");
            AddWatched(list, 0x102, "BPLCON1");
            AddWatched(list, 0x108, "BPL1MOD");
            AddWatched(list, 0x10A, "BPL2MOD");
            for (int index = 0; index < 6; index++)
            {
                AddWatched(list, (uint)(0x0E0 + (index * 4)), "BPL" + (index + 1) + "PTH");
                AddWatched(list, (uint)(0x0E2 + (index * 4)), "BPL" + (index + 1) + "PTL");
            }

            for (int index = 0; index < 8; index++)
            {
                AddWatched(list, (uint)(0x120 + (index * 4)), "SPR" + index + "PTH");
                AddWatched(list, (uint)(0x122 + (index * 4)), "SPR" + index + "PTL");
            }

            for (int index = 0; index < 32; index++)
            {
                AddWatched(list, (uint)(0x180 + (index * 2)), "COLOR" + index.ToString("00"));
            }

            return list;
        }

        /// <summary>
        /// Executes the Add Watched logic.
        /// </summary>
        private static void AddWatched(List<WatchedCustomRegister> list, uint address, string name)
        {
            list.Add(new WatchedCustomRegister { Address = address, Name = name });
        }

        /// <summary>
        /// Runs the Until PC routine.
        /// </summary>
        private static void RunUntilPc(uint pc, int maxCycles, int requestedHit)
        {
            using (var system = new AmigaSystem(AmigaModel.PalA500))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                int hits = 0;
                for (int cycle = 0; cycle < maxCycles; cycle++)
                {
                    system.TickChipCycle();
                    MachineTraceSample sample = system.LastTraceSample;
                    if (sample.CpuTrace != null &&
                        sample.CpuTrace.AccessType == M68kTraceAccessType.OpcodeFetch &&
                        sample.CpuTrace.PcBefore == pc)
                    {
                        hits++;
                        if (hits < requestedHit)
                        {
                            continue;
                        }

                        Console.WriteLine("Hit PC=$" + pc.ToString("X6") + " occurrence=" + hits + " at cycle=" + sample.GlobalChipCycle);
                        Console.WriteLine("Trace=" + sample.CpuTrace);
                        Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                        Console.WriteLine("OverlayAtZero=" + system.Bus.RomOverlayAtZero);
                        DumpWords(system, sample.CpuTrace.PcBefore, 6, "PC");
                        DumpWords(system, 0x000001D0, 24, "LOW");
                        DumpCpuRegisters(system);
                        DumpStackWords(system, 8);
                        Console.WriteLine("Beam=" + system.GetBeamDebugInfo());
                        return;
                    }
                }

                Console.WriteLine("PC=$" + pc.ToString("X6") + " occurrence=" + requestedHit + " not hit within " + maxCycles + " cycles. Hits=" + hits);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                DumpCpuRegisters(system);
                DumpStackWords(system, 8);
            }
        }

        /// <summary>
        /// Runs the emulator until Kickstart programs the first visible display registers.
        /// </summary>
        private static void RunUntilDisplaySetup(int maxCycles)
        {
            using (var system = new AmigaSystem(AmigaModel.PalA500, CreateNoTraceAccuracyOptions()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                for (int cycle = 0; cycle < maxCycles; cycle++)
                {
                    system.TickChipCycle();
                    if (HasDisplaySetup(system))
                    {
                        MachineTraceSample sample = system.LastTraceSample;
                        Console.WriteLine("Display setup observed at cycle=" + sample.GlobalChipCycle);
                        Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                        DumpDisplayRegisters(system);
                        return;
                    }
                }

                Console.WriteLine("No display setup observed within " + maxCycles + " cycles.");
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                DumpCpuRegisters(system);
                DumpDisplayRegisters(system);
            }
        }

        /// <summary>
        /// Creates the No Trace Accuracy Options.
        /// </summary>
        private static AmigaAccuracyOptions CreateNoTraceAccuracyOptions()
        {
            return new AmigaAccuracyOptions
            {
                Name = "NoTraceCompatible",
                EnableCpuTrace = false,
                EnableDmaSlotTrace = false,
                StrictChipBusArbitration = !_relaxedBusDiagnostics
            };
        }

        /// <summary>
        /// Creates the CPU Trace Accuracy Options.
        /// </summary>
        private static AmigaAccuracyOptions CreateCpuTraceAccuracyOptions()
        {
            return new AmigaAccuracyOptions
            {
                Name = "CpuTraceCompatible",
                EnableCpuTrace = true,
                EnableDmaSlotTrace = false,
                StrictChipBusArbitration = !_relaxedBusDiagnostics
            };
        }

        /// <summary>
        /// Executes the Profile Program Counter logic.
        /// </summary>
        private static void ProfileProgramCounter(int maxCycles, int top)
        {
            using (var system = new AmigaSystem(AmigaModel.PalA500))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                var counts = new Dictionary<uint, int>();
                for (int cycle = 0; cycle < maxCycles; cycle++)
                {
                    system.TickChipCycle();
                    MachineTraceSample sample = system.LastTraceSample;
                    if (sample.CpuTrace == null || sample.CpuTrace.AccessType != M68kTraceAccessType.OpcodeFetch)
                    {
                        continue;
                    }

                    uint pc = sample.CpuTrace.PcBefore;
                    counts.TryGetValue(pc, out int count);
                    counts[pc] = count + 1;
                }

                var entries = new List<KeyValuePair<uint, int>>(counts);
                entries.Sort(delegate(KeyValuePair<uint, int> left, KeyValuePair<uint, int> right)
                {
                    int compare = right.Value.CompareTo(left.Value);
                    return compare != 0 ? compare : left.Key.CompareTo(right.Key);
                });

                Console.WriteLine("PC PROFILE");
                Console.WriteLine("Cycles=" + maxCycles);
                Console.WriteLine("UniquePcs=" + counts.Count);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                DumpCpuRegisters(system);
                for (int index = 0; index < entries.Count && index < top; index++)
                {
                    Console.WriteLine("$" + entries[index].Key.ToString("X6") + " " + entries[index].Value);
                }
            }
        }

        /// <summary>
        /// Traces After PC for diagnostics.
        /// </summary>
        private static void TraceAfterPc(uint pc, int requestedHit, int traceCycles, string logPath)
        {
            TraceAfterPc(pc, requestedHit, traceCycles, logPath, AmigaModel.PalA500);
        }

        /// <summary>
        /// Traces After PC for diagnostics.
        /// </summary>
        private static void TraceAfterPc(uint pc, int requestedHit, int traceCycles, string logPath, AmigaModel model)
        {
            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                int hits = 0;
                while (hits < requestedHit)
                {
                    system.TickChipCycle();
                    MachineTraceSample sample = system.LastTraceSample;
                    if (sample.CpuTrace != null &&
                        sample.CpuTrace.AccessType == M68kTraceAccessType.OpcodeFetch &&
                        sample.CpuTrace.PcBefore == pc)
                    {
                        hits++;
                    }
                }

                MachineTraceExporter.ExportJsonLines(system, traceCycles, 1, logPath);
                Console.WriteLine("Trace-after-PC written: " + Path.GetFullPath(logPath));
                Console.WriteLine("PC=$" + pc.ToString("X6") + " occurrence=" + requestedHit + " cycles=" + traceCycles);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                DumpCpuRegisters(system);
            }
        }

        /// <summary>
        /// Traces Before PC for diagnostics.
        /// </summary>
        private static void TraceBeforePc(uint pc, int maxCycles, int samplesToKeep, int requestedHit, AmigaModel model)
        {
            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                var samples = new Queue<M68kTraceEntry>();
                int hits = 0;
                for (int cycle = 0; cycle < maxCycles; cycle++)
                {
                    system.TickChipCycle();
                    MachineTraceSample sample = system.LastTraceSample;
                    if (sample.CpuTrace == null ||
                        sample.CpuTrace.AccessType != M68kTraceAccessType.OpcodeFetch)
                    {
                        continue;
                    }

                    if (samples.Count == samplesToKeep)
                    {
                        samples.Dequeue();
                    }

                    samples.Enqueue(sample.CpuTrace);
                    if (sample.CpuTrace.PcBefore == pc)
                    {
                        hits++;
                        if (hits < requestedHit)
                        {
                            continue;
                        }

                        Console.WriteLine("Trace before PC=$" + pc.ToString("X6") + " hit=" + requestedHit + " at cycle=" + sample.GlobalChipCycle);
                        foreach (M68kTraceEntry entry in samples)
                        {
                            Console.WriteLine(entry);
                        }

                        Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                        Console.WriteLine("OverlayAtZero=" + system.Bus.RomOverlayAtZero);
                        Console.WriteLine("PaulaDisk=" + system.Paula.GetDiskDebugInfo());
                        Console.WriteLine("DF0=" + system.Bus.FloppyController.GetDebugInfo());
                        Console.WriteLine("CIAA=" + system.CiaA.GetDebugInfo());
                        Console.WriteLine("CIAB=" + system.CiaB.GetDebugInfo());
                        Console.WriteLine("INTENA=$" + system.Agnus.CustomRegisters.InterruptEnable.ToString("X4") + " INTREQ=$" + system.Agnus.CustomRegisters.InterruptRequest.ToString("X4") + " LEVEL=" + system.Agnus.CustomRegisters.ActiveInterruptLevel);
                        DumpCpuRegisters(system);
                        DumpDisplayRegisters(system);
                        DumpStackWords(system, 8);
                        return;
                    }
                }

                Console.WriteLine("PC=$" + pc.ToString("X6") + " hit=" + requestedHit + " not reached within " + maxCycles + " cycles. hits=" + hits);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
            }
        }

        /// <summary>
        /// Traces ADF Before PC for diagnostics.
        /// </summary>
        private static void TraceAdfBeforePc(string adfPath, uint pc, int maxCycles, int samplesToKeep, int requestedHit, AmigaModel model)
        {
            if (string.IsNullOrWhiteSpace(adfPath) || !File.Exists(adfPath))
            {
                Console.WriteLine("ADF not found: " + adfPath);
                Environment.ExitCode = 2;
                return;
            }

            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                system.Bus.FloppyController.InsertDrive0(AdfImage.Load(adfPath), true);
                var samples = new Queue<M68kTraceEntry>();
                int hits = 0;
                for (int cycle = 0; cycle < maxCycles; cycle++)
                {
                    system.TickChipCycle();
                    MachineTraceSample sample = system.LastTraceSample;
                    if (sample.CpuTrace == null ||
                        sample.CpuTrace.AccessType != M68kTraceAccessType.OpcodeFetch)
                    {
                        continue;
                    }

                    if (samples.Count == samplesToKeep)
                    {
                        samples.Dequeue();
                    }

                    samples.Enqueue(sample.CpuTrace);
                    if (sample.CpuTrace.PcBefore == pc)
                    {
                        hits++;
                        if (hits < requestedHit)
                        {
                            continue;
                        }

                        Console.WriteLine("ADF trace before PC=$" + pc.ToString("X6") + " hit=" + requestedHit + " at cycle=" + sample.GlobalChipCycle);
                        foreach (M68kTraceEntry entry in samples)
                        {
                            Console.WriteLine(entry);
                        }

                        Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                        Console.WriteLine("PaulaDisk=" + system.Paula.GetDiskDebugInfo());
                        Console.WriteLine("DF0=" + system.Bus.FloppyController.GetDebugInfo());
                        Console.WriteLine("CIAA=" + system.CiaA.GetDebugInfo());
                        Console.WriteLine("CIAB=" + system.CiaB.GetDebugInfo());
                        Console.WriteLine("INTENA=$" + system.Agnus.CustomRegisters.InterruptEnable.ToString("X4") + " INTREQ=$" + system.Agnus.CustomRegisters.InterruptRequest.ToString("X4") + " LEVEL=" + system.Agnus.CustomRegisters.ActiveInterruptLevel);
                        DumpCpuRegisters(system);
                        DumpWords(system, sample.CpuTrace.PcBefore >= 16 ? sample.CpuTrace.PcBefore - 16u : sample.CpuTrace.PcBefore, 24, "PCCTX");
                        DumpStackWords(system, 8);
                        return;
                    }
                }

                Console.WriteLine("PC=$" + pc.ToString("X6") + " hit=" + requestedHit + " not reached within " + maxCycles + " cycles. hits=" + hits);
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
            }
        }

        /// <summary>
        /// Traces Until PC Range for diagnostics.
        /// </summary>
        private static void TraceUntilPcRange(uint minPc, uint maxPc, int maxCycles, int samplesToKeep, AmigaModel model)
        {
            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                var samples = new Queue<M68kTraceEntry>();
                for (int cycle = 0; cycle < maxCycles; cycle++)
                {
                    system.TickChipCycle();
                    MachineTraceSample sample = system.LastTraceSample;
                    if (sample.CpuTrace == null ||
                        sample.CpuTrace.AccessType != M68kTraceAccessType.OpcodeFetch)
                    {
                        continue;
                    }

                    if (samples.Count == samplesToKeep)
                    {
                        samples.Dequeue();
                    }

                    samples.Enqueue(sample.CpuTrace);
                    uint pc = sample.CpuTrace.PcBefore;
                    if (pc >= minPc && pc <= maxPc)
                    {
                        Console.WriteLine("Trace until PC range $" + minPc.ToString("X6") + "-$" + maxPc.ToString("X6") + " at cycle=" + sample.GlobalChipCycle);
                        foreach (M68kTraceEntry entry in samples)
                        {
                            Console.WriteLine(entry);
                        }

                        Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                        DumpCpuRegisters(system);
                        DumpStackWords(system, 8);
                        return;
                    }
                }

                Console.WriteLine("PC range $" + minPc.ToString("X6") + "-$" + maxPc.ToString("X6") + " not hit within " + maxCycles + " cycles.");
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
            }
        }

        /// <summary>
        /// Traces ADF Until PC Range for diagnostics.
        /// </summary>
        private static void TraceAdfUntilPcRange(string adfPath, uint minPc, uint maxPc, int maxCycles, int samplesToKeep, AmigaModel model)
        {
            if (string.IsNullOrWhiteSpace(adfPath) || !File.Exists(adfPath))
            {
                Console.WriteLine("ADF not found: " + adfPath);
                Environment.ExitCode = 2;
                return;
            }

            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                system.Bus.FloppyController.InsertDrive0(AdfImage.Load(adfPath), true);
                var samples = new Queue<M68kTraceEntry>();
                for (int cycle = 0; cycle < maxCycles; cycle++)
                {
                    system.TickChipCycle();
                    MachineTraceSample sample = system.LastTraceSample;
                    if (sample.CpuTrace == null ||
                        sample.CpuTrace.AccessType != M68kTraceAccessType.OpcodeFetch)
                    {
                        continue;
                    }

                    if (samples.Count == samplesToKeep)
                    {
                        samples.Dequeue();
                    }

                    samples.Enqueue(sample.CpuTrace);
                    uint pc = sample.CpuTrace.PcBefore;
                    if (pc >= minPc && pc <= maxPc)
                    {
                        Console.WriteLine("ADF trace until PC range $" + minPc.ToString("X6") + "-$" + maxPc.ToString("X6") + " at cycle=" + sample.GlobalChipCycle);
                        Console.WriteLine("ADF=" + Path.GetFileName(adfPath));
                        foreach (M68kTraceEntry entry in samples)
                        {
                            Console.WriteLine(entry);
                        }

                        Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                        Console.WriteLine("PaulaDisk=" + system.Paula.GetDiskDebugInfo());
                        Console.WriteLine("DF0=" + system.Bus.FloppyController.GetDebugInfo());
                        Console.WriteLine("CIAA=" + system.CiaA.GetDebugInfo());
                        Console.WriteLine("CIAB=" + system.CiaB.GetDebugInfo());
                        Console.WriteLine("INTENA=$" + system.Agnus.CustomRegisters.InterruptEnable.ToString("X4") + " INTREQ=$" + system.Agnus.CustomRegisters.InterruptRequest.ToString("X4") + " LEVEL=" + system.Agnus.CustomRegisters.ActiveInterruptLevel);
                        DumpCpuRegisters(system);
                        DumpStackWords(system, 8);
                        return;
                    }
                }

                Console.WriteLine("ADF PC range $" + minPc.ToString("X6") + "-$" + maxPc.ToString("X6") + " not hit within " + maxCycles + " cycles.");
                Console.WriteLine("ADF=" + Path.GetFileName(adfPath));
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                Console.WriteLine("PaulaDisk=" + system.Paula.GetDiskDebugInfo());
                Console.WriteLine("DF0=" + system.Bus.FloppyController.GetDebugInfo());
            }
        }

        /// <summary>
        /// Finds Suspicious CPU Instruction in the available emulator data.
        /// </summary>
        private static void FindSuspiciousCpuInstruction(int maxCycles, AmigaModel model)
        {
            using (var system = new AmigaSystem(model ?? AmigaModel.PalA500, CreateCpuTraceAccuracyOptions()))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                for (int cycle = 0; cycle < maxCycles; cycle++)
                {
                    system.TickChipCycle();
                    MachineTraceSample sample = system.LastTraceSample;
                    if (IsSuspiciousCpuInstruction(sample))
                    {
                        Console.WriteLine("SUSPICIOUS CPU INSTRUCTION");
                        Console.WriteLine("Cycle=" + sample.GlobalChipCycle);
                        Console.WriteLine("Trace=" + sample.CpuTrace);
                        Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                        Console.WriteLine("OverlayAtZero=" + system.Bus.RomOverlayAtZero);
                        DumpCpuRegisters(system);
                        DumpStackWords(system, 8);
                        DumpDisplayRegisters(system);
                        Environment.ExitCode = 1;
                        return;
                    }
                }

                Console.WriteLine("No suspicious CPU instruction within " + maxCycles + " cycles.");
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                DumpDisplayRegisters(system);
                Environment.ExitCode = 0;
            }
        }

        /// <summary>
        /// Returns whether Suspicious CPU Instruction is true.
        /// </summary>
        private static bool IsSuspiciousCpuInstruction(MachineTraceSample sample)
        {
            if (sample == null || sample.CpuTrace == null || string.IsNullOrEmpty(sample.CpuTrace.Instruction))
            {
                return false;
            }

            string instruction = sample.CpuTrace.Instruction;
            if (instruction.StartsWith("DC.W", StringComparison.Ordinal))
            {
                return true;
            }

            if (instruction.StartsWith("ADDRESS ERROR", StringComparison.Ordinal) ||
                instruction.StartsWith("PRIVILEGE", StringComparison.Ordinal))
            {
                return true;
            }

            if (instruction.StartsWith("LINEA", StringComparison.Ordinal) ||
                instruction.StartsWith("LINEF", StringComparison.Ordinal))
            {
                return sample.CpuTrace.PcBefore >= 0x000010u;
            }

            if (instruction.StartsWith("ILLEGAL", StringComparison.Ordinal) &&
                !instruction.Contains("$4E7B", StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Runs the Until Overlay Off routine.
        /// </summary>
        private static void RunUntilOverlayOff(int maxCycles)
        {
            using (var system = new AmigaSystem(AmigaModel.PalA500))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                for (int cycle = 0; cycle < maxCycles; cycle++)
                {
                    system.TickChipCycle();
                    if (!system.Bus.RomOverlayAtZero)
                    {
                        MachineTraceSample sample = system.LastTraceSample;
                        Console.WriteLine("Overlay disabled at cycle=" + sample.GlobalChipCycle);
                        Console.WriteLine("Trace=" + sample.CpuTrace);
                        Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
                        Console.WriteLine("CIAA PRA=$" + system.CiaA.Pra.ToString("X2") + " DDRA=$" + system.CiaA.Ddra.ToString("X2"));
                        DumpCpuRegisters(system);
                        return;
                    }
                }

                Console.WriteLine("Overlay still enabled after " + maxCycles + " cycles.");
                Console.WriteLine("CPU=" + system.GetCpuDebugInfo());
            }
        }

        /// <summary>
        /// Returns whether Display Setup is present.
        /// </summary>
        private static bool HasDisplaySetup(AmigaSystem system)
        {
            var registers = system.Agnus.CustomRegisters;
            if ((registers.ReadWord(0x100) & 0x7000) != 0)
            {
                return true;
            }

            if (registers.ReadWord(0x08E) != 0 || registers.ReadWord(0x090) != 0 ||
                registers.ReadWord(0x092) != 0 || registers.ReadWord(0x094) != 0)
            {
                return true;
            }

            uint copper = ((uint)registers.ReadWord(0x080) << 16) | registers.ReadWord(0x082);
            if (copper != 0)
            {
                return true;
            }

            for (int index = 0; index < 6; index++)
            {
                uint address = ((uint)registers.ReadWord((uint)(0x0E0 + (index * 4))) << 16) |
                    registers.ReadWord((uint)(0x0E2 + (index * 4)));
                if (address != 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns whether Bitplane Pointer is present.
        /// </summary>
        private static bool HasBitplanePointer(AmigaSystem system)
        {
            var registers = system.Agnus.CustomRegisters;
            for (int index = 0; index < 5; index++)
            {
                uint address = ((uint)registers.ReadWord((uint)(0x0E0 + (index * 4))) << 16) |
                    registers.ReadWord((uint)(0x0E2 + (index * 4)));
                if (IsValidChipPointer(system, address, 2))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns whether Likely Raw Copper List is true.
        /// </summary>
        private static bool IsLikelyRawCopperList(AmigaSystem system, uint address)
        {
            address &= 0x00FFFFFEu;
            if (!IsValidChipPointer(system, address, 4))
            {
                return false;
            }

            int plausibleInstructions = 0;
            int moveInstructions = 0;
            int protectedMoves = 0;
            int displayMoves = 0;
            int bitplanePointerMoves = 0;
            for (int index = 0; index < 16; index++)
            {
                ushort first = system.Bus.ReadWord(address + (uint)(index * 4));
                ushort second = system.Bus.ReadWord(address + (uint)(index * 4) + 2u);
                if (first == 0xFFFF && second == 0xFFFE)
                {
                    return plausibleInstructions >= 2 && moveInstructions > 0 && protectedMoves == 0 && (displayMoves > 0 || bitplanePointerMoves > 0);
                }

                if ((first & 1) != 0)
                {
                    plausibleInstructions++;
                    continue;
                }

                uint registerOffset = (uint)(first & 0x01FE);
                if (registerOffset == 0 || registerOffset > 0x01FE)
                {
                    return false;
                }

                if (registerOffset < 0x040)
                {
                    protectedMoves++;
                }

                if ((registerOffset >= 0x080 && registerOffset <= 0x0A4) ||
                    (registerOffset >= 0x100 && registerOffset <= 0x1BE))
                {
                    displayMoves++;
                }

                if (registerOffset >= 0x0E0 && registerOffset <= 0x0F6)
                {
                    bitplanePointerMoves++;
                }

                plausibleInstructions++;
                moveInstructions++;
            }

            return plausibleInstructions >= 4 && moveInstructions > 0 && protectedMoves == 0 && (displayMoves > 0 || bitplanePointerMoves > 0);
        }

        /// <summary>
        /// Returns whether Valid Chip Pointer is true.
        /// </summary>
        private static bool IsValidChipPointer(AmigaSystem system, uint address, int bytes)
        {
            address &= 0x00FFFFFEu;
            if (address == 0 || bytes <= 0)
            {
                return false;
            }

            uint end = address + (uint)bytes - 1u;
            if (end < address || end >= (uint)system.Model.ChipRamBytes)
            {
                return false;
            }

            uint value = system.Bus.ReadLong(address);
            return value != 0xAAAAAAAAu && value != 0x55555555u && value != 0xFFFFFFFFu;
        }

        /// <summary>
        /// Returns whether Valid RAM Pointer is true.
        /// </summary>
        private static bool IsValidRamPointer(AmigaSystem system, uint address, int bytes)
        {
            address &= 0x00FFFFFEu;
            if (address == 0 || bytes <= 0)
            {
                return false;
            }

            uint end = address + (uint)bytes - 1u;
            if (end < address)
            {
                return false;
            }

            bool inChip = end < (uint)system.Model.ChipRamBytes;
            bool inSlow = system.Model.SlowRamBytes > 0 &&
                address >= 0x00C00000u &&
                end < 0x00C00000u + (uint)system.Model.SlowRamBytes;
            if (!inChip && !inSlow)
            {
                return false;
            }

            uint value = system.Bus.ReadLong(address);
            return value != 0xAAAAAAAAu && value != 0x55555555u && value != 0xFFFFFFFFu;
        }

        /// <summary>
        /// Executes the Format Pass logic.
        /// </summary>
        private static string FormatPass(bool value)
        {
            return value ? "OK" : "MISSING";
        }

        /// <summary>
        /// Dumps Display Registers for diagnostics.
        /// </summary>
        private static void DumpDisplayRegisters(AmigaSystem system)
        {
            var registers = system.Agnus.CustomRegisters;
            Console.WriteLine("Beam=" + system.GetBeamDebugInfo());
            Console.WriteLine("DMA=" + system.GetDmaDebugInfo());
            Console.WriteLine("DMACON=$" + registers.ReadWord(0x096).ToString("X4"));
            Console.WriteLine("INTENA=$" + registers.ReadWord(0x09A).ToString("X4"));
            Console.WriteLine("INTREQ=$" + registers.ReadWord(0x09C).ToString("X4"));
            Console.WriteLine("BPLCON0=$" + registers.ReadWord(0x100).ToString("X4"));
            Console.WriteLine("BPLCON1=$" + registers.ReadWord(0x102).ToString("X4"));
            Console.WriteLine("BPL1MOD=$" + registers.ReadWord(0x108).ToString("X4"));
            Console.WriteLine("BPL2MOD=$" + registers.ReadWord(0x10A).ToString("X4"));
            Console.WriteLine("DIWSTRT=$" + registers.ReadWord(0x08E).ToString("X4"));
            Console.WriteLine("DIWSTOP=$" + registers.ReadWord(0x090).ToString("X4"));
            Console.WriteLine("DDFSTRT=$" + registers.ReadWord(0x092).ToString("X4"));
            Console.WriteLine("DDFSTOP=$" + registers.ReadWord(0x094).ToString("X4"));
            Console.WriteLine("COP1LC=$" + (((uint)registers.ReadWord(0x080) << 16) | registers.ReadWord(0x082)).ToString("X8"));
            for (int index = 0; index < 6; index++)
            {
                uint address = ((uint)registers.ReadWord((uint)(0x0E0 + (index * 4))) << 16) |
                    registers.ReadWord((uint)(0x0E2 + (index * 4)));
                Console.WriteLine("BPL" + (index + 1) + "PT=$" + address.ToString("X8"));
                if (address != 0)
                {
                    Console.WriteLine("BPL" + (index + 1) + "[0..7]=" + FormatWords(system, address, 8));
                }
            }

            for (int index = 0; index < 8; index++)
            {
                uint address = ((uint)registers.ReadWord((uint)(0x120 + (index * 4))) << 16) |
                    registers.ReadWord((uint)(0x122 + (index * 4)));
                Console.WriteLine("SPR" + index + "PT=$" + address.ToString("X8"));
            }
        }

        /// <summary>
        /// Dumps CPU Registers for diagnostics.
        /// </summary>
        private static void DumpCpuRegisters(AmigaSystem system)
        {
            for (int index = 0; index < 8; index++)
            {
                Console.WriteLine("D" + index + "=$" + system.Cpu.GetD(index).ToString("X8") +
                    " A" + index + "=$" + system.Cpu.GetA(index).ToString("X8"));
            }
        }

        /// <summary>
        /// Dumps Stack Words for diagnostics.
        /// </summary>
        private static void DumpStackWords(AmigaSystem system, int words)
        {
            uint address = system.Cpu.GetA(7);
                DumpWords(system, address, words, "SP");
        }

        /// <summary>
        /// Executes the Parse Model Argument logic.
        /// </summary>
        private static AmigaModel ParseModelArgument(string[] args, int startIndex)
        {
            for (int index = startIndex; args != null && index < args.Length - 1; index++)
            {
                if (!string.Equals(args[index], "--model", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string value = args[index + 1];
                if (string.Equals(value, "slow512", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "512slow", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "a500slow512", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "a500-512slow", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "a500-pal-512slow", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "a500-512slow-512", StringComparison.OrdinalIgnoreCase))
                {
                    return AmigaModel.A500Pal512Slow512;
                }

                if (string.Equals(value, "1mbchip", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "chip1mb", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "a500chip1mb", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "a500-1mbchip", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "a500-1mb-chip", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "a500pal1mbchip", StringComparison.OrdinalIgnoreCase))
                {
                    return AmigaModel.A500Pal1MbChip;
                }

                if (string.Equals(value, "a500", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "chip512", StringComparison.OrdinalIgnoreCase))
                {
                    return AmigaModel.PalA500;
                }
            }

            return AmigaModel.PalA500;
        }

        /// <summary>
        /// Executes the Parse interrupt Option logic.
        /// </summary>
        private static int ParseIntOption(string[] args, string name, int defaultValue)
        {
            for (int index = 0; args != null && index < args.Length - 1; index++)
            {
                if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(args[index + 1], out int value))
                {
                    return value;
                }
            }

            return defaultValue;
        }

        /// <summary>
        /// Returns whether Option is present.
        /// </summary>
        private static bool HasOption(string[] args, string name)
        {
            for (int index = 0; args != null && index < args.Length; index++)
            {
                if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns a stable relative path when the file belongs to the supplied root directory.
        /// </summary>
        private static string MakeRelativePath(string rootDirectory, string path)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory) || string.IsNullOrWhiteSpace(path))
            {
                return path ?? string.Empty;
            }

            string root = Path.GetFullPath(rootDirectory);
            string fullPath = Path.GetFullPath(path);
            if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) &&
                !root.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                root += Path.DirectorySeparatorChar;
            }

            if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.Substring(root.Length);
            }

            return fullPath;
        }

        /// <summary>
        /// Executes the Sanitize File Name logic.
        /// </summary>
        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unnamed";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            for (int index = 0; index < value.Length; index++)
            {
                char c = value[index];
                bool ok = true;
                for (int invalidIndex = 0; invalidIndex < invalid.Length; invalidIndex++)
                {
                    if (c == invalid[invalidIndex])
                    {
                        ok = false;
                        break;
                    }
                }

                builder.Append(ok ? c : '_');
            }

            return builder.ToString();
        }

        /// <summary>
        /// Executes the Csv Escape logic.
        /// </summary>
        private static string CsvEscape(string value)
        {
            value = value ?? string.Empty;
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>
        /// Dumps Words for diagnostics.
        /// </summary>
        private static void DumpWords(AmigaSystem system, uint address, int words, string label)
        {
            for (int index = 0; index < words; index++)
            {
                uint current = address + (uint)(index * 2);
                Console.WriteLine(label + "+" + (index * 2).ToString("X2") + " $" + current.ToString("X8") + "=$" + system.Bus.ReadWord(current).ToString("X4"));
            }
        }
    }
}

