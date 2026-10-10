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
namespace AmigaEmulator.Core.CustomChips
{
    internal sealed class ChipDmaAddress
    {
        /// <summary>
        /// Stores mask.
        /// </summary>
        private readonly uint _mask;

        /// <summary>
        /// Initializes a new instance of the ChipDmaAddress class.
        /// </summary>
        public ChipDmaAddress(int chipRamBytes)
        {
            uint size = chipRamBytes > 0 ? (uint)chipRamBytes : 512u * 1024u;
            uint mask = 1u;
            while (mask < size)
            {
                mask <<= 1;
            }

            _mask = (mask - 1u) & 0x00FFFFFEu;
        }

        public uint Normalize(uint address)
        {
            return address & _mask;
        }

        /// <summary>
        /// Runs From Registers.
        /// </summary>
        public uint FromRegisters(ushort high, ushort low)
        {
            return Normalize(((uint)high << 16) | low);
        }

        public uint Add(uint address, int bytes)
        {
            return Normalize((uint)((int)address + bytes));
        }

        public uint Add(uint address, uint bytes)
        {
            return Normalize(address + bytes);
        }
    }
}
