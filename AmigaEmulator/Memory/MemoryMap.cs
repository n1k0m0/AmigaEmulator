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
        /// Represents the U nm ap pe d option.
        /// </summary>
        Unmapped,
        /// <summary>
        /// Represents the C hi pR am option.
        /// </summary>
        ChipRam,
        /// <summary>
        /// Represents the S lo wR am option.
        /// </summary>
        SlowRam,
        /// <summary>
        /// Represents the F as tR am option.
        /// </summary>
        FastRam,
        /// <summary>
        /// Represents the C ia A option.
        /// </summary>
        CiaA,
        /// <summary>
        /// Represents the C ia B option.
        /// </summary>
        CiaB,
        /// <summary>
        /// Represents the C us to mR eg is te rs option.
        /// </summary>
        CustomRegisters,
        /// <summary>
        /// Represents the K ic ks ta rt Ro m option.
        /// </summary>
        KickstartRom
    }

    /// <summary>
    /// Stores Memory Map Entry data used by the emulator.
    /// </summary>
    public struct MemoryMapEntry
    {
        /// <summary>
        /// Stores the Start.
        /// </summary>
        public uint Start;
        /// <summary>
        /// Stores the End.
        /// </summary>
        public uint End;
        /// <summary>
        /// Stores the Region.
        /// </summary>
        public AmigaAddressRegion Region;
        /// <summary>
        /// Stores the Name.
        /// </summary>
        public string Name;

        /// <summary>
        /// Executes the Contains logic.
        /// </summary>
        public bool Contains(uint address)
        {
            return address >= Start && address <= End;
        }
    }

    /// <summary>
    /// Provides Memory Map behavior used by the emulator.
    /// </summary>
    public static class MemoryMap
    {
        /// <summary>
        /// Defines the Address Mask constant.
        /// </summary>
        public const uint AddressMask = 0x00FFFFFF;
        /// <summary>
        /// Defines the Chip RAM Start constant.
        /// </summary>
        public const uint ChipRamStart = 0x000000;
        /// <summary>
        /// Defines the Chip RAM End constant.
        /// </summary>
        public const uint ChipRamEnd = 0x07FFFF;
        /// <summary>
        /// Defines the CIA B Start constant.
        /// </summary>
        public const uint CiaBStart = 0xBFD000;
        /// <summary>
        /// Defines the CIA B End constant.
        /// </summary>
        public const uint CiaBEnd = 0xBFDFFF;
        /// <summary>
        /// Defines the CIA A Start constant.
        /// </summary>
        public const uint CiaAStart = 0xBFE000;
        /// <summary>
        /// Defines the CIA A End constant.
        /// </summary>
        public const uint CiaAEnd = 0xBFEFFF;
        /// <summary>
        /// Defines the Slow RAM Start constant.
        /// </summary>
        public const uint SlowRamStart = 0xC00000;
        /// <summary>
        /// Defines the Slow RAM End constant.
        /// </summary>
        public const uint SlowRamEnd = 0xC7FFFF;
        /// <summary>
        /// Defines the Custom Start constant.
        /// </summary>
        public const uint CustomStart = 0xDFF000;
        /// <summary>
        /// Defines the Custom End constant.
        /// </summary>
        public const uint CustomEnd = 0xDFFFFF;
        /// <summary>
        /// Defines the Kickstart Start constant.
        /// </summary>
        public const uint KickstartStart = 0xF80000;
        /// <summary>
        /// Defines the Kickstart End constant.
        /// </summary>
        public const uint KickstartEnd = 0xFFFFFF;

        /// <summary>
        /// Executes the Normalize logic.
        /// </summary>
        public static uint Normalize(uint address)
        {
            return address & AddressMask;
        }
    }
}
