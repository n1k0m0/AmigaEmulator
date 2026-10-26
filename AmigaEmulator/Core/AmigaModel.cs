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

namespace AmigaEmulator.Core
{
    /// <summary>Core machine identities. A1000 is development-only pending native boot acceptance.</summary>
    public enum AmigaMachineModel { A500, A500Plus, A600, A2000Ocs, A2000Ecs, A1200, A4000Ec030, A3000, A1000 }
    public enum AmigaChipset { Ocs, Ecs, Aga }
    public enum AmigaCpuModel { M68000, M68EC020, M68020, M68EC030, M68030 }

    /// <summary>Immutable machine capabilities and memory configuration.</summary>
    public sealed class AmigaModel
    {
        public static bool IsSelectableMachine(AmigaMachineModel machine) => Enum.IsDefined(machine) && machine != AmigaMachineModel.A1000;

        public static AmigaMachineModel CycleSelectableMachine(AmigaMachineModel machine, int direction)
        {
            var values = Enum.GetValues<AmigaMachineModel>();
            int index = Array.IndexOf(values, machine);
            if (index < 0) index = 0;
            int step = direction < 0 ? -1 : 1;
            if (direction == 0 && IsSelectableMachine(machine)) return machine;
            do { index = (index + step + values.Length) % values.Length; }
            while (!IsSelectableMachine(values[index]));
            return values[index];
        }

        /// <summary>Core-only A1000 PAL board. Bootstrap/DF0 acceptance is required before frontend exposure.</summary>
        public static AmigaModel CreateDevelopmentA1000Pal(int chipRamBytes = 256 * 1024)
        {
            if (chipRamBytes != 256 * 1024 && chipRamBytes != 512 * 1024)
                throw new ArgumentOutOfRangeException(nameof(chipRamBytes), "A1000 bring-up supports 256 or 512 KiB Chip RAM.");
            return new AmigaModel("A1000 PAL DEVELOPMENT", 7093790, chipRamBytes, 0, 227, 312, 640, 256)
                { Machine = AmigaMachineModel.A1000 };
        }

        public AmigaMachineModel Machine { get; private set; } = AmigaMachineModel.A500;
        public AmigaChipset Chipset { get; private set; } = AmigaChipset.Ocs;
        public AmigaCpuModel Cpu { get; private set; } = AmigaCpuModel.M68000;
        public int ChipMasterClockHz { get; private set; }
        public Media.AmigaIdeModel IdeModel { get; private set; }
        public bool HasDesktopResources { get; private set; }
        public int ZorroIIIRamBytes { get; private set; }
        public System.Collections.Generic.IReadOnlyList<int> ZorroIIIRamBoardBytes { get; private set; } = Array.Empty<int>();
        public bool HasRicohClock => HasDesktopResources;
        public uint MotherboardFastRamStart => HasDesktopResources ? 0x08000000u - (uint)FastRamBytes : 0;
        public bool HasBuiltInIde => Machine == AmigaMachineModel.A600 || Machine == AmigaMachineModel.A1200 || Machine == AmigaMachineModel.A4000Ec030;
        public bool HasBuiltInScsi => Machine == AmigaMachineModel.A3000;
        public bool HasBuiltInHardDiskController => HasBuiltInIde || HasBuiltInScsi;
        public string HardDiskControllerName => HasBuiltInScsi ? "A3000 SCSI" : IdeModel == Media.AmigaIdeModel.A4000 ? "A4000 IDE" : "GAYLE IDE";
        public bool HasPcmcia => Machine == AmigaMachineModel.A600 || Machine == AmigaMachineModel.A1200;
        public bool HasBatteryClock => Machine == AmigaMachineModel.A500Plus ||
            Machine == AmigaMachineModel.A2000Ocs || Machine == AmigaMachineModel.A2000Ecs || HasRicohClock;
        public bool HasMainsTodClock => Machine == AmigaMachineModel.A2000Ocs || Machine == AmigaMachineModel.A2000Ecs || Machine == AmigaMachineModel.A3000;
        public byte UnmappedReadValue => Machine == AmigaMachineModel.A2000Ocs || Machine == AmigaMachineModel.A2000Ecs ? (byte)0 : (byte)0xFF;
        public ushort AgnusId => Chipset == AmigaChipset.Aga ? (ushort)0x2300 : Chipset == AmigaChipset.Ecs ? (ushort)0x2000 : (ushort)0;

        public static AmigaModel Create(AmigaMachineModel machine, AmigaRamProfile ram)
        {
            if (!Enum.IsDefined(machine)) throw new ArgumentOutOfRangeException(nameof(machine));
            if (!IsSelectableMachine(machine)) throw new InvalidOperationException("A1000 is available only through the diagnostic board factory pending native boot validation.");
            if (!Enum.IsDefined(ram)) throw new ArgumentOutOfRangeException(nameof(ram));
            AmigaModel memory = ForRamProfile(ram);
            if (machine == AmigaMachineModel.A500) return memory;
            if (NormalizeRamProfile(machine, ram) != ram)
                throw new ArgumentException("RAM layout is not supported by the selected machine revision.", nameof(ram));
            string name = machine switch
            {
                AmigaMachineModel.A500Plus => "A500+ PAL ECS",
                AmigaMachineModel.A600 => "A600 PAL ECS",
                AmigaMachineModel.A2000Ocs => "A2000 PAL OCS",
                AmigaMachineModel.A1200 => "A1200 PAL AGA",
                AmigaMachineModel.A4000Ec030 => "A4000/030 PAL AGA",
                AmigaMachineModel.A3000 => "A3000/030 PAL ECS",
                _ => "A2000 PAL ECS"
            };
            int clock = machine == AmigaMachineModel.A1200 ? 14187580 : machine == AmigaMachineModel.A4000Ec030 || machine == AmigaMachineModel.A3000 ? 25000000 : memory.CpuClockHz;
            return new AmigaModel(name, clock, memory.ChipRamBytes, memory.SlowRamBytes, memory.FastRamBytes,
                memory.ChipCyclesPerLine, memory.LinesPerFrame, memory.VisibleWidth, memory.VisibleHeight)
                { Machine = machine, Chipset = GetChipset(machine), ChipMasterClockHz = memory.ChipMasterClockHz,
                  Cpu = machine == AmigaMachineModel.A1200 ? AmigaCpuModel.M68EC020 : machine == AmigaMachineModel.A4000Ec030 ? AmigaCpuModel.M68EC030 : machine == AmigaMachineModel.A3000 ? AmigaCpuModel.M68030 : AmigaCpuModel.M68000,
                  IdeModel = machine == AmigaMachineModel.A4000Ec030 ? Media.AmigaIdeModel.A4000 : machine == AmigaMachineModel.A3000 ? Media.AmigaIdeModel.A3000Scsi : Media.AmigaIdeModel.Gayle,
                  HasDesktopResources = machine == AmigaMachineModel.A4000Ec030 || machine == AmigaMachineModel.A3000 };
        }

        public static AmigaChipset GetChipset(AmigaMachineModel machine)
        {
            if (!Enum.IsDefined(machine)) throw new ArgumentOutOfRangeException(nameof(machine));
            if (machine == AmigaMachineModel.A1200 || machine == AmigaMachineModel.A4000Ec030) return AmigaChipset.Aga;
            return machine == AmigaMachineModel.A500 || machine == AmigaMachineModel.A2000Ocs || machine == AmigaMachineModel.A1000
                ? AmigaChipset.Ocs : AmigaChipset.Ecs;
        }

        public static AmigaRamProfile GetDefaultRamProfile(AmigaMachineModel machine)
        {
            if (!Enum.IsDefined(machine)) throw new ArgumentOutOfRangeException(nameof(machine));
            if (!IsSelectableMachine(machine)) throw new InvalidOperationException("A1000 development memory is configured through the diagnostic board factory.");
            return machine == AmigaMachineModel.A3000 ? AmigaRamProfile.Chip2MbFast4Mb :
                GetChipset(machine) == AmigaChipset.Aga ? machine == AmigaMachineModel.A4000Ec030 ? AmigaRamProfile.Chip2MbFast4Mb : AmigaRamProfile.Chip2Mb :
                GetChipset(machine) == AmigaChipset.Ocs ? AmigaRamProfile.Chip512KSlow512K : AmigaRamProfile.Chip1Mb;
        }

        public static AmigaRamProfile NormalizeRamProfile(AmigaMachineModel machine, AmigaRamProfile ram)
        {
            if (!Enum.IsDefined(machine)) throw new ArgumentOutOfRangeException(nameof(machine));
            if (!IsSelectableMachine(machine)) throw new InvalidOperationException("A1000 development memory is configured through the diagnostic board factory.");
            if (!Enum.IsDefined(ram)) throw new ArgumentOutOfRangeException(nameof(ram));
            if (machine == AmigaMachineModel.A500) return ram;
            var info = AmigaOverlaySettingCatalog.GetRamProfileInfo(ram);
            if (GetChipset(machine) == AmigaChipset.Aga || machine == AmigaMachineModel.A3000)
                return info.FastRamBytes switch
                {
                    2 * 1024 * 1024 => AmigaRamProfile.Chip2MbFast2Mb,
                    4 * 1024 * 1024 => AmigaRamProfile.Chip2MbFast4Mb,
                    8 * 1024 * 1024 => AmigaRamProfile.Chip2MbFast8Mb,
                    _ => AmigaRamProfile.Chip2Mb
                };
            if (machine == AmigaMachineModel.A2000Ocs)
            {
                if (info.ChipRamBytes == 512 * 1024) return ram;
                return info.FastRamBytes switch
                {
                    2 * 1024 * 1024 => AmigaRamProfile.Chip512KSlow512KFast2Mb,
                    4 * 1024 * 1024 => AmigaRamProfile.Chip512KSlow512KFast4Mb,
                    8 * 1024 * 1024 => AmigaRamProfile.Chip512KSlow512KFast8Mb,
                    _ => AmigaRamProfile.Chip512KSlow512K
                };
            }
            if (info.ChipRamBytes >= 1024 * 1024 && info.SlowRamBytes == 0) return ram;
            return info.FastRamBytes switch
            {
                2 * 1024 * 1024 => AmigaRamProfile.Chip1MbFast2Mb,
                4 * 1024 * 1024 => AmigaRamProfile.Chip1MbFast4Mb,
                8 * 1024 * 1024 => AmigaRamProfile.Chip2MbFast8Mb,
                _ => AmigaRamProfile.Chip1Mb
            };
        }

        /// <summary>CPU bring-up on an existing motherboard; not a released hardware profile.</summary>
        public AmigaModel WithDevelopmentProcessor(AmigaCpuModel cpu, int clockHz)
        {
            if (!Enum.IsDefined(cpu)) throw new ArgumentOutOfRangeException(nameof(cpu));
            if (Machine == AmigaMachineModel.A1000 && cpu != AmigaCpuModel.M68000)
                throw new InvalidOperationException("The A1000 development board has a 68000 processor.");
            if (HasDesktopResources && (cpu == AmigaCpuModel.M68000 || cpu == AmigaCpuModel.M68EC020))
                throw new InvalidOperationException("Motherboard Fast RAM requires 32 physical CPU address lines.");
            if (cpu == AmigaCpuModel.M68000 && clockHz != ChipMasterClockHz) throw new ArgumentException("68000 clock upgrades are not implemented.", nameof(clockHz));
            if (clockHz < ChipMasterClockHz || clockHz > 100000000) throw new ArgumentOutOfRangeException(nameof(clockHz));
            return new AmigaModel(Name + " CPU DEVELOPMENT", clockHz, ChipRamBytes, SlowRamBytes, FastRamBytes,
                ChipCyclesPerLine, LinesPerFrame, VisibleWidth, VisibleHeight)
                { Machine = Machine, Chipset = Chipset, Cpu = cpu, ChipMasterClockHz = ChipMasterClockHz, IdeModel = IdeModel,
                  HasDesktopResources = HasDesktopResources, ZorroIIIRamBytes = ZorroIIIRamBytes, ZorroIIIRamBoardBytes = ZorroIIIRamBoardBytes };
        }

        /// <summary>Custom-chip bring-up; not a complete A1200/A4000 motherboard.</summary>
        public AmigaModel WithDevelopmentChipset(AmigaChipset chipset)
        {
            if (!Enum.IsDefined(chipset)) throw new ArgumentOutOfRangeException(nameof(chipset));
            if (Machine == AmigaMachineModel.A1000 && chipset != AmigaChipset.Ocs)
                throw new InvalidOperationException("The A1000 development board has the original OCS chipset.");
            return new AmigaModel(Name + " CHIPSET DEVELOPMENT", CpuClockHz, ChipRamBytes, SlowRamBytes, FastRamBytes,
                ChipCyclesPerLine, LinesPerFrame, VisibleWidth, VisibleHeight)
                { Machine = Machine, Chipset = chipset, Cpu = Cpu, ChipMasterClockHz = ChipMasterClockHz, IdeModel = IdeModel,
                  HasDesktopResources = HasDesktopResources, ZorroIIIRamBytes = ZorroIIIRamBytes, ZorroIIIRamBoardBytes = ZorroIIIRamBoardBytes };
        }

        public AmigaModel WithDevelopmentIde(Media.AmigaIdeModel ide)
        {
            if (!Enum.IsDefined(ide)) throw new ArgumentOutOfRangeException(nameof(ide));
            return new AmigaModel(Name + " IDE DEVELOPMENT", CpuClockHz, ChipRamBytes, SlowRamBytes, FastRamBytes,
                ChipCyclesPerLine, LinesPerFrame, VisibleWidth, VisibleHeight)
                { Machine = Machine, Chipset = Chipset, Cpu = Cpu, ChipMasterClockHz = ChipMasterClockHz, IdeModel = ide,
                  HasDesktopResources = HasDesktopResources, ZorroIIIRamBytes = ZorroIIIRamBytes, ZorroIIIRamBoardBytes = ZorroIIIRamBoardBytes };
        }

        public AmigaModel WithDevelopmentDesktopResources()
        {
            if (Cpu == AmigaCpuModel.M68000 || Cpu == AmigaCpuModel.M68EC020)
                throw new InvalidOperationException("Motherboard Fast RAM requires 32 physical CPU address lines.");
            return new AmigaModel(Name + " DESKTOP DEVELOPMENT", CpuClockHz, ChipRamBytes, SlowRamBytes, FastRamBytes,
                ChipCyclesPerLine, LinesPerFrame, VisibleWidth, VisibleHeight)
                { Machine = Machine, Chipset = Chipset, Cpu = Cpu, ChipMasterClockHz = ChipMasterClockHz, IdeModel = IdeModel,
                  HasDesktopResources = true, ZorroIIIRamBytes = ZorroIIIRamBytes, ZorroIIIRamBoardBytes = ZorroIIIRamBoardBytes };
        }

        /// <summary>Opt-in Zorro III RAM board for bus bring-up; not a released configuration.</summary>
        public AmigaModel WithDevelopmentZorroIIIRam(int bytes) => WithDevelopmentZorroIIIRamBoards(bytes);

        /// <summary>Up to four RAM-only expansion slots; no bus-master DMA or IRQ hardware.</summary>
        public AmigaModel WithDevelopmentZorroIIIRamBoards(params int[] sizes)
        {
            if (!HasDesktopResources || Cpu == AmigaCpuModel.M68000 || Cpu == AmigaCpuModel.M68EC020)
                throw new InvalidOperationException("Zorro III requires a 32-bit desktop motherboard and CPU.");
            if (sizes == null) throw new ArgumentNullException(nameof(sizes));
            if (sizes.Length < 1 || sizes.Length > 4)
                throw new ArgumentException("Development expansion supports one to four RAM boards.", nameof(sizes));
            int total = 0;
            foreach (int size in sizes)
            {
                if (size != 8 * 1024 * 1024 && size != 16 * 1024 * 1024 && size != 32 * 1024 * 1024)
                    throw new ArgumentOutOfRangeException(nameof(sizes), "Only 8, 16 or 32 MiB boards are implemented.");
                total += size;
            }
            return new AmigaModel(Name + " ZORRO III DEVELOPMENT", CpuClockHz, ChipRamBytes, SlowRamBytes, FastRamBytes,
                ChipCyclesPerLine, LinesPerFrame, VisibleWidth, VisibleHeight)
                { Machine = Machine, Chipset = Chipset, Cpu = Cpu, ChipMasterClockHz = ChipMasterClockHz,
                  IdeModel = IdeModel, HasDesktopResources = HasDesktopResources, ZorroIIIRamBytes = total,
                  ZorroIIIRamBoardBytes = Array.AsReadOnly((int[])sizes.Clone()) };
        }

        public static AmigaModel ForRamProfile(AmigaRamProfile profile)
        {
            switch (profile)
            {
                case AmigaRamProfile.Chip512K: return A500Pal;
                case AmigaRamProfile.Chip512KSlow512K: return A500Pal512Slow512;
                case AmigaRamProfile.Chip1Mb: return A500Pal1MbChip;
                case AmigaRamProfile.Chip2Mb: return A500Pal2MbChip;
                case AmigaRamProfile.Chip2MbFast8Mb: return A500Pal2MbChipFast8Mb;
                default:
                    var info = AmigaOverlaySettingCatalog.GetRamProfileInfo(profile);
                    return new AmigaModel("PAL + " + info.Label, 7093790, info.ChipRamBytes, info.SlowRamBytes, info.FastRamBytes, 227, 312, 640, 256);
            }
        }
        /// <summary>
        /// Default PAL OCS Amiga 500 with 512 KB Chip RAM.
        /// </summary>
        public static readonly AmigaModel A500Pal = new AmigaModel(
            "A500 PAL OCS",
            7093790,
            512 * 1024,
            0,
            227,
            312,
            640,
            256);

        /// <summary>
        /// PAL OCS Amiga 500 with the common 512 KB trapdoor Slow RAM expansion.
        /// </summary>
        public static readonly AmigaModel A500Pal512Slow512 = new AmigaModel(
            "A500 PAL OCS + 512K SLOW",
            7093790,
            512 * 1024,
            512 * 1024,
            227,
            312,
            640,
            256);

        /// <summary>
        /// PAL OCS Amiga 500 profile with 1 MB Chip RAM.
        /// </summary>
        public static readonly AmigaModel A500Pal1MbChip = new AmigaModel(
            "A500 PAL OCS + 1MB CHIP",
            7093790,
            1024 * 1024,
            0,
            227,
            312,
            640,
            256);

        /// <summary>
        /// PAL OCS Amiga 500 profile with 2 MB Chip RAM.
        /// </summary>
        public static readonly AmigaModel A500Pal2MbChip = new AmigaModel(
            "A500 PAL OCS + 2MB CHIP",
            7093790,
            2 * 1024 * 1024,
            0,
            227,
            312,
            640,
            256);

        /// <summary>
        /// PAL OCS Amiga 500 profile with 2 MB Chip RAM and 8 MB Zorro II Fast RAM.
        /// </summary>
        public static readonly AmigaModel A500Pal2MbChipFast8Mb = new AmigaModel(
            "A500 PAL OCS + 2MB CHIP + 8MB FAST",
            7093790,
            2 * 1024 * 1024,
            0,
            8 * 1024 * 1024,
            227,
            312,
            640,
            256);

        /// <summary>
        /// Gets the default PAL A500 model profile.
        /// </summary>
        public static AmigaModel PalA500
        {
            get { return A500Pal; }
        }

        /// <summary>
        /// Initializes a new instance of the AmigaModel class.
        /// </summary>
        public AmigaModel(
            string name,
            int cpuClockHz,
            int chipRamBytes,
            int slowRamBytes,
            int chipCyclesPerLine,
            int linesPerFrame,
            int visibleWidth,
            int visibleHeight)
            : this(name, cpuClockHz, chipRamBytes, slowRamBytes, 0, chipCyclesPerLine, linesPerFrame, visibleWidth, visibleHeight)
        {
        }

        /// <summary>
        /// Initializes a new instance of the AmigaModel class.
        /// </summary>
        public AmigaModel(
            string name,
            int cpuClockHz,
            int chipRamBytes,
            int slowRamBytes,
            int fastRamBytes,
            int chipCyclesPerLine,
            int linesPerFrame,
            int visibleWidth,
            int visibleHeight)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Model name is required.", nameof(name));
            }

            Name = name;
            CpuClockHz = cpuClockHz;
            ChipMasterClockHz = cpuClockHz;
            ChipRamBytes = chipRamBytes;
            SlowRamBytes = slowRamBytes;
            FastRamBytes = fastRamBytes;
            ChipCyclesPerLine = chipCyclesPerLine;
            LinesPerFrame = linesPerFrame;
            VisibleWidth = visibleWidth;
            VisibleHeight = visibleHeight;
        }

        /// <summary>
        /// Gets the display name for this hardware profile.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Gets the configured 68000 CPU clock in hertz.
        /// </summary>
        public int CpuClockHz { get; private set; }

        /// <summary>
        /// Gets the PAL chip clock frequency in hertz.
        /// </summary>
        public double ChipClockHz
        {
            get { return ChipMasterClockHz / 2.0; }
        }

        /// <summary>
        /// Gets the PAL frame rate derived from the beam timing.
        /// </summary>
        public double FrameRateHz
        {
            get { return ChipClockHz / (ChipCyclesPerLine * LinesPerFrame); }
        }

        /// <summary>
        /// Gets the amount of Chip RAM mapped from address zero.
        /// </summary>
        public int ChipRamBytes { get; private set; }

        /// <summary>
        /// Gets the amount of trapdoor Slow RAM mapped at the A500 Slow RAM range.
        /// </summary>
        public int SlowRamBytes { get; private set; }

        /// <summary>
        /// Gets the configured Zorro II style Fast RAM bytes.
        /// </summary>
        public int FastRamBytes { get; private set; }

        /// <summary>
        /// Gets the PAL chip cycles per scanline used by the beam model.
        /// </summary>
        public int ChipCyclesPerLine { get; private set; }

        /// <summary>
        /// Gets the PAL scanline count per frame used by the beam model.
        /// </summary>
        public int LinesPerFrame { get; private set; }

        /// <summary>
        /// Gets the visible framebuffer width exposed to the frontend.
        /// </summary>
        public int VisibleWidth { get; private set; }

        /// <summary>
        /// Gets the visible framebuffer height exposed to the frontend.
        /// </summary>
        public int VisibleHeight { get; private set; }
    }
}
