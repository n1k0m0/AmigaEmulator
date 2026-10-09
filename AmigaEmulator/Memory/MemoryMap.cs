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
namespace AmigaEmulator.Core.Memory
{
    /// <summary>
    /// Lists the supported Amiga Address Region values.
    /// </summary>
    public enum AmigaAddressRegion
    {
        /// <summary>
        /// Represents Unmapped.
        /// </summary>
        Unmapped,
        /// <summary>
        /// Represents Chip RAM.
        /// </summary>
        ChipRam,
        /// <summary>
        /// Represents Slow RAM.
        /// </summary>
        SlowRam,
        /// <summary>
        /// Represents Fast RAM.
        /// </summary>
        FastRam,
        /// <summary>
        /// Represents CIA A.
        /// </summary>
        CiaA,
        /// <summary>
        /// Represents CIA B.
        /// </summary>
        CiaB,
        /// <summary>
        /// Represents Custom Registers.
        /// </summary>
        CustomRegisters,
        /// <summary>
        /// Represents Kickstart ROM.
        /// </summary>
        KickstartRom,
        GayleIde,
        AutoConfig,
        BatteryClock,
        Pcmcia,
        MotherboardResources
    }

    /// <summary>
    /// Stores Memory Map Entry data used by the emulator.
    /// </summary>
    public struct MemoryMapEntry
    {
        /// <summary>
        /// Stores Start.
        /// </summary>
        public uint Start;
        /// <summary>
        /// Stores End.
        /// </summary>
        public uint End;
        /// <summary>
        /// Stores Region.
        /// </summary>
        public AmigaAddressRegion Region;
        /// <summary>
        /// Stores Name.
        /// </summary>
        public string Name;

        /// <summary>
        /// Runs Contains.
        /// </summary>
        public bool Contains(uint address)
        {
            return address >= Start && address <= End;
        }
    }

    /// <summary>
        /// Defines the 24-bit A500 memory regions used by the bus decoder.
    /// </summary>
    public static class MemoryMap
    {
        /// <summary>
        /// Mask applied to 68000 addresses before region decoding.
        /// </summary>
        public const uint AddressMask = 0x00FFFFFF;
        /// <summary>
        /// Start address of Chip RAM.
        /// </summary>
        public const uint ChipRamStart = 0x000000;
        /// <summary>
        /// Default end address for a 512 KB Chip RAM map.
        /// </summary>
        public const uint ChipRamEnd = 0x07FFFF;
        /// <summary>
        /// Start address of the supported Zorro II Fast RAM window.
        /// </summary>
        public const uint FastRamStart = 0x200000;
        /// <summary>
        /// End address of the supported Zorro II Fast RAM window.
        /// </summary>
        public const uint FastRamEnd = 0x9FFFFF;
        /// <summary>
        /// Start address of the CIA-B register mirror range.
        /// </summary>
        public const uint CiaBStart = 0xBFD000;
        /// <summary>
        /// End address of the CIA-B register mirror range.
        /// </summary>
        public const uint CiaBEnd = 0xBFDFFF;
        /// <summary>
        /// Start address of the CIA-A register mirror range.
        /// </summary>
        public const uint CiaAStart = 0xBFE000;
        /// <summary>
        /// End address of the CIA-A register mirror range.
        /// </summary>
        public const uint CiaAEnd = 0xBFEFFF;
        /// <summary>
        /// Start address of the A500 trapdoor Slow RAM window.
        /// </summary>
        public const uint SlowRamStart = 0xC00000;
        /// <summary>
        /// End address of the A500 trapdoor Slow RAM window.
        /// </summary>
        public const uint SlowRamEnd = 0xC7FFFF;
        /// <summary>
        /// Start address of the custom-chip register range.
        /// </summary>
        public const uint CustomStart = 0xDFF000;
        /// <summary>
        /// End address of the custom-chip register range.
        /// </summary>
        public const uint CustomEnd = 0xDFFFFF;
        /// <summary>
        /// Start address of the 512 KB Kickstart ROM range.
        /// </summary>
        public const uint KickstartStart = 0xF80000;
        /// <summary>
        /// End address of the 512 KB Kickstart ROM range.
        /// </summary>
        public const uint KickstartEnd = 0xFFFFFF;

        /// <summary>
        /// Normalizes an address to the 68000 24-bit bus width.
        /// </summary>
        public static uint Normalize(uint address)
        {
            return address & AddressMask;
        }
    }
}
