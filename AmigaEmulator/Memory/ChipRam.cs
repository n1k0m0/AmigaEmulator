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

namespace AmigaEmulator.Core.Memory
{
    /// <summary>
    /// Provides Chip RAM functionality.
    /// </summary>
    public sealed class ChipRam
    {
        /// <summary>
        /// Stores bytes.
        /// </summary>
        private readonly byte[] _bytes;

        /// <summary>
        /// Initializes a new instance of the ChipRam class.
        /// </summary>
        public ChipRam(int sizeBytes)
        {
            if (sizeBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sizeBytes));
            }

            _bytes = new byte[sizeBytes];
        }

        /// <summary>
        /// Gets Size.
        /// </summary>
        public int Size
        {
            get { return _bytes.Length; }
        }

        /// <summary>
        /// Reads one byte from chip RAM using the mirrored A500 chip-memory address range.
        /// </summary>
        public byte Read(uint address)
        {
            return _bytes[address % (uint)_bytes.Length];
        }

        /// <summary>
        /// Writes one byte to chip RAM using the mirrored A500 chip-memory address range.
        /// </summary>
        public void Write(uint address, byte value)
        {
            _bytes[address % (uint)_bytes.Length] = value;
        }

        /// <summary>
        /// Runs Clear.
        /// </summary>
        public void Clear()
        {
            Array.Clear(_bytes, 0, _bytes.Length);
        }

        /// <summary>
        /// Runs Snapshot.
        /// </summary>
        public byte[] Snapshot()
        {
            byte[] copy = new byte[_bytes.Length];
            Array.Copy(_bytes, copy, copy.Length);
            return copy;
        }
    }
}
