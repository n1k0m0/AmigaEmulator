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
    /// Provides Fast RAM functionality.
    /// </summary>
    public sealed class FastRam
    {
        /// <summary>
        /// Stores bytes.
        /// </summary>
        private readonly byte[] _bytes;

        /// <summary>
        /// Initializes a new instance of the FastRam class.
        /// </summary>
        public FastRam(int sizeBytes)
        {
            _bytes = new byte[Math.Max(0, sizeBytes)];
        }

        /// <summary>
        /// Gets Size.
        /// </summary>
        public int Size
        {
            get { return _bytes.Length; }
        }

        /// <summary>
        /// Gets Enabled.
        /// </summary>
        public bool Enabled
        {
            get { return _bytes.Length > 0; }
        }

        /// <summary>
        /// Reads one byte from optional fast RAM, returning open-bus data outside the range.
        /// </summary>
        public byte Read(uint offset)
        {
            return _bytes.Length == 0 ? (byte)0xFF : _bytes[offset % (uint)_bytes.Length];
        }

        /// <summary>
        /// Writes one byte to optional fast RAM when the offset is mapped.
        /// </summary>
        public void Write(uint offset, byte value)
        {
            if (_bytes.Length != 0)
            {
                _bytes[offset % (uint)_bytes.Length] = value;
            }
        }

        /// <summary>
        /// Runs Clear.
        /// </summary>
        public void Clear()
        {
            Array.Clear(_bytes, 0, _bytes.Length);
        }
    }
}
