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
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using AmigaEmulator.Core.Cpu;

namespace AmigaEmulator.Core.DevTools
{
    /// <summary>
    /// Provides Machine Trace Sample behavior used by the emulator.
    /// </summary>
    public sealed class MachineTraceSample
    {
        /// <summary>
        /// Gets or updates the Global Chip Cycle.
        /// </summary>
        public long GlobalChipCycle { get; set; }

        /// <summary>
        /// Gets or updates the Frame.
        /// </summary>
        public long Frame { get; set; }

        /// <summary>
        /// Gets or updates the Beam X.
        /// </summary>
        public int BeamX { get; set; }

        /// <summary>
        /// Gets or updates the Beam Y.
        /// </summary>
        public int BeamY { get; set; }

        /// <summary>
        /// Gets or updates the DMA Slot.
        /// </summary>
        public int DmaSlot { get; set; }

        /// <summary>
        /// Gets or updates the DMA Owner.
        /// </summary>
        public string DmaOwner { get; set; }

        /// <summary>
        /// Gets or updates the CPU Can Use Chip Bus.
        /// </summary>
        public bool CpuCanUseChipBus { get; set; }

        /// <summary>
        /// Gets or updates the CPU PC.
        /// </summary>
        public uint CpuPc { get; set; }

        /// <summary>
        /// Gets or updates the CPU SR.
        /// </summary>
        public ushort CpuSr { get; set; }

        /// <summary>
        /// Gets or updates the CPU Trace.
        /// </summary>
        public M68kTraceEntry CpuTrace { get; set; }
    }

    /// <summary>
    /// Provides Machine Trace Exporter behavior used by the emulator.
    /// </summary>
    public static class MachineTraceExporter
    {
        /// <summary>
        /// Executes the Export logic.
        /// </summary>
        public static void Export(int chipCycles, string path)
        {
            using (var system = new AmigaSystem(AmigaModel.A500Pal))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                ExportJsonLines(system, chipCycles, 1, path);
            }
        }

        /// <summary>
        /// Executes the Export Until Suspicious Instruction logic.
        /// </summary>
        public static MachineTraceSample ExportUntilSuspiciousInstruction(int maxChipCycles, string path)
        {
            using (var system = new AmigaSystem(AmigaModel.A500Pal))
            {
                if (KickstartRomResolver.TryLoadDefaultKickstart(out byte[] romBytes, out _))
                {
                    system.LoadKickstart(romBytes);
                }

                return ExportJsonLinesUntilSuspiciousInstruction(system, maxChipCycles, path);
            }
        }

        /// <summary>
        /// Executes the Export Json Lines Until Suspicious Instruction logic.
        /// </summary>
        public static MachineTraceSample ExportJsonLinesUntilSuspiciousInstruction(AmigaSystem system, int maxChipCycles, string path)
        {
            if (system == null)
            {
                throw new ArgumentNullException(nameof(system));
            }

            EnsureDirectory(path);
            using (var writer = new StreamWriter(path, false, Encoding.UTF8))
            {
                writer.WriteLine(JsonSerializer.Serialize(system.LastTraceSample));
                for (int cycle = 1; cycle <= maxChipCycles; cycle++)
                {
                    system.TickChipCycle();
                    MachineTraceSample sample = system.LastTraceSample;
                    writer.WriteLine(JsonSerializer.Serialize(sample));
                    if (IsSuspiciousInstruction(sample))
                    {
                        return sample;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Executes the Export Json Lines logic.
        /// </summary>
        public static void ExportJsonLines(AmigaSystem system, int chipCycles, int sampleInterval, string path)
        {
            if (system == null)
            {
                throw new ArgumentNullException(nameof(system));
            }

            EnsureDirectory(path);
            sampleInterval = Math.Max(1, sampleInterval);
            using (var writer = new StreamWriter(path, false, Encoding.UTF8))
            {
                writer.WriteLine(JsonSerializer.Serialize(system.LastTraceSample));
                for (int cycle = 1; cycle <= chipCycles; cycle++)
                {
                    system.TickChipCycle();
                    if ((cycle % sampleInterval) == 0 || cycle == chipCycles)
                    {
                        writer.WriteLine(JsonSerializer.Serialize(system.LastTraceSample));
                    }
                }
            }
        }

        /// <summary>
        /// Executes the Export Csv logic.
        /// </summary>
        public static void ExportCsv(AmigaSystem system, int chipCycles, int sampleInterval, string path)
        {
            if (system == null)
            {
                throw new ArgumentNullException(nameof(system));
            }

            EnsureDirectory(path);
            sampleInterval = Math.Max(1, sampleInterval);
            using (var writer = new StreamWriter(path, false, Encoding.UTF8))
            {
                writer.WriteLine("cycle,frame,beamX,beamY,dmaSlot,dmaOwner,cpuCanUseChipBus,pc,sr,instruction");
                WriteCsv(writer, system.LastTraceSample);
                for (int cycle = 1; cycle <= chipCycles; cycle++)
                {
                    system.TickChipCycle();
                    if ((cycle % sampleInterval) == 0 || cycle == chipCycles)
                    {
                        WriteCsv(writer, system.LastTraceSample);
                    }
                }
            }
        }

        /// <summary>
        /// Writes one machine-trace sample as a CSV row.
        /// </summary>
        private static void WriteCsv(TextWriter writer, MachineTraceSample sample)
        {
            string instruction = sample.CpuTrace != null ? sample.CpuTrace.Instruction : string.Empty;
            writer.Write(sample.GlobalChipCycle.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(sample.Frame.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(sample.BeamX.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(sample.BeamY.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(sample.DmaSlot.ToString(CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(sample.DmaOwner);
            writer.Write(',');
            writer.Write(sample.CpuCanUseChipBus ? "1" : "0");
            writer.Write(',');
            writer.Write(sample.CpuPc.ToString("X6", CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(sample.CpuSr.ToString("X4", CultureInfo.InvariantCulture));
            writer.Write(',');
            writer.Write(instruction);
            writer.WriteLine();
        }

        /// <summary>
        /// Returns whether Suspicious Instruction is true.
        /// </summary>
        private static bool IsSuspiciousInstruction(MachineTraceSample sample)
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

            if (instruction.StartsWith("LINEA", StringComparison.Ordinal) || instruction.StartsWith("LINEF", StringComparison.Ordinal))
            {
                return sample.CpuTrace.PcBefore >= 0x000010;
            }

            if (instruction.StartsWith("ILLEGAL", StringComparison.Ordinal) && !instruction.Contains("$4E7B"))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Executes the Ensure Directory logic.
        /// </summary>
        private static void EnsureDirectory(string path)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
    }
}
